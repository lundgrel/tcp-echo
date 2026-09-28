using System;
using System.Threading;

namespace TcpEcho.Shared
{
    /// <summary>
    /// Live counters for a single TCP connection. Kept after the connection closes so the
    /// periodic report can show the reconnect history alongside the ongoing connection.
    /// </summary>
    public class ConnectionStats
    {
        private long _messagesSent;
        private long _bytesSent;
        private long _messagesReceived;
        private long _bytesReceived;

        public ConnectionStats(int id, string remoteEndPoint)
        {
            Id = id;
            RemoteEndPoint = remoteEndPoint;
            StartedUtc = DateTime.UtcNow;
        }

        public int Id { get; private set; }
        public string RemoteEndPoint { get; set; }
        public DateTime StartedUtc { get; private set; }
        public DateTime? EndedUtc { get; private set; }
        public string CloseReason { get; private set; }

        public long MessagesSent { get { return Interlocked.Read(ref _messagesSent); } }
        public long BytesSent { get { return Interlocked.Read(ref _bytesSent); } }
        public long MessagesReceived { get { return Interlocked.Read(ref _messagesReceived); } }
        public long BytesReceived { get { return Interlocked.Read(ref _bytesReceived); } }

        public bool IsActive { get { return EndedUtc == null; } }

        /// <summary>Uptime for an open connection, total lifetime for a closed one.</summary>
        public TimeSpan Duration
        {
            get { return (EndedUtc ?? DateTime.UtcNow) - StartedUtc; }
        }

        public void RecordSent(int wireBytes)
        {
            Interlocked.Increment(ref _messagesSent);
            Interlocked.Add(ref _bytesSent, wireBytes);
        }

        public void RecordReceived(int wireBytes)
        {
            Interlocked.Increment(ref _messagesReceived);
            Interlocked.Add(ref _bytesReceived, wireBytes);
        }

        internal void MarkClosed(string reason)
        {
            if (EndedUtc == null)
            {
                EndedUtc = DateTime.UtcNow;
                CloseReason = reason;
            }
        }
    }
}
