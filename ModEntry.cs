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
            // 每 20 刻 (约 0.3 秒) 检查一次，避免性能消耗
            if (!e.IsMultipleOf(20) || placedGhosts.Count == 0 || Game1.currentLocation == null)
                return;

            List<Vector2> toRemove = new List<Vector2>();
            foreach (var ghost in placedGhosts)
            {
                Vector2 tile = ghost.Key;
                string requiredId = ghost.Value;

                // 检查物体
                if (Game1.currentLocation.Objects.TryGetValue(tile, out var obj) && obj.QualifiedItemId == requiredId)
                {
                    toRemove.Add(tile);
                    continue;
                }

                // 检查地板
                if (Game1.currentLocation.terrainFeatures.TryGetValue(tile, out var feature) && feature is StardewValley.TerrainFeatures.Flooring flooring)
                {
                    // 将地板的 ID 转为 QualifiedID 格式进行对比
                    string flooringId = "(O)" + (flooring.GetData()?.ItemId ?? "");
                    if (flooringId == requiredId)
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
            // 1. 【核心修复】最高优先级拦截：只要按住 Ctrl，彻底屏蔽游戏原版鼠标动作
            if (Helper.Input.IsDown(SButton.LeftControl))
            {
                if (e.Button == SButton.MouseLeft || e.Button == SButton.MouseRight)
                {
                    Helper.Input.Suppress(e.Button);
                }
            }

            // 2. 模式切换 (K键)
            if (e.Button == SButton.K)
            {
                isCreativeMode = !isCreativeMode;
                string modeName = isCreativeMode ? "创造模式" : "生存模式";
                Game1.addHUDMessage(new HUDMessage($"蓝图模式: {modeName}", 3));
                return;
            }

            // 2.5 清除所有虚影 (Ctrl + Shift + C)
            if (e.Button == SButton.C && Helper.Input.IsDown(SButton.LeftControl) && Helper.Input.IsDown(SButton.LeftShift))
            {
                placedGhosts.Clear();
                Game1.playSound("trashcan");
                Game1.addHUDMessage(new HUDMessage("已清除所有蓝图虚影", 3));
                return;
            }

            // 3. 虚影拆除逻辑 (Ctrl + 右键)
            // 注意：坐标必须取整以保证字典匹配的精确度
            if (e.Button == SButton.MouseRight && Helper.Input.IsDown(SButton.LeftControl))
            {
                Vector2 tile = new Vector2((int)e.Cursor.Tile.X, (int)e.Cursor.Tile.Y);
                if (placedGhosts.ContainsKey(tile))
                {
                    placedGhosts.Remove(tile);
                    Game1.playSound("hammer"); // 播放拆除音效
                    return;
                }
            }

            // 4. 预览模式下的放置与切换逻辑
            if (isPreviewMode)
            {
                // 预览时强制拦截鼠标，防止干扰
                if (e.Button == SButton.MouseLeft || e.Button == SButton.MouseRight)
                    Helper.Input.Suppress(e.Button);

                if (e.Button == SButton.MouseLeft)
                {
                    HandlePlacementAttempt();
                }
                else if (e.Button == SButton.MouseRight)
                {
                    // 右键退出预览
                    isPreviewMode = false;
                    previewItems = null;
                }
                else if (e.Button == SButton.Left || e.Button == SButton.Right)
                {
                    // 左右方向键切换蓝图
                    SwitchBlueprint(e.Button == SButton.Right);
                }
                return;
            }

            // 5. 生存模式填充逻辑 (非预览、无Ctrl点击时)
            if (!isCreativeMode && e.Button == SButton.MouseLeft && !Helper.Input.IsDown(SButton.LeftControl))
            {
                HandleGhostFilling(new Vector2((int)e.Cursor.Tile.X, (int)e.Cursor.Tile.Y));
            }

            // 6. 记录蓝图范围 (Ctrl + 左键)
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
            // 开启预览菜单 (Ctrl + L)
            else if (e.Button == SButton.L && Helper.Input.IsDown(SButton.LeftControl))
            {
                EnterPreviewMode();
            }
        }

        // 方案 A 核心：全局预检放置逻辑
        private void HandlePlacementAttempt()
        {
            Vector2 mouseTile = Helper.Input.GetCursorPosition().Tile;
            mouseTile = new Vector2((int)mouseTile.X, (int)mouseTile.Y);
            bool hasHardCollision = false;

            // 遍历整个蓝图结构进行扫描
            foreach (var item in previewItems)
            {
                Vector2 targetTile = new Vector2(mouseTile.X + item.TileX, mouseTile.Y + item.TileY);
                
                // 检查是否有“硬障碍”
                if (Game1.currentLocation.Objects.TryGetValue(targetTile, out var obj))
                {
                    // 如果该位置有物体，且它不是杂草、石头、树枝等可清理碎屑
                    if (!IsDebris(obj))
                    {
                        hasHardCollision = true;
                        break; 
                    }
                }
            }

            // 创造模式下无视一切障碍（因为我们会清理），生存模式下只拦截硬障碍
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

        // 判断是否为可清理的杂物
        private bool IsDebris(StardewValley.Object obj)
        {
            // 1.6 版本中，可以使用 IsWeeds()，并结合名称判断碎石和枯枝
            if (obj == null) return false;
            
            string name = obj.Name ?? "";
            return obj.IsWeeds() 
                || name.Contains("Stone") 
                || name.Contains("Twig") 
                || name.Contains("Weed")
                || obj.QualifiedItemId == "(O)343" // 常见的石头 ID
                || obj.QualifiedItemId == "(O)450" // 另一种石头 ID
                || obj.QualifiedItemId == "(O)294" // 常见的木枝 ID
                || obj.QualifiedItemId == "(O)295"; // 另一种木枝 ID
        }

        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            // 绘制选取框
            if (startTile.HasValue)
                DrawSelectionBox(e.SpriteBatch, startTile.Value, Helper.Input.GetCursorPosition().Tile, Color.White * 0.3f);

            // 绘制放置预览
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
                    
                    // 红色表示被挡，绿色(创造)或青色(生存)表示可用
                    Color previewColor = isBlocked ? Color.Red * 0.8f : (isCreativeMode ? Color.LightGreen : Color.Cyan);
                    DrawGhost(e.SpriteBatch, targetTile, item.ItemId, 0.5f, previewColor);
                }
                
                string fileName = blueprintFiles[currentBlueprintIndex].Name;
                e.SpriteBatch.DrawString(Game1.dialogueFont, $"当前蓝图: {fileName}", new Vector2(100, 100), Color.White);
            }

            // 持续渲染已放置的虚影
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
                    // 1. 清理该位置的旧地形特征 (杂草、老地砖等)
                    if (Game1.currentLocation.terrainFeatures.ContainsKey(targetTile))
                        Game1.currentLocation.terrainFeatures.Remove(targetTile);
                    
                    // 2. 移除 (O) 前缀以获取原始 ID
                    string rawId = item.ItemId;
                    if (rawId.StartsWith("(O)")) rawId = rawId.Substring(3);

                    // 3. 放置地板
                    Game1.currentLocation.terrainFeatures.Add(targetTile, new StardewValley.TerrainFeatures.Flooring(rawId));
                }
                else
                {
                    // 移除旧物体防止重叠
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
                    
                    // 尝试作为地板放置
                    var itemData = ItemRegistry.GetData(requiredId);
                    if (itemData?.ObjectType == "Flooring" || requiredId.Contains("Path") || requiredId.Contains("Floor"))
                    {
                        if (!Game1.currentLocation.terrainFeatures.ContainsKey(tile))
                        {
                            // 同样需要移除 (O) 前缀
                            string rawId = requiredId;
                            if (rawId.StartsWith("(O)")) rawId = rawId.Substring(3);

                            Game1.currentLocation.terrainFeatures.Add(tile, new StardewValley.TerrainFeatures.Flooring(rawId));
                            placed = true;
                        }
                    }
                    
                    // 如果不是地板或地板放置失败，尝试作为普通物体放置
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

            // 保存普通物体
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

            // 保存地板/道路
            foreach (var pair in location.terrainFeatures.Pairs)
            {
                Vector2 tile = pair.Key;
                if (tile.X >= minX && tile.X <= maxX && tile.Y >= minY && tile.Y <= maxY)
                {
                    if (pair.Value is StardewValley.TerrainFeatures.Flooring flooring)
                    {
                        string itemId = flooring.GetData()?.ItemId;
                        if (itemId != null)
                        {
                            items.Add(new BlueprintItem {
                                ItemId = "(O)" + itemId, // 保持 QualifiedID 格式
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
        public float TileX { get; set; }
        public float TileY { get; set; }
        public string Name { get; set; }
        public string ItemType { get; set; } = "Object";
    }
}