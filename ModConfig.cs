namespace LANScanner
{
    public sealed class ModConfig
    {
        // 1. Constants
        /// <summary>
        /// The default interval to broadcast in seconds
        /// </summary>
        private const int DefaultBroadcastIntervalSeconds = 2;

        /// <summary>
        /// The default port the game listens on
        /// </summary>
        private const int DefaultGamePort = 24642;

        /// <summary>
        /// The default port to broadcast on
        /// </summary>
        private const int DefaultBroadcastPort = 24644;

        // 2. Fields
        /// <summary>
        /// How often to broadcast in seconds
        /// </summary>
        public int BroadcastIntervalSeconds { get; set; } = DefaultBroadcastIntervalSeconds;

        /// <summary>
        /// The port the game listens on
        /// </summary>
        public int GamePort { get; set; } = DefaultGamePort;

        /// <summary>
        /// The port to broadcast on
        /// </summary>
        public int BroadcastPort { get; set; } = DefaultBroadcastPort;

        /// <summary>
        /// Broadcast across all interfaces
        /// </summary>
        public bool BroadcastAcrossAllInterfaces { get; set; } = true;
        public string NetworkAdapterName { get; set; } = "";

        /// <summary>
        /// Enable LAN server discovery
        /// </summary>
        public bool EnableLanScanning { get; set; } = true;
    }
}
