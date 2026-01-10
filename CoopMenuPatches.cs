using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley.Menus;
using LANScanner.UI;

namespace LANScanner.Patches
{
    /// <summary>
    /// Harmony patches for integrating LAN server discovery into CoopMenu.
    /// </summary>
    internal class CoopMenuPatches
    {
        #region Fields

        private static IMonitor? _monitor;

        // Dictionary to track which CoopMenu instances we've already patched
        private static readonly Dictionary<object, bool> PatchedMenus = new();

        // Store slots per menu instance to allow cleanup
        private static readonly Dictionary<object, List<LanServerSlot>> MenuSlots = new();

        #endregion

        #region Initialization

        public static void Initialize(IMonitor monitor, Harmony harmony)
        {
            _monitor = monitor;

            try
            {
                // Patch the saveFileScanComplete method (where friend lobbies are initialized)
                harmony.Patch(
                    original: AccessTools.Method(typeof(CoopMenu), "saveFileScanComplete"),
                    postfix: new HarmonyMethod(typeof(CoopMenuPatches), nameof(Postfix_SaveFileScanComplete))
                );

                // Patch the Dispose method for cleanup
                harmony.Patch(
                    original: AccessTools.Method(typeof(LoadGameMenu), "Dispose"),
                    prefix: new HarmonyMethod(typeof(CoopMenuPatches), nameof(Prefix_Dispose))
                );

                _monitor.Log("CoopMenu patches applied successfully.", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                _monitor.Log($"Failed to apply CoopMenu patches: {ex}", LogLevel.Error);
            }
        }

        #endregion

        #region Patches

        /// <summary>
        /// Postfix for CoopMenu.saveFileScanComplete()
        /// This is called after save files are scanned and friend lobbies are set up.
        /// </summary>
        private static void Postfix_SaveFileScanComplete(CoopMenu __instance)
        {
            try
            {
                // Guard clauses
                if (!ShouldEnableLanScanning(__instance))
                {
                    return;
                }

                // Prevent double-patching the same menu instance
                if (PatchedMenus.ContainsKey(__instance))
                {
                    return;
                }

                PatchedMenus[__instance] = true;
                MenuSlots[__instance] = new List<LanServerSlot>();

                // Start LAN scanner
                var scanner = ModEntry.Instance?.LanScanner;
                if (scanner == null)
                {
                    _monitor?.Log("LAN Scanner not initialized.", LogLevel.Warn);
                    return;
                }

                scanner.Start(ModEntry.Instance.Config.BroadcastPort);

                // Subscribe to scanner events
                scanner.OnServerDiscovered += (server) => OnServerDiscovered(__instance, server);
                scanner.OnServerUpdated += (server) => OnServerUpdated(__instance, server);
                scanner.OnServerRemoved += (server) => OnServerRemoved(__instance, server);

                // Populate already known servers (Fix for persistence issue)
                var existingServers = scanner.GetActiveServers();
                foreach (var server in existingServers)
                {
                    OnServerDiscovered(__instance, server);
                }

                _monitor?.Log("LAN scanning enabled for CoopMenu.", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                _monitor?.Log($"Error in saveFileScanComplete patch: {ex}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Prefix for CoopMenu.Dispose()
        /// Clean up LAN scanner and tracking dictionaries.
        /// </summary>
        private static void Prefix_Dispose(CoopMenu __instance)
        {
            try
            {
                // Clean up tracking
                PatchedMenus.Remove(__instance);
                MenuSlots.Remove(__instance);

                // Stop scanner if no other menus are using it
                if (PatchedMenus.Count == 0)
                {
                    ModEntry.Instance?.LanScanner?.Stop();
                    _monitor?.Log("LAN scanner stopped (no active menus).", LogLevel.Debug);
                }
            }
            catch (Exception ex)
            {
                _monitor?.Log($"Error in Dispose patch: {ex}", LogLevel.Error);
            }
        }

        #endregion

        #region Event Handlers

        private static void OnServerDiscovered(CoopMenu menu, LanServerData server)
        {
            try
            {
                // Get the menuSlots field via reflection
                var menuSlotsField = AccessTools.Field(typeof(LoadGameMenu), "menuSlots");
                if (menuSlotsField == null)
                {
                    _monitor?.Log("Could not find menuSlots field.", LogLevel.Error);
                    return;
                }

                var menuSlotsList = menuSlotsField.GetValue(menu) as System.Collections.IList;
                if (menuSlotsList == null)
                {
                    _monitor?.Log("menuSlots is null or not a list.", LogLevel.Error);
                    return;
                }

                // Create new slot
                var slot = new LanServerSlot(menu, server);

                // Track this slot
                if (!MenuSlots.ContainsKey(menu))
                {
                    MenuSlots[menu] = new List<LanServerSlot>();
                }
                MenuSlots[menu].Add(slot);

                // Add to menu's slot list
                menuSlotsList.Add(slot);

                // Update UI
                UpdateMenuUI(menu);

                _monitor?.Log($"Added LAN server to menu: {server.FarmName}", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                _monitor?.Log($"Error adding server to menu: {ex}", LogLevel.Error);
            }
        }

        private static void OnServerUpdated(CoopMenu menu, LanServerData server)
        {
            try
            {
                // Find existing slot and update it
                if (!MenuSlots.TryGetValue(menu, out var slots))
                {
                    return;
                }

                var existingSlot = slots.Find(s => s.MatchesServer(server));
                existingSlot?.Update(server);

                _monitor?.Log($"Updated LAN server in menu: {server.FarmName}", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                _monitor?.Log($"Error updating server in menu: {ex}", LogLevel.Error);
            }
        }

        private static void OnServerRemoved(CoopMenu menu, LanServerData server)
        {
            try
            {
                // Get the menuSlots field
                var menuSlotsField = AccessTools.Field(typeof(LoadGameMenu), "menuSlots");
                if (menuSlotsField == null) return;

                var menuSlotsList = menuSlotsField.GetValue(menu) as System.Collections.IList;
                if (menuSlotsList == null) return;

                // Find and remove slot
                if (!MenuSlots.TryGetValue(menu, out var slots))
                {
                    return;
                }

                var slotToRemove = slots.Find(s => s.MatchesServer(server));
                if (slotToRemove != null)
                {
                    slots.Remove(slotToRemove);
                    menuSlotsList.Remove(slotToRemove);

                    // Update UI
                    UpdateMenuUI(menu);

                    _monitor?.Log($"Removed stale LAN server from menu: {server.FarmName}", LogLevel.Trace);
                }
            }
            catch (Exception ex)
            {
                _monitor?.Log($"Error removing server from menu: {ex}", LogLevel.Error);
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Check if LAN scanning should be enabled for this menu instance.
        /// </summary>
        private static bool ShouldEnableLanScanning(CoopMenu menu)
        {
            // Config check
            if (ModEntry.Instance?.Config?.EnableLanScanning != true)
            {
                return false;
            }

            // Split-screen check (using reflection)
            var splitScreenField = AccessTools.Field(typeof(CoopMenu), "_splitScreen");
            if (splitScreenField != null)
            {
                var isSplitScreen = (bool)splitScreenField.GetValue(menu);
                if (isSplitScreen)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Update the menu's UI components after modifying slots.
        /// </summary>
        private static void UpdateMenuUI(CoopMenu menu)
        {
            try
            {
                // Call UpdateButtons() to refresh slot positions
                var updateButtonsMethod = AccessTools.Method(typeof(LoadGameMenu), "UpdateButtons");
                updateButtonsMethod?.Invoke(menu, null);

                // Call populateClickableComponentList() to refresh navigation
                var populateMethod = AccessTools.Method(typeof(LoadGameMenu), "populateClickableComponentList");
                populateMethod?.Invoke(menu, null);
            }
            catch (Exception ex)
            {
                _monitor?.Log($"Error updating menu UI: {ex}", LogLevel.Error);
            }
        }

        #endregion
    }
}