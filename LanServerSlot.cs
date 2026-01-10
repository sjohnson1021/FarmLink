using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.GameData;
using StardewValley.Menus;
using StardewValley.Network;

namespace LANScanner.UI
{
    /// <summary>
    /// Menu slot wrapper for discovered LAN servers.
    /// Inherits directly from LoadGameMenu.MenuSlot for compatibility.
    /// </summary>
    public class LanServerSlot : LoadGameMenu.MenuSlot
    {
        #region Fields

        public LanServerData Server { get; private set; }

        #endregion

        #region Lifecycle

        public LanServerSlot(CoopMenu menu, LanServerData server)
            : base(menu)
        {
            Server = server;
        }

        public void Update(LanServerData newData)
        {
            Server = newData;
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
            // Access slotButtons via reflection from our stored menu reference
            // Access: Since we are a child of MenuSlot, we have 'protected LoadGameMenu menu'
            // But 'slotButtons' is public on LoadGameMenu, so we can likely access it directly if we cast 'menu' back to 'CoopMenu' or just use 'LoadGameMenu' reference.
            
            // Wait, LoadGameMenu.slotButtons IS public.
            
            if (slotIndex >= menu.slotButtons.Count) return;

            Rectangle bounds = menu.slotButtons[slotIndex].bounds;

            DrawSlotIcon(b, bounds);
            DrawSlotName(b, bounds);
            DrawPlayerCount(b, bounds);
            DrawHostName(b, bounds);
        }

        #endregion

        #region Drawing Helpers

        /// <summary>
        /// Draw the icon area (Farm Type).
        /// </summary>
        /// <summary>
        /// Draw the icon area (Farm Type).
        /// </summary>
        private void DrawSlotIcon(SpriteBatch b, Rectangle bounds)
        {
            // 1. Define the icon area (Left side of the slot)
            // Center the 88x80 icon within the 160px wide area
            int iconWidth = 88;
            int iconHeight = 80;
            int x = bounds.X + (160 - iconWidth) / 2;
            int y = bounds.Y + (bounds.Height - iconHeight) / 2;
            
            Rectangle destRect = new Rectangle(x, y, iconWidth, iconHeight);

            // 2. Resolve Texture and SourceRect
            Texture2D texture = Game1.mouseCursors;
            Rectangle sourceRect = new Rectangle(0, 324, 22, 20); // Default: Standard

            string farmTypeId = Server.FarmTypeId;
            
            // Map Standard types
            switch (farmTypeId)
            {
                case "0":
                    sourceRect = new Rectangle(0, 324, 22, 20);
                    break;
                case "1":
                    sourceRect = new Rectangle(22, 324, 22, 20);
                    break;
                case "2":
                    sourceRect = new Rectangle(44, 324, 22, 20);
                    break;
                case "3":
                    sourceRect = new Rectangle(66, 324, 22, 20);
                    break;
                case "4":
                    sourceRect = new Rectangle(88, 324, 22, 20);
                    break;
                case "5":
                    sourceRect = new Rectangle(0, 345, 22, 20);
                    break;
                case "6":
                    sourceRect = new Rectangle(22, 345, 22, 20);
                    break;
                case "7":
                    // Meadowlands is likely the next one in the sheet or handled differently.
                    // Based on 1.6 logic: (44, 345) would be next after Beach (22, 345).
                    sourceRect = new Rectangle(44, 345, 22, 20);
                    break;
                default:
                    // Check Modded Farms
                    bool found = false;
                    List<ModFarmType> modFarms = DataLoader.AdditionalFarms(Game1.content);
                    if (modFarms != null)
                    {
                        foreach (var modFarm in modFarms)
                        {
                            if (modFarm.Id == farmTypeId || "ModFarm_" + modFarm.Id == farmTypeId)
                            {
                                if (modFarm.IconTexture != null)
                                {
                                    try
                                    {
                                        texture = Game1.content.Load<Texture2D>(modFarm.IconTexture);
                                        sourceRect = new Rectangle(0, 0, 22, 20);
                                        found = true;
                                    }
                                    catch (Exception)
                                    {
                                        // Fallback if texture fails to load
                                    }
                                }
                                break;
                            }
                        }
                    }
                    if (!found) sourceRect = new Rectangle(0, 324, 22, 20);
                    break;
            }

            // 3. Draw the Icon
            b.Draw(texture, destRect, sourceRect, Color.White);
        }

        private void DrawSlotName(SpriteBatch b, Rectangle bounds)
        {
            string displayName = $"{Server.FarmName} (LAN)";
            SpriteText.drawString(b, displayName, bounds.X + 164, bounds.Y + 36);
        }

        private void DrawPlayerCount(SpriteBatch b, Rectangle bounds)
        {
            Vector2 position = new Vector2(bounds.X + 160, bounds.Y + 104);
            Utility.drawTextWithShadow(
                b,
                Server.PlayerCount,
                Game1.dialogueFont,
                position,
                Game1.textColor
            );
        }

        private void DrawHostName(SpriteBatch b, Rectangle bounds)
        {
            Vector2 hostNameSize = Game1.dialogueFont.MeasureString(Server.HostName);
            Vector2 position = new Vector2(
                bounds.X + bounds.Width - 160 - hostNameSize.X,
                bounds.Y + 44
            );

            Utility.drawTextWithShadow(
                b,
                Server.HostName,
                Game1.dialogueFont,
                position,
                Game1.textColor
            );
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