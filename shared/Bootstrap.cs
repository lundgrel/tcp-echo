using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TcpEcho.Shared
{
    /// <summary>Configuration and console logging setup shared by both executables.</summary>
    public static class Bootstrap
    {
        public static IConfigurationRoot BuildConfiguration(string settingsFileName, string[] args, IDictionary<string, string> switchMappings)
        {
            return new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile(settingsFileName, optional: true, reloadOnChange: false)
                .AddCommandLine(args, switchMappings)
                .Build();
        }

        public static ILoggerFactory CreateLoggerFactory(IConfiguration configuration)
        {
            LogLevel minimum;
            if (!Enum.TryParse(configuration["LogLevel"] ?? "Information", ignoreCase: true, result: out minimum))
                minimum = LogLevel.Information;

            return LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(minimum);
                builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = false;
                    options.TimestampFormat = "HH:mm:ss.fff ";
                });
            });
        }

        /// <summary>Wires Ctrl+C (and Ctrl+Break) to a cancellation token source instead of a hard kill.</summary>
        public static CancellationTokenSourceHolder CreateShutdownToken(ILogger logger)
        {
            var holder = new CancellationTokenSourceHolder();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                logger.LogInformation("Shutdown requested (Ctrl+C).");
                holder.Cancel();
            };
            return holder;
        }
    }

    /// <summary>Tiny wrapper so the Ctrl+C handler can cancel without capturing a disposed source.</summary>
    public class CancellationTokenSourceHolder : IDisposable
    {
        private readonly System.Threading.CancellationTokenSource _cts = new System.Threading.CancellationTokenSource();

        public System.Threading.CancellationToken Token { get { return _cts.Token; } }

        public void Cancel()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose()
        {
            _cts.Dispose();
        }
    }
}
