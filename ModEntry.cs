using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlueprintMod
{
    public class ModEntry : Mod
    {
        private Vector2? startTile = null;
        private List<BlueprintItem> previewItems = null; // 存储预览中的物品
        private bool isPreviewMode = false;

        public override void Entry(IModHelper helper)
        {
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            // --- 情况 1: 预览模式下的操作 ---
            if (isPreviewMode)
            {
                if (e.Button == SButton.MouseLeft) // 左键确认放置
                {
                    PlaceBlueprint(Helper.Input.GetCursorPosition().Tile);
                    isPreviewMode = false;
                    previewItems = null;
                    Helper.Input.Suppress(SButton.MouseLeft); // 防止触发游戏内原本的左键动作
                }
                else if (e.Button == SButton.MouseRight) // 右键取消预览
                {
                    isPreviewMode = false;
                    previewItems = null;
                    this.Monitor.Log("预览已取消", LogLevel.Debug);
                }
                return; // 预览模式下不执行其他逻辑
            }

            // --- 情况 2: 记录蓝图 (Ctrl + Left Click) ---
            if (e.Button == SButton.MouseLeft && Helper.Input.IsDown(SButton.LeftControl))
            {
                Vector2 currentTile = e.Cursor.Tile;
                if (startTile == null) {
                    startTile = currentTile;
                } else {
                    SaveBlueprint(Game1.currentLocation, startTile.Value, currentTile);
                    startTile = null;
                }
            }
            // --- 情况 3: 开启预览 (Ctrl + L) ---
            else if (e.Button == SButton.L && Helper.Input.IsDown(SButton.LeftControl))
            {
                EnterPreviewMode();
            }
        }

        private void EnterPreviewMode()
        {
            string folderPath = Path.Combine(this.Helper.DirectoryPath, "blueprints");
            if (!Directory.Exists(folderPath)) return;

            var latestFile = new DirectoryInfo(folderPath).GetFiles("*.json")
                                 .OrderByDescending(f => f.LastWriteTime)
                                 .FirstOrDefault();

            if (latestFile == null) return;

            previewItems = this.Helper.Data.ReadJsonFile<List<BlueprintItem>>($"blueprints/{latestFile.Name}");
            if (previewItems != null)
            {
                isPreviewMode = true;
                this.Monitor.Log($"已开启预览：{latestFile.Name}。左键点击放置，右键取消。", LogLevel.Info);
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
            this.Monitor.Log("蓝图已放置完成！", LogLevel.Info);
        }

        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            // 1. 绘制“记录区域”的白框
            if (startTile.HasValue)
            {
                DrawSelectionBox(e.SpriteBatch, startTile.Value, Helper.Input.GetCursorPosition().Tile, Color.White * 0.3f);
            }

            // 2. 绘制“粘贴预览”的虚影
            if (isPreviewMode && previewItems != null)
            {
                Vector2 mouseTile = Helper.Input.GetCursorPosition().Tile;
                foreach (var item in previewItems)
                {
                    // 计算每个预览物品的像素位置
                    float drawX = (mouseTile.X + item.TileX) * 64 - Game1.viewport.X;
                    float drawY = (mouseTile.Y + item.TileY) * 64 - Game1.viewport.Y;
                    Rectangle destRect = new Rectangle((int)drawX, (int)drawY, 64, 64);

                    // 绘制一个半透明的蓝色方块代表虚影（进阶版可以绘制物品图标，目前先用方块确认位置）
                    e.SpriteBatch.Draw(Game1.staminaRect, destRect, Color.Cyan * 0.5f);
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

        // 保存逻辑保持不变... (SaveBlueprint 方法)
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
                        Name = obj.DisplayName
                    });
                }
            }
            if (items.Count > 0) {
                this.Helper.Data.WriteJsonFile($"blueprints/blueprint_{DateTime.Now:yyyyMMdd_HHmmss}.json", items);
                this.Monitor.Log($"已保存 {items.Count} 个物品。", LogLevel.Info);
            }
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