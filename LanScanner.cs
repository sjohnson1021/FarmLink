using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using StardewModdingAPI;

namespace LANScanner
{
    /// <summary>
    /// Represents a discovered LAN server.
    /// </summary>
    public class LanServerData
    {
        public string Address { get; set; } = "";
        public string Protocol { get; set; } = "";
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

    /// <summary>
    /// Non-blocking UDP listener for LAN server discovery.
    /// Runs on a background thread and raises events when servers are found.
    /// </summary>
    public class LanScanner : IDisposable
    {
        #region Constants

        private const int StaleServerTimeoutSeconds = 15;
        private const int CleanupIntervalMs = 5000;

        #endregion

        #region Fields

        private readonly IMonitor _monitor;
        private UdpClient? _listener;
        private Thread? _listenerThread;
        private Thread? _cleanupThread;
        private bool _isRunning;
        private int _listenPort;

        /// <summary>
        /// Thread-safe dictionary of discovered servers.
        /// Key: UniqueKey (FarmName|HostName) to handle multi-NIC scenarios.
        /// </summary>
        private readonly ConcurrentDictionary<string, LanServerData> _discoveredServers = new();

        #endregion

        #region Events

        /// <summary>
        /// Raised when a new server is discovered.
        /// </summary>
        public event Action<LanServerData>? OnServerDiscovered;

        /// <summary>
        /// Raised when an existing server's data is updated.
        /// </summary>
        public event Action<LanServerData>? OnServerUpdated;

        /// <summary>
        /// Raised when a server is removed (timed out).
        /// </summary>
        public event Action<LanServerData>? OnServerRemoved;

        #endregion

        #region Lifecycle

        public LanScanner(IMonitor monitor)
        {
            _monitor = monitor;
        }

        /// <summary>
        /// Start listening for LAN broadcasts on the specified port.
        /// </summary>
        public void Start(int port)
        {
            if (_isRunning)
            {
                _monitor.Log("LAN Scanner already running.", LogLevel.Trace);
                return;
            }

            _listenPort = port;

            try
            {
                // Cross-platform UDP broadcast receiver setup
                _listener = new UdpClient();

                // SO_REUSEADDR: Allows multiple sockets to bind to same port
                // Works on: Windows, Linux, macOS
                _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                // SO_BROADCAST: Required for receiving broadcasts on some platforms
                // Works on: Windows, Linux, macOS
                _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);

                // Bind to all interfaces (0.0.0.0)
                // Works on: Windows, Linux, macOS
                _listener.Client.Bind(new IPEndPoint(IPAddress.Any, port));

                // Enable broadcast at UdpClient level (redundant but harmless)
                // Works on: Windows, Linux, macOS
                _listener.EnableBroadcast = true;

                _listener.Client.ReceiveTimeout = 5000;

                // Diagnostic logging (can be reduced for production)
                _monitor.Log($"LAN Scanner bound to {_listener.Client.LocalEndPoint}", LogLevel.Debug);

                _isRunning = true;

                // Start listener thread
                _listenerThread = new Thread(ListenerLoop)
                {
                    IsBackground = true,
                    Name = "LANScanner-Listener"
                };
                _listenerThread.Start();

                // Start cleanup thread
                _cleanupThread = new Thread(CleanupLoop)
                {
                    IsBackground = true,
                    Name = "LANScanner-Cleanup"
                };
                _cleanupThread.Start();

                _monitor.Log($"LAN Scanner started on port {port}.", LogLevel.Info);
            }
            catch (SocketException ex)
            {
                _monitor.Log($"Failed to start LAN Scanner on port {port}: {ex.SocketErrorCode} - {ex.Message}", LogLevel.Error);
                _isRunning = false;
                _listener?.Dispose();
                _listener = null;
            }
        }

        /// <summary>
        /// Stop listening and cleanup resources.
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;

            // Close listener to unblock ReceiveAsync
            _listener?.Close();

            // Wait for threads to exit (with timeout)
            _listenerThread?.Join(2000);
            _cleanupThread?.Join(2000);

            _listener?.Dispose();
            _listener = null;

            _discoveredServers.Clear();

            _monitor.Log("LAN Scanner stopped.", LogLevel.Info);
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Listener Loop

        private void ListenerLoop()
        {
            _monitor.Log("Listener thread started.", LogLevel.Trace);

            while (_isRunning && _listener != null)
            {
                try
                {
                    // Non-blocking receive with timeout
                    IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _listener.Receive(ref remoteEP);

                    if (remoteEP != null && data != null && data.Length > 0)
                    {
                        _monitor.Log($"Received {data.Length} bytes from {remoteEP.Address}", LogLevel.Trace);
                        ProcessPacket(data, remoteEP.Address.ToString());
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    // Expected timeout, continue loop
                    // Don't log this - it's normal behavior
                    continue;
                }
                catch (SocketException ex) when (!_isRunning)
                {
                    // Socket closed during shutdown, exit gracefully
                    _monitor.Log("Socket closed during shutdown.", LogLevel.Trace);
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // Listener was disposed, exit gracefully
                    _monitor.Log("Listener disposed.", LogLevel.Trace);
                    break;
                }
                catch (Exception ex)
                {
                    _monitor.Log($"Listener error: {ex.GetType().Name} - {ex.Message}", LogLevel.Warn);
                    // Continue running unless it's a critical failure
                    if (ex is SocketException se && se.SocketErrorCode == SocketError.NetworkDown)
                    {
                        break;
                    }
                }
            }

            _monitor.Log("Listener thread stopped.", LogLevel.Trace);
        }

        #endregion

        #region Packet Processing

        private void ProcessPacket(byte[] data, string senderAddress)
        {
            try
            {
                string json = Encoding.UTF8.GetString(data);
                var payload = JsonConvert.DeserializeObject<dynamic>(json);

                // Validate protocol
                if (payload?.Protocol != "StardewLAN")
                {
                    return;
                }

                // Extract server data
                var server = new LanServerData
                {
                    Address = senderAddress,
                    Protocol = payload.Protocol,
                    FarmName = payload.FarmName ?? "Unknown Farm",
                    HostName = payload.HostName ?? "Unknown Host",
                    PlayerCount = payload.PlayerCount ?? "?/?",
                    GameVersion = payload.GameVersion ?? "",
                    Port = payload.Port ?? 24642,
                    FarmTypeId = payload.FarmTypeId ?? "Standard",  
                    LastSeen = DateTime.UtcNow
                };

                string key = server.UniqueKey;

                // Add or update server
                if (_discoveredServers.TryGetValue(key, out var existing))
                {
                    // Update existing server
                    existing.Address = server.Address; // May change if multi-NIC
                    existing.PlayerCount = server.PlayerCount;
                    existing.LastSeen = server.LastSeen;

                    OnServerUpdated?.Invoke(existing);
                    _monitor.Log($"Updated server: {server.FarmName} ({server.HostName}) at {server.Address}", LogLevel.Trace);
                }
                else
                {
                    // New server discovered
                    _discoveredServers[key] = server;
                    OnServerDiscovered?.Invoke(server);
                    _monitor.Log($"Discovered server: {server.FarmName} ({server.HostName}) at {server.Address}:{server.Port}", LogLevel.Info);
                }
            }
            catch (JsonException ex)
            {
                _monitor.Log($"Invalid JSON packet: {ex.Message}", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                _monitor.Log($"Packet processing error: {ex.Message}", LogLevel.Trace);
            }
        }

        #endregion

        #region Cleanup Loop

        private void CleanupLoop()
        {
            _monitor.Log("Cleanup thread started.", LogLevel.Trace);

            while (_isRunning)
            {
                try
                {
                    Thread.Sleep(CleanupIntervalMs);

                    var staleThreshold = DateTime.UtcNow.AddSeconds(-StaleServerTimeoutSeconds);
                    var staleServers = _discoveredServers
                        .Where(kvp => kvp.Value.LastSeen < staleThreshold)
                        .ToList();

                    foreach (var kvp in staleServers)
                    {
                        if (_discoveredServers.TryRemove(kvp.Key, out var removed))
                        {
                            OnServerRemoved?.Invoke(removed);
                            _monitor.Log($"Removed stale server: {removed.FarmName} ({removed.HostName})", LogLevel.Trace);
                        }
                    }
                }
                catch (ThreadInterruptedException)
                {
                    // Thread interrupted during shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _monitor.Log($"Cleanup error: {ex.Message}", LogLevel.Trace);
                }
            }

            _monitor.Log("Cleanup thread stopped.", LogLevel.Trace);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Get all currently active servers.
        /// </summary>
        public IEnumerable<LanServerData> GetActiveServers()
        {
            return _discoveredServers.Values.ToList();
        }

        /// <summary>
        /// Get the count of active servers.
        /// </summary>
        public int ServerCount => _discoveredServers.Count;

        /// <summary>
        /// Check if the scanner is currently running.
        /// </summary>
        public bool IsRunning => _isRunning;

        #endregion
    }
}