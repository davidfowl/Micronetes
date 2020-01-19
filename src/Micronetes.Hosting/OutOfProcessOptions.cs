using System;
using System.Collections.Generic;
using System.Linq;

namespace Micronetes.Hosting
{
    public class OutOfProcessOptions
    {
        public bool DebugMode { get; set; }
        public bool BuildProjects { get; set; }

        public static OutOfProcessOptions FromArgs(string[] args)
        {
            return new OutOfProcessOptions
            {
                BuildProjects = !args.Contains("--no-build"),
                DebugMode = args.Contains("--debug")
            };
        }
    }
}
