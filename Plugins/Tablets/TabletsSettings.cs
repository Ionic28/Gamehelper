namespace Tablets
{
    using System.Numerics;
    using GameHelper.Plugin;

    /// <summary>
    ///     Settings for tower tablet highlighting.
    /// </summary>
    public sealed class TabletsSettings : IPSettings
    {
        public bool ShowStashOverlay = true;
        public bool ShowInventoryOverlay = false;
        public bool HideWhenGameInBackground = true;
        public bool HideWhenHoveringItem = false;
        public bool ShowDebugInfo = false;

        public bool DrawNormalTablet = true;
        public bool DrawMagicTablet = true;
        public bool DrawRareTablet = true;
        public bool OnlyShowMatchingMods = false;
        public float FrameThickness = 2f;
        public float FrameInset = 3f;

        public CommonPrefixModsSettings CommonPrefixModsSettings = new();
        public BreachTabletSettings BreachTabletSettings = new();
        public RitualTabletSettings RitualTabletSettings = new();
        public OverseerTabletSettings OverseerTabletSettings = new();
        public ExpeditionTabletSettings ExpeditionTabletSettings = new();
        public DeliriumTabletSettings DeliriumTabletSettings = new();
        public PrecursorTabletSettings PrecursorTabletSettings = new();
        public BorderRenderSettings BorderRenderSettings = new();
    }

    public sealed class BorderRenderSettings
    {
        public Vector4 NormalBorderColor = new(1f, 1f, 1f, 1f);
        public Vector4 MagicBorderColor = new(0.15f, 0.45f, 1f, 1f);
        public Vector4 RareBorderColor = new(1f, 0.86f, 0.18f, 1f);
    }

    public sealed class CommonPrefixModsSettings
    {
        public bool MapDroppedItemQuantityIncrease;
        public bool MapDroppedItemRarityIncrease;
        public bool MapPackSizeIncrease;
        public bool MapMagicPackIncrease;
        public bool MapRarePackIncrease;
        public bool MapDroppedGoldIncrease;
        public bool MapExperienceGainIncrease;
    }

    public sealed class BreachTabletSettings
    {
        public bool EnableBreachTablet;
        public bool BreachMagicMonsterIncrease;
        public bool BreachRareMonsterIncrease;
        public bool BreachDensityIncrease;
        public bool BreachSpeedIncrease;
        public bool BreachChestAdditional;
        public bool BreachMonsterSplinterIncrease;
        public bool Breach3AdditionalChance;
        public bool BreachAdditionalChance;
    }

    public sealed class RitualTabletSettings
    {
        public bool EnableRitualTablet;
        public bool RitualTributeIncrease;
        public bool RitualRerollCostDecrease;
        public bool RitualDeferCostDecrease;
        public bool RitualDeferSpeedIncrease;
        public bool RitualExtraReroll;
        public bool RitualFreeRerollChance;
        public bool RitualRareMonstersIncrease;
        public bool RitualMagicMonstersIncrease;
        public bool RitualOmensIncrease;
    }

    public sealed class OverseerTabletSettings
    {
        public bool EnableOverseerTablet;
        public bool MapBossStrongboxAdditional;
        public bool MapBossShrineAdditional;
        public bool MapBossEssenceAdditional;
        public bool MapBossWaystoneChance;
        public bool MapBossExperienceIncrease;
        public bool MapBossRarityIncrease;
        public bool MapBossQuantityIncrease;
    }

    public sealed class ExpeditionTabletSettings
    {
        public bool EnableExpeditionTablet;
        public bool ExpeditionArtifactsIncrease;
        public bool ExpeditionPlacementRangeIncrease;
        public bool ExpeditionRemnantAdditional;
        public bool ExpeditionRadiusIncrease;
        public bool ExpeditionLogbookQuantityIncrease;
        public bool ExpeditionRaresIncrease;
        public bool ExpeditionRemnantEffectIncrease;
        public bool ExpeditionRunicMonstersIncrease;
    }

    public sealed class DeliriumTabletSettings
    {
        public bool EnableDeliriumTablet;
        public bool DeliriumSplintersIncrease;
        public bool DeliriumProgressIncrease;
        public bool DeliriumDurationIncrease;
        public bool DeliriumDissipationDecrease;
        public bool DeliriumDifficultyIncrease;
        public bool DeliriumPackSizeIncrease;
        public bool DeliriumMirrorsIncrease;
        public bool DeliriumPauseOnRareKills;
        public bool DeliriumBossChanceIncrease;
        public bool DeliriumRewardTypeAdditionalChance;
    }

    public sealed class PrecursorTabletSettings
    {
        public bool EnablePrecursorTablet;
        public bool PrecursorWaystonesIncrease;
        public bool PrecursorRareModifierChance;
        public bool PrecursorShrineChance;
        public bool PrecursorStrongboxChance;
        public bool PrecursorEssenceChance;
        public bool PrecursorModifierAdditional;
    }
}
