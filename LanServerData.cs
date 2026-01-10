namespace FarmLink
{
    /// <summary>
    /// Represents a discovered LAN server.
    /// </summary>
    public class LanServerData
    {
        public const string ProtocolId = "StardewLAN";
        public string Address { get; set; } = "";
        public string Protocol { get; set; } = ProtocolId;
        public string FarmName { get; set; } = "";
        public string HostName { get; set; } = "";
        public string PlayerCount { get; set; } = "";
        public string GameVersion { get; set; } = "";
        public int Port { get; set; }
        public string FarmTypeId { get; set; } = "Standard";
        public DateTime LastSeen { get; set; }

        /// <summary>
        /// Unique key for deduplication: FarmName + HostName
        /// (Multiple NICs may report same server from different IPs)
        /// </summary>
        public string UniqueKey => $"{FarmName}|{HostName}";
    }
}
