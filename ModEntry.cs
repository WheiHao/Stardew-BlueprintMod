using System;
using Microsoft.Xna.Framework;
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
            // 注册按键按下事件
            helper.Events.Input.ButtonPressed += OnButtonPressed;
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            // --- 逻辑 A: 蓝图记录 (Ctrl + 鼠标左键) ---
            if (e.Button == SButton.MouseLeft && Helper.Input.IsDown(SButton.LeftControl))
            {
                // 使用 Tile 属性获取鼠标点击的准确格子
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
                    this.Monitor.Log($"终点已设置：{endTile}. 开始扫描区域...", LogLevel.Debug);

                    List<BlueprintItem> items = new List<BlueprintItem>();

                    // 计算区域边界
                    int minX = (int)Math.Min(startTile.Value.X, endTile.X);
                    int maxX = (int)Math.Max(startTile.Value.X, endTile.X);
                    int minY = (int)Math.Min(startTile.Value.Y, endTile.Y);
                    int maxY = (int)Math.Max(startTile.Value.Y, endTile.Y);

                    // 遍历当前地图的所有物品
                    foreach (var tile in location.Objects.Keys)
                    {
                        if (tile.X >= minX && tile.X <= maxX && tile.Y >= minY && tile.Y <= maxY)
                        {
                            var obj = location.Objects[tile];
                            items.Add(new BlueprintItem
                            {
                                // 使用 QualifiedItemId 确保 1.6 版本的物品识别准确
                                ItemId = obj.QualifiedItemId, 
                                TileX = tile.X - minX,
                                TileY = tile.Y - minY,
                                Name = obj.DisplayName
                            });
                        }
                    }

                    if (items.Count == 0)
                    {
                        this.Monitor.Log("选定区域内没有可记录的物品。", LogLevel.Warn);
                    }
                    else
                    {
                        string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string fileName = $"blueprint_{timeStamp}.json";
                        // 保存到 Mod 根目录下的 blueprints 文件夹
                        this.Helper.Data.WriteJsonFile($"blueprints/{fileName}", items);
                        this.Monitor.Log($"已成功保存 {items.Count} 个物品到 {fileName}", LogLevel.Info);
                    }

                    startTile = null; // 重置起点
                }
            }
            // --- 逻辑 B: 蓝图加载 (Ctrl + L) ---
            else if (e.Button == SButton.L && Helper.Input.IsDown(SButton.LeftControl))
            {
                LoadAndPlaceBlueprint();
            }
        }

        private void LoadAndPlaceBlueprint()
        {
            string folderPath = Path.Combine(this.Helper.DirectoryPath, "blueprints");
            if (!Directory.Exists(folderPath))
            {
                this.Monitor.Log("蓝图文件夹不存在！", LogLevel.Error);
                return;
            }

            // 获取最新修改的蓝图文件
            var directory = new DirectoryInfo(folderPath);
            var latestFile = directory.GetFiles("*.json")
                                     .OrderByDescending(f => f.LastWriteTime)
                                     .FirstOrDefault();

            if (latestFile == null)
            {
                this.Monitor.Log("未找到蓝图文件。", LogLevel.Warn);
                return;
            }

            List<BlueprintItem> savedItems = this.Helper.Data.ReadJsonFile<List<BlueprintItem>>($"blueprints/{latestFile.Name}");
            if (savedItems == null) return;

            GameLocation location = Game1.currentLocation;
            Vector2 playerPos = Game1.player.Tile; // 以玩家当前位置为粘贴起点

            int placedCount = 0;
            foreach (var item in savedItems)
            {
                Vector2 targetTile = new Vector2(playerPos.X + item.TileX, playerPos.Y + item.TileY);

                // 使用 1.6 推荐的 ItemRegistry 来创建物品，解决红叉错误
                Item newItem = ItemRegistry.Create(item.ItemId);
                
                if (newItem is StardewValley.Object obj)
                {
                    obj.TileLocation = targetTile;
                    
                    // 检查目标格子是否已有物品
                    if (!location.Objects.ContainsKey(targetTile))
                    {
                        location.Objects.Add(targetTile, obj);
                        placedCount++;
                    }
                }
            }
            this.Monitor.Log($"成功从 {latestFile.Name} 加载并放置了 {placedCount} 个物品。", LogLevel.Info);
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