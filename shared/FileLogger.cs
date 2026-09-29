using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TcpEcho.Shared
{
    /// <summary>Appends log lines to a daily file (name-yyyyMMdd.log) and prunes old ones.</summary>
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        private readonly object _gate = new object();
        private readonly string _directory;
        private readonly string _baseName;
        private readonly int _retainedDays;
        private StreamWriter _writer;
        private string _currentDate;

        public FileLoggerProvider(string directory, string baseName, int retainedDays)
        {
            _directory = directory;
            _baseName = baseName;
            _retainedDays = retainedDays;
            Directory.CreateDirectory(_directory);
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new FileLogger(this, categoryName);
        }

        internal void Write(string line)
        {
            lock (_gate)
            {
                try
                {
                    string date = DateTime.Now.ToString("yyyyMMdd");
                    if (_writer == null || date != _currentDate)
                        Roll(date);
                    _writer.WriteLine(line);
                }
                catch (IOException)
                {
                    // Logging must never take the service down.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private void Roll(string date)
        {
            if (_writer != null)
                _writer.Dispose();

            string path = Path.Combine(_directory, _baseName + "-" + date + ".log");
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            _currentDate = date;
            Prune();
        }

        private void Prune()
        {
            if (_retainedDays <= 0)
                return;

            DateTime cutoff = DateTime.Now.AddDays(-_retainedDays);
            try
            {
                foreach (string file in Directory.GetFiles(_directory, _baseName + "-*.log"))
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_writer != null)
                    _writer.Dispose();
                _writer = null;
            }
        }

        private sealed class FileLogger : ILogger
        {
            private readonly FileLoggerProvider _provider;
            private readonly string _category;

            public FileLogger(FileLoggerProvider provider, string category)
            {
                _provider = provider;
                _category = category;
            }

            public IDisposable BeginScope<TState>(TState state)
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel != LogLevel.None;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel))
                    return;

                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.Append(" [").Append(ShortLevel(logLevel)).Append("] ");
                sb.Append(_category).Append(": ");
                sb.Append(formatter(state, exception));
                if (exception != null)
                    sb.AppendLine().Append(exception);
                _provider.Write(sb.ToString());
            }

            private static string ShortLevel(LogLevel level)
            {
                switch (level)
                {
                    case LogLevel.Trace: return "TRC";
                    case LogLevel.Debug: return "DBG";
                    case LogLevel.Information: return "INF";
                    case LogLevel.Warning: return "WRN";
                    case LogLevel.Error: return "ERR";
                    default: return "CRT";
                }
            }
        }
    }
}
