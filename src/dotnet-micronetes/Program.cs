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
            command.Add(BrowseCommand());

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

        private static Command BrowseCommand()
        {
            var command = new Command("show", "show the status of running services");

            command.AddArgument(new Argument("resource")
            {
                Arity = ArgumentArity.ZeroOrOne
            });

            command.Handler = CommandHandler.Create<IConsole, string>(async (console, resource) =>
            {
                var client = new HttpClient();

                var url = "http://localhost:3745";
                if (!string.IsNullOrEmpty(resource))
                {
                    url += $"/api/v1/{resource}";
                }

                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    console.Out.WriteLine(await response.Content.ReadAsStringAsync());
                    return;
                }

                var value = await response.Content.ReadAsStringAsync();
                var element = JsonConvert.DeserializeObject(value);

                console.Out.WriteLine(element.ToString());
            });

            return command;
        }

        private static Command NewCommand()
        {
            var command = new Command("new", "create a manifest")
            {
            };

            command.Handler = CommandHandler.Create<IConsole>((console) =>
            {
                if (File.Exists("app.yaml"))
                {
                    console.Out.WriteLine("\"app.yaml\" already exists.");
                    return;
                }

                File.WriteAllText("app.yaml", @"- name: app
  # project: app.csproj # msbuild project path (relative to this file)
  # executable: app.exe # path to an executable (relative to this file)
  # args: --arg1=3 # arguments to pass to the process
  # replicas: 5 # number of times to launch the application
  # env: # environment variables
  # bindings: # optional array of bindings (ports, connection strings)
    # port: 8080 # number port of the binding
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

            var argument = new Argument("manifest")
            {
                Arity = ArgumentArity.ZeroOrOne
            };

            command.AddOption(new Option("--k8s")
            {
                Required = false
            });

            command.AddOption(new Option("--debug")
            {
                Required = false
            });

            command.AddArgument(argument);

            command.Handler = CommandHandler.Create<IConsole, string>((console, manifest) =>
            {
                Application app = ResolveApplication(manifest);
                return MicronetesHost.RunAsync(app, args);
            });

            return command;
        }

        private static Application ResolveApplication(string manifestPath)
        {
            if (string.IsNullOrEmpty(manifestPath))
            {
                manifestPath = ResolveManifestFromDirectory(Directory.GetCurrentDirectory());
            }
            else if (Directory.Exists(manifestPath))
            {
                manifestPath = ResolveManifestFromDirectory(Path.GetFullPath(manifestPath));
            }

            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException($"{manifestPath} does not exist");
            }

            var app = Application.FromYaml(manifestPath);
            return app;
        }

        private static string ResolveManifestFromDirectory(string basePath)
        {
            var files = Directory.GetFiles(basePath, "*.yaml");
            if (files.Length == 0)
            {
                throw new InvalidOperationException($"No manifest found");
            }

            if (files.Length > 1)
            {
                throw new InvalidOperationException($"Ambiguous match found {string.Join(", ", files.Select(Path.GetFileName))}");
            }

            return files[0];
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
