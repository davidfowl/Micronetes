namespace Micronetes.Hosting.Logging
{
    using System;
    using System.IO;

    using Micronetes.Hosting.Model;

    internal class ProcessLogCollector : IDisposable
    {
        private readonly Application _application;
        private readonly Service _service;
        private FileStream _logFileStream;
        private StreamWriter _logStreamWriter;

        internal ProcessLogCollector(Application application, Service service)
        {
            this._application = application;
            this._service = service;

            if (!string.IsNullOrEmpty(service.Description.ConsoleLogDestinationPath))
            {
                this.StartLogging();
            }
        }

        public void Dispose()
        {
            if (this._logFileStream == null)
            {
                return;
            }

            lock (this._logStreamWriter)
            {
                this._logStreamWriter.Dispose();
                this._logFileStream.Dispose();
                this._logFileStream = null;
            }
        }

        private void StartLogging()
        {
            var expandedConsoleLogDestinationPath = Environment.ExpandEnvironmentVariables(this._service.Description.ConsoleLogDestinationPath);
            var fullConsoleLogDestinationPath = Path.GetFullPath(Path.Combine(this._application.ContextDirectory, expandedConsoleLogDestinationPath));

            this._logFileStream = new FileStream(fullConsoleLogDestinationPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            this._logStreamWriter = new StreamWriter(this._logFileStream);
            this._service.Logs.Subscribe(log =>
            {
                lock (this._logStreamWriter)
                {
                    if (this._logFileStream == null)
                    {
                        return;
                    }

                    this._logFileStream.Seek(0, SeekOrigin.End);
                    this._logStreamWriter.WriteLine(log);
                    this._logStreamWriter.Flush();
                }
            });
        }
    }
}
