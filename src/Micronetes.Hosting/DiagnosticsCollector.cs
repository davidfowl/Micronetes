using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Micronetes.Hosting.Logging;
using Micronetes.Hosting.Metrics;
using Micronetes.Hosting.Model;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventSource;
using Microsoft.Hosting.Logging;
using OpenTelemetry.Trace;
using OpenTelemetry.Trace.Configuration;

namespace Micronetes.Hosting
{
    public class DiagnosticsCollector
    {
        // This list of event sources needs to be extensible
        private static readonly string MicrosoftExtensionsLoggingProviderName = "Microsoft-Extensions-Logging";
        private static readonly string SystemRuntimeEventSourceName = "System.Runtime";
        private static readonly string MicrosoftAspNetCoreHostingEventSourceName = "Microsoft.AspNetCore.Hosting";
        private static readonly string GrpcAspNetCoreServer = "Grpc.AspNetCore.Server";
        private static readonly string DiagnosticSourceEventSource = "Microsoft-Diagnostics-DiagnosticSource";
        private static readonly string TplEventSource = "System.Threading.Tasks.TplEventSource";

        // This is the list of events for distributed tracing
        private static readonly string DiagnosticFilterString = "\"" +
          "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Start@Activity1Start:-" +
            "Request.Path" +
            ";Request.Method" +
            ";ActivityStartTime=*Activity.StartTimeUtc.Ticks" +
            ";ActivityParentId=*Activity.ParentId" +
            ";ActivityId=*Activity.Id" +
            ";ActivityIdFormat=*Activity.IdFormat" +
          "\r\n" +
        "Microsoft.AspNetCore/Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop@Activity1Stop:-" +
            "Response.StatusCode" +
            ";ActivityDuration=*Activity.Duration.Ticks" +
            ";ActivityId=*Activity.Id" +
        "\r\n" +
        "HttpHandlerDiagnosticListener/System.Net.Http.HttpRequestOut.Start@Activity2Start:-" +
            ";ActivityStartTime=*Activity.StartTimeUtc.Ticks" +
            ";ActivityParentId=*Activity.ParentId" +
            ";ActivityId=*Activity.Id" +
         "\r\n" +
        "HttpHandlerDiagnosticListener/System.Net.Http.HttpRequestOut.Stop@Activity2Stop:-" +
            ";ActivityStartTime=*Activity.StartTimeUtc.Ticks" +
            ";ActivityParentId=*Activity.ParentId" +
            ";ActivityId=*Activity.Id" +
        "\r\n" +

        "\"";

        private readonly ILogger _logger;

        public DiagnosticsCollector(ILogger logger)
        {
            _logger = logger;
        }

        public void ProcessEvents(Action<string, string, TracerBuilder> configureTracing,
                                  Action<string, string, ILoggingBuilder> configureLogging,
                                  string applicationName,
                                  string serviceName,
                                  int processId,
                                  string replicaName,
                                  ServiceReplica replica,
                                  CancellationToken cancellationToken)
        {
            var hasEventPipe = false;

            for (int i = 0; i < 10; ++i)
            {
                if (DiagnosticsClient.GetPublishedProcesses().Contains(processId))
                {
                    hasEventPipe = true;
                    break;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                Thread.Sleep(500);
            }

            if (!hasEventPipe)
            {
                _logger.LogInformation("Process id {PID}, does not support event pipe", processId);
                return;
            }

            _logger.LogInformation("Listening for event pipe events for {ServiceName} on process id {PID}", replicaName, processId);

            // Create the logger factory for this replica
            using var loggerFactory = LoggerFactory.Create(builder => configureLogging(serviceName, replicaName, builder));

            // Create the tracer for this replica
            Tracer tracer = null;

            using var tracerFactory = TracerFactory.Create(builder =>
            {
                builder.AddCollector(t =>
                {
                    tracer = t;
                    return t;
                });

                configureTracing(serviceName, replicaName, builder);
            });

            var providers = new List<EventPipeProvider>()
            {
                // Runtime Metrics
                new EventPipeProvider(
                    SystemRuntimeEventSourceName,
                    EventLevel.Informational,
                    (long)ClrTraceEventParser.Keywords.None,
                    new Dictionary<string, string>() {
                        { "EventCounterIntervalSec", "1" }
                    }
                ),
                new EventPipeProvider(
                    MicrosoftAspNetCoreHostingEventSourceName,
                    EventLevel.Informational,
                    (long)ClrTraceEventParser.Keywords.None,
                    new Dictionary<string, string>() {
                        { "EventCounterIntervalSec", "1" }
                    }
                ),
                new EventPipeProvider(
                    GrpcAspNetCoreServer,
                    EventLevel.Informational,
                    (long)ClrTraceEventParser.Keywords.None,
                    new Dictionary<string, string>() {
                        { "EventCounterIntervalSec", "1" }
                    }
                ),
                
                // Application Metrics
                new EventPipeProvider(
                    applicationName,
                    EventLevel.Informational,
                    (long)ClrTraceEventParser.Keywords.None,
                    new Dictionary<string, string>() {
                        { "EventCounterIntervalSec", "1" }
                    }
                ),

                // Logging
                new EventPipeProvider(
                    MicrosoftExtensionsLoggingProviderName,
                    EventLevel.LogAlways,
                    (long)(LoggingEventSource.Keywords.JsonMessage | LoggingEventSource.Keywords.FormattedMessage)
                ),

                // Distributed Tracing

                // Activity correlation
                new EventPipeProvider(TplEventSource,
                        keywords: 0x80,
                        eventLevel: EventLevel.LogAlways),

                // Diagnostic source events
                new EventPipeProvider(DiagnosticSourceEventSource,
                        keywords: 0x1 | 0x2,
                        eventLevel: EventLevel.Verbose,
                        arguments: new Dictionary<string,string>
                        {
                            { "FilterAndPayloadSpecs", DiagnosticFilterString }
                        })
            };

            while (!cancellationToken.IsCancellationRequested)
            {
                EventPipeSession session = null;
                var client = new DiagnosticsClient(processId);

                try
                {
                    session = client.StartEventPipeSession(providers);
                }
                catch (EndOfStreamException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogDebug(0, ex, "Failed to start the event pipe session");
                    }

                    // We can't even start the session, wait until the process boots up again to start another metrics thread
                    break;
                }

                void StopSession()
                {
                    try
                    {
                        session.Stop();
                    }
                    catch (EndOfStreamException)
                    {
                        // If the app we're monitoring exits abruptly, this may throw in which case we just swallow the exception and exit gracefully.
                    }
                    // We may time out if the process ended before we sent StopTracing command. We can just exit in that case.
                    catch (TimeoutException)
                    {
                    }
                    // On Unix platforms, we may actually get a PNSE since the pipe is gone with the process, and Runtime Client Library
                    // does not know how to distinguish a situation where there is no pipe to begin with, or where the process has exited
                    // before dotnet-counters and got rid of a pipe that once existed.
                    // Since we are catching this in StopMonitor() we know that the pipe once existed (otherwise the exception would've 
                    // been thrown in StartMonitor directly)
                    catch (PlatformNotSupportedException)
                    {
                    }
                }

                using var _ = cancellationToken.Register(() => StopSession());

                try
                {
                    var source = new EventPipeEventSource(session.EventStream);
                    var activities = new Dictionary<string, ActivityItem>();

                    source.Dynamic.All += traceEvent =>
                    {
                        try
                        {
                            // Uncomment to debug the diagnostics source event source
                            //if (traceEvent.EventName == "Message")
                            //{
                            //    _logger.LogTrace("[" + replicaName + "]:" + traceEvent.PayloadValue(0));
                            //}
                            //// Distributed tracing
                            // else 
                            if (traceEvent.EventName == "Activity1Start/Start")
                            {
                                var listenerEventName = (string)traceEvent.PayloadByName("EventName");

                                if (traceEvent.PayloadByName("Arguments") is IDictionary<string, object>[] arguments)
                                {
                                    string activityId = null;
                                    string parentId = null;
                                    string operationName = null;
                                    string httpMethod = null;
                                    string path = null;
                                    DateTime startTime = default;
                                    ActivityIdFormat idFormat = default;

                                    foreach (var arg in arguments)
                                    {
                                        var key = (string)arg["Key"];
                                        var value = (string)arg["Value"];

                                        if (key == "ActivityId") activityId = value;
                                        else if (key == "ActivityParentId") parentId = value;
                                        else if (key == "ActivityOperationName") operationName = value;
                                        else if (key == "Method") httpMethod = value;
                                        else if (key == "Path") path = value;
                                        else if (key == "ActivityStartTime") startTime = new DateTime(long.Parse(value), DateTimeKind.Utc);
                                        else if (key == "ActivityIdFormat") idFormat = Enum.Parse<ActivityIdFormat>(value);
                                    }

                                    if (string.IsNullOrEmpty(activityId))
                                    {
                                        // Not a 3.1 application (we can detect this earlier)
                                        return;
                                    }

                                    // REVIEW: We need to create the Span with the specific trace and span id that came from remote process
                                    // but OpenTelemetry doesn't currently have an easy way to do that so this only works for a single
                                    // process as we're making new ids instead of using the ones in the application.
                                    //if (idFormat == ActivityIdFormat.Hierarchical)
                                    //{
                                    //    // We need W3C to make it work
                                    //    return;
                                    //}

                                    // This is what open telemetry currently does
                                    // https://github.com/open-telemetry/opentelemetry-dotnet/blob/4ba732af062ddc2759c02aebbc91335aaa3f7173/src/OpenTelemetry.Collector.AspNetCore/Implementation/HttpInListener.cs#L65-L92

                                    ISpan parentSpan = null;

                                    if (!string.IsNullOrEmpty(parentId) &&
                                       activities.TryGetValue(parentId, out var parentItem))
                                    {
                                        parentSpan = parentItem.Span;
                                    }

                                    var span = tracer.StartSpan(path, parentSpan, SpanKind.Server, new SpanCreationOptions { StartTimestamp = startTime });

                                    // REVIEW: Things we're unable to do
                                    // - We can't get a headers
                                    // - We can't get at features in the feature collection of the HttpContext
                                    span.PutHttpPathAttribute(path);
                                    span.PutHttpMethodAttribute(httpMethod);
                                    span.SetAttribute("service.instance", replicaName);

                                    activities[activityId] = new ActivityItem
                                    {
                                        Span = span,
                                        StartTime = startTime
                                    };
                                }
                            }
                            else if (traceEvent.EventName == "Activity1Stop/Stop")
                            {
                                var listenerEventName = (string)traceEvent.PayloadByName("EventName");

                                if (traceEvent.PayloadByName("Arguments") is IDictionary<string, object>[] arguments)
                                {
                                    string activityId = null;
                                    TimeSpan duration = default;
                                    int statusCode = 0;

                                    foreach (var arg in arguments)
                                    {
                                        var key = (string)arg["Key"];
                                        var value = (string)arg["Value"];

                                        if (key == "ActivityId") activityId = value;
                                        else if (key == "StatusCode") statusCode = int.Parse(value);
                                        else if (key == "ActivityDuration") duration = new TimeSpan(long.Parse(value));
                                    }

                                    if (string.IsNullOrEmpty(activityId))
                                    {
                                        // Not a 3.1 application (we can detect this earlier)
                                        return;
                                    }

                                    if (activities.TryGetValue(activityId, out var activityItem))
                                    {
                                        var span = activityItem.Span;

                                        span.PutHttpStatusCode(statusCode, null);

                                        span.End(activityItem.StartTime + duration);

                                        activities.Remove(activityId);
                                    }
                                }
                            }
                            else if (traceEvent.EventName == "Activity2Start/Start")
                            {
                                var listenerEventName = (string)traceEvent.PayloadByName("EventName");

                                _logger.LogDebug("[" + replicaName + "]: " + listenerEventName + " fired");
                            }
                            else if (traceEvent.EventName == "Activity2Stop/Stop")
                            {
                                var listenerEventName = (string)traceEvent.PayloadByName("EventName");

                                _logger.LogDebug("[" + replicaName + "]: " + listenerEventName + " fired");
                            }

                            // Metrics
                            else if (traceEvent.EventName.Equals("EventCounters"))
                            {
                                var payloadVal = (IDictionary<string, object>)traceEvent.PayloadValue(0);
                                var eventPayload = (IDictionary<string, object>)payloadVal["Payload"];

                                ICounterPayload payload = CounterPayload.FromPayload(eventPayload);

                                replica.Metrics[traceEvent.ProviderName + "/" + payload.Name] = payload.Value;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing counter for {ProviderName}:{EventName}", traceEvent.ProviderName, traceEvent.EventName);
                        }
                    };

                    // Logging
                    string lastFormattedMessage = "";

                    source.Dynamic.AddCallbackForProviderEvent(MicrosoftExtensionsLoggingProviderName, "MessageJson", (traceEvent) =>
                    {
                        // Level, FactoryID, LoggerName, EventID, EventName, ExceptionJson, ArgumentsJson
                        var logLevel = (LogLevel)traceEvent.PayloadByName("Level");
                        var factoryId = (int)traceEvent.PayloadByName("FactoryID");
                        var categoryName = (string)traceEvent.PayloadByName("LoggerName");
                        var eventId = (int)traceEvent.PayloadByName("EventId");
                        var eventName = (string)traceEvent.PayloadByName("EventName");
                        var exceptionJson = (string)traceEvent.PayloadByName("ExceptionJson");
                        var argsJson = (string)traceEvent.PayloadByName("ArgumentsJson");

                        // There's a bug that causes some of the columns to get mixed up
                        if (eventName.StartsWith("{"))
                        {
                            argsJson = exceptionJson;
                            exceptionJson = eventName;
                            eventName = null;
                        }

                        if (string.IsNullOrEmpty(argsJson))
                        {
                            return;
                        }

                        Exception exception = null;

                        var logger = loggerFactory.CreateLogger(categoryName);

                        // TODO: Support scopes
                        // using var scope = logger.BeginScope(scope);

                        try
                        {
                            if (exceptionJson != "{}")
                            {
                                var exceptionMessage = JsonSerializer.Deserialize<JsonElement>(exceptionJson);
                                exception = new LoggerException(exceptionMessage);
                            }

                            var message = JsonSerializer.Deserialize<JsonElement>(argsJson);
                            if (message.TryGetProperty("{OriginalFormat}", out var formatElement))
                            {
                                var formatString = formatElement.GetString();
                                var formatter = new LogValuesFormatter(formatString);
                                object[] args = new object[formatter.ValueNames.Count];
                                for (int i = 0; i < args.Length; i++)
                                {
                                    args[i] = message.GetProperty(formatter.ValueNames[i]).GetString();
                                }

                                logger.Log(logLevel, new EventId(eventId, eventName), exception, formatString, args);
                            }
                            else
                            {
                                var obj = new LogObject(message, lastFormattedMessage);
                                logger.Log(logLevel, new EventId(eventId, eventName), obj, exception, LogObject.Callback);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Error processing log entry for {ServiceName}", replicaName);
                        }
                    });

                    source.Dynamic.AddCallbackForProviderEvent(MicrosoftExtensionsLoggingProviderName, "FormattedMessage", (traceEvent) =>
                    {
                        // Level, FactoryID, LoggerName, EventID, EventName, FormattedMessage
                        var logLevel = (LogLevel)traceEvent.PayloadByName("Level");
                        var factoryId = (int)traceEvent.PayloadByName("FactoryID");
                        var categoryName = (string)traceEvent.PayloadByName("LoggerName");
                        var eventId = (int)traceEvent.PayloadByName("EventId");
                        var eventName = (string)traceEvent.PayloadByName("EventName");
                        var formattedMessage = (string)traceEvent.PayloadByName("FormattedMessage");

                        if (string.IsNullOrEmpty(formattedMessage))
                        {
                            formattedMessage = eventName;
                            eventName = "";
                        }

                        lastFormattedMessage = formattedMessage;
                    });

                    source.Process();
                }
                catch (DiagnosticsClientException ex)
                {
                    _logger.LogDebug(0, ex, "Failed to start the event pipe session");
                }
                catch (Exception)
                {
                    // This fails if stop is called or if the process dies
                }
                finally
                {
                    session?.Dispose();
                }
            }

            _logger.LogInformation("Event pipe collection completed for {ServiceName} on process id {PID}", replicaName, processId);
        }

        private class ActivityItem
        {
            public ISpan Span { get; set; }

            public DateTime StartTime { get; set; }
        }
    }
}
