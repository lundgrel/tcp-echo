using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TcpEcho.Shared;

namespace tcp_echo_server
{
    /// <summary>
    /// One accepted client. Runs two loops over the same stream: echoing whatever the
    /// client sends, and pushing random event_data fragments on its own jittered interval.
    /// </summary>
    internal class ServerConnection
    {
        private readonly TcpClient _client;
        private readonly ConnectionRegistry _registry;
        private readonly ConnectionStats _stats;
        private readonly ServerOptions _options;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        public ServerConnection(TcpClient client, ConnectionRegistry registry, ConnectionStats stats, ServerOptions options, ILogger logger)
        {
            _client = client;
            _registry = registry;
            _stats = stats;
            _options = options;
            _logger = logger;
        }

        public async Task RunAsync(CancellationToken shutdownToken)
        {
            string closeReason = "unknown";

            using (var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken))
            {
                // A pending socket read does not observe the token on .NET Framework;
                // disposing the client is what actually unblocks it.
                using (connectionCts.Token.Register(() => SafeClose()))
                {
                    try
                    {
                        _client.NoDelay = true;
                        NetworkStream stream = _client.GetStream();

                        Task<string> echoLoop = EchoLoopAsync(stream, connectionCts.Token);
                        Task<string> pushLoop = PushLoopAsync(stream, connectionCts.Token);

                        Task<string> first = await Task.WhenAny(echoLoop, pushLoop).ConfigureAwait(false);
                        closeReason = await first.ConfigureAwait(false);

                        connectionCts.Cancel();
                        await Task.WhenAll(
                            Swallow(echoLoop),
                            Swallow(pushLoop)).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        closeReason = Describe(ex);
                    }
                }
            }

            SafeClose();
            _registry.Close(_stats, closeReason);
            _logger.LogInformation(
                "Connection #{Id} from {Remote} closed after {Duration} ({Reason}); tx {MsgTx} msgs/{BytesTx} B, rx {MsgRx} msgs/{BytesRx} B.",
                _stats.Id, _stats.RemoteEndPoint, _stats.Duration, closeReason,
                _stats.MessagesSent, _stats.BytesSent, _stats.MessagesReceived, _stats.BytesReceived);
        }

        /// <summary>Reads client messages and echoes the identical payload back.</summary>
        private async Task<string> EchoLoopAsync(NetworkStream stream, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    Message message = await Framing.ReadMessageAsync(stream, _options.MaxFrameBytes, ct).ConfigureAwait(false);
                    if (message == null)
                        return "peer closed";

                    _stats.RecordReceived(message.WireBytes);
                    _logger.LogDebug("Connection #{Id} received {Bytes} B: {Payload}", _stats.Id, message.WireBytes, message.Payload);

                    int sent = await Framing.WriteMessageAsync(stream, MessageType.Echo, message.Payload, _writeLock, ct).ConfigureAwait(false);
                    _stats.RecordSent(sent);
                }
                return "shutdown";
            }
            catch (Exception ex)
            {
                return Describe(ex);
            }
        }

        /// <summary>Pushes a random fragment of event_data at a jittered interval.</summary>
        private async Task<string> PushLoopAsync(NetworkStream stream, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(Jitter.Next(_options.PushInterval, _options.JitterFraction), ct).ConfigureAwait(false);

                    string fragment = Jitter.NextFragment(EventData.Lines);
                    int sent = await Framing.WriteMessageAsync(stream, MessageType.EventPush, fragment, _writeLock, ct).ConfigureAwait(false);
                    _stats.RecordSent(sent);
                    _logger.LogDebug("Connection #{Id} pushed event fragment ({Bytes} B).", _stats.Id, sent);
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

        private void SafeClose()
        {
            try
            {
                _client.Close();
            }
            catch (Exception)
            {
                // Already gone.
            }
        }
    }
}
