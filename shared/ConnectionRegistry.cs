using System;
using System.Collections.Generic;
using System.Linq;

namespace TcpEcho.Shared
{
    /// <summary>
    /// Thread-safe store of every connection this process has seen: the active ones plus
    /// the most recent closed ones, so a client that dropped twice reports three rows.
    /// </summary>
    public class ConnectionRegistry
    {
        private readonly object _gate = new object();
        private readonly List<ConnectionStats> _connections = new List<ConnectionStats>();
        private readonly int _closedHistoryLimit;
        private int _nextId;

        public ConnectionRegistry(int closedHistoryLimit)
        {
            _closedHistoryLimit = Math.Max(0, closedHistoryLimit);
        }

        public ConnectionStats Open(string remoteEndPoint)
        {
            lock (_gate)
            {
                var stats = new ConnectionStats(++_nextId, remoteEndPoint);
                _connections.Add(stats);
                return stats;
            }
        }

        public void Close(ConnectionStats stats, string reason)
        {
            lock (_gate)
            {
                stats.MarkClosed(reason);
                Trim();
            }
        }

        /// <summary>Marks every still-open connection as closed, e.g. on shutdown.</summary>
        public void CloseAll(string reason)
        {
            lock (_gate)
            {
                foreach (var stats in _connections.Where(c => c.IsActive))
                    stats.MarkClosed(reason);
            }
        }

        public IReadOnlyList<ConnectionStats> Snapshot()
        {
            lock (_gate)
            {
                return _connections.OrderBy(c => c.Id).ToList();
            }
        }

        /// <summary>Drops the oldest closed entries once the history limit is exceeded.</summary>
        private void Trim()
        {
            int excess = _connections.Count(c => !c.IsActive) - _closedHistoryLimit;
            for (int i = 0; i < _connections.Count && excess > 0; )
            {
                if (_connections[i].IsActive)
                {
                    i++;
                    continue;
                }
                _connections.RemoveAt(i);
                excess--;
            }
        }
    }
}
