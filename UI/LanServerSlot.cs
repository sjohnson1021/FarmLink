using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;
using StardewValley.Network;

namespace FarmLink.UI
{
    /// <summary>
    /// Menu slot wrapper for discovered LAN servers.
    /// Inherits directly from LoadGameMenu.MenuSlot for compatibility.
    /// </summary>
    public class LanServerSlot : LoadGameMenu.MenuSlot
    {
        #region Fields

        public LanServerData Server { get; private set; }

        // Cached display data (Zero-allocation rendering)
        private Texture2D _iconTexture = null!;
        private Rectangle _iconSourceRect;
        private string _formattedFarmName = null!;
        private Vector2 _hostNameSize;
        private string _formattedPlayerCount = null!;
        private string _formattedIpAddress = null!;
        private Vector2 _ipAddressSize;


        #endregion

        #region Lifecycle

        public LanServerSlot(CoopMenu menu, LanServerData server)
            : base(menu)
        {
            Server = server;
            RefreshCache();
        }

        public void Update(LanServerData newData)
        {
            Server = newData;
            RefreshCache();
        }

        private void RefreshCache()
        {
            // Cache Strings
            _formattedFarmName = $"{Server.FarmName} (LAN)";
            _formattedPlayerCount = Server.PlayerCount; // Already a string "1/4"
            _formattedIpAddress = Server.Address;

            // Measure Text
            _hostNameSize = Game1.dialogueFont.MeasureString(Server.HostName);
            _ipAddressSize = Game1.dialogueFont.MeasureString(_formattedIpAddress);

            // Resolve Icon
            ResolveIcon();
        }

        private void ResolveIcon()
        {
             // Default: Standard Farm
            _iconTexture = Game1.mouseCursors;
            _iconSourceRect = new Rectangle(0, 324, 22, 20);

            string farmTypeId = Server.FarmTypeId;

            // Vanilla Farm Types
            switch (farmTypeId)
            {
                case "0": _iconSourceRect = new Rectangle(0, 324, 22, 20); break;
                case "1": _iconSourceRect = new Rectangle(22, 324, 22, 20); break;
                case "2": _iconSourceRect = new Rectangle(44, 324, 22, 20); break;
                case "3": _iconSourceRect = new Rectangle(66, 324, 22, 20); break;
                case "4": _iconSourceRect = new Rectangle(88, 324, 22, 20); break;
                case "5": _iconSourceRect = new Rectangle(0, 345, 22, 20); break;
                case "6": _iconSourceRect = new Rectangle(22, 345, 22, 20); break;
                case "7": _iconSourceRect = new Rectangle(44, 345, 22, 20); break;
                default:
                    // Modded Farms
                    ResolveModdedIcon(farmTypeId);
                    break;
            }
        }

        private void ResolveModdedIcon(string farmTypeId)
        {
            var modFarms = DataLoader.AdditionalFarms(Game1.content);
            if (modFarms == null) return;

            foreach (var modFarm in modFarms)
            {
                if (modFarm.Id != farmTypeId && "ModFarm_" + modFarm.Id != farmTypeId) continue;

                if (modFarm.IconTexture != null)
                {
                    try
                    {
                        _iconTexture = Game1.content.Load<Texture2D>(modFarm.IconTexture);
                        _iconSourceRect = new Rectangle(0, 0, 22, 20);
                    }
                    catch
                    {
                        // Fallback to default if texture fails load
                    }
                }
                return;
            }
        }

        #endregion

        #region Slot Interface

        /// <summary>
        /// Called when player clicks this slot.
        /// </summary>
        public override void Activate()
        {
            try
            {
                string address = $"{Server.Address}:{Server.Port}";
                Client client = Game1.Multiplayer.InitClient(new LidgrenClient(address));
                IClickableMenu farmhandMenu = new FarmhandMenu(client);

                if (Game1.activeClickableMenu is TitleMenu)
                {
                    TitleMenu.subMenu = farmhandMenu;
                }
                else
                {
                    Game1.activeClickableMenu = farmhandMenu;
                }
            }
            catch (Exception ex)
            {
                ModEntry.Instance?.Monitor.Log($"LAN connection failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Draw this slot.
        /// </summary>
        public override void Draw(SpriteBatch b, int slotIndex)
        {
            // Guard clause: Ensure slot index is valid
            if (slotIndex < 0 || slotIndex >= menu.slotButtons.Count) return;

            Rectangle bounds = menu.slotButtons[slotIndex].bounds;

            // Draw components using cached data
            DrawSlotIcon(b, bounds);
            
            // Farm Name
            SpriteText.drawString(b, _formattedFarmName, bounds.X + 164, bounds.Y + 36);

            // Host Name (Right-aligned)
            Vector2 hostPos = new(
                bounds.X + bounds.Width - 160 - _hostNameSize.X,
                bounds.Y + 44
            );
            Utility.drawTextWithShadow(b, Server.HostName, Game1.dialogueFont, hostPos, Game1.textColor);

            // Player Count
            Vector2 countPos = new(bounds.X + 160, bounds.Y + 104);
            Utility.drawTextWithShadow(b, _formattedPlayerCount, Game1.dialogueFont, countPos, Game1.textColor);

            // IP Address (Right-aligned, underneath Host Name)
            Vector2 ipPos = new(
                bounds.X + bounds.Width - 160 - _ipAddressSize.X,
                bounds.Y + 104
            );
            Utility.drawTextWithShadow(b, _formattedIpAddress, Game1.dialogueFont, ipPos, Game1.textColor);
        }

        #endregion

        #region Drawing Helpers

        /// <summary>
        /// Draw the icon area (Farm Type).
        /// </summary>
        private void DrawSlotIcon(SpriteBatch b, Rectangle bounds)
        {
            // Center the 88x80 icon within the 160px wide area
            const int iconWidth = 88;
            const int iconHeight = 80;
            
            // Calculate destination rect
            int x = bounds.X + (160 - iconWidth) / 2;
            int y = bounds.Y + (bounds.Height - iconHeight) / 2;
            
            // Draw using cached texture and source rect
            b.Draw(_iconTexture, new Rectangle(x, y, iconWidth, iconHeight), _iconSourceRect, Color.White);
        }

        #endregion

        #region Helpers

        public bool MatchesServer(LanServerData other)
        {
            return Server.UniqueKey == other.UniqueKey;
        }

        #endregion
    }
}