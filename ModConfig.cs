using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace BlueprintMod
{
    public class ModConfig
    {
        // === 推荐全部改成 KeybindList ===
        public KeybindList ToggleCreativeMode { get; set; } = KeybindList.Parse("K");
        public KeybindList ToggleOverwriteMode { get; set; } = KeybindList.Parse("O");
        public KeybindList OpenBlueprintBrowser { get; set; } = KeybindList.Parse("LeftControl + L");
        public KeybindList UndoKey { get; set; } = KeybindList.Parse("LeftControl + Z");
        public KeybindList ClearGhosts { get; set; } = KeybindList.Parse("LeftControl + C");

        // === 鼠标操作（不建议改 KeybindList）===
        public SButton SelectStartTile { get; set; } = SButton.MouseLeft;
        public SButton SelectEndTile { get; set; } = SButton.MouseLeft;

        // === 修饰键（建议保持 SButton）===
        public SButton ModModifier { get; set; } = SButton.LeftControl;

        // === 其他配置 ===
        public bool EnableCreativeMode { get; set; } = false;
        public bool DefaultOverwriteMode { get; set; } = true;
        public bool DefaultCreativeMode { get; set; } = false;
        public int MaxUndoSteps { get; set; } = 10;

        public string ChestRangeMode { get; set; } = "Global";
        public int ChestSearchRange { get; set; } = 10;
    }
}
