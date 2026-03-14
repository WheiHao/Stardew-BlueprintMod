using System;
using Microsoft.Xna.Framework; // 用于 Vector2 结构
using StardewModdingAPI;
using StardewModdingAPI.Events;
using xTile.Tiles;
using StardewValley;
using System.Collections.Generic;

namespace BlueprintMod
{
    // 这里的 ": Mod" 表示继承 SMAPI 的基础功能
    public class ModEntry : Mod
    {
        private Vector2? startTile = null; // 用来记录蓝图的起始位置

        public override void Entry(IModHelper helper) // Entry 是 Mod 的入口方法，就像游戏的“启动开关”
        {
            // 我们在这里告诉游戏：当玩家按下键盘按键时，请通知我们
            helper.Events.Input.ButtonPressed += OnButtonPressed;
        }

        // 这是一个“事件处理方法”，每当按键按下，这段代码就会运行
        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (e.Button == SButton.MouseLeft && Helper.Input.IsDown(SButton.LeftControl))
            {
                Vector2 currentTile = e.Cursor.GrabTile; // 获取当前鼠标所在的格子坐标

                GameLocation location = Game1.currentLocation;// 获取玩家当前所在的地图位置
                if (startTile == null)
                {
                    startTile = currentTile;
                    this.Monitor.Log($"起点已设置：{startTile}", LogLevel.Debug);
                }
                else
                {
                    //如果已经有起点，当前的点击就是终点
                    Vector2 endTile = currentTile;
                    this.Monitor.Log($"终点已设置：{endTile}. 准备开始扫描区域", LogLevel.Debug);
                    
                    List<BlueprintItem> items = new List<BlueprintItem>(); // 用来存储扫描到的物品信息

                    // 计算出扫描区域的边界
                    int minX = (int)Math.Min(startTile.Value.X, endTile.X);
                    int maxX = (int)Math.Max(startTile.Value.X, endTile.X);
                    int minY = (int)Math.Min(startTile.Value.Y, endTile.Y);
                    int maxY = (int)Math.Max(startTile.Value.Y, endTile.Y);
                    
                    for (int x = minX; x <= maxX; x++)
                    {
                        for (int y = minY; y <= maxY; y++)
                        {
                            Vector2 tile = new Vector2(x, y);
                            if(location.Objects.ContainsKey(tile))
                            {
                                var obj = location.Objects[tile];// 获取这个格子上的物品
                                BlueprintItem newItem = new BlueprintItem //创建蓝图项并赋值
                                {
                                    ItemId = obj.Name, // 物品的名称作为 ID
                                    TileX = x - minX, //计算相对于起点的x坐标
                                    TileY = y - minY //计算相对于起点的y坐标
                                };
                                items.Add (newItem); // 将这个物品添加到列表中
                            }
                        }
                    }//双重循环结束，扫描完成
                    
                    if (items.Count == 0)
                    {
                        this.Monitor.Log("在选定的区域内没有可记录的物品。", LogLevel.Warn);
                    }
                    else
                    {
                        this.Monitor.Log($"扫描完成，找到 {items.Count} 个物品,准备保存蓝图", LogLevel.Info);

                        string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");//获取当前时间戳，作为文件名的一部分
                        string fileName = $"blueprint_{timeStamp}.json"; //构造文件名                                                         
                        string path = $"blueprints/{fileName}"; //把文件夹名和文件名组合在一起
                        this.Helper.Data.WriteJsonFile(path, items);//将物品列表保存为 JSON 文件
                    }
                        startTile = null; // 重置起点，为下一次扫描做准备

                }
            }
        }
    }
    public class BlueprintItem
    {
        public string ItemId { get; set; }   // 物品的 ID（比如 "Cask"）
        public float TileX { get; set; }     // 横向坐标
        public float TileY { get; set; }     // 纵向坐标
        public string Data { get; set; }      // 额外数据（比如物品的状态）
    }
}