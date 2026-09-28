using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TcpEcho.Shared;

namespace tcp_echo_server
{
    internal class Program
    {
        private static readonly Dictionary<string, string> SwitchMappings = new Dictionary<string, string>
        {
            { "-p", "Port" },
            { "--port", "Port" },
            { "-a", "ListenAddress" },
            { "--address", "ListenAddress" },
            { "-i", "PushIntervalSeconds" },
            { "--interval", "PushIntervalSeconds" },
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
            IConfigurationRoot configuration = Bootstrap.BuildConfiguration("server.json", args, SwitchMappings);
            var options = new ServerOptions();
            configuration.Bind(options);

            using (ILoggerFactory loggerFactory = Bootstrap.CreateLoggerFactory(configuration))
            {
                ILogger logger = loggerFactory.CreateLogger("server");
                var registry = new ConnectionRegistry(options.ClosedHistoryLimit);
                var reporter = new StatsReporter(registry, logger, options.StatsInterval, "SERVER CONNECTION STATISTICS");

                using (CancellationTokenSourceHolder shutdown = Bootstrap.CreateShutdownToken(logger))
                {
                    TcpListener listener;
                    try
                    {
                        listener = new TcpListener(IPAddress.Parse(options.ListenAddress), options.Port);
                        listener.Start();
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Could not listen on {Address}:{Port}.", options.ListenAddress, options.Port);
                        return 1;
                    }

                    logger.LogInformation(
                        "Listening on {Address}:{Port}. Event pushes every {Push}s +/-{Jitter:P0}, statistics every {Stats}s.",
                        options.ListenAddress, options.Port, options.PushIntervalSeconds, options.JitterFraction, options.StatsIntervalSeconds);

                    Task statsTask = reporter.RunAsync(shutdown.Token);
                    await AcceptLoopAsync(listener, registry, options, loggerFactory, logger, shutdown.Token).ConfigureAwait(false);
                    await statsTask.ConfigureAwait(false);

                    registry.CloseAll("server shutdown");
                    reporter.Report("SERVER FINAL CONNECTION STATISTICS");
                }

                logger.LogInformation("Server stopped.");
            }

            return 0;
        }

        private static async Task AcceptLoopAsync(
            TcpListener listener,
            ConnectionRegistry registry,
            ServerOptions options,
            ILoggerFactory loggerFactory,
            ILogger logger,
            CancellationToken ct)
        {
            // AcceptTcpClientAsync has no cancellable overload here; stopping the listener
            // is what makes the pending accept complete.
            using (ct.Register(() => { try { listener.Stop(); } catch { } }))
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }
                    catch (SocketException ex)
                    {
                        if (ct.IsCancellationRequested)
                            break;
                        logger.LogWarning("Accept failed: {Error}.", ex.SocketErrorCode);
                        continue;
                    }

                    string remote = SafeRemote(client);
                    ConnectionStats stats = registry.Open(remote);
                    logger.LogInformation("Connection #{Id} accepted from {Remote}.", stats.Id, remote);

                    var connection = new ServerConnection(client, registry, stats, options, loggerFactory.CreateLogger("server.conn"));

                    // Detached on purpose: one client's failure must not touch the accept loop.
                    Task ignored = connection.RunAsync(ct);
                }
            }
        }

        private static string SafeRemote(TcpClient client)
        {
            try
            {
                return client.Client.RemoteEndPoint.ToString();
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
