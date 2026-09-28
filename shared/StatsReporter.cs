using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TcpEcho.Shared
{
    /// <summary>
    /// Prints a table of every connection - active and terminated - on a fixed interval,
    /// immediately whenever a connection is established or closed, and once on shutdown.
    /// </summary>
    public class StatsReporter
    {
        private readonly ConnectionRegistry _registry;
        private readonly ILogger _logger;
        private readonly TimeSpan _interval;
        private readonly string _role;

        /// <param name="role">SERVER or CLIENT; used as the prefix of every report heading.</param>
        public StatsReporter(ConnectionRegistry registry, ILogger logger, TimeSpan interval, string role)
        {
            _registry = registry;
            _logger = logger;
            _interval = interval;
            _role = role;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(_interval, ct).ConfigureAwait(false);
                    Report(_role + " CONNECTION STATISTICS");
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown; the final report is written by the caller.
            }
        }

        public void ReportFinal()
        {
            Report(_role + " FINAL CONNECTION STATISTICS");
        }

        /// <summary>Immediate report for a connection that has just come up.</summary>
        public void ReportConnectionOpened(ConnectionStats stats)
        {
            Report(_role + " CONNECTION #" + stats.Id + " ESTABLISHED");
        }

        /// <summary>
        /// Immediate report for a connection that has just gone away. Call it after the
        /// connection has been closed in the registry, so the row shows CLOSED and the reason.
        /// </summary>
        public void ReportConnectionClosed(ConnectionStats stats)
        {
            Report(_role + " CONNECTION #" + stats.Id + " CLOSED");
        }

        public void Report(string title)
        {
            var connections = _registry.Snapshot();
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("===== " + title + " @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " =====");

            if (connections.Count == 0)
            {
                sb.AppendLine("  (no connections yet)");
                _logger.LogInformation(sb.ToString());
                return;
            }

            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0,3} {1,-6} {2,-21} {3,-19} {4,-14} {5,8} {6,12} {7,8} {8,12}  {9}",
                "ID", "STATE", "REMOTE", "STARTED", "DURATION",
                "MSG TX", "BYTES TX", "MSG RX", "BYTES RX", "CLOSE REASON"));

            long totalMsgTx = 0, totalBytesTx = 0, totalMsgRx = 0, totalBytesRx = 0;
            int active = 0;

            foreach (var c in connections)
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  {0,3} {1,-6} {2,-21} {3,-19} {4,-14} {5,8} {6,12} {7,8} {8,12}  {9}",
                    c.Id,
                    c.IsActive ? "ACTIVE" : "CLOSED",
                    Truncate(c.RemoteEndPoint, 21),
                    c.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    FormatDuration(c.Duration),
                    c.MessagesSent,
                    c.BytesSent,
                    c.MessagesReceived,
                    c.BytesReceived,
                    c.CloseReason ?? string.Empty));

                totalMsgTx += c.MessagesSent;
                totalBytesTx += c.BytesSent;
                totalMsgRx += c.MessagesReceived;
                totalBytesRx += c.BytesReceived;
                if (c.IsActive)
                    active++;
            }

            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0,3} {1,-6} {2,-21} {3,-19} {4,-14} {5,8} {6,12} {7,8} {8,12}",
                "", "TOTAL", connections.Count + " conn", active + " active", "",
                totalMsgTx, totalBytesTx, totalMsgRx, totalBytesRx));

            _logger.LogInformation(sb.ToString());
        }

        private static string FormatDuration(TimeSpan d)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}d {1:00}:{2:00}:{3:00}",
                (int)d.TotalDays, d.Hours, d.Minutes, d.Seconds);
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Length <= max ? value : value.Substring(0, max - 1) + "~";
        }
    }
}
