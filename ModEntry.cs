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
        // 核心状态变量
        private Vector2? startTile = null;
        private List<BlueprintItem> previewItems = null;
        private bool isPreviewMode = false;
        private List<FileInfo> blueprintFiles = new List<FileInfo>();
        private int currentBlueprintIndex = 0;
        private Dictionary<Vector2, string> placedGhosts = new Dictionary<Vector2, string>();
        private bool isCreativeMode = false;

        public override void Entry(IModHelper helper)
        {
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!e.IsMultipleOf(20) || placedGhosts.Count == 0 || Game1.currentLocation == null)
                return;

            List<Vector2> toRemove = new List<Vector2>();
            foreach (var ghost in placedGhosts)
            {
                Vector2 tile = ghost.Key;
                string requiredId = ghost.Value;

                if (Game1.currentLocation.Objects.TryGetValue(tile, out var obj) && obj.QualifiedItemId == requiredId)
                {
                    toRemove.Add(tile);
                    continue;
                }

                if (Game1.currentLocation.terrainFeatures.TryGetValue(tile, out var feature) && feature is StardewValley.TerrainFeatures.Flooring flooring)
                {
                    string flooringItemId = "(O)" + (flooring.GetData()?.ItemId ?? "");
                    if (flooringItemId == requiredId)
                    {
                        toRemove.Add(tile);
                    }
                }
            }

            foreach (var tile in toRemove)
            {
                placedGhosts.Remove(tile);
            }
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (Helper.Input.IsDown(SButton.LeftControl))
            {
                if (e.Button == SButton.MouseLeft || e.Button == SButton.MouseRight)
                {
                    Helper.Input.Suppress(e.Button);
                }
            }

            if (e.Button == SButton.K)
            {
                isCreativeMode = !isCreativeMode;
                string modeName = isCreativeMode ? "创造模式" : "生存模式";
                Game1.addHUDMessage(new HUDMessage($"蓝图模式: {modeName}", 3));
                return;
            }

            if (e.Button == SButton.C && Helper.Input.IsDown(SButton.LeftControl) && Helper.Input.IsDown(SButton.LeftShift))
            {
                placedGhosts.Clear();
                Game1.playSound("trashcan");
                Game1.addHUDMessage(new HUDMessage("已清除所有蓝图虚影", 3));
                return;
            }

            if (e.Button == SButton.MouseRight && Helper.Input.IsDown(SButton.LeftControl))
            {
                Vector2 tile = new Vector2((int)e.Cursor.Tile.X, (int)e.Cursor.Tile.Y);
                if (placedGhosts.ContainsKey(tile))
                {
                    placedGhosts.Remove(tile);
                    Game1.playSound("hammer");
                    return;
                }
            }

            if (isPreviewMode)
            {
                if (e.Button == SButton.MouseLeft || e.Button == SButton.MouseRight)
                    Helper.Input.Suppress(e.Button);

                if (e.Button == SButton.MouseLeft)
                {
                    HandlePlacementAttempt();
                }
                else if (e.Button == SButton.MouseRight)
                {
                    isPreviewMode = false;
                    previewItems = null;
                }
                else if (e.Button == SButton.Left || e.Button == SButton.Right)
                {
                    SwitchBlueprint(e.Button == SButton.Right);
                }
                return;
            }

            if (!isCreativeMode && e.Button == SButton.MouseLeft && !Helper.Input.IsDown(SButton.LeftControl))
            {
                HandleGhostFilling(new Vector2((int)e.Cursor.Tile.X, (int)e.Cursor.Tile.Y));
            }

            if (e.Button == SButton.MouseLeft && Helper.Input.IsDown(SButton.LeftControl))
            {
                if (startTile == null)
                {
                    startTile = e.Cursor.Tile;
                    Game1.addHUDMessage(new HUDMessage("起始点已设定", 3));
                }
                else
                {
                    SaveBlueprint(Game1.currentLocation, startTile.Value, e.Cursor.Tile);
                    startTile = null;
                    Game1.playSound("drumkit0");
                }
            }
            else if (e.Button == SButton.L && Helper.Input.IsDown(SButton.LeftControl))
            {
                EnterPreviewMode();
            }
        }

        private void HandlePlacementAttempt()
        {
            Vector2 mouseTile = Helper.Input.GetCursorPosition().Tile;
            mouseTile = new Vector2((int)mouseTile.X, (int)mouseTile.Y);
            bool hasHardCollision = false;

            foreach (var item in previewItems)
            {
                Vector2 targetTile = new Vector2(mouseTile.X + item.TileX, mouseTile.Y + item.TileY);
                if (Game1.currentLocation.Objects.TryGetValue(targetTile, out var obj))
                {
                    if (!IsDebris(obj))
                    {
                        hasHardCollision = true;
                        break; 
                    }
                }
            }

            if (hasHardCollision && !isCreativeMode)
            {
                Game1.playSound("cancel");
                Game1.showRedMessage("无法放置：蓝图范围内存在永久性障碍物！");
            }
            else
            {
                if (isCreativeMode) PlaceBlueprintReal(mouseTile);
                else PlaceGhosts(mouseTile);
                
                isPreviewMode = false;
                previewItems = null;
                Game1.playSound("purchase");
            }
        }

        private bool IsDebris(StardewValley.Object obj)
        {
            if (obj == null) return false;
            string name = obj.Name ?? "";
            return obj.IsWeeds() || name.Contains("Stone") || name.Contains("Twig") || name.Contains("Weed")
                || obj.QualifiedItemId == "(O)343" || obj.QualifiedItemId == "(O)450" 
                || obj.QualifiedItemId == "(O)294" || obj.QualifiedItemId == "(O)295";
        }

        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (startTile.HasValue)
                DrawSelectionBox(e.SpriteBatch, startTile.Value, Helper.Input.GetCursorPosition().Tile, Color.White * 0.3f);

            if (isPreviewMode && previewItems != null)
            {
                Vector2 mouseTile = Helper.Input.GetCursorPosition().Tile;
                mouseTile = new Vector2((int)mouseTile.X, (int)mouseTile.Y);

                foreach (var item in previewItems)
                {
                    Vector2 targetTile = new Vector2(mouseTile.X + item.TileX, mouseTile.Y + item.TileY);
                    bool isBlocked = false;
                    if (Game1.currentLocation.Objects.TryGetValue(targetTile, out var obj))
                    {
                        if (!isCreativeMode && !IsDebris(obj)) isBlocked = true;
                    }
                    Color previewColor = isBlocked ? Color.Red * 0.8f : (isCreativeMode ? Color.LightGreen : Color.Cyan);
                    DrawGhost(e.SpriteBatch, targetTile, item.ItemId, 0.5f, previewColor);
                }
                
                string fileName = blueprintFiles[currentBlueprintIndex].Name;
                e.SpriteBatch.DrawString(Game1.dialogueFont, $"当前蓝图: {fileName}", new Vector2(100, 100), Color.White);
            }

            foreach (var ghost in placedGhosts)
            {
                DrawGhost(e.SpriteBatch, ghost.Key, ghost.Value, 0.4f, Color.White * 0.6f);
            }
        }

        private void PlaceBlueprintReal(Vector2 origin)
        {
            foreach (var item in previewItems)
            {
                Vector2 targetTile = new Vector2(origin.X + item.TileX, origin.Y + item.TileY);
                if (item.ItemType == "Flooring")
                {
                    if (Game1.currentLocation.terrainFeatures.ContainsKey(targetTile))
                        Game1.currentLocation.terrainFeatures.Remove(targetTile);
                    
                    string fId = !string.IsNullOrEmpty(item.FlooringId) ? item.FlooringId : item.ItemId.Replace("(O)", "");
                    Game1.currentLocation.terrainFeatures.Add(targetTile, new StardewValley.TerrainFeatures.Flooring(fId));
                }
                else
                {
                    if (Game1.currentLocation.Objects.ContainsKey(targetTile))
                        Game1.currentLocation.Objects.Remove(targetTile);

                    Item newItem = ItemRegistry.Create(item.ItemId);
                    if (newItem is StardewValley.Object obj)
                    {
                        obj.TileLocation = targetTile;
                        Game1.currentLocation.Objects.Add(targetTile, obj);
                    }
                }
            }
        }

        private void PlaceGhosts(Vector2 origin)
        {
            foreach (var item in previewItems)
            {
                Vector2 targetTile = new Vector2(origin.X + item.TileX, origin.Y + item.TileY);
                placedGhosts[targetTile] = item.ItemId;
            }
        }

        private void HandleGhostFilling(Vector2 tile)
        {
            if (placedGhosts.ContainsKey(tile))
            {
                string requiredId = placedGhosts[tile];
                if (Game1.player.ActiveItem?.QualifiedItemId == requiredId)
                {
                    bool placed = false;
                    var itemData = ItemRegistry.GetData(requiredId);
                    if (itemData?.ObjectType == "Flooring" || requiredId.Contains("Path") || requiredId.Contains("Floor"))
                    {
                        if (!Game1.currentLocation.terrainFeatures.ContainsKey(tile))
                        {
                            string fId = null;
                            if (previewItems != null) fId = previewItems.FirstOrDefault(i => i.ItemId == requiredId)?.FlooringId;
                            if (string.IsNullOrEmpty(fId)) fId = requiredId.Replace("(O)", "");

                            Game1.currentLocation.terrainFeatures.Add(tile, new StardewValley.TerrainFeatures.Flooring(fId));
                            placed = true;
                        }
                    }
                    
                    if (!placed && !Game1.currentLocation.Objects.ContainsKey(tile))
                    {
                        Game1.currentLocation.Objects.Add(tile, (StardewValley.Object)ItemRegistry.Create(requiredId));
                        placed = true;
                    }

                    if (placed)
                    {
                        Game1.player.reduceActiveItemByOne();
                        placedGhosts.Remove(tile);
                        Game1.playSound("dirtyHit");
                        Helper.Input.Suppress(SButton.MouseLeft);
                    }
                }
            }
        }

        private void DrawGhost(SpriteBatch b, Vector2 tile, string itemId, float alpha, Color? tint = null)
        {
            Rectangle destRect = new Rectangle((int)(tile.X * 64 - Game1.viewport.X), (int)(tile.Y * 64 - Game1.viewport.Y), 64, 64);
            ParsedItemData itemData = ItemRegistry.GetData(itemId);
            if (itemData != null)
                b.Draw(itemData.GetTexture(), destRect, itemData.GetSourceRect(), (tint ?? Color.White) * alpha);
        }

        private void DrawSelectionBox(SpriteBatch b, Vector2 start, Vector2 end, Color color)
        {
            int minX = (int)Math.Min(start.X, end.X);
            int maxX = (int)Math.Max(start.X, end.X);
            int minY = (int)Math.Min(start.Y, end.Y);
            int maxY = (int)Math.Max(start.Y, end.Y);
            Rectangle rect = new Rectangle(minX * 64 - Game1.viewport.X, minY * 64 - Game1.viewport.Y, (maxX - minX + 1) * 64, (maxY - minY + 1) * 64);
            b.Draw(Game1.staminaRect, rect, color);
        }

        private void SaveBlueprint(GameLocation location, Vector2 start, Vector2 end)
        {
            List<BlueprintItem> items = new List<BlueprintItem>();
            int minX = (int)Math.Min(start.X, end.X);
            int maxX = (int)Math.Max(start.X, end.X);
            int minY = (int)Math.Min(start.Y, end.Y);
            int maxY = (int)Math.Max(start.Y, end.Y);

            foreach (var tile in location.Objects.Keys)
            {
                if (tile.X >= minX && tile.X <= maxX && tile.Y >= minY && tile.Y <= maxY)
                {
                    var obj = location.Objects[tile];
                    items.Add(new BlueprintItem { 
                        ItemId = obj.QualifiedItemId, 
                        TileX = tile.X - minX, 
                        TileY = tile.Y - minY, 
                        Name = obj.DisplayName,
                        ItemType = "Object"
                    });
                }
            }

            foreach (var pair in location.terrainFeatures.Pairs)
            {
                Vector2 tile = pair.Key;
                if (tile.X >= minX && tile.X <= maxX && tile.Y >= minY && tile.Y <= maxY)
                {
                    if (pair.Value is StardewValley.TerrainFeatures.Flooring flooring)
                    {
                        string itemId = flooring.GetData()?.ItemId;
                        string internalId = flooring.whichFloor.Value; // 修正：在 1.6 中使用 whichFloor.Value
                        if (itemId != null)
                        {
                            items.Add(new BlueprintItem {
                                ItemId = "(O)" + itemId,
                                FlooringId = internalId,
                                TileX = tile.X - minX,
                                TileY = tile.Y - minY,
                                Name = "Flooring",
                                ItemType = "Flooring"
                            });
                        }
                    }
                }
            }

            if (items.Count > 0)
            {
                string path = $"blueprints/blueprint_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                this.Helper.Data.WriteJsonFile(path, items);
                Game1.showGlobalMessage($"蓝图已保存: {path} (包含 {items.Count} 个项目)");
            }
        }

        private void EnterPreviewMode()
        {
            string folderPath = Path.Combine(this.Helper.DirectoryPath, "blueprints");
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
            blueprintFiles = new DirectoryInfo(folderPath).GetFiles("*.json").OrderByDescending(f => f.LastWriteTime).ToList();
            if (blueprintFiles.Count == 0)
            {
                Game1.showRedMessage("没有发现蓝图文件！请先用 Ctrl+左键 记录蓝图。");
                return;
            }
            currentBlueprintIndex = 0;
            LoadCurrentBlueprint();
            isPreviewMode = true;
        }

        private void SwitchBlueprint(bool next)
        {
            if (blueprintFiles.Count <= 1) return;
            currentBlueprintIndex = next ? (currentBlueprintIndex + 1) % blueprintFiles.Count : (currentBlueprintIndex - 1 + blueprintFiles.Count) % blueprintFiles.Count;
            LoadCurrentBlueprint();
            Game1.playSound("shwip");
        }

        private void LoadCurrentBlueprint()
        {
            previewItems = this.Helper.Data.ReadJsonFile<List<BlueprintItem>>($"blueprints/{blueprintFiles[currentBlueprintIndex].Name}");
        }
    }

    public class BlueprintItem
    {
        public string ItemId { get; set; }
        public string FlooringId { get; set; }
        public float TileX { get; set; }
        public float TileY { get; set; }
        public string Name { get; set; }
        public string ItemType { get; set; } = "Object";
    }
}