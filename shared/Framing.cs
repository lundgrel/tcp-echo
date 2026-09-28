using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TcpEcho.Shared
{
    /// <summary>Message kinds carried in the frame header.</summary>
    public enum MessageType : byte
    {
        ClientMessage = 1,
        Echo = 2,
        EventPush = 3,
    }

    /// <summary>A frame as read off the wire, plus how many bytes it actually cost.</summary>
    public class Message
    {
        public MessageType Type { get; set; }
        public string Payload { get; set; }
        public int WireBytes { get; set; }
    }

    /// <summary>
    /// Length-prefixed framing over a raw NetworkStream:
    /// [int32 big-endian payload length][byte message type][UTF-8 payload].
    /// </summary>
    public static class Framing
    {
        public const int HeaderBytes = 5;

        /// <summary>
        /// Writes one frame. The semaphore serialises writers, since the server sends
        /// echoes and event pushes from two different loops on the same stream.
        /// Returns the number of bytes put on the wire, header included.
        /// </summary>
        public static async Task<int> WriteMessageAsync(
            NetworkStream stream,
            MessageType type,
            string payload,
            SemaphoreSlim writeLock,
            CancellationToken ct)
        {
            byte[] body = Encoding.UTF8.GetBytes(payload ?? string.Empty);
            byte[] frame = new byte[HeaderBytes + body.Length];

            frame[0] = (byte)((body.Length >> 24) & 0xFF);
            frame[1] = (byte)((body.Length >> 16) & 0xFF);
            frame[2] = (byte)((body.Length >> 8) & 0xFF);
            frame[3] = (byte)(body.Length & 0xFF);
            frame[4] = (byte)type;
            Buffer.BlockCopy(body, 0, frame, HeaderBytes, body.Length);

            await writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await stream.WriteAsync(frame, 0, frame.Length, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                writeLock.Release();
            }

            return frame.Length;
        }

        /// <summary>
        /// Reads one frame, reassembling across partial reads.
        /// Returns null when the peer closed the connection cleanly.
        /// </summary>
        public static async Task<Message> ReadMessageAsync(
            NetworkStream stream,
            int maxFrameBytes,
            CancellationToken ct)
        {
            byte[] header = new byte[HeaderBytes];
            if (!await ReadExactlyAsync(stream, header, HeaderBytes, ct).ConfigureAwait(false))
                return null;

            int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            if (length < 0 || length > maxFrameBytes)
                throw new InvalidDataException("Frame length " + length + " is outside the allowed range (0.." + maxFrameBytes + ").");

            byte[] body = new byte[length];
            if (length > 0 && !await ReadExactlyAsync(stream, body, length, ct).ConfigureAwait(false))
                throw new EndOfStreamException("Peer closed the connection mid-frame.");

            return new Message
            {
                Type = (MessageType)header[4],
                Payload = Encoding.UTF8.GetString(body),
                WireBytes = HeaderBytes + length,
            };
        }

        /// <summary>Fills count bytes of buffer, or returns false on a clean EOF at a frame boundary.</summary>
        private static async Task<bool> ReadExactlyAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer, offset, count - offset, ct).ConfigureAwait(false);
                if (read == 0)
                {
                    if (offset == 0)
                        return false;
                    throw new EndOfStreamException("Peer closed the connection mid-frame.");
                }
                offset += read;
            }
            return true;
        }
    }
}
