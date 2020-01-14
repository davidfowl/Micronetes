using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Rendering;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Micronetes.Hosting;
using Micronetes.Hosting.Model;
using Newtonsoft.Json;

namespace Micronetes.Host
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var command = new RootCommand();

            command.Add(RunCommand(args));
            command.Add(NewCommand());

            command.Description = "Process manager and orchestrator for microservices.";

            var builder = new CommandLineBuilder(command);
            builder.UseHelp();
            builder.UseVersionOption();
            builder.UseDebugDirective();
            builder.UseParseErrorReporting();
            builder.ParseResponseFileAs(ResponseFileHandling.ParseArgsAsSpaceSeparated);
            builder.UsePrefixes(new[] { "-", "--", }); // disable garbage windows conventions

            builder.CancelOnProcessTermination();
            builder.UseExceptionHandler(HandleException);

            // Allow fancy drawing.
            builder.UseAnsiTerminalWhenAvailable();

            var parser = builder.Build();
            return await parser.InvokeAsync(args);
        }

        private static Command NewCommand()
        {
            var command = new Command("new", "create a yaml manifest")
            {
            };

            command.Handler = CommandHandler.Create<IConsole>((console) =>
            {
                if (File.Exists("m8s.yaml"))
                {
                    console.Out.WriteLine("\"m8s.yaml\" already exists.");
                    return;
                }

                File.WriteAllText("m8s.yaml", @"- name: app
  # project: app.csproj # msbuild project path (relative to this file)
  # executable: app.exe # path to an executable (relative to this file)
  # args: --arg1=3 # arguments to pass to the process
  # replicas: 5 # number of times to launch the application
  # env: # environment variables
  # bindings: # optional array of bindings (ports, connection strings)
    # - port: 8080 # number port of the binding
");
                console.Out.WriteLine("Created \"app.yaml\"");
            });

            return command;
        }

        private static Command RunCommand(string[] args)
        {
            var command = new Command("run", "run the application")
            {
            };

            var argument = new Argument("path")
            {
                Description = "A file or directory to execute. Supports a project files, solution files or a yaml manifest.",
                Arity = ArgumentArity.ZeroOrOne
            };

            command.AddOption(new Option("--port")
            {
                Description = "The port to run control plane on.",
                Argument = new Argument<int>("port"),
                Required = false
            });

            command.AddOption(new Option("--elastic")
            {
                Description = "Elasticsearch URL. Write structured application logs to Elasticsearch.",
                Argument = new Argument<string>("elastic"),
                Required = false
            });

            command.AddOption(new Option("--appinsights")
            {
                Description = "ApplicationInsights instrumentation key. Write structured application logs to ApplicationInsights.",
                Argument = new Argument<string>("instrumenation-key"),
                Required = false
            });

            command.AddOption(new Option("--debug")
            {
                Description = "Wait for debugger attach in all services.",
                Required = false
            });

            command.AddArgument(argument);

            command.Handler = CommandHandler.Create<IConsole, string>((console, path) =>
            {
                Application app = ResolveApplication(path);
                return MicronetesHost.RunAsync(app, args);
            });

            return command;
        }

        private static Application ResolveApplication(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                path = ResolveFileFromDirectory(Directory.GetCurrentDirectory());
            }
            else if (Directory.Exists(path))
            {
                path = ResolveFileFromDirectory(Path.GetFullPath(path));
            }

            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"{path} does not exist");
            }

            switch (Path.GetExtension(path).ToLower())
            {
                case ".yaml":
                case ".yml":
                    return Application.FromYaml(path);
                case ".csproj":
                case ".fsproj":
                    return Application.FromProject(path);
                case ".sln":
                    return Application.FromSolution(path);
                default:
                    throw new NotSupportedException($"{path} not supported");
            }
        }

        private static string ResolveFileFromDirectory(string basePath)
        {
            var formats = new[] { "m8s.yaml", "m8s.yml", "*.csproj", "*.fsproj", "*.sln" };

            foreach (var format in formats)
            {
                var files = Directory.GetFiles(basePath, format);
                if (files.Length == 0)
                {
                    continue;
                }

                if (files.Length > 1)
                {
                    throw new InvalidOperationException($"Ambiguous match found {string.Join(", ", files.Select(Path.GetFileName))}");
                }

                return files[0];
            }

            throw new InvalidOperationException($"None of the supported files were found (m8s.yaml, .csproj, .fsproj, .sln)");
        }

        private static void HandleException(Exception exception, InvocationContext context)
        {
            // context.Console.ResetTerminalForegroundColor();
            // context.Console.SetTerminalForegroundColor(ConsoleColor.Red);

            if (exception is OperationCanceledException)
            {
                context.Console.Error.WriteLine("operation canceled.");
            }
            else if (exception is TargetInvocationException tae && tae.InnerException is InvalidOperationException e)
            {
                context.Console.Error.WriteLine(e.Message);
            }
            //else if (exception is CommandException command)
            //{
            //    context.Console.Error.WriteLine($"{context.ParseResult.CommandResult.Name} failed:");
            //    context.Console.Error.WriteLine($"\t{command.Message}");
            //}
            else
            {
                context.Console.Error.WriteLine("unhandled exception: ");
                context.Console.Error.WriteLine(exception.ToString());
            }

            // context.Console.ResetTerminalForegroundColor();

            context.ResultCode = 1;
        }
    }
}
