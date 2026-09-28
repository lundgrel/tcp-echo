using System;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TcpEcho.Shared;

namespace tcp_echo_client
{
    /// <summary>
    /// One connection attempt and its lifetime: sends messages on a jittered interval and
    /// drains whatever the server sends back (echoes and event pushes), counting both.
    /// </summary>
    internal class ClientSession
    {
        private readonly ClientOptions _options;
        private readonly ConnectionRegistry _registry;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private int _sequence;

        public ClientSession(ClientOptions options, ConnectionRegistry registry, ILogger logger)
        {
            _options = options;
            _registry = registry;
            _logger = logger;
        }

        /// <summary>
        /// Connects and stays connected until the link drops or shutdown is requested.
        /// Returns the reason the connection ended; stats is null when the connect failed.
        /// </summary>
        public async Task<string> RunAsync(CancellationToken shutdownToken)
        {
            string target = _options.Host + ":" + _options.Port;
            var client = new TcpClient();
            ConnectionStats stats = null;
            string closeReason;

            try
            {
                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken))
                using (connectCts.Token.Register(() => SafeClose(client)))
                {
                    // ConnectAsync has no cancellable overload on .NET Framework; closing the
                    // socket from the registration is what aborts a pending connect.
                    await client.ConnectAsync(_options.Host, _options.Port).ConfigureAwait(false);
                }

                shutdownToken.ThrowIfCancellationRequested();

                client.NoDelay = true;
                stats = _registry.Open(SafeRemote(client, target));
                _logger.LogInformation("Connection #{Id} established to {Remote}.", stats.Id, stats.RemoteEndPoint);

                closeReason = await PumpAsync(client, stats, shutdownToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                closeReason = Describe(ex);
                if (stats == null)
                {
                    SafeClose(client);
                    _logger.LogWarning("Connect to {Target} failed: {Reason}.", target, closeReason);
                    return closeReason;
                }
            }

            SafeClose(client);
            _registry.Close(stats, closeReason);
            _logger.LogWarning(
                "Connection #{Id} to {Remote} closed after {Duration} ({Reason}); tx {MsgTx} msgs/{BytesTx} B, rx {MsgRx} msgs/{BytesRx} B.",
                stats.Id, stats.RemoteEndPoint, stats.Duration, closeReason,
                stats.MessagesSent, stats.BytesSent, stats.MessagesReceived, stats.BytesReceived);
            return closeReason;
        }

        private async Task<string> PumpAsync(TcpClient client, ConnectionStats stats, CancellationToken shutdownToken)
        {
            using (var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken))
            using (sessionCts.Token.Register(() => SafeClose(client)))
            {
                NetworkStream stream = client.GetStream();

                Task<string> sendLoop = SendLoopAsync(stream, stats, sessionCts.Token);
                Task<string> receiveLoop = ReceiveLoopAsync(stream, stats, sessionCts.Token);

                Task<string> first = await Task.WhenAny(sendLoop, receiveLoop).ConfigureAwait(false);
                string reason = await first.ConfigureAwait(false);

                sessionCts.Cancel();
                await Task.WhenAll(Swallow(sendLoop), Swallow(receiveLoop)).ConfigureAwait(false);
                return reason;
            }
        }

        private async Task<string> SendLoopAsync(NetworkStream stream, ConnectionStats stats, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(Jitter.Next(_options.SendInterval, _options.JitterFraction), ct).ConfigureAwait(false);

                    string payload = string.Format(
                        CultureInfo.InvariantCulture,
                        "msg #{0} from {1} @ {2:yyyy-MM-ddTHH:mm:ss.fffZ}",
                        ++_sequence, Environment.MachineName, DateTime.UtcNow);

                    int sent = await Framing.WriteMessageAsync(stream, MessageType.ClientMessage, payload, _writeLock, ct).ConfigureAwait(false);
                    stats.RecordSent(sent);
                    _logger.LogDebug("Connection #{Id} sent {Bytes} B: {Payload}", stats.Id, sent, payload);
                }
                return "shutdown";
            }
            catch (Exception ex)
            {
                return Describe(ex);
            }
        }

        /// <summary>Counts everything the server sends; the payloads themselves are discarded.</summary>
        private async Task<string> ReceiveLoopAsync(NetworkStream stream, ConnectionStats stats, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    Message message = await Framing.ReadMessageAsync(stream, _options.MaxFrameBytes, ct).ConfigureAwait(false);
                    if (message == null)
                        return "peer closed";

                    stats.RecordReceived(message.WireBytes);
                    _logger.LogDebug("Connection #{Id} received {Type} ({Bytes} B).", stats.Id, message.Type, message.WireBytes);
                }
                return "shutdown";
            }
            catch (Exception ex)
            {
                return Describe(ex);
            }
        }

        private static async Task Swallow(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // The loop's own close reason has already been captured.
            }
        }

        private static string Describe(Exception ex)
        {
            if (ex is OperationCanceledException)
                return "shutdown";
            if (ex is ObjectDisposedException)
                return "socket closed";
            if (ex is EndOfStreamException)
                return "peer closed mid-frame";

            var io = ex as IOException;
            var socketEx = (io != null ? io.InnerException as SocketException : null) ?? ex as SocketException;
            if (socketEx != null)
                return "socket error: " + socketEx.SocketErrorCode;

            return ex.GetType().Name + ": " + ex.Message;
        }

        private static string SafeRemote(TcpClient client, string fallback)
        {
            try
            {
                return client.Client.RemoteEndPoint.ToString();
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private static void SafeClose(TcpClient client)
        {
            try
            {
                client.Close();
            }
            catch (Exception)
            {
                // Already gone.
            }
        }
    }
}
