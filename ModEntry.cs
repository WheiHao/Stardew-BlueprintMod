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
            mouseTile = new Vector2((int)mouseTile.X, (int)mouseTile.Y); // 坐标取整
            bool hasCollision = false;

            // 遍历整个蓝图结构进行扫描
            foreach (var item in previewItems)
            {
                Vector2 targetTile = new Vector2(mouseTile.X + item.TileX, mouseTile.Y + item.TileY);
                // 检查：原版物体阻挡 OR 已有虚影阻挡
                if (Game1.currentLocation.Objects.ContainsKey(targetTile) || placedGhosts.ContainsKey(targetTile))
                {
                    hasCollision = true;
                    break; 
                }
            }

            if (hasCollision)
            {
                Game1.playSound("cancel");
                Game1.showRedMessage("无法放置：蓝图范围内存在障碍物！");
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
                    bool isBlocked = Game1.currentLocation.Objects.ContainsKey(targetTile) || placedGhosts.ContainsKey(targetTile);
                    
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
                Item newItem = ItemRegistry.Create(item.ItemId);
                if (newItem is StardewValley.Object obj)
                {
                    obj.TileLocation = targetTile;
                    Game1.currentLocation.Objects.Add(targetTile, obj);
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
                        Name = obj.DisplayName 
                    });
                }
            }
            if (items.Count > 0)
            {
                string path = $"blueprints/blueprint_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                this.Helper.Data.WriteJsonFile(path, items);
                Game1.showGlobalMessage($"蓝图已保存: {path}");
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
    }
}