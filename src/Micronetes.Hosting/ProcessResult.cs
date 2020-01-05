using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Micronetes.Hosting
{
    internal class ProcessResult
    {
        public ProcessResult(string standardOutput, string standardError, int exitCode)
        {
            StandardOutput = standardOutput;
            StandardError = standardError;
            ExitCode = exitCode;
        }

        public string StandardOutput { get; }
        public string StandardError { get; }
        public int ExitCode { get; }
    }
}
