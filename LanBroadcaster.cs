using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewValley;

namespace FarmLink
{
    public class LanBroadcaster : IDisposable
    {
        #region Fields
        private readonly IMonitor _monitor;
        private bool _isActive;
        #endregion

        #region Lifecycle

        public LanBroadcaster(IMonitor monitor)
        {
            _monitor = monitor;
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Public API

        public void Start()
        {
            if (_isActive) return;
            _isActive = true;
            _monitor.Log("LAN Broadcaster started.", LogLevel.Info);
        }

        public void Stop()
        {
            if (!_isActive) return;
            _isActive = false;
            _monitor.Log("LAN Broadcaster stopped.", LogLevel.Info);
        }

        public void BroadcastBeacon()
        {
            if (!_isActive || ModEntry.Instance == null) return;
            if (!Context.IsMainPlayer || Game1.server == null) return;

            try
            {
                var payload = new
                {
                    Protocol = LanServerData.ProtocolId,
                    FarmName = Game1.player.farmName.Value ?? "Unknown Farm",
                    HostName = Game1.player.Name ?? "Unknown Host",
                    // Remember, multiplayer.playerLimit is not accessible directly usually, ensure this works or keep as is. 
                    // Keeping original logic but ensuring safety.
                    PlayerCount = $"{Game1.getOnlineFarmers().Count}/{Game1.Multiplayer.playerLimit}",
                    GameVersion = Game1.version,
                    Port = ModEntry.Instance.Config.GamePort,
                    FarmTypeId = Game1.GetFarmTypeID()
                };

                string json = JsonConvert.SerializeObject(payload);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                int port = ModEntry.Instance.Config.BroadcastPort;

                if (ModEntry.Instance.Config.BroadcastAcrossAllInterfaces)
                {
                    foreach (var ni in ModEntry.Instance.NetworkInterfaces)
                    {
                        SendToInterface(ni, bytes, port);
                    }
                }
                else
                {
                    // Find the specific selected interface
                    var selectedId = ModEntry.Instance.Config.NetworkAdapterName;
                    var selectedNi = ModEntry.Instance.NetworkInterfaces.FirstOrDefault(ni => ni.Id == selectedId);

                    if (selectedNi != null)
                    {
                        SendToInterface(selectedNi, bytes, port);
                    }
                    else
                    {
                        // Fallback if config is stale: just send to default OS route
                        SendToDefault(bytes, port);
                    }
                }

                // Explicit Loopback Broadcast (Crucial for same-machine discovery on Linux)
                SendToLoopback(bytes, port);
            }
            catch (Exception ex)
            {
                _monitor.Log($"Broadcast loop failed: {ex.Message}", LogLevel.Error);
            }
        }

        #endregion

        #region Helpers

        private void SendToInterface(NetworkInterface ni, byte[] data, int port)
        {
            // Find the IPv4 address of this adapter
            var ipProps = ni.GetIPProperties().UnicastAddresses
                .FirstOrDefault(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork);

            if (ipProps == null) return;

            try
            {
                // CORRECTED FOR LINUX:
                // Instead of binding to specific IP and sending to Global Broadcast (255.255.255.255),
                // we calculate the subnet broadcast address (e.g. 192.168.1.255).
                // This ensures packets are routed correctly even if bound to a specific interface.
                
                IPAddress broadcastIp = GetBroadcastAddress(ipProps.Address, ipProps.IPv4Mask);
                
                using var client = new UdpClient(new IPEndPoint(ipProps.Address, 0));
                client.EnableBroadcast = true;
                client.Send(data, data.Length, new IPEndPoint(broadcastIp, port));
            }
            catch (Exception ex)
            {
                // Suppress individual adapter failures (e.g. disconnected cable) but log trace
                _monitor.Log($"Failed to broadcast on interface {ni.Name}: {ex.Message}");
            }
        }

        private void SendToDefault(byte[] data, int port)
        {
            // Simple broadcast using OS default routing
            try
            {
                using var client = new UdpClient();
                client.EnableBroadcast = true;
                client.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, port));
            }
            catch (Exception ex)
            {
                _monitor.Log($"Failed to send broadcast to default interface. Exception: {ex.Message}", LogLevel.Warn);
            }
        }

        private void SendToLoopback(byte[] data, int port)
        {
            try
            {
                // Explicitly send to 127.0.0.1 from 127.0.0.1
                using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                client.Send(data, data.Length, new IPEndPoint(IPAddress.Loopback, port));
            }
            catch (Exception ex) 
            {
                // Log only if verbose, as this might fail in strict environments
                _monitor.Log($"Loopback broadcast failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Calculates the broadcast address for a given IP and Mask.
        /// Broadcast = IP | (~Mask)
        /// </summary>
        private IPAddress GetBroadcastAddress(IPAddress address, IPAddress? mask)
        {
            if (mask == null) return IPAddress.Broadcast; // No mask available, fallback to global

            byte[] ipBytes = address.GetAddressBytes();
            byte[] maskBytes = mask.GetAddressBytes();
            byte[] broadcastBytes = new byte[ipBytes.Length];

            for (int i = 0; i < ipBytes.Length; i++)
            {
                broadcastBytes[i] = (byte)(ipBytes[i] | (maskBytes[i] ^ 255));
            }

            return new IPAddress(broadcastBytes);
        }

        #endregion
    }
}