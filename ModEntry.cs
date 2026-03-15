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
        
        // --- 新增：用于选择蓝图的变量 ---
        private List<FileInfo> blueprintFiles = new List<FileInfo>();
        private int currentBlueprintIndex = 0;

        public override void Entry(IModHelper helper)
        {
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (isPreviewMode)
            {
                if (e.Button == SButton.MouseLeft)
                {
                    PlaceBlueprint(Helper.Input.GetCursorPosition().Tile);
                    isPreviewMode = false;
                    previewItems = null;
                    Helper.Input.Suppress(SButton.MouseLeft);
                }
                else if (e.Button == SButton.MouseRight)
                {
                    isPreviewMode = false;
                    previewItems = null;
                }
                // --- 新增：切换蓝图逻辑 ---
                else if (e.Button == SButton.Left || e.Button == SButton.Right)
                {
                    SwitchBlueprint(e.Button == SButton.Right);
                }
                return;
            }

            if (e.Button == SButton.MouseLeft && Helper.Input.IsDown(SButton.LeftControl))
            {
                Vector2 currentTile = e.Cursor.Tile;
                if (startTile == null) startTile = currentTile;
                else
                {
                    SaveBlueprint(Game1.currentLocation, startTile.Value, currentTile);
                    startTile = null;
                }
            }
            else if (e.Button == SButton.L && Helper.Input.IsDown(SButton.LeftControl))
            {
                EnterPreviewMode();
            }
        }

        private void EnterPreviewMode()
        {
            string folderPath = Path.Combine(this.Helper.DirectoryPath, "blueprints");
            if (!Directory.Exists(folderPath)) return;

            // 获取所有 JSON 文件并按时间排序
            blueprintFiles = new DirectoryInfo(folderPath).GetFiles("*.json")
                                 .OrderByDescending(f => f.LastWriteTime)
                                 .ToList();

            if (blueprintFiles.Count == 0)
            {
                this.Monitor.Log("未找到蓝图文件！", LogLevel.Warn);
                return;
            }

            currentBlueprintIndex = 0;
            LoadCurrentBlueprint();
            isPreviewMode = true;
        }

        // 切换蓝图的核心逻辑
        private void SwitchBlueprint(bool next)
        {
            if (blueprintFiles.Count <= 1) return;

            if (next)
                currentBlueprintIndex = (currentBlueprintIndex + 1) % blueprintFiles.Count;
            else
                currentBlueprintIndex = (currentBlueprintIndex - 1 + blueprintFiles.Count) % blueprintFiles.Count;

            LoadCurrentBlueprint();
            this.Monitor.Log($"已切换蓝图: {blueprintFiles[currentBlueprintIndex].Name}", LogLevel.Info);
        }

        private void LoadCurrentBlueprint()
        {
            var file = blueprintFiles[currentBlueprintIndex];
            previewItems = this.Helper.Data.ReadJsonFile<List<BlueprintItem>>($"blueprints/{file.Name}");
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
                    Rectangle destRect = new Rectangle((int)(targetTile.X * 64 - Game1.viewport.X), (int)(targetTile.Y * 64 - Game1.viewport.Y), 64, 64);

                    ParsedItemData itemData = ItemRegistry.GetData(item.ItemId);
                    if (itemData != null)
                    {
                        e.SpriteBatch.Draw(itemData.GetTexture(), destRect, itemData.GetSourceRect(), Color.White * 0.5f);
                    }
                    else
                    {
                        e.SpriteBatch.Draw(Game1.staminaRect, destRect, Color.Cyan * 0.5f);
                    }
                }

                // --- 新增：在屏幕上显示当前蓝图的文件名 ---
                string fileName = blueprintFiles[currentBlueprintIndex].Name;
                e.SpriteBatch.DrawString(Game1.dialogueFont, $"Blueprint: {fileName}", new Vector2(100, 100), Color.White);
            }
        }

        private void PlaceBlueprint(Vector2 origin)
        {
            GameLocation location = Game1.currentLocation;
            foreach (var item in previewItems)
            {
                Vector2 targetTile = new Vector2(origin.X + item.TileX, origin.Y + item.TileY);
                Item newItem = ItemRegistry.Create(item.ItemId);
                if (newItem is StardewValley.Object obj)
                {
                    obj.TileLocation = targetTile;
                    if (!location.Objects.ContainsKey(targetTile))
                        location.Objects.Add(targetTile, obj);
                }
            }
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
                    items.Add(new BlueprintItem { ItemId = obj.QualifiedItemId, TileX = tile.X - minX, TileY = tile.Y - minY, Name = obj.DisplayName });
                }
            }
            if (items.Count > 0)
                this.Helper.Data.WriteJsonFile($"blueprints/blueprint_{DateTime.Now:yyyyMMdd_HHmmss}.json", items);
        }
    }

    public class BlueprintItem
    {
        public string ItemId { get; set; }
        public float TileX { get; set; }
        public float TileY { get; set; }
        public string Name { get; set; }
    }
}