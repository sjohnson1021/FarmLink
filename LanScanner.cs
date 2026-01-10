using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using StardewModdingAPI;

namespace FarmLink
{
    /// <summary>
    /// Non-blocking UDP listener for LAN server discovery.
    /// Runs on a background thread and raises events when servers are found.
    /// </summary>
    public class FarmLink : IDisposable
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

        public FarmLink(IMonitor monitor)
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

        /// <summary>
        /// Start listening for LAN broadcasts on the specified port.
        /// </summary>
        public void Start(int port)
        {
            if (_isRunning)
            {
                _monitor.Log("LAN Scanner already running.");
                return;
            }

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
                    Name = "FarmLink-Listener"
                };
                _listenerThread.Start();

                // Start cleanup thread
                _cleanupThread = new Thread(CleanupLoop)
                {
                    IsBackground = true,
                    Name = "FarmLink-Cleanup"
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

        #region Helpers

        private void ListenerLoop()
        {
            _monitor.Log("Listener thread started.");

            // Removed "&& _listener != null" as it's checked inside or handled by exception
            while (_isRunning && _listener != null)
            {
                try
                {
                    // Non-blocking receive with timeout
                    IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _listener.Receive(ref remoteEP);

                    if (data is { Length: > 0 })
                    {
                        _monitor.Log($"Received {data.Length} bytes from {remoteEP.Address}");
                        ProcessPacket(data, remoteEP.Address.ToString());
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    // Expected timeout, continue loop
                    // Don't log this - it's normal behavior
                }
                catch (SocketException) when (!_isRunning)
                {
                    // Socket closed during shutdown, exit gracefully
                    _monitor.Log("Socket closed during shutdown.");
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // Listener was disposed, exit gracefully
                    _monitor.Log("Listener disposed.");
                    break;
                }
                catch (Exception ex)
                {
                    _monitor.Log($"Listener error: {ex.GetType().Name} - {ex.Message}", LogLevel.Warn);
                    // Continue running unless it's a critical failure
                    if (ex is SocketException { SocketErrorCode: SocketError.NetworkDown })
                    {
                        break;
                    }
                }
            }

            _monitor.Log("Listener thread stopped.");
        }

        private void ProcessPacket(byte[] data, string senderAddress)
        {
            try
            {
                string json = Encoding.UTF8.GetString(data);
                var payload = JsonConvert.DeserializeObject<dynamic>(json);

                // Validate protocol
                if (payload?.Protocol != LanServerData.ProtocolId)
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
                    _monitor.Log($"Updated server: {server.FarmName} ({server.HostName}) at {server.Address}");
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
                _monitor.Log($"Invalid JSON packet: {ex.Message}");
            }
            catch (Exception ex)
            {
                _monitor.Log($"Packet processing error: {ex.Message}");
            }
        }

        private void CleanupLoop()
        {
            _monitor.Log("Cleanup thread started.");

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
                            _monitor.Log($"Removed stale server: {removed.FarmName} ({removed.HostName})");
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
                    _monitor.Log($"Cleanup error: {ex.Message}");
                }
            }

            _monitor.Log("Cleanup thread stopped.");
        }

        #endregion
    }
}