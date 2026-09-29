using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.ServiceProcess;
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

        private const string ServiceName = "TcpEchoServer";

        private static int Main(string[] args)
        {
            int? handled = ServiceCommands.TryHandle(args, ServiceName, "TCP Echo Server", "Accepts TCP echo clients and pushes periodic events to them.");
            if (handled.HasValue)
                return handled.Value;

            if (!Environment.UserInteractive)
            {
                ServiceBase.Run(new EchoService(ServiceName, token => MainAsync(args, true, token)));
                return 0;
            }

            return MainAsync(args, false, CancellationToken.None).GetAwaiter().GetResult();
        }

        private static async Task<int> MainAsync(string[] args, bool asService, CancellationToken stopToken)
        {
            IConfigurationRoot configuration = Bootstrap.BuildConfiguration("server.ini", args, SwitchMappings);
            var options = new ServerOptions();
            configuration.Bind(options);

            using (ILoggerFactory loggerFactory = Bootstrap.CreateLoggerFactory(configuration, asService, "tcp-echo-server"))
            {
                ILogger logger = loggerFactory.CreateLogger("server");
                var registry = new ConnectionRegistry(options.ClosedHistoryLimit);
                var reporter = new StatsReporter(registry, logger, options.StatsInterval, "SERVER");

                using (CancellationTokenSourceHolder shutdown = Bootstrap.CreateShutdownToken(logger, stopToken))
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
                    await AcceptLoopAsync(listener, registry, reporter, options, loggerFactory, logger, shutdown.Token).ConfigureAwait(false);
                    await statsTask.ConfigureAwait(false);

                    registry.CloseAll("server shutdown");
                    reporter.ReportFinal();
                }

                logger.LogInformation("Server stopped.");
            }

            return 0;
        }

        private static async Task AcceptLoopAsync(
            TcpListener listener,
            ConnectionRegistry registry,
            StatsReporter reporter,
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
                    reporter.ReportConnectionOpened(stats);

                    var connection = new ServerConnection(client, registry, reporter, stats, options, loggerFactory.CreateLogger("server.conn"));

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
