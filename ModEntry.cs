using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlueprintMod
{
    public class ModEntry : Mod
    {
        private ModConfig Config;
        private Vector2? startTile = null;
        private List<BlueprintItem> previewItems = null;
        private BlueprintMetadata currentMetadata = null;
        private bool isPreviewMode = false;
        private List<FileInfo> blueprintFiles = new List<FileInfo>();
        private int currentBlueprintIndex = 0;
        private List<GhostItem> placedGhosts = new List<GhostItem>();
        private List<PlacementAction> undoStack = new List<PlacementAction>();
        private bool isCreativeMode = false;
        private bool isOverwriteMode = true;

        private Vector2? pendingTile = null;

        public override void Entry(IModHelper helper)
        {
            this.Config = helper.ReadConfig<ModConfig>();
            this.isCreativeMode = this.Config.DefaultCreativeMode;
            this.isOverwriteMode = this.Config.DefaultOverwriteMode;

            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
            helper.Events.Display.RenderedHud += OnRenderedHud;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu != null)
            {
                configMenu.Register(
                    mod: ModManifest,
                    reset: () => Config = new ModConfig(),
                    save: () => Helper.WriteConfig(Config)
                );

                configMenu.AddKeybind(ModManifest, () => Config.ToggleCreativeMode, val => Config.ToggleCreativeMode = val, () => Helper.Translation.Get("config.creative-mode-key"));
                configMenu.AddKeybind(ModManifest, () => Config.ToggleOverwriteMode, val => Config.ToggleOverwriteMode = val, () => Helper.Translation.Get("config.overwrite-mode-key"));
                configMenu.AddKeybind(ModManifest, () => Config.OpenBlueprintBrowser, val => Config.OpenBlueprintBrowser = val, () => Helper.Translation.Get("config.preview-key"));
                configMenu.AddKeybind(ModManifest, () => Config.ClearGhosts, val => Config.ClearGhosts = val, () => Helper.Translation.Get("config.clear-ghosts-key"));
                configMenu.AddBoolOption(ModManifest, () => Config.DefaultOverwriteMode, val => Config.DefaultOverwriteMode = val, () => "默认开启覆盖模式");
            }
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!e.IsMultipleOf(20) || placedGhosts.Count == 0 || Game1.currentLocation == null) return;
            placedGhosts.RemoveAll(ghost => {
                Vector2 tile = ghost.Tile;
                if (Game1.currentLocation.Objects.TryGetValue(tile, out var obj) && obj.QualifiedItemId == ghost.ItemId) return true;
                if (Game1.currentLocation.terrainFeatures.TryGetValue(tile, out var feature) && feature is StardewValley.TerrainFeatures.Flooring flooring)
                {
                    if ("(O)" + (flooring.GetData()?.ItemId ?? "") == ghost.ItemId) return true;
                }
                return false;
            });
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (Game1.activeClickableMenu != null) return;

            if (e.Button == SButton.Escape)
            {
                if (startTile != null)
                {
                    startTile = null;
                    Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.selection-canceled"), 3));
                    Helper.Input.Suppress(e.Button);
                    return;
                }
                else if (isPreviewMode)
                {
                    isPreviewMode = false;
                    previewItems = null;
                    Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.preview-exited"), 3));
                    Helper.Input.Suppress(e.Button);
                    return;
                }
            }

            if (Helper.Input.IsDown(Config.ModModifier) && (e.Button == SButton.MouseLeft || e.Button == SButton.MouseRight)) Helper.Input.Suppress(e.Button);

            if (e.Button == Config.ToggleCreativeMode)
            {
                isCreativeMode = !isCreativeMode;
                Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get(isCreativeMode ? "msg.mode-creative" : "msg.mode-survival"), 3));
            }
            else if (e.Button == Config.ToggleOverwriteMode)
            {
                isOverwriteMode = !isOverwriteMode;
                Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get(isOverwriteMode ? "msg.overwrite-on" : "msg.overwrite-off"), 3));
            }
            else if (e.Button == Config.ClearGhosts && Helper.Input.IsDown(Config.ModModifier) && Helper.Input.IsDown(SButton.LeftShift))
            {
                placedGhosts.Clear();
                Game1.playSound("trashcan");
            }
            else if (isPreviewMode)
            {
                if (e.Button == SButton.MouseLeft || e.Button == SButton.MouseRight) Helper.Input.Suppress(e.Button);
                if (e.Button == SButton.MouseLeft) HandlePlacementAttempt();
                else if (e.Button == SButton.MouseRight) { isPreviewMode = false; previewItems = null; Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.preview-exited"), 3)); }
                else if (e.Button == SButton.Left || e.Button == SButton.Right) SwitchBlueprint(e.Button == SButton.Right);
            }
            else if (!isCreativeMode && e.Button == SButton.MouseLeft && !Helper.Input.IsDown(Config.ModModifier))
            {
                HandleGhostFilling(new Vector2((int)e.Cursor.Tile.X, (int)e.Cursor.Tile.Y));
            }
            else if (Helper.Input.IsDown(Config.ModModifier) && (e.Button == SButton.MouseLeft || e.Button == SButton.MouseRight))
            {
                if (e.Button == SButton.MouseLeft)
                {
                    if (startTile == null) { startTile = e.Cursor.Tile; Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.start-tile-set"), 3)); }
                    else
                    {
                        Vector2 capturedStart = startTile.Value;
                        Vector2 capturedEnd = e.Cursor.Tile;
                        
                        Game1.activeClickableMenu = new NamingMenu(name => {
                            SaveBlueprint(Game1.currentLocation, capturedStart, capturedEnd, name);
                            startTile = null;
                            Game1.exitActiveMenu();
                        }, Helper.Translation.Get("msg.naming-title"), Helper.Translation.Get("msg.naming-default"));
                    }
                }
                else if (e.Button == SButton.MouseRight && startTile != null)
                {
                    startTile = null;
                    Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.selection-canceled"), 3));
                }
            }
            else if (e.Button == Config.OpenBlueprintBrowser && Helper.Input.IsDown(Config.ModModifier)) EnterPreviewMode();
            else if (e.Button == Config.UndoKey && Helper.Input.IsDown(Config.ModModifier)) UndoLastPlacement();
        }

        private void HandlePlacementAttempt()
        {
            Vector2 mouseTile = new Vector2((int)Helper.Input.GetCursorPosition().Tile.X, (int)Helper.Input.GetCursorPosition().Tile.Y);
            bool hasCollision = false;

            if (!isOverwriteMode)
            {
                for (int x = 0; x < currentMetadata.Width; x++)
                {
                    for (int y = 0; y < currentMetadata.Height; y++)
                    {
                        Vector2 targetTile = new Vector2(mouseTile.X + x, mouseTile.Y + y);
                        var itemAtTile = previewItems.FirstOrDefault(i => (int)i.TileX == x && (int)i.TileY == y);
                        if (Game1.currentLocation.Objects.TryGetValue(targetTile, out var worldObj) && !IsDebris(worldObj)) { hasCollision = true; break; }
                        if (Game1.currentLocation.terrainFeatures.TryGetValue(targetTile, out var feature))
                        {
                            if (feature is StardewValley.TerrainFeatures.Flooring wf)
                            {
                                if (itemAtTile != null && itemAtTile.ItemType == "Flooring" && wf.whichFloor.Value != itemAtTile.FlooringId) { hasCollision = true; break; }
                            }
                            else { hasCollision = true; break; }
                        }
                    }
                    if (hasCollision) break;
                }
            }

            if (hasCollision) { Game1.playSound("cancel"); Game1.showRedMessage(Helper.Translation.Get("msg.error-collision")); return; }

            if (!isCreativeMode)
            {
                var requirements = previewItems.GroupBy(i => i.ItemId).Select(g => new ItemRequirement { ItemId = g.Key, Count = g.Count() }).ToList();
                bool hasEverything = requirements.All(req => Game1.player.Items.CountId(req.ItemId) >= req.Count);

                if (hasEverything)
                {
                    pendingTile = mouseTile;
                    var savedRequirements = requirements;
                    var savedPreviewItems = previewItems;
                    isPreviewMode = false;
                    previewItems = null;

                    Game1.currentLocation.createQuestionDialogue(
                        Helper.Translation.Get("msg.confirm-build"),
                        Game1.currentLocation.createYesNoResponses(),
                        (who, answer) => {
                            if (answer == "Yes")
                            {
                                foreach (var req in savedRequirements) Game1.player.Items.ReduceId(req.ItemId, req.Count);
                                previewItems = savedPreviewItems;
                                PlaceBlueprintReal(pendingTile.Value, savedRequirements);
                                previewItems = null;
                                Game1.playSound("purchase");
                            }
                            else
                            {
                                previewItems = savedPreviewItems;
                                PlaceGhosts(pendingTile.Value);
                                previewItems = null;
                            }
                        }
                    );
                    return;
                }
            }

            if (isCreativeMode) PlaceBlueprintReal(mouseTile); else PlaceGhosts(mouseTile);
            isPreviewMode = false; previewItems = null; Game1.playSound("purchase");
        }

        private bool IsDebris(StardewValley.Object obj)
        {
            if (obj == null) return false;
            string name = obj.Name ?? "";
            return obj.IsWeeds() || name.Contains("Stone") || name.Contains("Twig") || name.Contains("Weed")
                || obj.QualifiedItemId == "(O)343" || obj.QualifiedItemId == "(O)450" || obj.QualifiedItemId == "(O)294" || obj.QualifiedItemId == "(O)295";
        }

        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (startTile.HasValue) DrawSelectionBox(e.SpriteBatch, startTile.Value, Helper.Input.GetCursorPosition().Tile, Color.White * 0.3f);
            if (isPreviewMode && previewItems != null)
            {
                Vector2 mouseTile = new Vector2((int)Helper.Input.GetCursorPosition().Tile.X, (int)Helper.Input.GetCursorPosition().Tile.Y);
                var itemLookup = previewItems.ToLookup(i => new Vector2(i.TileX, i.TileY));

                for (int x = 0; x < currentMetadata.Width; x++)
                {
                    for (int y = 0; y < currentMetadata.Height; y++)
                    {
                        Vector2 relativeTile = new Vector2(x, y);
                        Vector2 targetTile = new Vector2(mouseTile.X + x, mouseTile.Y + y);
                        var itemsAtTile = itemLookup[relativeTile];
                        
                        bool tileBlockedByObject = Game1.currentLocation.Objects.TryGetValue(targetTile, out var worldObj) && !IsDebris(worldObj);
                        
                        if (itemsAtTile.Any())
                        {
                            foreach (var item in itemsAtTile)
                            {
                                bool isBlocked = false;
                                if (!isOverwriteMode)
                                {
                                    if (tileBlockedByObject) isBlocked = true;
                                    else if (Game1.currentLocation.terrainFeatures.TryGetValue(targetTile, out var feature))
                                    {
                                        if (feature is StardewValley.TerrainFeatures.Flooring wf)
                                        {
                                            if (item.ItemType == "Flooring" && wf.whichFloor.Value != item.FlooringId) isBlocked = true;
                                        }
                                        else isBlocked = true;
                                    }
                                }
                                DrawGhost(e.SpriteBatch, targetTile, item.ItemId, 0.5f, isBlocked ? Color.Red * 0.8f : (isCreativeMode ? Color.LightGreen : Color.Cyan));
                            }
                        }
                        else if (!isOverwriteMode)
                        {
                            bool isBlocked = tileBlockedByObject;
                            if (!isBlocked && Game1.currentLocation.terrainFeatures.TryGetValue(targetTile, out var feature) && !(feature is StardewValley.TerrainFeatures.Flooring)) isBlocked = true;
                            if (isBlocked) DrawSelectionBox(e.SpriteBatch, targetTile, targetTile, Color.Red * 0.2f);
                        }
                    }
                }
            }
            foreach (var ghost in placedGhosts) DrawGhost(e.SpriteBatch, ghost.Tile, ghost.ItemId, 0.4f, Color.White * 0.6f);
        }

        private void OnRenderedHud(object sender, RenderedHudEventArgs e)
        {
            if (isPreviewMode && previewItems != null)
            {
                string displayName = currentMetadata?.Name ?? blueprintFiles[currentBlueprintIndex].Name;
                string modeText = Helper.Translation.Get(isOverwriteMode ? "msg.mode-overwrite" : "msg.mode-safe");
                string topText = Helper.Translation.Get("msg.hud-blueprint", new { name = displayName, mode = modeText });
                e.SpriteBatch.DrawString(Game1.dialogueFont, topText, new Vector2(80, 80), Color.White);
                DrawShoppingList(e.SpriteBatch);
            }
        }

        private void DrawShoppingList(SpriteBatch b)
        {
            if (previewItems == null) return;
            var requirements = previewItems.GroupBy(i => i.ItemId).Select(g => new { ItemId = g.Key, RequiredCount = g.Count() }).ToList();
            int xPos = Game1.uiViewport.Width - 300, yPos = 150;
            b.Draw(Game1.staminaRect, new Rectangle(xPos - 10, yPos - 10, 280, requirements.Count * 40 + 60), Color.Black * 0.5f);
            b.DrawString(Game1.dialogueFont, Helper.Translation.Get("msg.shopping-list"), new Vector2(xPos, yPos), Color.Gold, 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 0f);
            yPos += 40;
            foreach (var req in requirements)
            {
                int playerHas = Game1.player.Items.CountId(req.ItemId);
                ParsedItemData itemData = ItemRegistry.GetData(req.ItemId);
                if (itemData != null)
                {
                    b.Draw(itemData.GetTexture(), new Rectangle(xPos, yPos, 32, 32), itemData.GetSourceRect(), Color.White);
                    b.DrawString(Game1.smallFont, $"{req.RequiredCount} ({playerHas})", new Vector2(xPos + 40, yPos + 4), playerHas >= req.RequiredCount ? Color.White : Color.Red);
                    yPos += 35;
                }
            }
        }

        private void PlaceBlueprintReal(Vector2 origin, List<ItemRequirement> refunds = null)
        {
            if (previewItems == null) return;

            PlacementAction action = new PlacementAction { Location = Game1.currentLocation, RefundItems = refunds ?? new List<ItemRequirement>() };
            var affectedTiles = previewItems.Select(i => new Vector2(origin.X + i.TileX, origin.Y + i.TileY)).Distinct();
            foreach (var tile in affectedTiles)
            {
                var change = new TileChange { Tile = tile };
                if (Game1.currentLocation.Objects.TryGetValue(tile, out var obj)) change.OldObject = obj;
                if (Game1.currentLocation.terrainFeatures.TryGetValue(tile, out var feature)) change.OldTerrainFeature = feature;
                action.Changes.Add(change);
            }

            var sortedItems = previewItems.OrderBy(i => i.ItemType == "Object" ? 1 : 0).ToList();
            foreach (var item in sortedItems)
            {
                Vector2 targetTile = new Vector2(origin.X + item.TileX, origin.Y + item.TileY);
                if (isOverwriteMode)
                {
                    if (Game1.currentLocation.Objects.ContainsKey(targetTile)) Game1.currentLocation.Objects.Remove(targetTile);
                    if (item.ItemType == "Flooring" && Game1.currentLocation.terrainFeatures.ContainsKey(targetTile)) Game1.currentLocation.terrainFeatures.Remove(targetTile);
                }
                if (item.ItemType == "Flooring")
                {
                    string fId = !string.IsNullOrEmpty(item.FlooringId) ? item.FlooringId : item.ItemId.Replace("(O)", "");
                    if (!Game1.currentLocation.terrainFeatures.ContainsKey(targetTile)) Game1.currentLocation.terrainFeatures.Add(targetTile, new StardewValley.TerrainFeatures.Flooring(fId));
                }
                else
                {
                    Item newItem = ItemRegistry.Create(item.ItemId);
                    if (newItem is StardewValley.Object obj) { obj.TileLocation = targetTile; Game1.currentLocation.Objects.Add(targetTile, obj); }
                }
            }

            undoStack.Add(action);
            if (undoStack.Count > Config.MaxUndoSteps) undoStack.RemoveAt(0);
        }

        private void PlaceGhosts(Vector2 origin)
        {
            if (previewItems == null) return;
            foreach (var item in previewItems)
            {
                Vector2 targetTile = new Vector2(origin.X + item.TileX, origin.Y + item.TileY);
                placedGhosts.Add(new GhostItem { Tile = targetTile, ItemId = item.ItemId });
            }
        }

        private void HandleGhostFilling(Vector2 tile)
        {
            var ghost = placedGhosts.FirstOrDefault(g => g.Tile == tile && Game1.player.ActiveItem?.QualifiedItemId == g.ItemId);
            if (ghost != null)
            {
                string reqId = ghost.ItemId;
                bool placed = false;
                var itemData = ItemRegistry.GetData(reqId);
                if (itemData?.ObjectType == "Flooring" || reqId.Contains("Path") || reqId.Contains("Floor"))
                {
                    if (!Game1.currentLocation.terrainFeatures.ContainsKey(tile))
                    {
                        string fId = reqId.Replace("(O)", "");
                        Game1.currentLocation.terrainFeatures.Add(tile, new StardewValley.TerrainFeatures.Flooring(fId));
                        placed = true;
                    }
                }
                else if (!Game1.currentLocation.Objects.ContainsKey(tile))
                {
                    Game1.currentLocation.Objects.Add(tile, (StardewValley.Object)ItemRegistry.Create(reqId));
                    placed = true;
                }
                if (placed)
                {
                    Game1.player.reduceActiveItemByOne();
                    placedGhosts.Remove(ghost);
                    Game1.playSound("dirtyHit");
                    Helper.Input.Suppress(SButton.MouseLeft);
                }
            }
        }

        private void DrawGhost(SpriteBatch b, Vector2 tile, string itemId, float alpha, Color tint)
        {
            ParsedItemData itemData = ItemRegistry.GetData(itemId);
            if (itemData != null)
            {
                Rectangle sourceRect = itemData.GetSourceRect();
                int width = sourceRect.Width * 4;
                int height = sourceRect.Height * 4;
                int x = (int)(tile.X * 64 - Game1.viewport.X);
                int y = (int)((tile.Y + 1) * 64 - Game1.viewport.Y - height);
                b.Draw(itemData.GetTexture(), new Rectangle(x, y, width, height), sourceRect, tint * alpha);
            }
        }

        private void DrawSelectionBox(SpriteBatch b, Vector2 start, Vector2 end, Color color)
        {
            int minX = (int)Math.Min(start.X, end.X), maxX = (int)Math.Max(start.X, end.X), minY = (int)Math.Min(start.Y, end.Y), maxY = (int)Math.Max(start.Y, end.Y);
            b.Draw(Game1.staminaRect, new Rectangle(minX * 64 - Game1.viewport.X, minY * 64 - Game1.viewport.Y, (maxX - minX + 1) * 64, (maxY - minY + 1) * 64), color);
        }

        private void UndoLastPlacement()
        {
            if (undoStack.Count == 0)
            {
                Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.undo-nothing"), 3));
                return;
            }

            PlacementAction action = undoStack.Last();
            undoStack.RemoveAt(undoStack.Count - 1);

            foreach (var change in action.Changes)
            {
                if (action.Location.Objects.ContainsKey(change.Tile)) action.Location.Objects.Remove(change.Tile);
                if (action.Location.terrainFeatures.ContainsKey(change.Tile)) action.Location.terrainFeatures.Remove(change.Tile);

                if (change.OldObject != null)
                {
                    change.OldObject.TileLocation = change.Tile;
                    action.Location.Objects.Add(change.Tile, change.OldObject);
                }
                if (change.OldTerrainFeature != null)
                {
                    action.Location.terrainFeatures.Add(change.Tile, change.OldTerrainFeature);
                }
            }

            foreach (var refund in action.RefundItems)
            {
                Game1.player.addItemToInventory(ItemRegistry.Create(refund.ItemId, refund.Count));
            }

            Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.undo-success"), 3));
            Game1.playSound("shwip");
        }

        private void SaveBlueprint(GameLocation location, Vector2 start, Vector2 end, string name)
        {
            int minX = (int)Math.Min(start.X, end.X), maxX = (int)Math.Max(start.X, end.X), minY = (int)Math.Min(start.Y, end.Y), maxY = (int)Math.Max(start.Y, end.Y);
            var items = new List<BlueprintItem>();
            foreach (var tile in location.Objects.Keys.Where(t => t.X >= minX && t.X <= maxX && t.Y >= minY && t.Y <= maxY))
                items.Add(new BlueprintItem { ItemId = location.Objects[tile].QualifiedItemId, TileX = tile.X - minX, TileY = tile.Y - minY, Name = location.Objects[tile].DisplayName, ItemType = "Object" });
            foreach (var pair in location.terrainFeatures.Pairs.Where(p => p.Key.X >= minX && p.Key.X <= maxX && p.Key.Y >= minY && p.Key.Y <= maxY))
                if (pair.Value is StardewValley.TerrainFeatures.Flooring f) items.Add(new BlueprintItem { ItemId = "(O)" + (f.GetData()?.ItemId ?? ""), FlooringId = f.whichFloor.Value, TileX = pair.Key.X - minX, TileY = pair.Key.Y - minY, Name = "Flooring", ItemType = "Flooring" });

            if (items.Count > 0)
            {
                string safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
                var data = new BlueprintFile { Metadata = new BlueprintMetadata { Name = name, Width = maxX - minX + 1, Height = maxY - minY + 1 }, Items = items };
                this.Helper.Data.WriteJsonFile($"blueprints/{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.json", data);
                Game1.playSound("drumkit0");
                Game1.showGlobalMessage(Helper.Translation.Get("msg.save-success", new { name = name }));
            }
        }

        private void EnterPreviewMode()
        {
            string path = Path.Combine(this.Helper.DirectoryPath, "blueprints");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            blueprintFiles = new DirectoryInfo(path).GetFiles("*.json").OrderByDescending(f => f.LastWriteTime).ToList();
            
            if (blueprintFiles.Count == 0) 
            {
                Game1.showRedMessage(Helper.Translation.Get("msg.error-no-blueprints"));
                return;
            }

            // 打开图形化浏览器
            Game1.activeClickableMenu = new BlueprintBrowserMenu(blueprintFiles, (selectedFile) => {
                currentBlueprintIndex = blueprintFiles.IndexOf(selectedFile);
                LoadCurrentBlueprint();
                isPreviewMode = true;
            });
        }

        private void SwitchBlueprint(bool next) { if (blueprintFiles.Count > 1) { currentBlueprintIndex = next ? (currentBlueprintIndex + 1) % blueprintFiles.Count : (currentBlueprintIndex - 1 + blueprintFiles.Count) % blueprintFiles.Count; LoadCurrentBlueprint(); Game1.playSound("shwip"); } }

        private void LoadCurrentBlueprint()
        {
            var file = this.Helper.Data.ReadJsonFile<BlueprintFile>($"blueprints/{blueprintFiles[currentBlueprintIndex].Name}");
            if (file != null && file.Items != null)
            {
                previewItems = file.Items;
                currentMetadata = file.Metadata;
            }
            else
            {
                Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.error-parse-failed"), 1));
                isPreviewMode = false;
            }
        }
    }

    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
        void AddKeybind(IManifest mod, Func<SButton> getValue, Action<SButton> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
        void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
    }


    public class BlueprintFile { public BlueprintMetadata Metadata { get; set; } public List<BlueprintItem> Items { get; set; } }
    public class BlueprintMetadata { public string Name { get; set; } public int Width { get; set; } public int Height { get; set; } }
    public class BlueprintItem { public string ItemId { get; set; } public string FlooringId { get; set; } public float TileX { get; set; } public float TileY { get; set; } public string Name { get; set; } public string ItemType { get; set; } = "Object"; }
    public class GhostItem { public Vector2 Tile { get; set; } public string ItemId { get; set; } }

    public class TileChange
    {
        public Vector2 Tile { get; set; }
        public StardewValley.Object OldObject { get; set; }
        public StardewValley.TerrainFeatures.TerrainFeature OldTerrainFeature { get; set; }
    }

    public class ItemRequirement
    {
        public string ItemId { get; set; }
        public int Count { get; set; }
    }

    public class PlacementAction
    {
        public GameLocation Location { get; set; }
        public List<TileChange> Changes { get; set; } = new List<TileChange>();
        public List<ItemRequirement> RefundItems { get; set; } = new List<ItemRequirement>();
    }
    }