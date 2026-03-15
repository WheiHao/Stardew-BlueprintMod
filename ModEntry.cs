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

        // --- 新增：模式切换开关 (默认开启规划模式，保护平衡性) ---
        private bool isCreativeMode = false;

        public override void Entry(IModHelper helper)
        {
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            // 模式切换按键：按下 K 键切换
            if (e.Button == SButton.K)
            {
                isCreativeMode = !isCreativeMode;
                string modeName = isCreativeMode ? "创造模式 (直接放置)" : "生存模式 (规划虚影)";
                this.Monitor.Log($"已切换到 {modeName}", LogLevel.Info);
                Game1.addHUDMessage(new HUDMessage($"蓝图模式: {modeName}", 3)); // 游戏内左下角弹出提示
                return;
            }

            if (isPreviewMode)
            {
                if (e.Button == SButton.MouseLeft)
                {
                    Vector2 mouseTile = Helper.Input.GetCursorPosition().Tile;
                    
                    // --- 核心分支逻辑 ---
                    if (isCreativeMode)
                        PlaceBlueprintReal(mouseTile); // 直接变出东西
                    else
                        PlaceGhosts(mouseTile);        // 变出虚影

                    isPreviewMode = false;
                    previewItems = null;
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

            // 填装虚影逻辑 (仅在生存模式有意义)
            if (!isCreativeMode && e.Button == SButton.MouseLeft)
            {
                HandleGhostFilling(e.Cursor.Tile);
            }

            // 记录蓝图逻辑
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

        // 逻辑 A: 直接放置真实物体 (创造模式)
        private void PlaceBlueprintReal(Vector2 origin)
        {
            foreach (var item in previewItems)
            {
                Vector2 targetTile = new Vector2(origin.X + item.TileX, origin.Y + item.TileY);
                Item newItem = ItemRegistry.Create(item.ItemId);
                if (newItem is StardewValley.Object obj && !Game1.currentLocation.Objects.ContainsKey(targetTile))
                {
                    obj.TileLocation = targetTile;
                    Game1.currentLocation.Objects.Add(targetTile, obj);
                }
            }
            this.Monitor.Log("蓝图已直接部署 (创造模式)。", LogLevel.Info);
        }

        // 逻辑 B: 放置待建造虚影 (生存模式)
        private void PlaceGhosts(Vector2 origin)
        {
            foreach (var item in previewItems)
            {
                Vector2 targetTile = new Vector2(origin.X + item.TileX, origin.Y + item.TileY);
                if (!Game1.currentLocation.Objects.ContainsKey(targetTile) && !placedGhosts.ContainsKey(targetTile))
                {
                    placedGhosts[targetTile] = item.ItemId;
                }
            }
            this.Monitor.Log("规划虚影已部署 (生存模式)。", LogLevel.Info);
        }

        // 处理虚影填充
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

        // 渲染逻辑
        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (startTile.HasValue)
                DrawSelectionBox(e.SpriteBatch, startTile.Value, Helper.Input.GetCursorPosition().Tile, Color.White * 0.3f);

            if (isPreviewMode && previewItems != null)
            {
                Vector2 mouseTile = Helper.Input.GetCursorPosition().Tile;
                foreach (var item in previewItems)
                    DrawGhost(e.SpriteBatch, new Vector2(mouseTile.X + item.TileX, mouseTile.Y + item.TileY), item.ItemId, 0.5f, isCreativeMode ? Color.LightGreen : Color.Cyan);
            }

            foreach (var ghost in placedGhosts)
                DrawGhost(e.SpriteBatch, ghost.Key, ghost.Value, 0.4f, Color.Red * 0.7f);
        }

        // 通用绘制方法
        private void DrawGhost(SpriteBatch b, Vector2 tile, string itemId, float alpha, Color? tint = null)
        {
            Rectangle destRect = new Rectangle((int)(tile.X * 64 - Game1.viewport.X), (int)(tile.Y * 64 - Game1.viewport.Y), 64, 64);
            ParsedItemData itemData = ItemRegistry.GetData(itemId);
            if (itemData != null) b.Draw(itemData.GetTexture(), destRect, itemData.GetSourceRect(), (tint ?? Color.White) * alpha);
        }

        // 其余方法 (DrawSelectionBox, SaveBlueprint, EnterPreviewMode, SwitchBlueprint, LoadCurrentBlueprint) 保持不变即可
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