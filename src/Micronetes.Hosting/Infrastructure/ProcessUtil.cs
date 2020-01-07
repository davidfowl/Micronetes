using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Micronetes.Hosting
{
    internal static class ProcessUtil
    {
        public static ProcessResult Run(
            string filename,
            string arguments,
            TimeSpan? timeout = null,
            string workingDirectory = null,
            bool throwOnError = true,
            IDictionary<string, string> environmentVariables = null,
            Action<string> outputDataReceived = null,
            bool log = false,
            Action<int> onStart = null,
            CancellationToken cancellationToken = default)
        {
            var logWorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();

            if (log)
            {
                // Log.WriteLine($"[{logWorkingDirectory}] {filename} {arguments}");
            }

            var process = new Process()
            {
                StartInfo =
                {
                    FileName = filename,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            using (process)
            {
                if (workingDirectory != null)
                {
                    process.StartInfo.WorkingDirectory = workingDirectory;
                }

                if (environmentVariables != null)
                {
                    foreach (var kvp in environmentVariables)
                    {
                        process.StartInfo.Environment.Add(kvp);
                    }
                }

                var outputBuilder = new StringBuilder();
                process.OutputDataReceived += (_, e) =>
                {
                    if (outputDataReceived != null)
                    {
                        outputDataReceived.Invoke(e.Data);
                    }
                    else
                    {
                        outputBuilder.AppendLine(e.Data);
                    }

                    if (log)
                    {
                        // Log.WriteLine(e.Data);
                    }

                };

                var errorBuilder = new StringBuilder();
                process.ErrorDataReceived += (_, e) =>
                {
                    errorBuilder.AppendLine(e.Data);
                    //Log.WriteLine(e.Data);
                };

                process.Start();
                onStart?.Invoke(process.Id);

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var start = DateTime.UtcNow;

                while (true)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        StopProcess(process);
                        break;
                    }

                    if (timeout.HasValue && (DateTime.UtcNow - start > timeout.Value))
                    {
                        StopProcess(process);
                        break;
                    }

                    if (process.HasExited)
                    {
                        break;
                    }

                    Thread.Sleep(500);
                }

                if (throwOnError && process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Command {filename} {arguments} returned exit code {process.ExitCode}");
                }


                if (log)
                {
                    // Log.WriteLine($"Exit code: {process.ExitCode}");
                }

                return new ProcessResult(outputBuilder.ToString(), errorBuilder.ToString(), process.ExitCode);
            }
        }

        public static void StopProcess(Process process)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Mono.Unix.Native.Syscall.kill(process.Id, Mono.Unix.Native.Signum.SIGINT);

                // Tentatively invoke SIGINT
                var waitForShutdownDelay = Task.Delay(TimeSpan.FromSeconds(5));
                while (!process.HasExited && !waitForShutdownDelay.IsCompletedSuccessfully)
                {
                    Thread.Sleep(200);
                }
            }

            if (!process.HasExited)
            {
                // Log.WriteLine($"Forcing process to stop ...");
                process.CloseMainWindow();

                if (!process.HasExited)
                {
                    process.Kill();
                }

                // process.Dispose();

                do
                {
                    // Log.WriteLine($"Waiting for process {process.Id} to stop ...");

                    Thread.Sleep(1000);

                    try
                    {
                        process = Process.GetProcessById(process.Id);
                        process.Refresh();
                    }
                    catch
                    {
                        process = null;
                    }

                } while (process != null && !process.HasExited);
            }
        }
    }
}