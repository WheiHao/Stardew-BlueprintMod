using StardewModdingAPI;

namespace BlueprintMod
{
    public class ModConfig
    {
        public SButton ToggleCreativeMode { get; set; } = SButton.K;
        public SButton ToggleOverwriteMode { get; set; } = SButton.O;
        public SButton OpenBlueprintBrowser { get; set; } = SButton.L;
        public SButton UndoKey { get; set; } = SButton.Z;
        public SButton ClearGhosts { get; set; } = SButton.C;
        public SButton SelectStartTile { get; set; } = SButton.MouseLeft;
        public SButton SelectEndTile { get; set; } = SButton.MouseLeft;
        public SButton ModModifier { get; set; } = SButton.LeftControl;
        public bool DefaultOverwriteMode { get; set; } = true;
        public bool DefaultCreativeMode { get; set; } = false;
        public int MaxUndoSteps { get; set; } = 10;
        public int ChestSearchRange { get; set; } = 10;
    }
}