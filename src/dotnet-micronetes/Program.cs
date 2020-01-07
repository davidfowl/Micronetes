using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Rendering;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Micronetes.Hosting;
using Micronetes.Hosting.Model;

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
            var command = new Command("new", "create a manifest")
            {
            };

            command.Handler = CommandHandler.Create<IConsole>((console) =>
            {
                File.WriteAllText("app.yaml", @"- name: app
  projectFile: app.csproj
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

            command.AddOption(new Option("--inprocess")
            {
                Required = false
            });

            command.AddOption(new Option("--k8s")
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

        private static Application ResolveApplication(string manifest)
        {
            if (string.IsNullOrEmpty(manifest))
            {
                var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.yaml");
                if (files.Length == 0)
                {
                    throw new InvalidOperationException($"No manifest found");
                }

                if (files.Length > 1)
                {
                    throw new InvalidOperationException($"Ambiguous match found {string.Join(", ", files.Select(Path.GetFileName))}");
                }

                manifest = files[0];
            }

            if (!File.Exists(manifest))
            {
                throw new InvalidOperationException($"{manifest} does not exist");
            }

            var app = Application.FromYaml(manifest);
            return app;
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
