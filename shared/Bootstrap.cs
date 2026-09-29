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
                .AddInMemoryCollection(IniFile.Read(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, settingsFileName)))
                .AddCommandLine(args, switchMappings)
                .Build();
        }

        /// <summary>
        /// Console logging normally; when running as a service, a daily log file in
        /// LogDirectory (default: "logs" next to the exe) named after <paramref name="logFileBaseName"/>.
        /// </summary>
        public static ILoggerFactory CreateLoggerFactory(IConfiguration configuration, bool asService, string logFileBaseName)
        {
            LogLevel minimum;
            if (!Enum.TryParse(configuration["LogLevel"] ?? "Information", ignoreCase: true, result: out minimum))
                minimum = LogLevel.Information;

            return LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(minimum);
                if (asService)
                {
                    string directory = configuration["LogDirectory"] ?? "logs";
                    if (!Path.IsPathRooted(directory))
                        directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, directory);

                    int retained;
                    if (!int.TryParse(configuration["LogRetentionDays"], out retained))
                        retained = 14;

                    builder.AddProvider(new FileLoggerProvider(directory, logFileBaseName, retained));
                }
                else
                {
                    builder.AddSimpleConsole(options =>
                    {
                        options.SingleLine = false;
                        options.TimestampFormat = "HH:mm:ss.fff ";
                    });
                }
            });
        }

        /// <summary>
        /// Cancels on Ctrl+C (and Ctrl+Break) when there is a console, and when <paramref name="external"/>
        /// (the service stop request) is cancelled.
        /// </summary>
        public static CancellationTokenSourceHolder CreateShutdownToken(ILogger logger, System.Threading.CancellationToken external)
        {
            var holder = new CancellationTokenSourceHolder();
            if (external.CanBeCanceled)
                holder.Link(external);

            if (Environment.UserInteractive)
            {
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    logger.LogInformation("Shutdown requested (Ctrl+C).");
                    holder.Cancel();
                };
            }
            else
            {
                logger.LogInformation("Running as a service.");
            }

            return holder;
        }
    }

    /// <summary>Tiny wrapper so the Ctrl+C handler can cancel without capturing a disposed source.</summary>
    public class CancellationTokenSourceHolder : IDisposable
    {
        private readonly System.Threading.CancellationTokenSource _cts = new System.Threading.CancellationTokenSource();

        public System.Threading.CancellationToken Token { get { return _cts.Token; } }

        private System.Threading.CancellationTokenRegistration _linked;

        /// <summary>Cancels this holder when <paramref name="external"/> is cancelled.</summary>
        public void Link(System.Threading.CancellationToken external)
        {
            _linked = external.Register(Cancel);
        }

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
            _linked.Dispose();
            _cts.Dispose();
        }
    }
}
