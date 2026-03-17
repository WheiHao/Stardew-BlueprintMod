using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlueprintMod
{
    public class ModEntry : Mod
    {
        private Vector2? startTile = null;
        private List<BlueprintItem> previewItems = null;
        private BlueprintMetadata currentMetadata = null;
        private bool isPreviewMode = false;
        private List<FileInfo> blueprintFiles = new List<FileInfo>();
        private int currentBlueprintIndex = 0;
        private Dictionary<Vector2, string> placedGhosts = new Dictionary<Vector2, string>();
        private bool isCreativeMode = false;
        private bool isOverwriteMode = true;

        private Vector2? pendingTile = null;

        public override void Entry(IModHelper helper)
        {
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
            helper.Events.Display.RenderedHud += OnRenderedHud;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!e.IsMultipleOf(20) || placedGhosts.Count == 0 || Game1.currentLocation == null) return;
            List<Vector2> toRemove = new List<Vector2>();
            foreach (var ghost in placedGhosts)
            {
                Vector2 tile = ghost.Key;
                if (Game1.currentLocation.Objects.TryGetValue(tile, out var obj) && obj.QualifiedItemId == ghost.Value) toRemove.Add(tile);
                else if (Game1.currentLocation.terrainFeatures.TryGetValue(tile, out var feature) && feature is StardewValley.TerrainFeatures.Flooring flooring)
                {
                    if ("(O)" + (flooring.GetData()?.ItemId ?? "") == ghost.Value) toRemove.Add(tile);
                }
            }
            foreach (var tile in toRemove) placedGhosts.Remove(tile);
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            // 如果当前有菜单或对话框打开，不处理 Mod 逻辑 (防止无限弹窗)
            if (Game1.activeClickableMenu != null) return;

            if (e.Button == SButton.Escape)
            {
                if (startTile != null)
                {
                    startTile = null;
                    Game1.addHUDMessage(new HUDMessage("已取消区域框选", 3));
                    Helper.Input.Suppress(e.Button);
                    return;
                }
                else if (isPreviewMode)
                {
                    isPreviewMode = false;
                    previewItems = null;
                    Game1.addHUDMessage(new HUDMessage("已退出蓝图预览", 3));
                    Helper.Input.Suppress(e.Button);
                    return;
                }
            }

            if (Helper.Input.IsDown(SButton.LeftControl) && (e.Button == SButton.MouseLeft || e.Button == SButton.MouseRight)) Helper.Input.Suppress(e.Button);

            if (e.Button == SButton.K)
            {
                isCreativeMode = !isCreativeMode;
                Game1.addHUDMessage(new HUDMessage($"蓝图模式: {(isCreativeMode ? "创造" : "生存")}", 3));
            }
            else if (e.Button == SButton.O)
            {
                isOverwriteMode = !isOverwriteMode;
                Game1.addHUDMessage(new HUDMessage($"覆盖模式: {(isOverwriteMode ? "开启" : "关闭")}", 3));
            }
            else if (e.Button == SButton.C && Helper.Input.IsDown(SButton.LeftControl) && Helper.Input.IsDown(SButton.LeftShift))
            {
                placedGhosts.Clear();
                Game1.playSound("trashcan");
            }
            else if (isPreviewMode)
            {
                if (e.Button == SButton.MouseLeft || e.Button == SButton.MouseRight) Helper.Input.Suppress(e.Button);
                if (e.Button == SButton.MouseLeft) HandlePlacementAttempt();
                else if (e.Button == SButton.MouseRight) { isPreviewMode = false; previewItems = null; Game1.addHUDMessage(new HUDMessage("已退出蓝图预览", 3)); }
                else if (e.Button == SButton.Left || e.Button == SButton.Right) SwitchBlueprint(e.Button == SButton.Right);
            }
            else if (!isCreativeMode && e.Button == SButton.MouseLeft && !Helper.Input.IsDown(SButton.LeftControl))
            {
                HandleGhostFilling(new Vector2((int)e.Cursor.Tile.X, (int)e.Cursor.Tile.Y));
            }
            else if (Helper.Input.IsDown(SButton.LeftControl) && (e.Button == SButton.MouseLeft || e.Button == SButton.MouseRight))
            {
                if (e.Button == SButton.MouseLeft)
                {
                    if (startTile == null) { startTile = e.Cursor.Tile; Game1.addHUDMessage(new HUDMessage("起始点已设定", 3)); }
                    else { SaveBlueprint(Game1.currentLocation, startTile.Value, e.Cursor.Tile); startTile = null; Game1.playSound("drumkit0"); }
                }
                else if (e.Button == SButton.MouseRight && startTile != null)
                {
                    startTile = null;
                    Game1.addHUDMessage(new HUDMessage("已取消区域框选", 3));
                }
            }
            else if (e.Button == SButton.L && Helper.Input.IsDown(SButton.LeftControl)) EnterPreviewMode();
        }

        private void HandlePlacementAttempt()
        {
            Vector2 mouseTile = new Vector2((int)Helper.Input.GetCursorPosition().Tile.X, (int)Helper.Input.GetCursorPosition().Tile.Y);
            bool hasCollision = false;

            if (!isOverwriteMode)
            {
                for (int x = 0; x < currentMetadata.Width; x++)
                {
                    for (int y = 0; y < currentMetadata.Height; y++)
                    {
                        Vector2 targetTile = new Vector2(mouseTile.X + x, mouseTile.Y + y);
                        var itemAtTile = previewItems.FirstOrDefault(i => (int)i.TileX == x && (int)i.TileY == y);
                        if (Game1.currentLocation.Objects.TryGetValue(targetTile, out var worldObj) && !IsDebris(worldObj)) { hasCollision = true; break; }
                        if (Game1.currentLocation.terrainFeatures.TryGetValue(targetTile, out var feature))
                        {
                            if (feature is StardewValley.TerrainFeatures.Flooring wf)
                            {
                                if (itemAtTile != null && itemAtTile.ItemType == "Flooring" && wf.whichFloor.Value != itemAtTile.FlooringId) { hasCollision = true; break; }
                            }
                            else { hasCollision = true; break; }
                        }
                    }
                    if (hasCollision) break;
                }
            }

            if (hasCollision) { Game1.playSound("cancel"); Game1.showRedMessage("无法放置：安全模式拦截了碰撞地块！"); return; }

            if (!isCreativeMode)
            {
                var requirements = previewItems.GroupBy(i => i.ItemId).Select(g => new { ItemId = g.Key, Count = g.Count() }).ToList();
                bool hasEverything = requirements.All(req => Game1.player.Items.CountId(req.ItemId) >= req.Count);

                if (hasEverything)
                {
                    pendingTile = mouseTile;
                    // 提前保存清单并关闭预览模式，防止弹窗点击时再次触发
                    var savedRequirements = requirements;
                    var savedPreviewItems = previewItems;
                    isPreviewMode = false;
                    previewItems = null;

                    Game1.currentLocation.createQuestionDialogue(
                        "检测到背包含有足够所需材料，是否消耗并一键放置蓝图？",
                        Game1.currentLocation.createYesNoResponses(),
                        (who, answer) => {
                            if (answer == "Yes")
                            {
                                foreach (var req in savedRequirements) Game1.player.Items.ReduceId(req.ItemId, req.Count);
                                // 临时恢复 previewItems 以执行 PlaceBlueprintReal
                                previewItems = savedPreviewItems;
                                PlaceBlueprintReal(pendingTile.Value);
                                previewItems = null;
                                Game1.playSound("purchase");
                            }
                            else
                            {
                                previewItems = savedPreviewItems;
                                PlaceGhosts(pendingTile.Value);
                                previewItems = null;
                            }
                        }
                    );
                    return;
                }
            }

            if (isCreativeMode) PlaceBlueprintReal(mouseTile); else PlaceGhosts(mouseTile);
            isPreviewMode = false; previewItems = null; Game1.playSound("purchase");
        }

        private bool IsDebris(StardewValley.Object obj)
        {
            if (obj == null) return false;
            string name = obj.Name ?? "";
            return obj.IsWeeds() || name.Contains("Stone") || name.Contains("Twig") || name.Contains("Weed")
                || obj.QualifiedItemId == "(O)343" || obj.QualifiedItemId == "(O)450" || obj.QualifiedItemId == "(O)294" || obj.QualifiedItemId == "(O)295";
        }

        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (startTile.HasValue) DrawSelectionBox(e.SpriteBatch, startTile.Value, Helper.Input.GetCursorPosition().Tile, Color.White * 0.3f);
            if (isPreviewMode && previewItems != null)
            {
                Vector2 mouseTile = new Vector2((int)Helper.Input.GetCursorPosition().Tile.X, (int)Helper.Input.GetCursorPosition().Tile.Y);
                for (int x = 0; x < currentMetadata.Width; x++)
                {
                    for (int y = 0; y < currentMetadata.Height; y++)
                    {
                        Vector2 targetTile = new Vector2(mouseTile.X + x, mouseTile.Y + y);
                        var item = previewItems.FirstOrDefault(i => (int)i.TileX == x && (int)i.TileY == y);
                        bool isBlocked = false;
                        if (!isOverwriteMode)
                        {
                            if (Game1.currentLocation.Objects.TryGetValue(targetTile, out var obj) && !IsDebris(obj)) isBlocked = true;
                            else if (Game1.currentLocation.terrainFeatures.TryGetValue(targetTile, out var feature))
                            {
                                if (feature is StardewValley.TerrainFeatures.Flooring wf)
                                {
                                    if (item != null && item.ItemType == "Flooring" && wf.whichFloor.Value != item.FlooringId) isBlocked = true;
                                }
                                else isBlocked = true;
                            }
                        }
                        if (item != null) DrawGhost(e.SpriteBatch, targetTile, item.ItemId, 0.5f, isBlocked ? Color.Red * 0.8f : (isCreativeMode ? Color.LightGreen : Color.Cyan));
                        else if (isBlocked) DrawSelectionBox(e.SpriteBatch, targetTile, targetTile, Color.Red * 0.2f);
                    }
                }
            }
            foreach (var ghost in placedGhosts) DrawGhost(e.SpriteBatch, ghost.Key, ghost.Value, 0.4f, Color.White * 0.6f);
        }

        private void OnRenderedHud(object sender, RenderedHudEventArgs e)
        {
            if (isPreviewMode && previewItems != null)
            {
                string topText = $"蓝图: {blueprintFiles[currentBlueprintIndex].Name} ({(isOverwriteMode ? "覆盖开启" : "安全模式")})";
                e.SpriteBatch.DrawString(Game1.dialogueFont, topText, new Vector2(80, 80), Color.White);
                DrawShoppingList(e.SpriteBatch);
            }
        }

        private void DrawShoppingList(SpriteBatch b)
        {
            if (previewItems == null) return;
            var requirements = previewItems.GroupBy(i => i.ItemId).Select(g => new { ItemId = g.Key, RequiredCount = g.Count() }).ToList();
            int xPos = Game1.uiViewport.Width - 300, yPos = 150;
            b.Draw(Game1.staminaRect, new Rectangle(xPos - 10, yPos - 10, 280, requirements.Count * 40 + 60), Color.Black * 0.5f);
            b.DrawString(Game1.dialogueFont, "所需物资清单:", new Vector2(xPos, yPos), Color.Gold, 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 0f);
            yPos += 40;
            foreach (var req in requirements)
            {
                int playerHas = Game1.player.Items.CountId(req.ItemId);
                ParsedItemData itemData = ItemRegistry.GetData(req.ItemId);
                if (itemData != null)
                {
                    b.Draw(itemData.GetTexture(), new Rectangle(xPos, yPos, 32, 32), itemData.GetSourceRect(), Color.White);
                    b.DrawString(Game1.smallFont, $"{req.RequiredCount} ({playerHas})", new Vector2(xPos + 40, yPos + 4), playerHas >= req.RequiredCount ? Color.White : Color.Red);
                    yPos += 35;
                }
            }
        }

        private void PlaceBlueprintReal(Vector2 origin)
        {
            if (previewItems == null) return;
            var sortedItems = previewItems.OrderBy(i => i.ItemType == "Object" ? 1 : 0).ToList();
            foreach (var item in sortedItems)
            {
                Vector2 targetTile = new Vector2(origin.X + item.TileX, origin.Y + item.TileY);
                if (isOverwriteMode)
                {
                    if (Game1.currentLocation.Objects.ContainsKey(targetTile)) Game1.currentLocation.Objects.Remove(targetTile);
                    if (item.ItemType == "Flooring" && Game1.currentLocation.terrainFeatures.ContainsKey(targetTile)) Game1.currentLocation.terrainFeatures.Remove(targetTile);
                }
                if (item.ItemType == "Flooring")
                {
                    string fId = !string.IsNullOrEmpty(item.FlooringId) ? item.FlooringId : item.ItemId.Replace("(O)", "");
                    if (!Game1.currentLocation.terrainFeatures.ContainsKey(targetTile)) Game1.currentLocation.terrainFeatures.Add(targetTile, new StardewValley.TerrainFeatures.Flooring(fId));
                }
                else
                {
                    Item newItem = ItemRegistry.Create(item.ItemId);
                    if (newItem is StardewValley.Object obj) { obj.TileLocation = targetTile; Game1.currentLocation.Objects.Add(targetTile, obj); }
                }
            }
        }

        private void PlaceGhosts(Vector2 origin)
        {
            if (previewItems == null) return;
            foreach (var item in previewItems) placedGhosts[new Vector2(origin.X + item.TileX, origin.Y + item.TileY)] = item.ItemId;
        }

        private void HandleGhostFilling(Vector2 tile)
        {
            if (placedGhosts.TryGetValue(tile, out string reqId) && Game1.player.ActiveItem?.QualifiedItemId == reqId)
            {
                bool placed = false;
                var itemData = ItemRegistry.GetData(reqId);
                if (itemData?.ObjectType == "Flooring" || reqId.Contains("Path") || reqId.Contains("Floor"))
                {
                    if (!Game1.currentLocation.terrainFeatures.ContainsKey(tile))
                    {
                        string fId = reqId.Replace("(O)", ""); // 由于填充时没有预览列表，这里退回到解析 ID
                        Game1.currentLocation.terrainFeatures.Add(tile, new StardewValley.TerrainFeatures.Flooring(fId));
                        placed = true;
                    }
                }
                else if (!Game1.currentLocation.Objects.ContainsKey(tile))
                {
                    Game1.currentLocation.Objects.Add(tile, (StardewValley.Object)ItemRegistry.Create(reqId));
                    placed = true;
                }
                if (placed) { Game1.player.reduceActiveItemByOne(); placedGhosts.Remove(tile); Game1.playSound("dirtyHit"); Helper.Input.Suppress(SButton.MouseLeft); }
            }
        }

        private void DrawGhost(SpriteBatch b, Vector2 tile, string itemId, float alpha, Color tint)
        {
            ParsedItemData itemData = ItemRegistry.GetData(itemId);
            if (itemData != null) b.Draw(itemData.GetTexture(), new Rectangle((int)(tile.X * 64 - Game1.viewport.X), (int)(tile.Y * 64 - Game1.viewport.Y), 64, 64), itemData.GetSourceRect(), tint * alpha);
        }

        private void DrawSelectionBox(SpriteBatch b, Vector2 start, Vector2 end, Color color)
        {
            int minX = (int)Math.Min(start.X, end.X), maxX = (int)Math.Max(start.X, end.X), minY = (int)Math.Min(start.Y, end.Y), maxY = (int)Math.Max(start.Y, end.Y);
            b.Draw(Game1.staminaRect, new Rectangle(minX * 64 - Game1.viewport.X, minY * 64 - Game1.viewport.Y, (maxX - minX + 1) * 64, (maxY - minY + 1) * 64), color);
        }

        private void SaveBlueprint(GameLocation location, Vector2 start, Vector2 end)
        {
            int minX = (int)Math.Min(start.X, end.X), maxX = (int)Math.Max(start.X, end.X), minY = (int)Math.Min(start.Y, end.Y), maxY = (int)Math.Max(start.Y, end.Y);
            var items = new List<BlueprintItem>();
            foreach (var tile in location.Objects.Keys.Where(t => t.X >= minX && t.X <= maxX && t.Y >= minY && t.Y <= maxY))
                items.Add(new BlueprintItem { ItemId = location.Objects[tile].QualifiedItemId, TileX = tile.X - minX, TileY = tile.Y - minY, Name = location.Objects[tile].DisplayName, ItemType = "Object" });
            foreach (var pair in location.terrainFeatures.Pairs.Where(p => p.Key.X >= minX && p.Key.X <= maxX && p.Key.Y >= minY && p.Key.Y <= maxY))
                if (pair.Value is StardewValley.TerrainFeatures.Flooring f) items.Add(new BlueprintItem { ItemId = "(O)" + (f.GetData()?.ItemId ?? ""), FlooringId = f.whichFloor.Value, TileX = pair.Key.X - minX, TileY = pair.Key.Y - minY, Name = "Flooring", ItemType = "Flooring" });

            if (items.Count > 0)
            {
                var data = new BlueprintFile { Metadata = new BlueprintMetadata { Width = maxX - minX + 1, Height = maxY - minY + 1 }, Items = items };
                this.Helper.Data.WriteJsonFile($"blueprints/blueprint_{DateTime.Now:yyyyMMdd_HHmmss}.json", data);
                Game1.showGlobalMessage("蓝图已保存！");
            }
        }

        private void EnterPreviewMode()
        {
            string path = Path.Combine(this.Helper.DirectoryPath, "blueprints");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            blueprintFiles = new DirectoryInfo(path).GetFiles("*.json").OrderByDescending(f => f.LastWriteTime).ToList();
            if (blueprintFiles.Count == 0) Game1.showRedMessage("没有蓝图文件！");
            else { currentBlueprintIndex = 0; LoadCurrentBlueprint(); isPreviewMode = true; }
        }

        private void SwitchBlueprint(bool next) { if (blueprintFiles.Count > 1) { currentBlueprintIndex = next ? (currentBlueprintIndex + 1) % blueprintFiles.Count : (currentBlueprintIndex - 1 + blueprintFiles.Count) % blueprintFiles.Count; LoadCurrentBlueprint(); Game1.playSound("shwip"); } }

        private void LoadCurrentBlueprint() { var file = this.Helper.Data.ReadJsonFile<BlueprintFile>($"blueprints/{blueprintFiles[currentBlueprintIndex].Name}"); previewItems = file.Items; currentMetadata = file.Metadata; }
    }

    public class BlueprintFile { public BlueprintMetadata Metadata { get; set; } public List<BlueprintItem> Items { get; set; } }
    public class BlueprintMetadata { public int Width { get; set; } public int Height { get; set; } }
    public class BlueprintItem { public string ItemId { get; set; } public string FlooringId { get; set; } public float TileX { get; set; } public float TileY { get; set; } public string Name { get; set; } public string ItemType { get; set; } = "Object"; }
}