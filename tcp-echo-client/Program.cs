using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TcpEcho.Shared;

namespace tcp_echo_client
{
    internal class Program
    {
        /// <summary>A connection that lasted at least this long resets the reconnect backoff.</summary>
        private static readonly TimeSpan StableConnectionThreshold = TimeSpan.FromSeconds(30);

        private static readonly Dictionary<string, string> SwitchMappings = new Dictionary<string, string>
        {
            { "-h", "Host" },
            { "--host", "Host" },
            { "-p", "Port" },
            { "--port", "Port" },
            { "-i", "SendIntervalSeconds" },
            { "--interval", "SendIntervalSeconds" },
            { "-s", "StatsIntervalSeconds" },
            { "--stats", "StatsIntervalSeconds" },
            { "-l", "LogLevel" },
            { "--log", "LogLevel" },
        };

        private static int Main(string[] args)
        {
            return MainAsync(args).GetAwaiter().GetResult();
        }

        private static async Task<int> MainAsync(string[] args)
        {
            IConfigurationRoot configuration = Bootstrap.BuildConfiguration("client.json", args, SwitchMappings);
            var options = new ClientOptions();
            configuration.Bind(options);

            using (ILoggerFactory loggerFactory = Bootstrap.CreateLoggerFactory(configuration))
            {
                ILogger logger = loggerFactory.CreateLogger("client");
                var registry = new ConnectionRegistry(options.ClosedHistoryLimit);
                var reporter = new StatsReporter(registry, logger, options.StatsInterval, "CLIENT");

                using (CancellationTokenSourceHolder shutdown = Bootstrap.CreateShutdownToken(logger))
                {
                    logger.LogInformation(
                        "Connecting to {Host}:{Port}. Sending every {Send}s +/-{Jitter:P0}, statistics every {Stats}s.",
                        options.Host, options.Port, options.SendIntervalSeconds, options.JitterFraction, options.StatsIntervalSeconds);

                    Task statsTask = reporter.RunAsync(shutdown.Token);
                    await SuperviseAsync(options, registry, reporter, loggerFactory, logger, shutdown.Token).ConfigureAwait(false);
                    await statsTask.ConfigureAwait(false);

                    registry.CloseAll("client shutdown");
                    reporter.ReportFinal();
                }

                logger.LogInformation("Client stopped.");
            }

            return 0;
        }

        /// <summary>
        /// Keeps exactly one connection alive: reconnects with exponential, jittered backoff
        /// whenever the current one ends. Every attempt that connects becomes its own row in
        /// the statistics table, so the history shows the terminated connections too.
        /// </summary>
        private static async Task SuperviseAsync(
            ClientOptions options,
            ConnectionRegistry registry,
            StatsReporter reporter,
            ILoggerFactory loggerFactory,
            ILogger logger,
            CancellationToken ct)
        {
            var session = new ClientSession(options, registry, reporter, loggerFactory.CreateLogger("client.conn"));
            TimeSpan backoff = options.ReconnectInitialDelay;

            while (!ct.IsCancellationRequested)
            {
                DateTime attemptStarted = DateTime.UtcNow;
                await session.RunAsync(ct).ConfigureAwait(false);

                if (ct.IsCancellationRequested)
                    break;

                if (DateTime.UtcNow - attemptStarted >= StableConnectionThreshold)
                    backoff = options.ReconnectInitialDelay;

                TimeSpan delay = Jitter.Next(backoff, options.JitterFraction);
                logger.LogInformation("Reconnecting in {Delay:0.0}s.", delay.TotalSeconds);

                try
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                double next = Math.Min(backoff.TotalSeconds * 2.0, options.ReconnectMaxDelay.TotalSeconds);
                backoff = TimeSpan.FromSeconds(next);
            }
        }
    }
}
