using System;

namespace TcpEcho.Shared
{
    /// <summary>Settings common to both ends, bound from appsettings.json / command line.</summary>
    public abstract class CommonOptions
    {
        /// <summary>Random variation applied to every interval, as a fraction (0.2 = +/-20%).</summary>
        public double JitterFraction { get; set; } = 0.2;

        /// <summary>How often the connection statistics table is printed.</summary>
        public int StatsIntervalSeconds { get; set; } = 60;

        /// <summary>Largest accepted frame payload; guards against a corrupt length prefix.</summary>
        public int MaxFrameBytes { get; set; } = 65536;

        /// <summary>How many terminated connections stay in the statistics table.</summary>
        public int ClosedHistoryLimit { get; set; } = 100;

        public TimeSpan StatsInterval { get { return TimeSpan.FromSeconds(Math.Max(1, StatsIntervalSeconds)); } }
    }

    public class ServerOptions : CommonOptions
    {
        public string ListenAddress { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 5001;

        /// <summary>Base interval between unsolicited event_data pushes to each client.</summary>
        public int PushIntervalSeconds { get; set; } = 10;

        public TimeSpan PushInterval { get { return TimeSpan.FromSeconds(Math.Max(1, PushIntervalSeconds)); } }
    }

    public class ClientOptions : CommonOptions
    {
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 5001;

        /// <summary>Base interval between messages sent to the server.</summary>
        public int SendIntervalSeconds { get; set; } = 5;

        public int ReconnectInitialDelaySeconds { get; set; } = 1;
        public int ReconnectMaxDelaySeconds { get; set; } = 30;

        public TimeSpan SendInterval { get { return TimeSpan.FromSeconds(Math.Max(1, SendIntervalSeconds)); } }
        public TimeSpan ReconnectInitialDelay { get { return TimeSpan.FromSeconds(Math.Max(1, ReconnectInitialDelaySeconds)); } }
        public TimeSpan ReconnectMaxDelay { get { return TimeSpan.FromSeconds(Math.Max(1, ReconnectMaxDelaySeconds)); } }
    }
}
