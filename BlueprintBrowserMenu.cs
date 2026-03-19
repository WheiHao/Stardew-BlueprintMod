using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using StardewValley.BellsAndWhistles;

namespace BlueprintMod
{
    public class BlueprintBrowserMenu : IClickableMenu
    {
        private List<FileInfo> blueprintFiles;
        private List<ClickableComponent> blueprintButtons = new List<ClickableComponent>();
        private Action<FileInfo> onBlueprintSelected;
        
        private int itemsPerPage = 6;
        private int currentPage = 0;
        private ClickableTextureComponent upArrow;
        private ClickableTextureComponent downArrow;

        public BlueprintBrowserMenu(List<FileInfo> files, Action<FileInfo> onSelected)
            : base(Game1.uiViewport.Width / 2 - 400, Game1.uiViewport.Height / 2 - 300, 800, 600, true)
        {
            this.blueprintFiles = files;
            this.onBlueprintSelected = onSelected;
            
            this.upArrow = new ClickableTextureComponent(new Rectangle(this.xPositionOnScreen + this.width + 16, this.yPositionOnScreen + 16, 44, 48), Game1.mouseCursors, new Rectangle(421, 459, 11, 12), 4f);
            this.downArrow = new ClickableTextureComponent(new Rectangle(this.xPositionOnScreen + this.width + 16, this.yPositionOnScreen + this.height - 64, 44, 48), Game1.mouseCursors, new Rectangle(421, 472, 11, 12), 4f);
            
            LayoutButtons();
        }

        private void LayoutButtons()
        {
            blueprintButtons.Clear();
            int startIdx = currentPage * itemsPerPage;
            for (int i = 0; i < itemsPerPage && (startIdx + i) < blueprintFiles.Count; i++)
            {
                blueprintButtons.Add(new ClickableComponent(new Rectangle(this.xPositionOnScreen + 32, this.yPositionOnScreen + 80 + (i * 80), this.width - 64, 70), (startIdx + i).ToString()));
            }
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);
            
            if (upArrow.containsPoint(x, y) && currentPage > 0)
            {
                currentPage--;
                Game1.playSound("shwip");
                LayoutButtons();
            }
            if (downArrow.containsPoint(x, y) && (currentPage + 1) * itemsPerPage < blueprintFiles.Count)
            {
                currentPage++;
                Game1.playSound("shwip");
                LayoutButtons();
            }

            foreach (var button in blueprintButtons)
            {
                if (button.containsPoint(x, y))
                {
                    int index = int.Parse(button.name);
                    onBlueprintSelected?.Invoke(blueprintFiles[index]);
                    Game1.playSound("select");
                    exitThisMenu();
                    break;
                }
            }
        }

        public override void draw(SpriteBatch b)
        {
            // 背景遮罩
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.4f);
            
            // 菜单背景
            Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, false, true);
            
            // 标题
            SpriteText.drawStringWithScrollCenteredAt(b, "农场蓝图浏览器", this.xPositionOnScreen + this.width / 2, this.yPositionOnScreen + 20);

            // 绘制列表项
            foreach (var button in blueprintButtons)
            {
                int index = int.Parse(button.name);
                bool isHovered = button.containsPoint(Game1.getMouseX(), Game1.getMouseY());
                
                IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 396, 15, 15), button.bounds.X, button.bounds.Y, button.bounds.Width, button.bounds.Height, isHovered ? Color.Wheat : Color.White, 4f, false);
                
                string fileName = blueprintFiles[index].Name.Replace(".json", "");
                b.DrawString(Game1.dialogueFont, fileName, new Vector2(button.bounds.X + 20, button.bounds.Y + 12), Color.DarkSlateGray, 0f, Vector2.Zero, 0.7f, SpriteEffects.None, 0f);
            }

            if (currentPage > 0) upArrow.draw(b);
            if ((currentPage + 1) * itemsPerPage < blueprintFiles.Count) downArrow.draw(b);

            base.draw(b);
            drawMouse(b);
        }
    }
}