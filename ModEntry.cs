using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
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

        public override void Entry(IModHelper helper)
        {
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
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
                else if (e.Button == SButton.MouseRight) { isPreviewMode = false; previewItems = null; }
                else if (e.Button == SButton.Left || e.Button == SButton.Right) SwitchBlueprint(e.Button == SButton.Right);
            }
            else if (!isCreativeMode && e.Button == SButton.MouseLeft && !Helper.Input.IsDown(SButton.LeftControl))
            {
                HandleGhostFilling(new Vector2((int)e.Cursor.Tile.X, (int)e.Cursor.Tile.Y));
            }
            else if (e.Button == SButton.MouseLeft && Helper.Input.IsDown(SButton.LeftControl))
            {
                if (startTile == null) { startTile = e.Cursor.Tile; Game1.addHUDMessage(new HUDMessage("起始点已设定", 3)); }
                else { SaveBlueprint(Game1.currentLocation, startTile.Value, e.Cursor.Tile); startTile = null; Game1.playSound("drumkit0"); }
            }
            else if (e.Button == SButton.L && Helper.Input.IsDown(SButton.LeftControl)) EnterPreviewMode();
        }

        private void HandlePlacementAttempt()
        {
            Vector2 mouseTile = new Vector2((int)Helper.Input.GetCursorPosition().Tile.X, (int)Helper.Input.GetCursorPosition().Tile.Y);
            bool hasCollision = false;

            // 1. 只有在安全模式下才进行严格碰撞检测
            if (!isOverwriteMode)
            {
                // 检测整个蓝图覆盖的矩形区域
                for (int x = 0; x < currentMetadata.Width; x++)
                {
                    for (int y = 0; y < currentMetadata.Height; y++)
                    {
                        Vector2 targetTile = new Vector2(mouseTile.X + x, mouseTile.Y + y);
                        var itemAtTile = previewItems.FirstOrDefault(i => (int)i.TileX == x && (int)i.TileY == y);

                        // 检查物体
                        if (Game1.currentLocation.Objects.TryGetValue(targetTile, out var worldObj))
                        {
                            if (!IsDebris(worldObj)) { hasCollision = true; break; }
                        }
                        // 检查地形 (地砖等)
                        if (Game1.currentLocation.terrainFeatures.TryGetValue(targetTile, out var feature))
                        {
                            if (feature is StardewValley.TerrainFeatures.Flooring worldFlooring)
                            {
                                // 如果蓝图在此处要放地砖，且地砖样式不同，则视为冲突
                                if (itemAtTile != null && itemAtTile.ItemType == "Flooring")
                                {
                                    if (worldFlooring.whichFloor.Value != itemAtTile.FlooringId) { hasCollision = true; break; }
                                }
                                // 如果蓝图在此处要放物体，地砖不冲突（原版允许）
                            }
                            else { hasCollision = true; break; } // 树、耕地等视为冲突
                        }
                    }
                    if (hasCollision) break;
                }
            }

            if (hasCollision)
            {
                Game1.playSound("cancel");
                Game1.showRedMessage("无法放置：安全模式拦截了碰撞地块！");
            }
            else
            {
                if (isCreativeMode) PlaceBlueprintReal(mouseTile);
                else PlaceGhosts(mouseTile);
                isPreviewMode = false; previewItems = null; Game1.playSound("purchase");
            }
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
                        else if (isBlocked) DrawSelectionBox(e.SpriteBatch, targetTile, targetTile, Color.Red * 0.2f); // 空白地块冲突显示
                    }
                }
                e.SpriteBatch.DrawString(Game1.dialogueFont, $"蓝图: {blueprintFiles[currentBlueprintIndex].Name} ({(isOverwriteMode ? "覆盖" : "安全")})", new Vector2(100, 100), Color.White);
            }
            foreach (var ghost in placedGhosts) DrawGhost(e.SpriteBatch, ghost.Key, ghost.Value, 0.4f, Color.White * 0.6f);
        }

        private void PlaceBlueprintReal(Vector2 origin)
        {
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
                        string fId = previewItems?.FirstOrDefault(i => i.ItemId == reqId)?.FlooringId ?? reqId.Replace("(O)", "");
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