using System;
using System.Linq;
using System.Net.NetworkInformation;
using GenericModConfigMenu;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using HarmonyLib;
using LANScanner.Patches;

namespace LANScanner
{
    public sealed class ModEntry : Mod
    {
        #region Properties

        public static ModEntry? Instance { get; private set; }
        public ModConfig Config { get; private set; } = new();

        /// <summary>
        /// Cached list of interfaces to avoid polling the OS every frame.
        /// </summary>
        public NetworkInterface[] NetworkInterfaces { get; private set; } = Array.Empty<NetworkInterface>();
        public LanScanner? LanScanner { get; private set; }
        #endregion

        #region Fields

        private ITranslationHelper? _t;
        private LanBroadcaster? _broadcaster;
        private int _ticksSinceLastBroadcast = 0;
        private Harmony? _harmony;
        #endregion

        #region Lifecycle

        // public override void Entry(IModHelper helper)
        // {
        //     Instance = this;
        //     _t = helper.Translation;
        //     Config = helper.ReadConfig<ModConfig>();

        //     // 1. Cache Interfaces immediately
        //     RefreshNetworkInterfaces();

        //     // 2. Validate Config: If no adapter is selected (first run), pick the first valid one
        //     if (string.IsNullOrEmpty(Config.NetworkAdapterName) && NetworkInterfaces.Length > 0)
        //     {
        //         Config.NetworkAdapterName = NetworkInterfaces[0].Id;
        //         Helper.WriteConfig(Config);
        //     }

        //     // 3. Initialize Broadcaster
        //     _broadcaster = new LanBroadcaster(Monitor);

        //     // 4. Initialize LanScanner
        //     LanScanner = new LanScanner(Monitor);

        //     // 5. Initialize Harmony
        //     _harmony = new Harmony(ModManifest.UniqueID);
        //     _harmony.PatchAll();

        //     // 6. Hook Events
        //     helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        //     helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
        //     helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        //     helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        // }

        public override void Entry(IModHelper helper)
        {
            Instance = this;
            _t = helper.Translation;
            Config = helper.ReadConfig<ModConfig>();

            // 1. Cache Interfaces immediately
            RefreshNetworkInterfaces();

            // 2. Validate Config
            if (string.IsNullOrEmpty(Config.NetworkAdapterName) && NetworkInterfaces.Length > 0)
            {
                Config.NetworkAdapterName = NetworkInterfaces[0].Id;
                Helper.WriteConfig(Config);
            }

            // 3. Initialize Broadcaster
            _broadcaster = new LanBroadcaster(Monitor);

            // 4. Initialize LanScanner
            LanScanner = new LanScanner(Monitor);

            // 5. Initialize Harmony
            _harmony = new Harmony(ModManifest.UniqueID);

            // **CRITICAL FIX: Initialize CoopMenu patches**
            CoopMenuPatches.Initialize(Monitor, _harmony);

            // 6. Hook Events
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        }
        private void RefreshNetworkInterfaces()
        {
            // Filter out disabled or loopback adapters immediately to keep the list clean
            NetworkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToArray();
        }

        #endregion

        #region Event Handlers

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            var configApi = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configApi != null)
            {
                RegisterConfigMenu(configApi);
            }
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (Context.IsMainPlayer)
            {
                _broadcaster?.Start();
            }
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            _broadcaster?.Stop();
        }

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !Context.IsMainPlayer || _broadcaster == null) return;

            _ticksSinceLastBroadcast++;

            if (_ticksSinceLastBroadcast >= Config.BroadcastIntervalSeconds)
            {
                _broadcaster.BroadcastBeacon();
                _ticksSinceLastBroadcast = 0;
            }
        }

        #endregion

        #region GMCM Integration

        private void RegisterConfigMenu(IGenericModConfigMenuApi api)
        {
            api.Register(
                mod: ModManifest,
                reset: () =>
                {
                    Config = new ModConfig();
                    // Re-validate default on reset
                    if (NetworkInterfaces.Length > 0) Config.NetworkAdapterName = NetworkInterfaces[0].Id;
                },
                save: () => Helper.WriteConfig(Config)
            );

            if (_t == null) return;

            api.AddNumberOption(
                mod: ModManifest,
                name: () => _t.Get("config.broadcastIntervalSeconds.name"),
                tooltip: () => _t.Get("config.broadcastIntervalSeconds.tooltip"),
                getValue: () => Config.BroadcastIntervalSeconds,
                setValue: value => Config.BroadcastIntervalSeconds = value,
                min: 1, max: 30
            );

            api.AddBoolOption(
                mod: ModManifest,
                name: () => _t.Get("config.broadcastAcrossAllInterfaces.name"),
                tooltip: () => _t.Get("config.broadcastAcrossAllInterfaces.tooltip"),
                getValue: () => Config.BroadcastAcrossAllInterfaces,
                setValue: value => Config.BroadcastAcrossAllInterfaces = value
            );

            api.AddBoolOption(
                mod: ModManifest,
                name: () => _t.Get("config.enableLanScanning.name"),
                tooltip: () => _t.Get("config.enableLanScanning.tooltip"),
                getValue: () => Config.EnableLanScanning,
                setValue: value => Config.EnableLanScanning = value
            );

            api.AddNumberOption(
                mod: ModManifest,
                name: () => _t.Get("config.gamePort.name"),
                tooltip: () => _t.Get("config.gamePort.tooltip"),
                getValue: () => Config.GamePort,
                setValue: value => Config.GamePort = Math.Clamp(value, 1, 65535)
            );

            api.AddNumberOption(
                mod: ModManifest,
                name: () => _t.Get("config.broadcastPort.name"),
                tooltip: () => _t.Get("config.broadcastPort.tooltip"),
                getValue: () => Config.BroadcastPort,
                setValue: value => Config.BroadcastPort = Math.Clamp(value, 1, 65535)
            );

            // Dynamic Dropdown for Network Adapters
            api.AddTextOption(
                mod: ModManifest,
                name: () => _t.Get("config.networkAdapterName.name"),
                tooltip: () => _t.Get("config.networkAdapterName.tooltip"),
                getValue: () => Config.NetworkAdapterName,
                setValue: value => Config.NetworkAdapterName = value,
                allowedValues: NetworkInterfaces.Select(ni => ni.Id).ToArray(),
                // This makes the UI show "Ethernet" instead of "{A12B...}"
                formatAllowedValue: (id) =>
                {
                    var adapter = NetworkInterfaces.FirstOrDefault(ni => ni.Id == id);
                    return adapter != null ? $"{adapter.Name} ({adapter.Description})" : "Unknown Interface";
                }
            );
        }

        #endregion

        #region Cleanup

        protected override void Dispose(bool disposing)
        {
            _broadcaster?.Dispose();
            LanScanner?.Dispose();
            _harmony?.UnpatchAll();
            base.Dispose(disposing);
        }
        #endregion
    }
}