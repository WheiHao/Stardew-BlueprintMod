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
        private bool isPreviewMode = false;
        private List<FileInfo> blueprintFiles = new List<FileInfo>();
        private int currentBlueprintIndex = 0;
        private Dictionary<Vector2, string> placedGhosts = new Dictionary<Vector2, string>();
        private bool isCreativeMode = false;

        public override void Entry(IModHelper helper)
        {
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            // K 键切换模式
            if (e.Button == SButton.K)
            {
                isCreativeMode = !isCreativeMode;
                string modeName = isCreativeMode ? "创造模式" : "生存模式";
                Game1.addHUDMessage(new HUDMessage($"蓝图模式: {modeName}", 3));
                return;
            }

            // 预览状态逻辑
            if (isPreviewMode)
            {
                if (e.Button == SButton.MouseLeft)
                {
                    HandlePlacementAttempt();
                    Helper.Input.Suppress(SButton.MouseLeft);
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

            // 填充虚影逻辑
            if (!isCreativeMode && e.Button == SButton.MouseLeft)
                HandleGhostFilling(e.Cursor.Tile);

            // 记录蓝图
            if (e.Button == SButton.MouseLeft && Helper.Input.IsDown(SButton.LeftControl))
            {
                if (startTile == null) startTile = e.Cursor.Tile;
                else { SaveBlueprint(Game1.currentLocation, startTile.Value, e.Cursor.Tile); startTile = null; }
            }
            else if (e.Button == SButton.L && Helper.Input.IsDown(SButton.LeftControl))
            {
                EnterPreviewMode();
            }
        }

        // --- 核心修改：全局预检放置逻辑 ---
        private void HandlePlacementAttempt()
        {
            Vector2 mouseTile = Helper.Input.GetCursorPosition().Tile;
            bool hasCollision = false;

            // 1. 预检阶段：遍历所有计划放置的位置
            foreach (var item in previewItems)
            {
                Vector2 targetTile = new Vector2(mouseTile.X + item.TileX, mouseTile.Y + item.TileY);
                // 检查：是否有游戏物体 OR 是否已经有待建虚影
                if (Game1.currentLocation.Objects.ContainsKey(targetTile) || placedGhosts.ContainsKey(targetTile))
                {
                    hasCollision = true;
                    break; 
                }
            }

            // 2. 决策阶段
            if (hasCollision)
            {
                Game1.playSound("cancel");
                Game1.showRedMessage("无法放置：蓝图范围内存在障碍物！");
            }
            else
            {
                // 只有完全通过才执行
                if (isCreativeMode) PlaceBlueprintReal(mouseTile);
                else PlaceGhosts(mouseTile);
                
                isPreviewMode = false;
                previewItems = null;
                Game1.playSound("purchase"); // 成功音效
            }
        }

        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (startTile.HasValue)
                DrawSelectionBox(e.SpriteBatch, startTile.Value, Helper.Input.GetCursorPosition().Tile, Color.White * 0.3f);

            if (isPreviewMode && previewItems != null)
            {
                Vector2 mouseTile = Helper.Input.GetCursorPosition().Tile;
                foreach (var item in previewItems)
                {
                    Vector2 targetTile = new Vector2(mouseTile.X + item.TileX, mouseTile.Y + item.TileY);
                    bool isBlocked = Game1.currentLocation.Objects.ContainsKey(targetTile) || placedGhosts.ContainsKey(targetTile);
                    Color previewColor = isBlocked ? Color.Red * 0.8f : (isCreativeMode ? Color.LightGreen : Color.Cyan);
                    DrawGhost(e.SpriteBatch, targetTile, item.ItemId, 0.5f, previewColor);
                }
                
                string fileName = blueprintFiles[currentBlueprintIndex].Name;
                e.SpriteBatch.DrawString(Game1.dialogueFont, $"Blueprint: {fileName}", new Vector2(100, 100), Color.White);
            }

            foreach (var ghost in placedGhosts)
                DrawGhost(e.SpriteBatch, ghost.Key, ghost.Value, 0.4f, Color.Red * 0.6f);
        }

        // 放置实物（现在不需要再做单格检查，因为 HandlePlacementAttempt 已经查过了）
        private void PlaceBlueprintReal(Vector2 origin)
        {
            foreach (var item in previewItems)
            {
                Vector2 targetTile = new Vector2(origin.X + item.TileX, origin.Y + item.TileY);
                Item newItem = ItemRegistry.Create(item.ItemId);
                if (newItem is StardewValley.Object obj)
                {
                    obj.TileLocation = targetTile;
                    Game1.currentLocation.Objects.Add(targetTile, obj);
                }
            }
        }

        // 放置虚影
        private void PlaceGhosts(Vector2 origin)
        {
            foreach (var item in previewItems)
            {
                Vector2 targetTile = new Vector2(origin.X + item.TileX, origin.Y + item.TileY);
                placedGhosts[targetTile] = item.ItemId;
            }
        }

        // 辅助方法... (DrawGhost, HandleGhostFilling, DrawSelectionBox, SaveBlueprint, EnterPreviewMode, SwitchBlueprint, LoadCurrentBlueprint)
        private void HandleGhostFilling(Vector2 tile)
        {
            if (placedGhosts.ContainsKey(tile))
            {
                string requiredId = placedGhosts[tile];
                if (Game1.player.ActiveItem?.QualifiedItemId == requiredId)
                {
                    Game1.player.reduceActiveItemByOne();
                    Game1.currentLocation.Objects.Add(tile, (StardewValley.Object)ItemRegistry.Create(requiredId));
                    placedGhosts.Remove(tile);
                    Game1.playSound("dirtyHit");
                    Helper.Input.Suppress(SButton.MouseLeft);
                }
            }
        }

        private void DrawGhost(SpriteBatch b, Vector2 tile, string itemId, float alpha, Color? tint = null)
        {
            Rectangle destRect = new Rectangle((int)(tile.X * 64 - Game1.viewport.X), (int)(tile.Y * 64 - Game1.viewport.Y), 64, 64);
            ParsedItemData itemData = ItemRegistry.GetData(itemId);
            if (itemData != null) b.Draw(itemData.GetTexture(), destRect, itemData.GetSourceRect(), (tint ?? Color.White) * alpha);
        }

        private void DrawSelectionBox(SpriteBatch b, Vector2 start, Vector2 end, Color color)
        {
            int minX = (int)Math.Min(start.X, end.X); int maxX = (int)Math.Max(start.X, end.X);
            int minY = (int)Math.Min(start.Y, end.Y); int maxY = (int)Math.Max(start.Y, end.Y);
            Rectangle rect = new Rectangle(minX * 64 - Game1.viewport.X, minY * 64 - Game1.viewport.Y, (maxX - minX + 1) * 64, (maxY - minY + 1) * 64);
            b.Draw(Game1.staminaRect, rect, color);
        }

        private void SaveBlueprint(GameLocation location, Vector2 start, Vector2 end)
        {
            List<BlueprintItem> items = new List<BlueprintItem>();
            int minX = (int)Math.Min(start.X, end.X); int maxX = (int)Math.Max(start.X, end.X);
            int minY = (int)Math.Min(start.Y, end.Y); int maxY = (int)Math.Max(start.Y, end.Y);
            foreach (var tile in location.Objects.Keys)
            {
                if (tile.X >= minX && tile.X <= maxX && tile.Y >= minY && tile.Y <= maxY)
                {
                    var obj = location.Objects[tile];
                    items.Add(new BlueprintItem { ItemId = obj.QualifiedItemId, TileX = tile.X - minX, TileY = tile.Y - minY, Name = obj.DisplayName });
                }
            }
            if (items.Count > 0) this.Helper.Data.WriteJsonFile($"blueprints/blueprint_{DateTime.Now:yyyyMMdd_HHmmss}.json", items);
        }

        private void EnterPreviewMode()
        {
            string folderPath = Path.Combine(this.Helper.DirectoryPath, "blueprints");
            if (!Directory.Exists(folderPath)) return;
            blueprintFiles = new DirectoryInfo(folderPath).GetFiles("*.json").OrderByDescending(f => f.LastWriteTime).ToList();
            if (blueprintFiles.Count == 0) return;
            currentBlueprintIndex = 0; LoadCurrentBlueprint(); isPreviewMode = true;
        }

        private void SwitchBlueprint(bool next) { if (blueprintFiles.Count <= 1) return; currentBlueprintIndex = next ? (currentBlueprintIndex + 1) % blueprintFiles.Count : (currentBlueprintIndex - 1 + blueprintFiles.Count) % blueprintFiles.Count; LoadCurrentBlueprint(); }
        private void LoadCurrentBlueprint() => previewItems = this.Helper.Data.ReadJsonFile<List<BlueprintItem>>($"blueprints/{blueprintFiles[currentBlueprintIndex].Name}");
    }

    public class BlueprintItem { public string ItemId { get; set; } public float TileX { get; set; } public float TileY { get; set; } public string Name { get; set; } }
}