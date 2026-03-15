using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics; // 必须引用这个来处理绘图
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

        public override void Entry(IModHelper helper)
        {
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            // 新增：订阅每帧渲染事件
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            // --- 逻辑 A: 记录蓝图 ---
            if (e.Button == SButton.MouseLeft && Helper.Input.IsDown(SButton.LeftControl))
            {
                Vector2 currentTile = e.Cursor.Tile;
                GameLocation location = Game1.currentLocation;

                if (startTile == null)
                {
                    startTile = currentTile;
                    this.Monitor.Log($"起点已设置：{startTile}", LogLevel.Debug);
                }
                else
                {
                    Vector2 endTile = currentTile;
                    SaveBlueprint(location, startTile.Value, endTile);
                    startTile = null; // 扫描完重置
                }
            }
            // --- 逻辑 B: 粘贴蓝图 (Ctrl + L) ---
            else if (e.Button == SButton.L && Helper.Input.IsDown(SButton.LeftControl))
            {
                LoadAndPlaceBlueprint();
            }
        }

        // --- 新增：实时绘制选区框 ---
        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            // 只有当设置了起点时才画框
            if (startTile.HasValue)
            {
                Vector2 currentMouseTile = Helper.Input.GetCursorPosition().Tile;
                
                // 计算区域
                int minX = (int)Math.Min(startTile.Value.X, currentMouseTile.X);
                int maxX = (int)Math.Max(startTile.Value.X, currentMouseTile.X);
                int minY = (int)Math.Min(startTile.Value.Y, currentMouseTile.Y);
                int maxY = (int)Math.Max(startTile.Value.Y, currentMouseTile.Y);

                // 将格子坐标转换为屏幕像素坐标
                // 星露谷每个格子是 64x64 像素
                Rectangle displayArea = new Rectangle(
                    minX * 64 - (int)Game1.viewport.X,
                    minY * 64 - (int)Game1.viewport.Y,
                    (maxX - minX + 1) * 64,
                    (maxY - minY + 1) * 64
                );

                // 在地面画一个半透明的白色矩形
                // 使用内置的白色像素贴图，设置 0.3 的透明度
                e.SpriteBatch.Draw(
                    Game1.staminaRect, 
                    displayArea, 
                    Color.White * 0.3f
                );
            }
        }

        // 提取出的保存逻辑
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

            if (items.Count > 0)
            {
                string fileName = $"blueprint_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                this.Helper.Data.WriteJsonFile($"blueprints/{fileName}", items);
                this.Monitor.Log($"已保存 {items.Count} 个物品。", LogLevel.Info);
            }
        }

        private void LoadAndPlaceBlueprint()
        {
            string folderPath = Path.Combine(this.Helper.DirectoryPath, "blueprints");
            if (!Directory.Exists(folderPath)) return;

            var latestFile = new DirectoryInfo(folderPath).GetFiles("*.json")
                                 .OrderByDescending(f => f.LastWriteTime)
                                 .FirstOrDefault();

            if (latestFile == null) return;

            List<BlueprintItem> savedItems = this.Helper.Data.ReadJsonFile<List<BlueprintItem>>($"blueprints/{latestFile.Name}");
            if (savedItems == null) return;

            GameLocation location = Game1.currentLocation;
            Vector2 playerPos = Game1.player.Tile;

            foreach (var item in savedItems)
            {
                Vector2 targetTile = new Vector2(playerPos.X + item.TileX, playerPos.Y + item.TileY);
                Item newItem = ItemRegistry.Create(item.ItemId);
                if (newItem is StardewValley.Object obj)
                {
                    obj.TileLocation = targetTile;
                    if (!location.Objects.ContainsKey(targetTile))
                        location.Objects.Add(targetTile, obj);
                }
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