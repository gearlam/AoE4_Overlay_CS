using System.IO;

namespace AoE4OverlayCS
{
    public static class LogPaths
    {
        private static readonly object _lock = new();
        private static string? _logsDirectory;

        public static string LogsDirectory
        {
            get
            {
                if (_logsDirectory == null)
                {
                    lock (_lock)
                    {
                        if (_logsDirectory == null)
                        {
                            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                            _logsDirectory = Path.Combine(baseDir, "logs");
                            if (!Directory.Exists(_logsDirectory))
                            {
                                Directory.CreateDirectory(_logsDirectory);
                            }
                        }
                    }
                }
                return _logsDirectory;
            }
        }

        public static string Get(string fileName)
        {
            return Path.Combine(LogsDirectory, fileName);
        }
    }
}
