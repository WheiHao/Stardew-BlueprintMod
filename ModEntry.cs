using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

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
        private List<PlantingPlan> currentPlantingPlans = new List<PlantingPlan>();
        private Vector2? pendingPlantingPromptOrigin = null;

        private Vector2? pendingTile = null;

        public override void Entry(IModHelper helper)
        {
            this.Config = helper.ReadConfig<ModConfig>();
            this.isCreativeMode = this.Config.EnableCreativeMode && this.Config.DefaultCreativeMode;
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

                configMenu.AddKeybindList(ModManifest,
                    () => Config.ToggleCreativeMode,
                    val => Config.ToggleCreativeMode = val,
                    () => Helper.Translation.Get("config.creative-mode-key"));

                configMenu.AddBoolOption(ModManifest,
                    () => Config.EnableCreativeMode,
                    val =>
                    {
                        Config.EnableCreativeMode = val;
                        if (!val)
                            isCreativeMode = false;
                    },
                    () => Helper.Translation.Get("config.enable-creative-mode"),
                    () => Helper.Translation.Get("config.enable-creative-mode.tooltip"));

                configMenu.AddKeybindList(ModManifest,
                    () => Config.ToggleOverwriteMode,
                    val => Config.ToggleOverwriteMode = val,
                    () => Helper.Translation.Get("config.overwrite-mode-key"));

                configMenu.AddKeybindList(ModManifest,
                    () => Config.OpenBlueprintBrowser,
                    val => Config.OpenBlueprintBrowser = val,
                    () => Helper.Translation.Get("config.open-blueprint-browser-key"),
                    () => Helper.Translation.Get("config.open-blueprint-browser-key.tooltip"));

                configMenu.AddKeybindList(ModManifest,
                    () => Config.UndoKey,
                    val => Config.UndoKey = val,
                    () => Helper.Translation.Get("config.undo-key"));

                configMenu.AddKeybindList(ModManifest,
                    () => Config.ClearGhosts,
                    val => Config.ClearGhosts = val,
                    () => Helper.Translation.Get("config.clear-ghosts-key"),
                    () => Helper.Translation.Get("config.clear-ghosts-key.tooltip"));

                configMenu.AddKeybindList(ModManifest,
                    () => Config.AssistPlantingKey,
                    val => Config.AssistPlantingKey = val,
                    () => Helper.Translation.Get("config.assist-planting-key"),
                    () => Helper.Translation.Get("config.assist-planting-key.tooltip"));

                configMenu.AddBoolOption(ModManifest,
                    () => Config.DefaultOverwriteMode,
                    val => Config.DefaultOverwriteMode = val,
                    () => "默认开启覆盖模式");

                configMenu.AddTextOption(ModManifest,
                    () => Config.ExportPath,
                    val => Config.ExportPath = val ?? "",
                    () => Helper.Translation.Get("config.export-path"),
                    () => Helper.Translation.Get("config.export-path.tooltip"));
            }
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (Game1.currentLocation == null)
                return;

            if (e.IsMultipleOf(20) && placedGhosts.Count > 0)
                placedGhosts.RemoveAll(ghost => IsGhostInCurrentLocation(ghost) && IsGhostSatisfied(ghost));

            if (pendingPlantingPromptOrigin.HasValue && Game1.activeClickableMenu == null && Context.IsPlayerFree)
            {
                Vector2 origin = pendingPlantingPromptOrigin.Value;
                pendingPlantingPromptOrigin = null;
                TryOpenAssistedPlantingPrompt(origin);
            }
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

            if (Helper.Input.IsDown(Config.ModModifier) &&
                (e.Button == SButton.MouseLeft || e.Button == SButton.MouseRight))
            {
                Helper.Input.Suppress(e.Button);
            }

            // ✅ 修复：KeybindList 判断
            if (Config.ToggleCreativeMode.JustPressed())
            {
                if (!Config.EnableCreativeMode)
                {
                    isCreativeMode = false;
                    Game1.addHUDMessage(new HUDMessage(
                        Helper.Translation.Get("msg.creative-disabled"), 3));
                    return;
                }

                isCreativeMode = !isCreativeMode;
                Game1.addHUDMessage(new HUDMessage(
                    Helper.Translation.Get(isCreativeMode ? "msg.mode-creative" : "msg.mode-survival"), 3));
            }
            else if (Config.ToggleOverwriteMode.JustPressed())
            {
                isOverwriteMode = !isOverwriteMode;
                Game1.addHUDMessage(new HUDMessage(
                    Helper.Translation.Get(isOverwriteMode ? "msg.overwrite-on" : "msg.overwrite-off"), 3));
            }
            else if (Config.ClearGhosts.JustPressed())
            {
                placedGhosts.Clear();
                Game1.playSound("trashcan");
            }
            else if (Config.AssistPlantingKey.JustPressed())
            {
                if (!CanCurrentPlayerModifyWorld())
                    return;

                TriggerAssistedPlantingForCurrentLocation();
            }
            else if (Config.OpenBlueprintBrowser.JustPressed())
            {
                EnterPreviewMode();
            }
            else if (Config.UndoKey.JustPressed())
            {
                if (!CanCurrentPlayerModifyWorld())
                    return;

                UndoLastPlacement();
            }
            // Re-inserted missing logic for blueprint range selection
            else if (Helper.Input.IsDown(Config.ModModifier) && (e.Button == SButton.MouseLeft || e.Button == SButton.MouseRight))
            {
                if (e.Button == SButton.MouseLeft)
                {
                    if (startTile == null) { startTile = e.Cursor.Tile; Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.start-tile-set"), 3)); }
                    else
                    {
                        Vector2 capturedStart = startTile.Value;
                        Vector2 capturedEnd = e.Cursor.Tile;

                        bool didSaveBlueprint = false;
                        var namingMenu = new CancelableNamingMenu(name => {
                            didSaveBlueprint = true;
                            SaveBlueprint(Game1.currentLocation, capturedStart, capturedEnd, name);
                            startTile = null;
                            Game1.exitActiveMenu();
                        }, Helper.Translation.Get("msg.naming-title"), Helper.Translation.Get("msg.naming-default"));

                        namingMenu.exitFunction = () =>
                        {
                            if (didSaveBlueprint)
                                return;

                            startTile = null;
                            Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.selection-canceled"), 3));
                        };

                        Game1.activeClickableMenu = namingMenu;
                    }
                }
                else if (e.Button == SButton.MouseRight && startTile != null)
                {
                    startTile = null;
                    Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.selection-canceled"), 3));
                }
            }

            // === 下面保持你原逻辑 ===
            else if (isPreviewMode)
            {
                if (e.Button == SButton.MouseLeft || e.Button == SButton.MouseRight)
                    Helper.Input.Suppress(e.Button);

                if (e.Button == SButton.MouseLeft)
                {
                    if (!CanCurrentPlayerModifyWorld())
                        return;

                    HandlePlacementAttempt();
                }
                else if (e.Button == SButton.MouseRight)
                {
                    isPreviewMode = false;
                    previewItems = null;
                    Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.preview-exited"), 3));
                }
                else if (e.Button == SButton.Left || e.Button == SButton.Right)
                    SwitchBlueprint(e.Button == SButton.Right);
            }
            else if (!isCreativeMode && e.Button == SButton.MouseLeft &&
                     !Helper.Input.IsDown(Config.ModModifier))
            {
                Vector2 tile = new Vector2((int)e.Cursor.Tile.X, (int)e.Cursor.Tile.Y);
                GhostItem matchingGhost = GetFillableGhostAtTile(tile);
                if (matchingGhost == null)
                    return;

                if (!CanCurrentPlayerModifyWorld())
                    return;

                HandleGhostFilling(tile, matchingGhost);
            }
        }

        private bool CanCurrentPlayerModifyWorld()
        {
            if (Context.IsMainPlayer)
                return true;

            Game1.showRedMessage(Helper.Translation.Get("msg.host-only-action"));
            return false;
        }

        private void HandlePlacementAttempt()
        {
            Vector2 mouseTile = new Vector2((int)Helper.Input.GetCursorPosition().Tile.X, (int)Helper.Input.GetCursorPosition().Tile.Y);
            bool hasCollision = false;
            var plantingLookup = currentPlantingPlans.ToLookup(plan => new Vector2(plan.TileX, plan.TileY));

            if (!isOverwriteMode)
            {
                for (int x = 0; x < currentMetadata.Width; x++)
                {
                    for (int y = 0; y < currentMetadata.Height; y++)
                    {
                        Vector2 targetTile = new Vector2(mouseTile.X + x, mouseTile.Y + y);
                        Vector2 relativeTile = new Vector2(x, y);
                        var itemAtTile = previewItems.FirstOrDefault(i => (int)i.TileX == x && (int)i.TileY == y);
                        bool hasPlantingPlan = plantingLookup[relativeTile].Any();
                        if (Game1.currentLocation.Objects.TryGetValue(targetTile, out var worldObj) && IsObjectPlacementBlocked(worldObj, itemAtTile, hasPlantingPlan)) { hasCollision = true; break; }
                        if (Game1.currentLocation.terrainFeatures.TryGetValue(targetTile, out var feature))
                        {
                            if (IsTerrainFeatureBlocked(feature, itemAtTile, hasPlantingPlan)) { hasCollision = true; break; }
                        }
                    }
                    if (hasCollision) break;
                }
            }

            if (hasCollision) { Game1.playSound("cancel"); Game1.showRedMessage(Helper.Translation.Get("msg.error-collision")); return; }

            if (!isCreativeMode)
            {
                var requirements = previewItems.GroupBy(i => i.ItemId).Select(g => new ItemRequirement { ItemId = g.Key, Count = g.Count() }).ToList();
                bool hasEverything = requirements.All(req => GetTotalItemCount(req.ItemId) >= req.Count);

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
                                foreach (var req in savedRequirements) ConsumeItems(req.ItemId, req.Count);
                                previewItems = savedPreviewItems;
                                PlaceBlueprintReal(pendingTile.Value, savedRequirements);
                                QueueAssistedPlantingPrompt(pendingTile.Value);
                                previewItems = null;
                                Game1.playSound("purchase");
                            }
                            else
                            {
                                previewItems = savedPreviewItems;
                                PlaceGhosts(pendingTile.Value, trackUndo: true);
                                previewItems = null;
                            }
                        }
                    );
                    return;
                }
            }

            if (isCreativeMode)
            {
                PlaceBlueprintReal(mouseTile);
                int plantedCount = DirectPlantCrops(mouseTile);
                if (plantedCount > 0)
                    Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.assisted-planting-success", new { count = plantedCount }), 2));
            }
            else
                PlaceGhosts(mouseTile, trackUndo: true);

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
                var plantingLookup = currentPlantingPlans.ToLookup(plan => new Vector2(plan.TileX, plan.TileY));

                for (int x = 0; x < currentMetadata.Width; x++)
                {
                    for (int y = 0; y < currentMetadata.Height; y++)
                    {
                        Vector2 relativeTile = new Vector2(x, y);
                        Vector2 targetTile = new Vector2(mouseTile.X + x, mouseTile.Y + y);
                        var itemsAtTile = itemLookup[relativeTile];
                        var plantingPlansAtTile = plantingLookup[relativeTile];
                        var primaryItemAtTile = itemsAtTile.FirstOrDefault();
                        
                        bool tileBlockedByObject = Game1.currentLocation.Objects.TryGetValue(targetTile, out var worldObj) && !IsDebris(worldObj);
                        bool itemPlacementBlockedByObject = Game1.currentLocation.Objects.TryGetValue(targetTile, out var blockingObj) && IsObjectPlacementBlocked(blockingObj, primaryItemAtTile, plantingPlansAtTile.Any());
                        
                        if (itemsAtTile.Any())
                        {
                            foreach (var item in itemsAtTile)
                            {
                                bool isBlocked = false;
                                if (!isOverwriteMode)
                                {
                                    if (itemPlacementBlockedByObject) isBlocked = true;
                                    else if (Game1.currentLocation.terrainFeatures.TryGetValue(targetTile, out var feature))
                                    {
                                        isBlocked = IsTerrainFeatureBlocked(feature, item, plantingPlansAtTile.Any());
                                    }
                                }
                                DrawGhost(e.SpriteBatch, targetTile, item.ItemId, 0.5f, isBlocked ? Color.Red * 0.8f : (isCreativeMode ? Color.LightGreen : Color.Cyan));
                            }
                        }
                        else if (!isOverwriteMode)
                        {
                            bool isBlocked = itemPlacementBlockedByObject;
                            if (!isBlocked && Game1.currentLocation.terrainFeatures.TryGetValue(targetTile, out var feature))
                                isBlocked = IsTerrainFeatureBlocked(feature, null, plantingPlansAtTile.Any());
                            if (isBlocked) DrawSelectionBox(e.SpriteBatch, targetTile, targetTile, Color.Red * 0.2f);
                        }

                        foreach (var plan in plantingPlansAtTile)
                        {
                            bool isBlocked = tileBlockedByObject || IsPlantingPlanBlocked(targetTile, plan);
                            DrawGhost(e.SpriteBatch, targetTile, plan.SeedItemId, 0.55f, isBlocked ? Color.OrangeRed * 0.9f : Color.YellowGreen * 0.9f, 0.8f);
                        }
                    }
                }
            }
            foreach (var ghost in placedGhosts.Where(IsGhostInCurrentLocation))
            {
                if (ghost.IsPlantingHint)
                    DrawPlantingHintBackground(e.SpriteBatch, ghost);

                DrawGhost(e.SpriteBatch, ghost.Tile, ghost.ItemId, GetGhostAlpha(ghost), GetGhostTint(ghost), GetGhostScale(ghost));
            }
        }

        private void OnRenderedHud(object sender, RenderedHudEventArgs e)
        {
            if (isPreviewMode && previewItems != null)
            {
                string displayName = currentMetadata?.Name ?? blueprintFiles[currentBlueprintIndex].Name;
                string overwriteModeText = Helper.Translation.Get(isOverwriteMode ? "msg.mode-overwrite" : "msg.mode-safe");
                string creativeModeText = Helper.Translation.Get(isCreativeMode ? "msg.mode-creative-short" : "msg.mode-survival-short");
                string topText = Helper.Translation.Get("msg.hud-blueprint", new { name = displayName, overwrite = overwriteModeText, creative = creativeModeText });
                e.SpriteBatch.DrawString(Game1.dialogueFont, topText, new Vector2(80, 80), Color.White);
                if (currentPlantingPlans.Count > 0)
                {
                    string plantingText = Helper.Translation.Get("msg.hud-planting", new { count = currentPlantingPlans.Count });
                    e.SpriteBatch.DrawString(Game1.smallFont, plantingText, new Vector2(80, 120), Color.LightGreen);
                }

                GhostItem hoveredPlantingGhost = GetHoveredPlantingGhost();
                if (hoveredPlantingGhost != null && !string.IsNullOrWhiteSpace(hoveredPlantingGhost.DisplayName))
                {
                    string modeText = Helper.Translation.Get(hoveredPlantingGhost.PlantingMode == PlantingMode.IndoorPot ? "msg.planting-mode-pot" : "msg.planting-mode-ground");
                    string hoverText = $"{hoveredPlantingGhost.DisplayName} [{modeText}]";
                    e.SpriteBatch.DrawString(Game1.smallFont, hoverText, new Vector2(80, 145), Color.Wheat);
                }

                DrawShoppingList(e.SpriteBatch);
            }
            else
            {
                GhostItem hoveredPlantingGhost = GetHoveredPlantingGhost();
                if (hoveredPlantingGhost != null && !string.IsNullOrWhiteSpace(hoveredPlantingGhost.DisplayName))
                {
                    string modeText = Helper.Translation.Get(hoveredPlantingGhost.PlantingMode == PlantingMode.IndoorPot ? "msg.planting-mode-pot" : "msg.planting-mode-ground");
                    string hoverText = $"{hoveredPlantingGhost.DisplayName} [{modeText}]";
                    e.SpriteBatch.DrawString(Game1.smallFont, hoverText, new Vector2(80, 80), Color.Wheat);
                }

                int plantingGhostCount = GetPendingPlantingGhostsForCurrentLocation().Count;
                if (plantingGhostCount > 0)
                {
                    string readyText = Helper.Translation.Get("msg.assisted-planting-ready", new { key = Config.AssistPlantingKey.ToString() });
                    e.SpriteBatch.DrawString(Game1.smallFont, readyText, new Vector2(80, 105), Color.LightGreen);
                }
            }
        }

        private void DrawShoppingList(SpriteBatch b)
        {
            if (previewItems == null) return;
            var requirements = previewItems.GroupBy(i => i.ItemId).Select(g => new { ItemId = g.Key, RequiredCount = g.Count() }).ToList();
            int xPos = Game1.uiViewport.Width - 300, yPos = 150;
            int plantingRows = currentPlantingPlans
                .GroupBy(plan => new { plan.SeedItemId, plan.Mode })
                .Count();
            int panelHeight = requirements.Count * 40 + 60;
            if (plantingRows > 0)
                panelHeight += plantingRows * 35 + 55;

            b.Draw(Game1.staminaRect, new Rectangle(xPos - 10, yPos - 10, 280, panelHeight), Color.Black * 0.5f);
            b.DrawString(Game1.dialogueFont, Helper.Translation.Get("msg.shopping-list"), new Vector2(xPos, yPos), Color.Gold, 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 0f);
            yPos += 40;
            foreach (var req in requirements)
            {
                int totalHas = GetTotalItemCount(req.ItemId);
                ParsedItemData itemData = ItemRegistry.GetData(req.ItemId);
                if (itemData != null)
                {
                    b.Draw(itemData.GetTexture(), new Rectangle(xPos, yPos, 32, 32), itemData.GetSourceRect(), Color.White);
                    b.DrawString(Game1.smallFont, $"{req.RequiredCount} ({totalHas})", new Vector2(xPos + 40, yPos + 4), totalHas >= req.RequiredCount ? Color.White : Color.Red);
                    yPos += 35;
                }
            }

            var plantingRequirements = currentPlantingPlans
                .GroupBy(plan => new { plan.SeedItemId, plan.Mode, plan.DisplayName })
                .Select(g => new
                {
                    g.Key.SeedItemId,
                    g.Key.Mode,
                    DisplayName = string.IsNullOrWhiteSpace(g.Key.DisplayName) ? g.Key.SeedItemId : g.Key.DisplayName,
                    RequiredCount = g.Count()
                })
                .ToList();

            if (plantingRequirements.Count > 0)
            {
                yPos += 10;
                b.DrawString(Game1.dialogueFont, Helper.Translation.Get("msg.planting-list"), new Vector2(xPos, yPos), Color.LightGreen, 0f, Vector2.Zero, 0.7f, SpriteEffects.None, 0f);
                yPos += 35;

                foreach (var req in plantingRequirements)
                {
                    int totalHas = GetTotalItemCount(req.SeedItemId);
                    ParsedItemData itemData = ItemRegistry.GetData(req.SeedItemId);
                    if (itemData != null)
                        b.Draw(itemData.GetTexture(), new Rectangle(xPos, yPos, 32, 32), itemData.GetSourceRect(), Color.White);

                    string modeText = Helper.Translation.Get(req.Mode == PlantingMode.IndoorPot ? "msg.planting-mode-pot" : "msg.planting-mode-ground");
                    string text = $"{req.DisplayName} x{req.RequiredCount} ({totalHas}) [{modeText}]";
                    b.DrawString(Game1.smallFont, text, new Vector2(xPos + 40, yPos + 4), totalHas >= req.RequiredCount ? Color.White : Color.Red);
                    yPos += 35;
                }
            }
        }

        private IEnumerable<StardewValley.Objects.Chest> GetAllChests()
        {
            switch (Config.ChestRangeMode)
            {
                case "Global":
                    {
                        var locations = new List<GameLocation> { Game1.currentLocation };
                        var farm = Game1.getFarm();
                        if (farm != null && !locations.Contains(farm)) locations.Add(farm);

                        foreach (GameLocation location in locations)
                        {
                            foreach (var obj in location.Objects.Values)
                            {
                                if (obj is StardewValley.Objects.Chest chest && chest.playerChest.Value && !chest.fridge.Value)
                                    yield return chest;
                            }
                        }
                        break;
                    }

                case "Location":
                    {
                        foreach (var obj in Game1.currentLocation.Objects.Values)
                        {
                            if (obj is StardewValley.Objects.Chest chest && chest.playerChest.Value && !chest.fridge.Value)
                                yield return chest;
                        }
                        break;
                    }

                case "Custom":
                    {
                        foreach (var obj in Game1.currentLocation.Objects.Values)
                        {
                            if (obj is StardewValley.Objects.Chest chest && chest.playerChest.Value && !chest.fridge.Value)
                            {
                                if (Vector2.Distance(Game1.player.Tile, chest.TileLocation) <= Config.ChestSearchRange)
                                {
                                    yield return chest;
                                }
                            }
                        }
                        break;
                    }
            }
        }

        private int GetTotalItemCount(string itemId)
        {
            int count = Game1.player.Items.CountId(itemId);
            foreach (var chest in GetAllChests())
            {
                count += chest.Items.CountId(itemId);
            }
            return count;
        }

        private void ConsumeItems(string itemId, int amount)
        {
            int remaining = amount;
            
            // 先扣除玩家背包
            int fromPlayer = Math.Min(remaining, Game1.player.Items.CountId(itemId));
            if (fromPlayer > 0)
            {
                Game1.player.Items.ReduceId(itemId, fromPlayer);
                remaining -= fromPlayer;
            }

            // 如果还需要，扣除箱子
            if (remaining > 0)
            {
                foreach (var chest in GetAllChests())
                {
                    int fromChest = Math.Min(remaining, chest.Items.CountId(itemId));
                    if (fromChest > 0)
                    {
                        chest.Items.ReduceId(itemId, fromChest);
                        remaining -= fromChest;
                        if (remaining <= 0) break;
                    }
                }
            }
        }

        private PlacementAction PlaceBlueprintReal(Vector2 origin, List<ItemRequirement> refunds = null)
        {
            if (previewItems == null) return null;

            PlacementAction action = new PlacementAction { Location = Game1.currentLocation, RefundItems = refunds ?? new List<ItemRequirement>() };
            var affectedTiles = previewItems
                .Select(i => new Vector2(origin.X + i.TileX, origin.Y + i.TileY))
                .Concat((currentPlantingPlans ?? new List<PlantingPlan>()).Select(plan => new Vector2(origin.X + plan.TileX, origin.Y + plan.TileY)))
                .Distinct();
            foreach (var tile in affectedTiles)
            {
                var change = new TileChange { Tile = tile };
                if (Game1.currentLocation.Objects.TryGetValue(tile, out var obj)) change.OldObject = CloneWorldObject(obj);
                if (Game1.currentLocation.terrainFeatures.TryGetValue(tile, out var feature)) change.OldTerrainFeature = CloneTerrainFeature(feature, tile);
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
                    if (newItem is StardewValley.Object obj)
                    {
                        obj.TileLocation = targetTile;
                        obj.placementAction(Game1.currentLocation, (int)targetTile.X * 64, (int)targetTile.Y * 64, Game1.player);
                    }
                }
            }

            undoStack.Add(action);
            if (undoStack.Count > Config.MaxUndoSteps) undoStack.RemoveAt(0);
            action.AddedGhosts.AddRange(AddPlantingGhosts(origin));
            return action;
        }

        private void PlaceGhosts(Vector2 origin, bool trackUndo = false)
        {
            if (previewItems == null) return;
            var addedGhosts = new List<GhostItem>();
            foreach (var item in previewItems)
            {
                Vector2 targetTile = new Vector2(origin.X + item.TileX, origin.Y + item.TileY);
                GhostItem ghost = new GhostItem { Tile = targetTile, ItemId = item.ItemId, LocationName = Game1.currentLocation?.NameOrUniqueName };
                if (AddGhost(ghost))
                    addedGhosts.Add(ghost);
            }

            addedGhosts.AddRange(AddPlantingGhosts(origin));

            if (trackUndo && addedGhosts.Count > 0)
            {
                PlacementAction action = new PlacementAction
                {
                    Location = Game1.currentLocation,
                    AddedGhosts = addedGhosts
                };
                undoStack.Add(action);
                if (undoStack.Count > Config.MaxUndoSteps) undoStack.RemoveAt(0);
            }
        }

        private GhostItem GetFillableGhostAtTile(Vector2 tile)
        {
            return placedGhosts.FirstOrDefault(g =>
                !g.IsPlantingHint &&
                IsGhostInCurrentLocation(g) &&
                g.Tile == tile &&
                Game1.player.ActiveItem?.QualifiedItemId == g.ItemId);
        }

        private void HandleGhostFilling(Vector2 tile, GhostItem ghost = null)
        {
            ghost ??= GetFillableGhostAtTile(tile);
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
                    var newObj = (StardewValley.Object)ItemRegistry.Create(reqId);
                    placed = newObj.placementAction(Game1.currentLocation, (int)tile.X * 64, (int)tile.Y * 64, Game1.player);
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

        private void DrawGhost(SpriteBatch b, Vector2 tile, string itemId, float alpha, Color tint, float scale = 1f)
        {
            ParsedItemData itemData = ItemRegistry.GetData(itemId);
            if (itemData != null)
            {
                Rectangle sourceRect = itemData.GetSourceRect();
                int width = (int)(sourceRect.Width * 4 * scale);
                int height = (int)(sourceRect.Height * 4 * scale);
                int x = (int)(tile.X * 64 - Game1.viewport.X + (64 - width) / 2f);
                int y = (int)((tile.Y + 1) * 64 - Game1.viewport.Y - height);
                b.Draw(itemData.GetTexture(), new Rectangle(x, y, width, height), sourceRect, tint * alpha);
            }
        }

        private bool IsGhostInCurrentLocation(GhostItem ghost)
        {
            return ghost?.LocationName == Game1.currentLocation?.NameOrUniqueName;
        }

        private Color GetGhostTint(GhostItem ghost)
        {
            return ghost.IsPlantingHint ? Color.YellowGreen * 0.9f : Color.White * 0.6f;
        }

        private float GetGhostScale(GhostItem ghost)
        {
            return ghost.IsPlantingHint ? 0.8f : 1f;
        }

        private float GetGhostAlpha(GhostItem ghost)
        {
            return ghost.IsPlantingHint ? 0.55f : 0.4f;
        }

        private List<GhostItem> AddPlantingGhosts(Vector2 origin)
        {
            if (currentPlantingPlans == null || currentPlantingPlans.Count == 0)
                return new List<GhostItem>();

            var addedGhosts = new List<GhostItem>();

            foreach (var plan in currentPlantingPlans)
            {
                Vector2 targetTile = new Vector2(origin.X + plan.TileX, origin.Y + plan.TileY);
                GhostItem ghost = new GhostItem
                {
                    Tile = targetTile,
                    ItemId = plan.SeedItemId,
                    IsPlantingHint = true,
                    PlantingMode = plan.Mode,
                    DisplayName = plan.DisplayName,
                    Season = plan.Season,
                    LocationName = Game1.currentLocation?.NameOrUniqueName
                };

                if (AddGhost(ghost))
                    addedGhosts.Add(ghost);
            }

            return addedGhosts;
        }

        private bool AddGhost(GhostItem ghost)
        {
            if (ghost == null)
                return false;

            bool exists = placedGhosts.Any(existing =>
                existing.LocationName == ghost.LocationName &&
                existing.Tile == ghost.Tile &&
                existing.ItemId == ghost.ItemId &&
                existing.IsPlantingHint == ghost.IsPlantingHint &&
                existing.PlantingMode == ghost.PlantingMode);

            if (!exists)
            {
                placedGhosts.Add(ghost);
                return true;
            }

            return false;
        }

        private bool IsGhostSatisfied(GhostItem ghost)
        {
            Vector2 tile = ghost.Tile;
            if (!ghost.IsPlantingHint)
            {
                if (Game1.currentLocation.Objects.TryGetValue(tile, out var obj) && obj.QualifiedItemId == ghost.ItemId)
                    return true;

                if (Game1.currentLocation.terrainFeatures.TryGetValue(tile, out var feature) && feature is StardewValley.TerrainFeatures.Flooring flooring)
                    return "(O)" + (flooring.GetData()?.ItemId ?? "") == ghost.ItemId;

                return false;
            }

            if (ghost.PlantingMode == PlantingMode.IndoorPot)
            {
                if (!Game1.currentLocation.Objects.TryGetValue(tile, out var obj))
                    return false;

                object hoeDirtRef = GetMemberValue(obj, "hoeDirt");
                object hoeDirt = UnwrapNetValue(hoeDirtRef);
                object crop = hoeDirt != null ? GetMemberValue(hoeDirt, "crop") : null;
                return UnwrapNetValue(crop) != null;
            }

            if (!Game1.currentLocation.terrainFeatures.TryGetValue(tile, out var terrainFeature))
                return false;

            if (terrainFeature is not StardewValley.TerrainFeatures.HoeDirt dirt)
                return false;

            object plantedCrop = GetMemberValue(dirt, "crop");
            return UnwrapNetValue(plantedCrop) != null;
        }

        private void DrawPlantingHintBackground(SpriteBatch b, GhostItem ghost)
        {
            Color baseColor = ghost.PlantingMode == PlantingMode.IndoorPot
                ? new Color(77, 128, 214) * 0.35f
                : new Color(92, 168, 96) * 0.35f;

            int x = (int)(ghost.Tile.X * 64 - Game1.viewport.X);
            int y = (int)(ghost.Tile.Y * 64 - Game1.viewport.Y);
            b.Draw(Game1.staminaRect, new Rectangle(x + 6, y + 6, 52, 52), baseColor);
        }

        private GhostItem GetHoveredPlantingGhost()
        {
            Vector2 cursorTile = Helper.Input.GetCursorPosition().Tile;
            return placedGhosts.FirstOrDefault(ghost =>
                ghost.IsPlantingHint &&
                IsGhostInCurrentLocation(ghost) &&
                ghost.Tile == new Vector2((int)cursorTile.X, (int)cursorTile.Y));
        }

        private List<GhostItem> GetPendingPlantingGhostsForCurrentLocation()
        {
            return placedGhosts
                .Where(ghost => ghost.IsPlantingHint && IsGhostInCurrentLocation(ghost))
                .GroupBy(ghost => new { ghost.Tile, ghost.ItemId, ghost.PlantingMode, ghost.DisplayName, ghost.LocationName })
                .Select(group => group.First())
                .OrderBy(ghost => ghost.Tile.Y)
                .ThenBy(ghost => ghost.Tile.X)
                .ToList();
        }

        private bool IsTerrainFeatureBlocked(StardewValley.TerrainFeatures.TerrainFeature feature, BlueprintItem itemAtTile, bool hasPlantingPlan)
        {
            if (feature == null)
                return false;

            if (feature is StardewValley.TerrainFeatures.Flooring flooring)
                return itemAtTile != null && itemAtTile.ItemType == "Flooring" && flooring.whichFloor.Value != itemAtTile.FlooringId;

            // Tilled soil is common in farm-design blueprints, so don't reject it as a hard collision in safe mode.
            if (feature is StardewValley.TerrainFeatures.HoeDirt)
                return false;

            return !hasPlantingPlan;
        }

        private bool IsObjectPlacementBlocked(StardewValley.Object worldObj, BlueprintItem itemAtTile, bool hasPlantingPlan)
        {
            if (worldObj == null || IsDebris(worldObj))
                return false;

            // Planting-only tiles are validated separately by the assisted planting rules,
            // so a placed object there shouldn't block the whole blueprint body in safe mode.
            if (itemAtTile == null && hasPlantingPlan)
                return false;

            return true;
        }

        private bool IsPlantingPlanBlocked(Vector2 targetTile, PlantingPlan plan)
        {
            if (plan == null)
                return false;

            if (plan.Mode == PlantingMode.IndoorPot)
            {
                if (!Game1.currentLocation.Objects.TryGetValue(targetTile, out var obj))
                    return true;

                return obj.GetType().FullName != "StardewValley.Objects.IndoorPot";
            }

            if (!Game1.currentLocation.terrainFeatures.TryGetValue(targetTile, out var feature))
                return true;

            return feature is not StardewValley.TerrainFeatures.HoeDirt;
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

            if (action.AddedGhosts.Count > 0)
            {
                placedGhosts.RemoveAll(ghost => action.AddedGhosts.Any(added =>
                    added.LocationName == ghost.LocationName &&
                    added.Tile == ghost.Tile &&
                    added.ItemId == ghost.ItemId &&
                    added.IsPlantingHint == ghost.IsPlantingHint &&
                    added.PlantingMode == ghost.PlantingMode));
            }

            if (action.RestoredGhosts.Count > 0)
            {
                foreach (GhostItem ghost in action.RestoredGhosts)
                {
                    AddGhost(new GhostItem
                    {
                        Tile = ghost.Tile,
                        ItemId = ghost.ItemId,
                        IsPlantingHint = ghost.IsPlantingHint,
                        PlantingMode = ghost.PlantingMode,
                        DisplayName = ghost.DisplayName,
                        Season = ghost.Season,
                        LocationName = ghost.LocationName
                    });
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
            var plantingPlans = new List<PlantingPlan>();
            foreach (var tile in location.Objects.Keys.Where(t => t.X >= minX && t.X <= maxX && t.Y >= minY && t.Y <= maxY))
            {
                items.Add(new BlueprintItem { ItemId = location.Objects[tile].QualifiedItemId, TileX = tile.X - minX, TileY = tile.Y - minY, Name = location.Objects[tile].DisplayName, ItemType = "Object" });
                PlantingPlan indoorPotPlan = TryCreateIndoorPotPlantingPlan(location.Objects[tile], (int)(tile.X - minX), (int)(tile.Y - minY));
                if (indoorPotPlan != null)
                    plantingPlans.Add(indoorPotPlan);
            }
            foreach (var pair in location.terrainFeatures.Pairs.Where(p => p.Key.X >= minX && p.Key.X <= maxX && p.Key.Y >= minY && p.Key.Y <= maxY))
            {
                if (pair.Value is StardewValley.TerrainFeatures.Flooring f)
                    items.Add(new BlueprintItem { ItemId = "(O)" + (f.GetData()?.ItemId ?? ""), FlooringId = f.whichFloor.Value, TileX = pair.Key.X - minX, TileY = pair.Key.Y - minY, Name = "Flooring", ItemType = "Flooring" });
                else if (pair.Value is StardewValley.TerrainFeatures.HoeDirt dirt)
                {
                    PlantingPlan groundPlan = TryCreateGroundPlantingPlan(dirt, (int)(pair.Key.X - minX), (int)(pair.Key.Y - minY));
                    if (groundPlan != null)
                        plantingPlans.Add(groundPlan);
                }
            }

            if (items.Count > 0 || plantingPlans.Count > 0)
            {
                string safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
                var data = new BlueprintFile
                {
                    Metadata = new BlueprintMetadata { Name = name, Width = maxX - minX + 1, Height = maxY - minY + 1 },
                    Items = items,
                    PlantingPlans = plantingPlans
                };
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
            }, (selectedFile) => ExportBlueprintFile(selectedFile), Helper, GetTotalItemCount);
        }

        public void SetExportPath(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                Config.ExportPath = path;
            else
                Config.ExportPath = "";

            this.Helper.WriteConfig(Config);
            Game1.addHUDMessage(new HUDMessage($"导出路径已设置为 {Config.ExportPath}", 3));
        }

        private void ExportBlueprintFile(FileInfo file)
        {
            if (file == null)
                return;

            try
            {
                string exportPath = Config.ExportPath;
                if (string.IsNullOrWhiteSpace(exportPath))
                {
                    exportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "BlueprintModExports");
                }

                if (!Directory.Exists(exportPath))
                    Directory.CreateDirectory(exportPath);

                string destPath = Path.Combine(exportPath, file.Name);
                File.Copy(file.FullName, destPath, true);

                Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.export-success"), 3));
            }
            catch (Exception ex)
            {
                Monitor.Log("导出蓝图失败：" + ex.Message, LogLevel.Error);
                Game1.showRedMessage("导出失败，请检查日志");
            }
        }

        private void SwitchBlueprint(bool next) { if (blueprintFiles.Count > 1) { currentBlueprintIndex = next ? (currentBlueprintIndex + 1) % blueprintFiles.Count : (currentBlueprintIndex - 1 + blueprintFiles.Count) % blueprintFiles.Count; LoadCurrentBlueprint(); Game1.playSound("shwip"); } }

        private void LoadCurrentBlueprint()
        {
            var file = this.Helper.Data.ReadJsonFile<BlueprintFile>($"blueprints/{blueprintFiles[currentBlueprintIndex].Name}");
            if (file != null && file.Items != null)
            {
                previewItems = file.Items;
                currentMetadata = file.Metadata;
                currentPlantingPlans = file.PlantingPlans ?? new List<PlantingPlan>();
            }
            else
            {
                Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.error-parse-failed"), 1));
                isPreviewMode = false;
            }
        }

        private void QueueAssistedPlantingPrompt(Vector2 origin)
        {
            if (currentPlantingPlans == null || currentPlantingPlans.Count == 0)
                return;

            int groundCount = currentPlantingPlans.Count(plan => plan.Mode == PlantingMode.Ground);
            int potCount = currentPlantingPlans.Count(plan => plan.Mode == PlantingMode.IndoorPot);
            Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.planting-summary", new { total = currentPlantingPlans.Count, ground = groundCount, pot = potCount }), 2));
            pendingPlantingPromptOrigin = origin;
        }

        private void TryOpenAssistedPlantingPrompt(Vector2 origin)
        {
            try
            {
                PromptForAssistedPlanting(origin);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to open assisted planting prompt at {origin}.", LogLevel.Error);
                Monitor.Log(ex.ToString(), LogLevel.Error);
                Game1.showRedMessage(Helper.Translation.Get("msg.assisted-planting-open-error"));
            }
        }

        private void PromptForAssistedPlanting(Vector2 origin)
        {
            if (currentPlantingPlans == null || currentPlantingPlans.Count == 0)
                return;

            List<PlantingTarget> targets = GetPlantingTargets(origin);
            List<PlantingValidationIssue> issues = ValidatePlantingTargets(targets);
            if (issues.Count > 0)
            {
                ShowPlantingIssues(issues);
                return;
            }

            Game1.currentLocation.createQuestionDialogue(
                Helper.Translation.Get("msg.confirm-assisted-planting", new { count = targets.Count }),
                Game1.currentLocation.createYesNoResponses(),
                (who, answer) =>
                {
                    if (answer == "Yes")
                    {
                        bool planted = TryAssistPlanting(targets);
                        if (planted)
                            Game1.playSound("dirtyHit");
                    }
                }
            );
        }

        private void TriggerAssistedPlantingForCurrentLocation()
        {
            List<GhostItem> ghosts = GetPendingPlantingGhostsForCurrentLocation();
            if (ghosts.Count == 0)
            {
                Game1.showRedMessage(Helper.Translation.Get("msg.assisted-planting-no-targets"));
                return;
            }

            TryOpenAssistedPlantingPrompt(ghosts.Select(CreatePlantingTarget).ToList(), useConfirmationDialog: false);
        }

        private void TryOpenAssistedPlantingPrompt(List<PlantingTarget> targets, bool useConfirmationDialog)
        {
            try
            {
                if (targets == null || targets.Count == 0)
                {
                    Game1.showRedMessage(Helper.Translation.Get("msg.assisted-planting-no-targets"));
                    return;
                }

                List<PlantingValidationIssue> issues = ValidatePlantingTargets(targets);
                if (issues.Count > 0)
                {
                    ShowPlantingIssues(issues);
                    return;
                }

                if (!useConfirmationDialog)
                {
                    if (TryAssistPlanting(targets))
                        Game1.playSound("dirtyHit");
                    return;
                }

                Game1.currentLocation.createQuestionDialogue(
                    Helper.Translation.Get("msg.confirm-assisted-planting", new { count = targets.Count }),
                    Game1.currentLocation.createYesNoResponses(),
                    (who, answer) =>
                    {
                        if (answer == "Yes" && TryAssistPlanting(targets))
                            Game1.playSound("dirtyHit");
                    }
                );
            }
            catch (Exception ex)
            {
                Monitor.Log("Failed to open assisted planting prompt.", LogLevel.Error);
                Monitor.Log(ex.ToString(), LogLevel.Error);
                Game1.showRedMessage(Helper.Translation.Get("msg.assisted-planting-open-error"));
            }
        }

        private List<PlantingTarget> GetPlantingTargets(Vector2 origin)
        {
            return currentPlantingPlans
                .Select(plan => new PlantingTarget
                {
                    Tile = new Vector2(origin.X + plan.TileX, origin.Y + plan.TileY),
                    SeedItemId = plan.SeedItemId,
                    Mode = plan.Mode,
                    DisplayName = GetSeedDisplayName(plan.SeedItemId, plan.DisplayName),
                    Season = plan.Season
                })
                .ToList();
        }

        private PlantingTarget CreatePlantingTarget(GhostItem ghost)
        {
            return new PlantingTarget
            {
                Tile = ghost.Tile,
                SeedItemId = ghost.ItemId,
                Mode = ghost.PlantingMode,
                DisplayName = GetSeedDisplayName(ghost.ItemId, ghost.DisplayName),
                Season = ghost.Season
            };
        }

        private List<PlantingValidationIssue> ValidatePlantingTargets(List<PlantingTarget> targets)
        {
            var issues = new List<PlantingValidationIssue>();
            var missingSeedCounts = new Dictionary<string, SeedShortage>(StringComparer.OrdinalIgnoreCase);

            foreach (PlantingTarget target in targets)
            {
                PlantingValidationIssue issue = ValidatePlantingTarget(target);
                if (issue != null)
                {
                    issues.Add(issue);
                    continue;
                }

                if (!missingSeedCounts.TryGetValue(target.SeedItemId, out var shortage))
                {
                    shortage = new SeedShortage
                    {
                        SeedItemId = target.SeedItemId,
                        DisplayName = target.DisplayName
                    };
                    missingSeedCounts[target.SeedItemId] = shortage;
                }

                shortage.RequiredCount++;
            }

            foreach (SeedShortage shortage in missingSeedCounts.Values.OrderBy(entry => entry.DisplayName))
            {
                int available = GetTotalItemCount(shortage.SeedItemId);
                if (available < shortage.RequiredCount)
                {
                    issues.Add(new PlantingValidationIssue
                    {
                        Tile = null,
                        SeedItemId = shortage.SeedItemId,
                        Reason = Helper.Translation.Get("msg.planting-fail-missing-seeds", new
                        {
                            name = shortage.DisplayName,
                            required = shortage.RequiredCount,
                            available
                        })
                    });
                }
            }

            return issues;
        }

        private PlantingValidationIssue ValidatePlantingTarget(PlantingTarget target)
        {
            if (target == null)
                return null;

            string deniedMessage;
            Vector2 targetTile = target.Tile;
            bool canPlantHere = Game1.currentLocation.CanPlantSeedsHere(target.SeedItemId, (int)targetTile.X, (int)targetTile.Y, target.Mode == PlantingMode.IndoorPot, out deniedMessage);
            string tileText = FormatTile(targetTile);

            if (!IsSeasonAllowedForTarget(target))
                return CreatePlantingIssue(target, Helper.Translation.Get("msg.planting-fail-season-mismatch", new { tile = tileText, season = GetCurrentLocationSeasonName() }));

            if (target.Mode == PlantingMode.Ground)
            {
                if (!Game1.currentLocation.terrainFeatures.TryGetValue(targetTile, out var terrainFeature) || terrainFeature is not StardewValley.TerrainFeatures.HoeDirt dirt)
                {
                    return CreatePlantingIssue(target, Helper.Translation.Get("msg.planting-fail-ground-tile", new { tile = tileText }));
                }

                object plantedCrop = UnwrapNetValue(GetMemberValue(dirt, "crop"));
                if (plantedCrop != null)
                    return CreatePlantingIssue(target, Helper.Translation.Get("msg.planting-fail-occupied-ground", new { tile = tileText }));
            }
            else
            {
                if (!Game1.currentLocation.Objects.TryGetValue(targetTile, out var obj))
                    return CreatePlantingIssue(target, Helper.Translation.Get("msg.planting-fail-missing-pot", new { tile = tileText }));

                if (obj.GetType().FullName != "StardewValley.Objects.IndoorPot")
                    return CreatePlantingIssue(target, Helper.Translation.Get("msg.planting-fail-not-pot", new { tile = tileText }));

                object hoeDirtRef = GetMemberValue(obj, "hoeDirt");
                object hoeDirt = UnwrapNetValue(hoeDirtRef);
                object crop = hoeDirt != null ? UnwrapNetValue(GetMemberValue(hoeDirt, "crop")) : null;
                if (crop != null)
                    return CreatePlantingIssue(target, Helper.Translation.Get("msg.planting-fail-occupied-pot", new { tile = tileText }));

                Item seedItem = ItemRegistry.Create(target.SeedItemId);
                if (seedItem == null || obj.GetType().GetMethod("IsPlantableItem")?.Invoke(obj, new object[] { seedItem }) is bool canPlantInPot && !canPlantInPot)
                    return CreatePlantingIssue(target, Helper.Translation.Get("msg.planting-fail-invalid-pot-crop", new { tile = tileText }));
            }

            if (!canPlantHere)
            {
                string reason = !string.IsNullOrWhiteSpace(deniedMessage)
                    ? deniedMessage
                    : Helper.Translation.Get("msg.planting-fail-season-location", new { tile = tileText });
                return CreatePlantingIssue(target, $"{tileText}: {reason}");
            }

            return null;
        }

        private bool IsSeasonAllowedForTarget(PlantingTarget target)
        {
            if (target == null)
                return true;

            if (Game1.currentLocation?.SeedsIgnoreSeasonsHere() == true)
                return true;

            string currentSeason = GetCurrentLocationSeasonKey();
            if (string.IsNullOrWhiteSpace(currentSeason))
                return true;

            string seasonData = !string.IsNullOrWhiteSpace(target.Season)
                ? target.Season
                : GetSeedSeasonData(target.SeedItemId);

            if (string.IsNullOrWhiteSpace(seasonData))
                return true;

            string[] allowedSeasons = seasonData
                .Split(',')
                .Select(season => season.Trim().ToLowerInvariant())
                .Where(season => !string.IsNullOrWhiteSpace(season))
                .ToArray();

            if (allowedSeasons.Length == 0)
                return true;

            return allowedSeasons.Contains(currentSeason);
        }

        private string GetCurrentLocationSeasonKey()
        {
            string season = Game1.currentLocation != null
                ? Game1.currentLocation.GetSeason().ToString()
                : null;
            if (string.IsNullOrWhiteSpace(season))
                season = Game1.currentSeason;

            return season?.Trim().ToLowerInvariant();
        }

        private string GetCurrentLocationSeasonName()
        {
            string seasonKey = GetCurrentLocationSeasonKey();
            return string.IsNullOrWhiteSpace(seasonKey) ? Game1.currentSeason : seasonKey;
        }

        private string GetSeedSeasonData(string seedItemId)
        {
            string unqualifiedSeedId = seedItemId?.StartsWith("(O)") == true ? seedItemId.Substring(3) : seedItemId;
            if (string.IsNullOrWhiteSpace(unqualifiedSeedId))
                return null;

            if (StardewValley.Crop.TryGetData(unqualifiedSeedId, out StardewValley.GameData.Crops.CropData cropData) &&
                cropData?.Seasons != null &&
                cropData.Seasons.Count > 0)
            {
                return string.Join(", ", cropData.Seasons.Select(season => season.ToString()));
            }

            return null;
        }

        private PlantingValidationIssue CreatePlantingIssue(PlantingTarget target, string reason)
        {
            return new PlantingValidationIssue
            {
                Tile = target?.Tile,
                SeedItemId = target?.SeedItemId,
                PlantingMode = target?.Mode,
                Reason = reason
            };
        }

        private bool TryAssistPlanting(List<PlantingTarget> targets)
        {
            try
            {
                List<PlantingValidationIssue> issues = ValidatePlantingTargets(targets);
                if (issues.Count > 0)
                {
                    ShowPlantingIssues(issues);
                    return false;
                }

                PlacementAction action = CreatePlantingUndoAction(targets);
                int plantedCount = 0;
                foreach (PlantingTarget target in targets)
                {
                    if (TryPlantTarget(target))
                    {
                        if (!isCreativeMode)
                        {
                            ConsumeItems(target.SeedItemId, 1);
                            action.RefundItems.Add(new ItemRequirement { ItemId = target.SeedItemId, Count = 1 });
                        }

                        GhostItem plantingGhost = FindMatchingPlantingGhost(target);
                        if (plantingGhost != null)
                            action.RestoredGhosts.Add(plantingGhost);

                        plantedCount++;
                    }
                }

                if (plantedCount > 0)
                {
                    undoStack.Add(action);
                    if (undoStack.Count > Config.MaxUndoSteps) undoStack.RemoveAt(0);
                }

                if (plantedCount > 0)
                    Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.assisted-planting-success", new { count = plantedCount }), 2));

                return plantedCount == targets.Count;
            }
            catch (Exception ex)
            {
                Monitor.Log("Failed to execute assisted planting.", LogLevel.Error);
                Monitor.Log(ex.ToString(), LogLevel.Error);
                Game1.showRedMessage(Helper.Translation.Get("msg.assisted-planting-run-error"));
                return false;
            }
        }

        private PlacementAction CreatePlantingUndoAction(List<PlantingTarget> targets)
        {
            PlacementAction action = new PlacementAction
            {
                Location = Game1.currentLocation
            };

            foreach (PlantingTarget target in targets)
            {
                TileChange change = new TileChange { Tile = target.Tile };
                if (Game1.currentLocation.Objects.TryGetValue(target.Tile, out var obj))
                    change.OldObject = CloneWorldObject(obj);
                if (Game1.currentLocation.terrainFeatures.TryGetValue(target.Tile, out var feature))
                    change.OldTerrainFeature = CloneTerrainFeature(feature, target.Tile);
                action.Changes.Add(change);
            }

            return action;
        }

        private GhostItem FindMatchingPlantingGhost(PlantingTarget target)
        {
            if (target == null)
                return null;

            return placedGhosts.FirstOrDefault(ghost =>
                ghost.IsPlantingHint &&
                IsGhostInCurrentLocation(ghost) &&
                ghost.Tile == target.Tile &&
                ghost.ItemId == target.SeedItemId &&
                ghost.PlantingMode == target.Mode);
        }

        private int DirectPlantCrops(Vector2 origin)
        {
            if (currentPlantingPlans == null || currentPlantingPlans.Count == 0)
                return 0;

            try
            {
                List<PlantingTarget> targets = GetPlantingTargets(origin);
                int plantedCount = 0;

                // 在创造模式下，先准备耕地
                PrepareGroundForPlanting(targets);

                foreach (PlantingTarget target in targets)
                {
                    if (TryPlantTarget(target))
                    {
                        plantedCount++;
                    }
                }

                return plantedCount;
            }
            catch (Exception ex)
            {
                Monitor.Log("Failed to execute direct planting in creative mode.", LogLevel.Error);
                Monitor.Log(ex.ToString(), LogLevel.Error);
                return 0;
            }
        }

        private void PrepareGroundForPlanting(List<PlantingTarget> targets)
        {
            foreach (PlantingTarget target in targets)
            {
                if (target.Mode == PlantingMode.Ground)
                {
                    Vector2 tile = target.Tile;

                    // 检查是否已经有耕地
                    if (!Game1.currentLocation.terrainFeatures.TryGetValue(tile, out var terrainFeature) ||
                        !(terrainFeature is StardewValley.TerrainFeatures.HoeDirt))
                    {
                        // 移除任何现有的地形特征
                        if (Game1.currentLocation.terrainFeatures.ContainsKey(tile))
                            Game1.currentLocation.terrainFeatures.Remove(tile);

                        // 创建新的耕地（已浇水状态）
                        var hoeDirt = new StardewValley.TerrainFeatures.HoeDirt(1, Game1.currentLocation);
                        Game1.currentLocation.terrainFeatures.Add(tile, hoeDirt);
                    }
                }
            }
        }

        private bool TryPlantTarget(PlantingTarget target)
        {
            if (target == null)
                return false;

            Item seedItem = ItemRegistry.Create(target.SeedItemId);
            if (seedItem == null)
                return false;

            Vector2 targetTile = target.Tile;

            if (target.Mode == PlantingMode.IndoorPot)
            {
                if (!Game1.currentLocation.Objects.TryGetValue(targetTile, out var obj))
                    return false;

                MethodInfo method = obj.GetType().GetMethod("performObjectDropInAction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null)
                    return false;

                object result = method.Invoke(obj, new object[] { seedItem, false, Game1.player, false });
                return result is bool planted && planted;
            }

            if (!Game1.currentLocation.terrainFeatures.TryGetValue(targetTile, out var terrainFeature) || terrainFeature is not StardewValley.TerrainFeatures.HoeDirt dirt)
                return false;

            object plantedCrop = UnwrapNetValue(GetMemberValue(dirt, "crop"));
            if (plantedCrop != null)
                return false;

            StardewValley.Crop crop = CreateCropInstance(target.SeedItemId, targetTile, Game1.currentLocation);
            if (crop == null)
                return false;

            return TrySetMemberValue(dirt, "crop", crop);
        }

        private void ShowPlantingIssues(List<PlantingValidationIssue> issues)
        {
            if (issues == null || issues.Count == 0)
                return;

            const int maxLines = 6;
            List<string> lines = new List<string>
            {
                Helper.Translation.Get("msg.assisted-planting-failed-header")
            };

            foreach (string reason in issues.Select(issue => issue.Reason).Distinct().Take(maxLines))
                lines.Add($"- {reason}");

            if (issues.Select(issue => issue.Reason).Distinct().Count() > maxLines)
                lines.Add(Helper.Translation.Get("msg.assisted-planting-failed-more"));

            Game1.drawObjectDialogue(string.Join(Environment.NewLine, lines));
        }

        private string GetSeedDisplayName(string seedItemId, string fallbackName)
        {
            ParsedItemData seedData = ItemRegistry.GetData(seedItemId);
            if (seedData != null)
                return seedData.DisplayName;

            return string.IsNullOrWhiteSpace(fallbackName) ? seedItemId : fallbackName;
        }

        private string FormatTile(Vector2 tile)
        {
            return $"({(int)tile.X}, {(int)tile.Y})";
        }

        private PlantingPlan TryCreateGroundPlantingPlan(StardewValley.TerrainFeatures.HoeDirt dirt, int tileX, int tileY)
        {
            object crop = GetMemberValue(dirt, "crop");
            return CreatePlantingPlanFromCrop(crop, tileX, tileY, PlantingMode.Ground, requiresPot: false, requiresWateredHoeDirt: true);
        }

        private PlantingPlan TryCreateIndoorPotPlantingPlan(StardewValley.Object obj, int tileX, int tileY)
        {
            if (obj == null || obj.GetType().FullName != "StardewValley.Objects.IndoorPot")
                return null;

            object hoeDirtRef = GetMemberValue(obj, "hoeDirt");
            object hoeDirt = UnwrapNetValue(hoeDirtRef);
            object crop = hoeDirt != null ? GetMemberValue(hoeDirt, "crop") : null;
            return CreatePlantingPlanFromCrop(crop, tileX, tileY, PlantingMode.IndoorPot, requiresPot: true, requiresWateredHoeDirt: false);
        }

        private PlantingPlan CreatePlantingPlanFromCrop(object cropRef, int tileX, int tileY, PlantingMode mode, bool requiresPot, bool requiresWateredHoeDirt)
        {
            object crop = UnwrapNetValue(cropRef);
            if (crop == null)
                return null;

            string seedItemId = GetCropSeedItemId(crop);
            if (string.IsNullOrWhiteSpace(seedItemId))
                return null;

            ParsedItemData seedData = ItemRegistry.GetData(seedItemId);
            return new PlantingPlan
            {
                TileX = tileX,
                TileY = tileY,
                Mode = mode,
                SeedItemId = seedItemId,
                CropId = GetCropHarvestId(crop),
                DisplayName = seedData?.DisplayName ?? seedItemId,
                RequiresPot = requiresPot,
                RequiresWateredHoeDirt = requiresWateredHoeDirt,
                Season = GetCropSeason(crop)
            };
        }

        private string GetCropSeedItemId(object crop)
        {
            object seedIndexRef = GetMemberValue(crop, "netSeedIndex") ?? GetMemberValue(crop, "seedIndex");
            object seedIndex = UnwrapNetValue(seedIndexRef);
            if (seedIndex == null)
                return null;

            string value = seedIndex.ToString();
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.StartsWith("(O)") ? value : $"(O){value}";
        }

        private string GetCropHarvestId(object crop)
        {
            object harvestRef = GetMemberValue(crop, "indexOfHarvest") ?? GetMemberValue(crop, "netHarvestItem");
            object harvest = UnwrapNetValue(harvestRef);
            return harvest?.ToString();
        }

        private string GetCropSeason(object crop)
        {
            object seasonsObj = GetMemberValue(crop, "seasonsToGrowIn");
            if (seasonsObj is IEnumerable<string> seasonStrings)
                return string.Join(", ", seasonStrings);

            if (seasonsObj is System.Collections.IEnumerable seasonsEnumerable)
            {
                var seasons = new List<string>();
                foreach (object season in seasonsEnumerable)
                {
                    if (season != null)
                        seasons.Add(season.ToString());
                }

                if (seasons.Count > 0)
                    return string.Join(", ", seasons);
            }

            return null;
        }

        private object GetMemberValue(object instance, string memberName)
        {
            if (instance == null)
                return null;

            Type type = instance.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null)
                return property.GetValue(instance);

            FieldInfo field = type.GetField(memberName, flags);
            return field?.GetValue(instance);
        }

        private bool TrySetMemberValue(object instance, string memberName, object value)
        {
            if (instance == null)
                return false;

            Type type = instance.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null)
            {
                if (property.CanWrite && property.PropertyType.IsInstanceOfType(value))
                {
                    property.SetValue(instance, value);
                    return true;
                }

                object wrappedValue = property.GetValue(instance);
                if (TrySetWrappedValue(wrappedValue, value))
                    return true;
            }

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
            {
                if (field.FieldType.IsInstanceOfType(value))
                {
                    field.SetValue(instance, value);
                    return true;
                }

                object wrappedValue = field.GetValue(instance);
                if (TrySetWrappedValue(wrappedValue, value))
                    return true;
            }

            return false;
        }

        private bool TrySetWrappedValue(object wrappedValue, object value)
        {
            if (wrappedValue == null)
                return false;

            PropertyInfo valueProperty = wrappedValue.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (valueProperty == null || !valueProperty.CanWrite)
                return false;

            valueProperty.SetValue(wrappedValue, value);
            return true;
        }

        private StardewValley.Crop CreateCropInstance(string seedItemId, Vector2 targetTile, GameLocation location)
        {
            string unqualifiedSeedId = seedItemId?.StartsWith("(O)") == true ? seedItemId.Substring(3) : seedItemId;
            if (string.IsNullOrWhiteSpace(unqualifiedSeedId))
                return null;

            return new StardewValley.Crop(unqualifiedSeedId, (int)targetTile.X, (int)targetTile.Y, location);
        }

        private StardewValley.Object CloneWorldObject(StardewValley.Object obj)
        {
            if (obj == null)
                return null;

            return obj.getOne() as StardewValley.Object;
        }

        private StardewValley.TerrainFeatures.TerrainFeature CloneTerrainFeature(StardewValley.TerrainFeatures.TerrainFeature feature, Vector2 tile)
        {
            if (feature == null)
                return null;

            if (feature is StardewValley.TerrainFeatures.HoeDirt dirt)
                return CloneHoeDirt(dirt, tile);

            if (feature is StardewValley.TerrainFeatures.Flooring flooring)
                return new StardewValley.TerrainFeatures.Flooring(flooring.whichFloor.Value);

            return feature;
        }

        private StardewValley.TerrainFeatures.HoeDirt CloneHoeDirt(StardewValley.TerrainFeatures.HoeDirt dirt, Vector2 tile)
        {
            int state = Convert.ToInt32(UnwrapNetValue(GetMemberValue(dirt, "state")) ?? 0);
            var clone = new StardewValley.TerrainFeatures.HoeDirt(state, Game1.currentLocation);

            object fertilizer = UnwrapNetValue(GetMemberValue(dirt, "fertilizer"));
            if (fertilizer != null)
                TrySetMemberValue(clone, "fertilizer", fertilizer);

            object crop = UnwrapNetValue(GetMemberValue(dirt, "crop"));
            if (crop != null)
            {
                string seedItemId = GetCropSeedItemId(crop);
                StardewValley.Crop cropClone = CreateCropInstance(seedItemId, tile, Game1.currentLocation);
                if (cropClone != null)
                    TrySetMemberValue(clone, "crop", cropClone);
            }

            return clone;
        }

        private object UnwrapNetValue(object value)
        {
            if (value == null)
                return null;

            PropertyInfo property = value.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property != null ? property.GetValue(value) : value;
        }
    }

    public class CancelableNamingMenu : NamingMenu
    {
        public CancelableNamingMenu(doneNamingBehavior behavior, string title, string defaultName)
            : base(behavior, title, defaultName)
        {
        }

        public override void receiveKeyPress(Keys key)
        {
            if (key == Keys.Escape)
            {
                this.exitThisMenu();
                return;
            }

            base.receiveKeyPress(key);
        }
    }

    public class BlueprintFile { public BlueprintMetadata Metadata { get; set; } public List<BlueprintItem> Items { get; set; } public List<PlantingPlan> PlantingPlans { get; set; } = new List<PlantingPlan>(); }
    public class BlueprintMetadata { public string Name { get; set; } public int Width { get; set; } public int Height { get; set; } }
    public class BlueprintItem { public string ItemId { get; set; } public string FlooringId { get; set; } public float TileX { get; set; } public float TileY { get; set; } public string Name { get; set; } public string ItemType { get; set; } = "Object"; }
    public enum PlantingMode { Ground, IndoorPot }
    public class PlantingPlan
    {
        public float TileX { get; set; }
        public float TileY { get; set; }
        public PlantingMode Mode { get; set; }
        public string SeedItemId { get; set; }
        public string CropId { get; set; }
        public string DisplayName { get; set; }
        public bool RequiresPot { get; set; }
        public bool RequiresWateredHoeDirt { get; set; }
        public string Season { get; set; }
    }
    public class GhostItem
    {
        public Vector2 Tile { get; set; }
        public string ItemId { get; set; }
        public bool IsPlantingHint { get; set; }
        public PlantingMode PlantingMode { get; set; }
        public string DisplayName { get; set; }
        public string Season { get; set; }
        public string LocationName { get; set; }
    }

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
        public List<GhostItem> AddedGhosts { get; set; } = new List<GhostItem>();
        public List<GhostItem> RestoredGhosts { get; set; } = new List<GhostItem>();
    }

    public class PlantingValidationIssue
    {
        public Vector2? Tile { get; set; }
        public string SeedItemId { get; set; }
        public PlantingMode? PlantingMode { get; set; }
        public string Reason { get; set; }
    }

    public class PlantingTarget
    {
        public Vector2 Tile { get; set; }
        public string SeedItemId { get; set; }
        public PlantingMode Mode { get; set; }
        public string DisplayName { get; set; }
        public string Season { get; set; }
    }

    public class SeedShortage
    {
        public string SeedItemId { get; set; }
        public string DisplayName { get; set; }
        public int RequiredCount { get; set; }
    }

    // Local definition for IGenericModConfigMenuApi to allow compilation.
    // The actual API is provided by the Generic Mod Config Menu mod at runtime.
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
        void AddKeybind(IManifest mod, Func<SButton> getValue, Action<SButton> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
        void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
        void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string> tooltip = null, int? min = null, int? max = null, int? interval = null, string fieldId = null);
        void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue, Func<string> name, Func<string> tooltip = null, string[] allowedValues = null, Func<string, string> formatAllowedValue = null, string fieldId = null);
        void AddKeybindList(IManifest mod, Func<KeybindList> getValue, Action<KeybindList> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
    }
    }
