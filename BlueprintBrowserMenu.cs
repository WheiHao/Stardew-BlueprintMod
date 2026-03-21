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
        private Action<FileInfo> onBlueprintExport;
        private IModHelper helper;
        private Func<string, int> getTotalItemCount;
        
        private int itemsPerPage = 6;
        private int currentPage = 0;
        private ClickableTextureComponent upArrow;
        private ClickableTextureComponent downArrow;
        private ClickableComponent exportButton;
        private ClickableComponent confirmButton;
        private ClickableComponent cancelButton;

        private bool isExportMode = false;

        private Dictionary<string, BlueprintFile> blueprintCache = new Dictionary<string, BlueprintFile>();
        private BlueprintFile hoveredBlueprint = null;
        private string hoveredBlueprintName = "";
        private int hoveredBlueprintIndex = -1;
        private int selectedBlueprintIndex = -1;

        public BlueprintBrowserMenu(List<FileInfo> files, Action<FileInfo> onSelected, Action<FileInfo> onExport, IModHelper helper, Func<string, int> getTotalItemCount)
            : base(Game1.uiViewport.Width / 2 - 500, Game1.uiViewport.Height / 2 - 300, 1000, 600, true)
        {
            this.blueprintFiles = files;
            this.onBlueprintSelected = onSelected;
            this.onBlueprintExport = onExport;
            this.helper = helper;
            this.getTotalItemCount = getTotalItemCount;
            
            this.upArrow = new ClickableTextureComponent(new Rectangle(this.xPositionOnScreen + 616, this.yPositionOnScreen + 16, 44, 48), Game1.mouseCursors, new Rectangle(421, 459, 11, 12), 4f);
            this.downArrow = new ClickableTextureComponent(new Rectangle(this.xPositionOnScreen + 616, this.yPositionOnScreen + this.height - 64, 44, 48), Game1.mouseCursors, new Rectangle(421, 472, 11, 12), 4f);
            
            this.exportButton = new ClickableComponent(new Rectangle(this.xPositionOnScreen + 660, this.yPositionOnScreen + 20, 220, 36), "export");
            this.confirmButton = new ClickableComponent(new Rectangle(this.xPositionOnScreen + 660, this.yPositionOnScreen + 64, 220, 36), "confirm");
            this.cancelButton = new ClickableComponent(new Rectangle(this.xPositionOnScreen + 660, this.yPositionOnScreen + 108, 220, 36), "cancel");
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

            if (exportButton.containsPoint(x, y))
            {
                isExportMode = true;
                selectedBlueprintIndex = -1;
                Game1.addHUDMessage(new HUDMessage("导出模式：请选择一个蓝图，然后点击确认或取消", 3));
                Game1.playSound("shwip");
                return;
            }

            if (isExportMode && confirmButton.containsPoint(x, y))
            {
                if (selectedBlueprintIndex >= 0 && selectedBlueprintIndex < blueprintFiles.Count && onBlueprintExport != null)
                {
                    onBlueprintExport(blueprintFiles[selectedBlueprintIndex]);
                    Game1.addHUDMessage(new HUDMessage("蓝图已导出", 3));
                }
                else
                {
                    Game1.addHUDMessage(new HUDMessage("请先选择一个蓝图", 3));
                }
                isExportMode = false;
                selectedBlueprintIndex = -1;
                return;
            }

            if (isExportMode && cancelButton.containsPoint(x, y))
            {
                isExportMode = false;
                selectedBlueprintIndex = -1;
                Game1.addHUDMessage(new HUDMessage("已取消导出", 3));
                return;
            }

            foreach (var button in blueprintButtons)
            {
                if (button.containsPoint(x, y))
                {
                    int index = int.Parse(button.name);
                    selectedBlueprintIndex = index;
                    if (isExportMode)
                    {
                        Game1.addHUDMessage(new HUDMessage($"已选择导出蓝图：{blueprintFiles[index].Name}", 3));
                    }
                    else
                    {
                        onBlueprintSelected?.Invoke(blueprintFiles[index]);
                        Game1.playSound("select");
                        exitThisMenu();
                    }
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
                    hoveredBlueprintIndex = index;
                    if (!blueprintCache.TryGetValue(blueprintFiles[index].Name, out hoveredBlueprint))
                    {
                        hoveredBlueprint = helper.Data.ReadJsonFile<BlueprintFile>($"blueprints/{blueprintFiles[index].Name}");
                        if (hoveredBlueprint != null) blueprintCache[blueprintFiles[index].Name] = hoveredBlueprint;
                    }
                }

                if (selectedBlueprintIndex == index)
                {
                    IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 396, 15, 15), button.bounds.X - 2, button.bounds.Y - 2, button.bounds.Width + 4, button.bounds.Height + 4, Color.Yellow * 0.7f, 4f, false);
                }
            }

            // 绘制预览侧边栏 (右侧)
            int previewX = this.xPositionOnScreen + 660;
            Game1.drawDialogueBox(previewX, this.yPositionOnScreen, 340, this.height, false, true);

            // 基础导出按钮（总是可见）
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 396, 15, 15), exportButton.bounds.X, exportButton.bounds.Y, exportButton.bounds.Width, exportButton.bounds.Height, exportButton.containsPoint(Game1.getMouseX(), Game1.getMouseY()) ? Color.LightGreen : Color.Wheat, 4f, false);
            b.DrawString(Game1.smallFont, helper.Translation.Get("msg.export-blueprint"), new Vector2(exportButton.bounds.X + 10, exportButton.bounds.Y + 10), Color.Black);

            if (isExportMode)
            {
                IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 396, 15, 15), confirmButton.bounds.X, confirmButton.bounds.Y, confirmButton.bounds.Width, confirmButton.bounds.Height, confirmButton.containsPoint(Game1.getMouseX(), Game1.getMouseY()) ? Color.LightGreen : Color.Wheat, 4f, false);
                b.DrawString(Game1.smallFont, helper.Translation.Get("msg.export-confirm"), new Vector2(confirmButton.bounds.X + 10, confirmButton.bounds.Y + 10), Color.Black);

                IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 396, 15, 15), cancelButton.bounds.X, cancelButton.bounds.Y, cancelButton.bounds.Width, cancelButton.bounds.Height, cancelButton.containsPoint(Game1.getMouseX(), Game1.getMouseY()) ? Color.Salmon : Color.Wheat, 4f, false);
                b.DrawString(Game1.smallFont, helper.Translation.Get("msg.export-cancel"), new Vector2(cancelButton.bounds.X + 10, cancelButton.bounds.Y + 10), Color.Black);
            }

            BlueprintFile activeBlueprint = null;
            string activeBlueprintName = "";
            if (isExportMode && selectedBlueprintIndex >= 0 && selectedBlueprintIndex < blueprintFiles.Count)
            {
                var selectedFile = blueprintFiles[selectedBlueprintIndex];
                activeBlueprintName = selectedFile.Name.Replace(".json", "");
                if (!blueprintCache.TryGetValue(selectedFile.Name, out activeBlueprint))
                {
                    activeBlueprint = helper.Data.ReadJsonFile<BlueprintFile>($"blueprints/{selectedFile.Name}");
                    if (activeBlueprint != null) blueprintCache[selectedFile.Name] = activeBlueprint;
                }
            }
            else if (hoveredBlueprint != null)
            {
                activeBlueprint = hoveredBlueprint;
                activeBlueprintName = hoveredBlueprintName;
            }

            if (!string.IsNullOrEmpty(activeBlueprintName))
            {
                b.DrawString(Game1.dialogueFont, activeBlueprintName, new Vector2(previewX + 30, this.yPositionOnScreen + 40), Color.DarkSlateGray, 0f, Vector2.Zero, 0.6f, SpriteEffects.None, 0f);
                hoveredBlueprint = activeBlueprint; // align with existing processing
            }

            if (activeBlueprint != null)
            {
                var requirements = activeBlueprint.Items.GroupBy(i => i.ItemId).Select(g => new { ItemId = g.Key, Count = g.Count() }).ToList();
                var plantingRequirements = (activeBlueprint.PlantingPlans ?? new List<PlantingPlan>())
                    .GroupBy(plan => new { plan.SeedItemId, plan.Mode, plan.DisplayName, plan.Season })
                    .Select(g => new
                    {
                        g.Key.SeedItemId,
                        g.Key.Mode,
                        g.Key.DisplayName,
                        g.Key.Season,
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

                    // 显示种植位统计
                    int groundCount = plantingRequirements.Where(p => p.Mode == PlantingMode.Ground).Sum(p => p.Count);
                    int potCount = plantingRequirements.Where(p => p.Mode == PlantingMode.IndoorPot).Sum(p => p.Count);
                    
                    if (groundCount > 0)
                        b.DrawString(Game1.smallFont, $"{groundCount} " + helper.Translation.Get("msg.planting-mode-ground"), new Vector2(previewX + 30, yOffset), Color.DarkGreen);
                    yOffset += 25;
                    
                    if (potCount > 0)
                        b.DrawString(Game1.smallFont, $"{potCount} " + helper.Translation.Get("msg.planting-mode-pot"), new Vector2(previewX + 30, yOffset), Color.DarkGreen);
                    yOffset += 25;

                    // 检查季节兼容性
                    string currentSeason = Game1.currentSeason;
                    bool hasSeasonMismatch = plantingRequirements.Any(req => 
                        !string.IsNullOrEmpty(req.Season) && req.Season != currentSeason);
                    
                    if (hasSeasonMismatch)
                    {
                        b.DrawString(Game1.smallFont, helper.Translation.Get("msg.planting-season-warning"), new Vector2(previewX + 30, yOffset), Color.Orange);
                        yOffset += 25;
                    }

                    // 显示种子需求详情
                    foreach (var req in plantingRequirements.Take(6))
                    {
                        ParsedItemData itemData = ItemRegistry.GetData(req.SeedItemId);
                        int hasCount = getTotalItemCount(req.SeedItemId);
                        if (itemData != null)
                            b.Draw(itemData.GetTexture(), new Rectangle(previewX + 30, yOffset, 24, 24), itemData.GetSourceRect(), Color.White);

                        string modeText = helper.Translation.Get(req.Mode == PlantingMode.IndoorPot ? "msg.planting-mode-pot-short" : "msg.planting-mode-ground-short");
                        string statusText = $"{req.DisplayName} {hasCount}/{req.Count} [{modeText}]";
                        b.DrawString(Game1.smallFont, statusText, new Vector2(previewX + 60, yOffset + 2), hasCount >= req.Count ? Color.Green : Color.Red);
                        yOffset += 30;
                    }

                    if (plantingRequirements.Count > 6)
                        b.DrawString(Game1.smallFont, "...", new Vector2(previewX + 30, yOffset), Color.Gray);
                }
            }
            else
            {
                b.DrawString(Game1.smallFont, "Hover or select a blueprint to see materials.", new Vector2(previewX + 40, this.yPositionOnScreen + 200), Color.Gray * 0.8f);
            }

            if (currentPage > 0) upArrow.draw(b);
            if ((currentPage + 1) * itemsPerPage < blueprintFiles.Count) downArrow.draw(b);

            base.draw(b);
            drawMouse(b);
        }
    }
}
