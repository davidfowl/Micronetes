using System;
using System.IO;
using System.Threading.Tasks;
using Micronetes.Hosting.Dashboard;
using Micronetes.Hosting.Model;

namespace Micronetes.Hosting
{
    public class RunState : IDisposable
    {
        private string _m8sFolderPath;
        
        public RunState(Application application)
        {
            _m8sFolderPath = Path.Join(Path.GetDirectoryName(application.Source), ".m8s");
            if (!Directory.Exists(_m8sFolderPath))
            {
                Directory.CreateDirectory(_m8sFolderPath);
            }
        }

        public async Task<(bool hasFile, string contents)> GetFile(string filename)
        {
            var fileFullPath = Path.Join(_m8sFolderPath, filename);
            if (!File.Exists(fileFullPath))
            {
                return (false, null);
            }
            
            var contents = await File.ReadAllTextAsync(fileFullPath);
            return (true, contents);
        }

        public async Task WriteFile(string filename, string contents)
        {
            var fileFullPath = Path.Join(_m8sFolderPath, filename);
            await File.WriteAllTextAsync(fileFullPath, contents);
        }

        public async Task AppendFile(string filename, string contents)
        {
            var fileFullPath = Path.Join(_m8sFolderPath, filename);
            await File.AppendAllTextAsync(fileFullPath, contents);
        }

        public void Dispose()
        {
            Directory.Delete(_m8sFolderPath, true);
        }
    }
}