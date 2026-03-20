using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using StardewValley.BellsAndWhistles;
using StardewModdingAPI;
using StardewValley.ItemTypeDefinitions;

namespace BlueprintMod
{
    public class BlueprintBrowserMenu : IClickableMenu
    {
        private List<FileInfo> blueprintFiles;
        private List<ClickableComponent> blueprintButtons = new List<ClickableComponent>();
        private Action<FileInfo> onBlueprintSelected;
        private IModHelper helper;
        private Func<string, int> getTotalItemCount;
        
        private int itemsPerPage = 6;
        private int currentPage = 0;
        private ClickableTextureComponent upArrow;
        private ClickableTextureComponent downArrow;

        private Dictionary<string, BlueprintFile> blueprintCache = new Dictionary<string, BlueprintFile>();
        private BlueprintFile hoveredBlueprint = null;
        private string hoveredBlueprintName = "";

        public BlueprintBrowserMenu(List<FileInfo> files, Action<FileInfo> onSelected, IModHelper helper, Func<string, int> getTotalItemCount)
            : base(Game1.uiViewport.Width / 2 - 500, Game1.uiViewport.Height / 2 - 300, 1000, 600, true)
        {
            this.blueprintFiles = files;
            this.onBlueprintSelected = onSelected;
            this.helper = helper;
            this.getTotalItemCount = getTotalItemCount;
            
            this.upArrow = new ClickableTextureComponent(new Rectangle(this.xPositionOnScreen + 616, this.yPositionOnScreen + 16, 44, 48), Game1.mouseCursors, new Rectangle(421, 459, 11, 12), 4f);
            this.downArrow = new ClickableTextureComponent(new Rectangle(this.xPositionOnScreen + 616, this.yPositionOnScreen + this.height - 64, 44, 48), Game1.mouseCursors, new Rectangle(421, 472, 11, 12), 4f);
            
            LayoutButtons();
        }

        private void LayoutButtons()
        {
            blueprintButtons.Clear();
            int startIdx = currentPage * itemsPerPage;
            for (int i = 0; i < itemsPerPage && (startIdx + i) < blueprintFiles.Count; i++)
            {
                blueprintButtons.Add(new ClickableComponent(new Rectangle(this.xPositionOnScreen + 32, this.yPositionOnScreen + 80 + (i * 80), 600 - 64, 70), (startIdx + i).ToString()));
            }
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);
            
            if (upArrow.containsPoint(x, y) && currentPage > 0)
            {
                currentPage--;
                Game1.playSound("shwip");
                LayoutButtons();
            }
            if (downArrow.containsPoint(x, y) && (currentPage + 1) * itemsPerPage < blueprintFiles.Count)
            {
                currentPage++;
                Game1.playSound("shwip");
                LayoutButtons();
            }

            foreach (var button in blueprintButtons)
            {
                if (button.containsPoint(x, y))
                {
                    int index = int.Parse(button.name);
                    onBlueprintSelected?.Invoke(blueprintFiles[index]);
                    Game1.playSound("select");
                    exitThisMenu();
                    break;
                }
            }
        }

        public override void draw(SpriteBatch b)
        {
            // 背景遮罩
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.4f);
            
            // 菜单背景 (左侧列表)
            Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, 650, this.height, false, true);
            
            // 标题
            SpriteText.drawStringWithScrollCenteredAt(b, helper.Translation.Get("msg.naming-default"), this.xPositionOnScreen + 325, this.yPositionOnScreen + 20);

            hoveredBlueprint = null;
            hoveredBlueprintName = "";

            // 绘制列表项
            foreach (var button in blueprintButtons)
            {
                int index = int.Parse(button.name);
                bool isHovered = button.containsPoint(Game1.getMouseX(), Game1.getMouseY());
                
                IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 396, 15, 15), button.bounds.X, button.bounds.Y, button.bounds.Width, button.bounds.Height, isHovered ? Color.Wheat : Color.White, 4f, false);
                
                string fileName = blueprintFiles[index].Name.Replace(".json", "");
                b.DrawString(Game1.dialogueFont, fileName, new Vector2(button.bounds.X + 20, button.bounds.Y + 12), Color.DarkSlateGray, 0f, Vector2.Zero, 0.7f, SpriteEffects.None, 0f);

                if (isHovered)
                {
                    hoveredBlueprintName = fileName;
                    if (!blueprintCache.TryGetValue(blueprintFiles[index].Name, out hoveredBlueprint))
                    {
                        hoveredBlueprint = helper.Data.ReadJsonFile<BlueprintFile>($"blueprints/{blueprintFiles[index].Name}");
                        if (hoveredBlueprint != null) blueprintCache[blueprintFiles[index].Name] = hoveredBlueprint;
                    }
                }
            }

            // 绘制预览侧边栏 (右侧)
            int previewX = this.xPositionOnScreen + 660;
            Game1.drawDialogueBox(previewX, this.yPositionOnScreen, 340, this.height, false, true);
            
            if (hoveredBlueprint != null)
            {
                b.DrawString(Game1.dialogueFont, hoveredBlueprintName, new Vector2(previewX + 30, this.yPositionOnScreen + 40), Color.DarkSlateGray, 0f, Vector2.Zero, 0.6f, SpriteEffects.None, 0f);
                
                var requirements = hoveredBlueprint.Items.GroupBy(i => i.ItemId).Select(g => new { ItemId = g.Key, Count = g.Count() }).ToList();
                var plantingRequirements = (hoveredBlueprint.PlantingPlans ?? new List<PlantingPlan>())
                    .GroupBy(plan => new { plan.SeedItemId, plan.Mode, plan.DisplayName })
                    .Select(g => new
                    {
                        g.Key.SeedItemId,
                        g.Key.Mode,
                        DisplayName = string.IsNullOrWhiteSpace(g.Key.DisplayName) ? g.Key.SeedItemId : g.Key.DisplayName,
                        Count = g.Count()
                    })
                    .ToList();
                int yOffset = this.yPositionOnScreen + 100;
                
                b.DrawString(Game1.smallFont, helper.Translation.Get("msg.shopping-list"), new Vector2(previewX + 30, yOffset), Color.Blue);
                yOffset += 40;

                foreach (var req in requirements.Take(12)) // 限制显示数量以防超出
                {
                    ParsedItemData itemData = ItemRegistry.GetData(req.ItemId);
                    int hasCount = getTotalItemCount(req.ItemId);
                    if (itemData != null)
                    {
                        b.Draw(itemData.GetTexture(), new Rectangle(previewX + 30, yOffset, 32, 32), itemData.GetSourceRect(), Color.White);
                        string statusText = $"{hasCount}/{req.Count}";
                        b.DrawString(Game1.smallFont, statusText, new Vector2(previewX + 70, yOffset + 4), hasCount >= req.Count ? Color.Green : Color.Red);
                        yOffset += 35;
                    }
                }
                if (requirements.Count > 12)
                {
                    b.DrawString(Game1.smallFont, "...", new Vector2(previewX + 30, yOffset), Color.Gray);
                }

                if (plantingRequirements.Count > 0)
                {
                    yOffset += 20;
                    b.DrawString(Game1.smallFont, helper.Translation.Get("msg.planting-list"), new Vector2(previewX + 30, yOffset), Color.ForestGreen);
                    yOffset += 35;

                    foreach (var req in plantingRequirements.Take(8))
                    {
                        ParsedItemData itemData = ItemRegistry.GetData(req.SeedItemId);
                        int hasCount = getTotalItemCount(req.SeedItemId);
                        if (itemData != null)
                            b.Draw(itemData.GetTexture(), new Rectangle(previewX + 30, yOffset, 32, 32), itemData.GetSourceRect(), Color.White);

                        string modeText = helper.Translation.Get(req.Mode == PlantingMode.IndoorPot ? "msg.planting-mode-pot" : "msg.planting-mode-ground");
                        string statusText = $"{req.DisplayName} {hasCount}/{req.Count} [{modeText}]";
                        b.DrawString(Game1.smallFont, statusText, new Vector2(previewX + 70, yOffset + 4), hasCount >= req.Count ? Color.Green : Color.Red);
                        yOffset += 35;
                    }

                    if (plantingRequirements.Count > 8)
                        b.DrawString(Game1.smallFont, "...", new Vector2(previewX + 30, yOffset), Color.Gray);
                }
            }
            else
            {
                b.DrawString(Game1.smallFont, "Hover over a blueprint\nto see materials.", new Vector2(previewX + 40, this.yPositionOnScreen + 200), Color.Gray * 0.8f);
            }

            if (currentPage > 0) upArrow.draw(b);
            if ((currentPage + 1) * itemsPerPage < blueprintFiles.Count) downArrow.draw(b);

            base.draw(b);
            drawMouse(b);
        }
    }
}
