namespace Tablets
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using System.Reflection;
    using GameHelper;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using GameOffsets.Objects.UiElement;
    using ImGuiNET;
    using Newtonsoft.Json;

    /// <summary>
    ///     Highlights useful tower tablets in visible stash/inventory slots.
    /// </summary>
    public sealed class Tablets : PCore<TabletsSettings>
    {
        private const int ItemPointerOffset = 0x4F8;

        private object? handleObj;
        private object? uiParentsObj;
        private MethodInfo? readUiOffsetMethod;
        private MethodInfo? readStdVectorMethod;
        private MethodInfo? readIntPtrMethod;

        private readonly List<FrameInfo> cachedFrames = new();
        private readonly Dictionary<string, List<string>> modTemplates = new(StringComparer.OrdinalIgnoreCase);
        private DateTime nextScanUtc = DateTime.MinValue;
        private bool anyItemHovered;

        private string SettingPathname => Path.Join(this.DllDirectory, "config", "settings.txt");
        private string ModsDirectory => Path.Join(this.DllDirectory, "mods");

        /// <inheritdoc/>
        public override void OnEnable(bool isGameOpened)
        {
            if (File.Exists(this.SettingPathname))
            {
                try
                {
                    this.Settings = JsonConvert.DeserializeObject<TabletsSettings>(File.ReadAllText(this.SettingPathname))
                        ?? new TabletsSettings();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Tablets] Failed to load settings: {ex.Message}");
                    this.Settings = new TabletsSettings();
                }
            }

            this.LoadModTemplates();
        }

        /// <inheritdoc/>
        public override void OnDisable()
        {
            this.cachedFrames.Clear();
            this.handleObj = null;
            this.uiParentsObj = null;
            this.readUiOffsetMethod = null;
            this.readStdVectorMethod = null;
            this.readIntPtrMethod = null;
        }

        /// <inheritdoc/>
        public override void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(this.SettingPathname) ?? string.Empty);
                File.WriteAllText(this.SettingPathname, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Tablets] Failed to save settings: {ex.Message}");
            }
        }

        private void LoadModTemplates()
        {
            this.modTemplates.Clear();
            if (!Directory.Exists(this.ModsDirectory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(this.ModsDirectory, "*.txt"))
            {
                var key = Path.GetFileNameWithoutExtension(file);
                var lines = File.ReadAllLines(file)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                this.modTemplates[key] = lines;
            }
        }

        /// <inheritdoc/>
        public override void DrawSettings()
        {
            if (ImGui.CollapsingHeader("Overlay", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Checkbox("Show stash tablet frames", ref this.Settings.ShowStashOverlay);
                ImGui.Checkbox("Show inventory tablet frames", ref this.Settings.ShowInventoryOverlay);
                ImGui.Checkbox("Hide when game is in background", ref this.Settings.HideWhenGameInBackground);
                ImGui.Checkbox("Hide frames while hovering an item", ref this.Settings.HideWhenHoveringItem);
                ImGui.Checkbox("Show debug boxes", ref this.Settings.ShowDebugInfo);
                ImGui.SliderFloat("Frame thickness", ref this.Settings.FrameThickness, 1f, 8f, "%.1f");
                ImGui.SliderFloat("Frame inset", ref this.Settings.FrameInset, 0f, 12f, "%.1f");
            }

            if (ImGui.CollapsingHeader("Tablet Rules", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Checkbox("Draw normal tablets", ref this.Settings.DrawNormalTablet);
                ImGui.Checkbox("Draw magic tablets", ref this.Settings.DrawMagicTablet);
                ImGui.Checkbox("Draw rare tablets", ref this.Settings.DrawRareTablet);
                ImGui.Checkbox("Only show tablets with selected mods", ref this.Settings.OnlyShowMatchingMods);
            }

            this.DrawColorSettings();
            this.DrawModSettings();
        }

        /// <inheritdoc/>
        public override void DrawUI()
        {
            if (Core.States.GameCurrentState != GameStateTypes.InGameState)
            {
                this.cachedFrames.Clear();
                return;
            }

            if (this.Settings.HideWhenGameInBackground && !Core.Process.Foreground && !Core.IsSettingsMenuOpen)
            {
                return;
            }

            if (!this.Settings.ShowStashOverlay && !this.Settings.ShowInventoryOverlay && !this.Settings.ShowDebugInfo)
            {
                return;
            }

            if (!this.EnsureReflection())
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (!Core.IsSettingsMenuOpen && now >= this.nextScanUtc)
            {
                this.RefreshFrames();
                this.nextScanUtc = now.AddMilliseconds(100);
            }

            if (this.Settings.HideWhenHoveringItem && this.anyItemHovered)
            {
                return;
            }

            this.DrawFrames();
        }

        private void DrawColorSettings()
        {
            if (!ImGui.CollapsingHeader("Colors"))
            {
                return;
            }

            ImGui.ColorEdit4("Normal tablet", ref this.Settings.BorderRenderSettings.NormalBorderColor);
            ImGui.ColorEdit4("Magic tablet", ref this.Settings.BorderRenderSettings.MagicBorderColor);
            ImGui.ColorEdit4("Rare tablet", ref this.Settings.BorderRenderSettings.RareBorderColor);
        }

        private void DrawModSettings()
        {
            if (ImGui.CollapsingHeader("Common Prefix Mods"))
            {
                var s = this.Settings.CommonPrefixModsSettings;
                ImGui.Checkbox("(10-20)% increased Quantity of Items found in your Maps", ref s.MapDroppedItemQuantityIncrease);
                ImGui.Checkbox("(10-20)% increased Rarity of Items found in your Maps", ref s.MapDroppedItemRarityIncrease);
                ImGui.Checkbox("(3-8)% increased Pack Size in your Maps", ref s.MapPackSizeIncrease);
                ImGui.Checkbox("(15-25)% increased Magic Monsters in your Maps", ref s.MapMagicPackIncrease);
                ImGui.Checkbox("(10-15)% increased Rare Monsters in your Maps", ref s.MapRarePackIncrease);
                ImGui.Checkbox("(5-10)% increased Gold found in your Maps", ref s.MapDroppedGoldIncrease);
                ImGui.Checkbox("(5-10)% increased Experience gain in your Maps", ref s.MapExperienceGainIncrease);
            }

            this.DrawBreachSettings();
            this.DrawRitualSettings();
            this.DrawOverseerSettings();
            this.DrawExpeditionSettings();
            this.DrawDeliriumSettings();
            this.DrawPrecursorSettings();
        }

        private void DrawBreachSettings()
        {
            if (!ImGui.CollapsingHeader("Breach Tablets"))
            {
                return;
            }

            var s = this.Settings.BreachTabletSettings;
            ImGui.Checkbox("Enable Breach tablets", ref s.EnableBreachTablet);
            ImGui.Checkbox("Breaches spawn increased Magic Monsters", ref s.BreachMagicMonsterIncrease);
            ImGui.Checkbox("Breaches spawn an additional Rare Monster", ref s.BreachRareMonsterIncrease);
            ImGui.Checkbox("Breaches have increased Monster density", ref s.BreachDensityIncrease);
            ImGui.Checkbox("Breaches open and close faster", ref s.BreachSpeedIncrease);
            ImGui.Checkbox("Breaches contain 1 additional Clasped Hand", ref s.BreachChestAdditional);
            ImGui.Checkbox("Increased Quantity of Breach Splinters", ref s.BreachMonsterSplinterIncrease);
            ImGui.Checkbox("Chance to contain three additional Breaches", ref s.Breach3AdditionalChance);
            ImGui.Checkbox("Chance to contain an additional Breach", ref s.BreachAdditionalChance);
        }

        private void DrawRitualSettings()
        {
            if (!ImGui.CollapsingHeader("Ritual Tablets"))
            {
                return;
            }

            var s = this.Settings.RitualTabletSettings;
            ImGui.Checkbox("Enable Ritual tablets", ref s.EnableRitualTablet);
            ImGui.Checkbox("Monsters grant increased Tribute", ref s.RitualTributeIncrease);
            ImGui.Checkbox("Rerolling costs reduced Tribute", ref s.RitualRerollCostDecrease);
            ImGui.Checkbox("Deferring costs reduced Tribute", ref s.RitualDeferCostDecrease);
            ImGui.Checkbox("Deferred Favours reappear sooner", ref s.RitualDeferSpeedIncrease);
            ImGui.Checkbox("Allow rerolling Favours an additional time", ref s.RitualExtraReroll);
            ImGui.Checkbox("Chance to reroll for no Tribute", ref s.RitualFreeRerollChance);
            ImGui.Checkbox("Increased chance for Rare Monsters", ref s.RitualRareMonstersIncrease);
            ImGui.Checkbox("Increased chance for Magic Monsters", ref s.RitualMagicMonstersIncrease);
            ImGui.Checkbox("Increased chance to contain Omens", ref s.RitualOmensIncrease);
        }

        private void DrawOverseerSettings()
        {
            if (!ImGui.CollapsingHeader("Overseer Tablets"))
            {
                return;
            }

            var s = this.Settings.OverseerTabletSettings;
            ImGui.Checkbox("Enable Overseer tablets", ref s.EnableOverseerTablet);
            ImGui.Checkbox("Map Boss areas contain an additional Strongbox", ref s.MapBossStrongboxAdditional);
            ImGui.Checkbox("Map Boss areas contain an additional Shrine", ref s.MapBossShrineAdditional);
            ImGui.Checkbox("Map Boss areas contain an additional Essence", ref s.MapBossEssenceAdditional);
            ImGui.Checkbox("Map Boss has chance to drop a Waystone", ref s.MapBossWaystoneChance);
            ImGui.Checkbox("Map Bosses grant increased Experience", ref s.MapBossExperienceIncrease);
            ImGui.Checkbox("Increased Rarity from Map Bosses", ref s.MapBossRarityIncrease);
            ImGui.Checkbox("Increased Quantity from Map Bosses", ref s.MapBossQuantityIncrease);
        }

        private void DrawExpeditionSettings()
        {
            if (!ImGui.CollapsingHeader("Expedition Tablets"))
            {
                return;
            }

            var s = this.Settings.ExpeditionTabletSettings;
            ImGui.Checkbox("Enable Expedition tablets", ref s.EnableExpeditionTablet);
            ImGui.Checkbox("Increased quantity of Artifacts", ref s.ExpeditionArtifactsIncrease);
            ImGui.Checkbox("Increased Explosive Placement Range", ref s.ExpeditionPlacementRangeIncrease);
            ImGui.Checkbox("Expeditions have +1 Remnant", ref s.ExpeditionRemnantAdditional);
            ImGui.Checkbox("Increased Explosive Radius", ref s.ExpeditionRadiusIncrease);
            ImGui.Checkbox("Increased Logbook Quantity", ref s.ExpeditionLogbookQuantityIncrease);
            ImGui.Checkbox("Increased Rare Expedition Monsters", ref s.ExpeditionRaresIncrease);
            ImGui.Checkbox("Increased Effect of Remnants", ref s.ExpeditionRemnantEffectIncrease);
            ImGui.Checkbox("Increased Runic Monster Markers", ref s.ExpeditionRunicMonstersIncrease);
        }

        private void DrawDeliriumSettings()
        {
            if (!ImGui.CollapsingHeader("Delirium Tablets"))
            {
                return;
            }

            var s = this.Settings.DeliriumTabletSettings;
            ImGui.Checkbox("Enable Delirium tablets", ref s.EnableDeliriumTablet);
            ImGui.Checkbox("Increased Simulacrum Splinter stack size", ref s.DeliriumSplintersIncrease);
            ImGui.Checkbox("Increased Reward Progress", ref s.DeliriumProgressIncrease);
            ImGui.Checkbox("Fog lasts additional seconds", ref s.DeliriumDurationIncrease);
            ImGui.Checkbox("Fog dissipates slower", ref s.DeliriumDissipationDecrease);
            ImGui.Checkbox("Faster difficulty increase", ref s.DeliriumDifficultyIncrease);
            ImGui.Checkbox("Increased Pack Size", ref s.DeliriumPackSizeIncrease);
            ImGui.Checkbox("Increased Fracturing Mirrors", ref s.DeliriumMirrorsIncrease);
            ImGui.Checkbox("Timer pauses on Rare kills", ref s.DeliriumPauseOnRareKills);
            ImGui.Checkbox("More likely for Unique Bosses", ref s.DeliriumBossChanceIncrease);
            ImGui.Checkbox("Chance for additional Reward type", ref s.DeliriumRewardTypeAdditionalChance);
        }

        private void DrawPrecursorSettings()
        {
            if (!ImGui.CollapsingHeader("Precursor Tablets"))
            {
                return;
            }

            var s = this.Settings.PrecursorTabletSettings;
            ImGui.Checkbox("Enable Precursor tablets", ref s.EnablePrecursorTablet);
            ImGui.Checkbox("Increased Quantity of Waystones", ref s.PrecursorWaystonesIncrease);
            ImGui.Checkbox("Rare Monsters can have an additional Modifier", ref s.PrecursorRareModifierChance);
            ImGui.Checkbox("Chance to contain a Shrine", ref s.PrecursorShrineChance);
            ImGui.Checkbox("Chance to contain a Strongbox", ref s.PrecursorStrongboxChance);
            ImGui.Checkbox("Chance to contain an Essence", ref s.PrecursorEssenceChance);
            ImGui.Checkbox("1 additional random Modifier", ref s.PrecursorModifierAdditional);
        }

        private bool EnsureReflection()
        {
            if (this.handleObj != null)
            {
                return true;
            }

            var handleProp = typeof(GameProcess).GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic);
            this.handleObj = handleProp?.GetValue(Core.Process);
            if (this.handleObj == null)
            {
                return false;
            }

            var methods = this.handleObj.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var readMem = methods.First(m => m.Name == "ReadMemory" && m.IsGenericMethod && m.GetParameters().Length == 1);
            var readVec = methods.First(m => m.Name == "ReadStdVector" && m.IsGenericMethod);
            this.readUiOffsetMethod = readMem.MakeGenericMethod(typeof(UiElementBaseOffset));
            this.readStdVectorMethod = readVec.MakeGenericMethod(typeof(IntPtr));
            this.readIntPtrMethod = readMem.MakeGenericMethod(typeof(IntPtr));
            return true;
        }

        private void RefreshFrames()
        {
            var frames = new List<FrameInfo>();
            var anyHovered = false;

            var gameUi = Core.States.InGameStateObject?.GameUi;
            if (gameUi == null || gameUi.Address == IntPtr.Zero)
            {
                this.cachedFrames.Clear();
                this.anyItemHovered = false;
                return;
            }

            if (this.Settings.ShowStashOverlay && gameUi.LeftPanel.IsVisible)
            {
                this.ScanPanel(gameUi.LeftPanel.Address, frames, ref anyHovered);
            }

            if (this.Settings.ShowInventoryOverlay && gameUi.RightPanel.IsVisible)
            {
                this.ScanPanel(gameUi.RightPanel.Address, frames, ref anyHovered);
            }

            this.cachedFrames.Clear();
            this.cachedFrames.AddRange(frames);
            this.anyItemHovered = anyHovered;
        }

        private void ScanPanel(IntPtr panelAddr, List<FrameInfo> frames, ref bool anyHovered)
        {
            if (panelAddr == IntPtr.Zero)
            {
                return;
            }

            this.uiParentsObj ??= PluginUiElementReflection.CreateParents();
            if (this.uiParentsObj == null)
            {
                return;
            }

            var queue = new Queue<IntPtr>();
            var visited = new HashSet<IntPtr>();
            var mousePos = ImGui.GetIO().MousePos;
            queue.Enqueue(panelAddr);

            while (queue.Count > 0 && visited.Count < 5000)
            {
                var el = queue.Dequeue();
                if (el == IntPtr.Zero || !visited.Add(el))
                {
                    continue;
                }

                if (this.readUiOffsetMethod!.Invoke(this.handleObj, new object[] { el }) is not UiElementBaseOffset off ||
                    !UiElementBaseFuncs.IsVisibleChecker(off.Flags))
                {
                    continue;
                }

                if (this.readStdVectorMethod!.Invoke(this.handleObj, new object[] { off.ChildrensPtr }) is IntPtr[] kids)
                {
                    foreach (var kid in kids)
                    {
                        queue.Enqueue(kid);
                    }
                }

                var itemPtrObj = this.readIntPtrMethod!.Invoke(this.handleObj, new object[] { el + ItemPointerOffset });
                var itemPtr = itemPtrObj is IntPtr ptr ? ptr : IntPtr.Zero;
                if (itemPtr == IntPtr.Zero)
                {
                    continue;
                }

                var item = ReadFreshItem(itemPtr);
                if (item == null || !IsTabletItem(item) || !this.ShouldRenderTablet(item))
                {
                    continue;
                }

                try
                {
                    var uiElement = PluginUiElementReflection.CreateUiElement(el, this.uiParentsObj);
                    if (uiElement == null)
                    {
                        continue;
                    }

                    var pos = (Vector2)PluginUiElementReflection.UiElementPositionProperty!.GetValue(uiElement)!;
                    var size = (Vector2)PluginUiElementReflection.UiElementSizeProperty!.GetValue(uiElement)!;
                    if (pos == Vector2.Zero || size.X <= 0f || size.Y <= 0f)
                    {
                        continue;
                    }

                    if (mousePos.X >= pos.X && mousePos.X <= pos.X + size.X &&
                        mousePos.Y >= pos.Y && mousePos.Y <= pos.Y + size.Y)
                    {
                        anyHovered = true;
                    }

                    var color = this.GetFrameColorByMods(item);
                    frames.Add(new FrameInfo(pos, size, color, DescribeTablet(item)));
                }
                catch
                {
                    // Stale UI elements can disappear while the panel is changing.
                }
            }
        }

        private void DrawFrames()
        {
            var draw = ImGui.GetForegroundDrawList();
            var inset = Math.Max(0f, this.Settings.FrameInset);
            var thickness = Math.Max(1f, this.Settings.FrameThickness);

            foreach (var frame in this.cachedFrames)
            {
                var min = frame.Pos + new Vector2(inset);
                var max = frame.Pos + frame.Size - new Vector2(inset);
                if (max.X <= min.X || max.Y <= min.Y)
                {
                    continue;
                }

                var color = ImGui.ColorConvertFloat4ToU32(frame.Color);
                draw.AddRect(min, max, color, 0f, ImDrawFlags.None, thickness);
                if (this.Settings.ShowDebugInfo)
                {
                    draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), min + new Vector2(2f, 2f), color, frame.DebugText);
                }
            }
        }

        private static bool IsTabletItem(Item item)
        {
            if (item.Path?.Contains("Tablet", StringComparison.OrdinalIgnoreCase) == true ||
                item.Path?.Contains("TowerAugment", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            if (item.TryGetComponent<Base>(out var baseComponent))
            {
                return baseComponent.BaseItemName.Contains("Tablet", StringComparison.OrdinalIgnoreCase) ||
                       baseComponent.InternalName.Contains("Tablet", StringComparison.OrdinalIgnoreCase) ||
                       baseComponent.InternalName.Contains("TowerAugment", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private bool ShouldRenderTablet(Item item)
        {
            var mods = GetTabletMods(item);
            var rarity = GetRarity(item);
            return rarity switch
            {
                Rarity.Normal => this.Settings.DrawNormalTablet,
                Rarity.Magic => this.Settings.DrawMagicTablet &&
                    (!this.Settings.OnlyShowMatchingMods || this.HasEnabledDesiredMod(mods)),
                Rarity.Rare => this.Settings.DrawRareTablet &&
                    (!this.Settings.OnlyShowMatchingMods || this.HasEnabledDesiredMod(mods)),
                _ => false,
            };
        }

        private Vector4 GetFrameColorByMods(Item item)
        {
            return GetRarity(item) switch
            {
                Rarity.Magic => this.Settings.BorderRenderSettings.MagicBorderColor,
                Rarity.Rare => this.Settings.BorderRenderSettings.RareBorderColor,
                _ => this.Settings.BorderRenderSettings.NormalBorderColor,
            };
        }

        private static List<TabletMod> GetTabletMods(Item item)
        {
            var result = new List<TabletMod>();
            if (item.TryGetComponent<Mods>(out var mods))
            {
                AddMods(result, mods.ImplicitMods);
                AddMods(result, mods.ExplicitMods);
                AddMods(result, mods.EnchantMods);
            }

            if (item.TryGetComponent<ObjectMagicProperties>(out var magicProps))
            {
                AddMods(result, magicProps.Mods);
            }

            return result;
        }

        private static void AddMods(List<TabletMod> result, List<(string name, (float value0, float value1) values)> mods)
        {
            foreach (var (name, values) in mods)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result.Add(new TabletMod(name, values));
                }
            }
        }

        private static Rarity GetRarity(Item item)
        {
            if (item.TryGetComponent<Mods>(out var mods))
            {
                return mods.Rarity;
            }

            if (item.TryGetComponent<ObjectMagicProperties>(out var magicProps))
            {
                return magicProps.Rarity;
            }

            return Rarity.Normal;
        }

        private bool HasEnabledDesiredMod(IEnumerable<TabletMod> mods) =>
            mods.Where(mod => !IsUsesMod(mod))
                .Any(mod => this.IsEnabledCommonPrefixMod(mod) ||
                            this.IsEnabledTypeSpecificMod(mod));

        private bool IsEnabledTabletType(TabletMod mod)
        {
            var id = mod.Id;
            return (Matches(id, "TowerAddDeliriumToMapsImplicit", "Delirium") && this.Settings.DeliriumTabletSettings.EnableDeliriumTablet) ||
                   (Matches(id, "TowerAddBreachToMapsImplicit", "Breach") && this.Settings.BreachTabletSettings.EnableBreachTablet) ||
                   (Matches(id, "TowerAddMapBossesToMapsImplicit", "MapBoss", "Boss") && this.Settings.OverseerTabletSettings.EnableOverseerTablet) ||
                   (Matches(id, "TowerAddRitualToMapsImplicit", "Ritual") && this.Settings.RitualTabletSettings.EnableRitualTablet) ||
                   (Matches(id, "TowerAddExpeditionToMapsImplicit", "Expedition") && this.Settings.ExpeditionTabletSettings.EnableExpeditionTablet) ||
                   (Matches(id, "TowerAddIrradiatedToMapsImplicit", "Irradiated", "Precursor") && this.Settings.PrecursorTabletSettings.EnablePrecursorTablet);
        }

        private bool IsEnabledTypeSpecificMod(TabletMod mod) =>
            (this.Settings.BreachTabletSettings.EnableBreachTablet && this.IsEnabledBreachMod(mod)) ||
            (this.Settings.RitualTabletSettings.EnableRitualTablet && this.IsEnabledRitualMod(mod)) ||
            (this.Settings.OverseerTabletSettings.EnableOverseerTablet && this.IsEnabledOverseerMod(mod)) ||
            (this.Settings.ExpeditionTabletSettings.EnableExpeditionTablet && this.IsEnabledExpeditionMod(mod)) ||
            (this.Settings.DeliriumTabletSettings.EnableDeliriumTablet && this.IsEnabledDeliriumMod(mod)) ||
            (this.Settings.PrecursorTabletSettings.EnablePrecursorTablet && this.IsEnabledPrecursorMod(mod));

        private bool IsEnabledCommonPrefixMod(TabletMod mod)
        {
            var s = this.Settings.CommonPrefixModsSettings;
            return MatchFlag(mod.Id,
                ("@delirium_prefix:Quantity of Items found|MapDroppedItemQuantityIncrease|Quantity of Items found", s.MapDroppedItemQuantityIncrease),
                ("@delirium_prefix:Rarity of Items found|MapDroppedItemRarityIncrease|Rarity of Items found", s.MapDroppedItemRarityIncrease),
                ("@delirium_prefix:Pack Size in Map|MapPackSizeIncrease|increased Pack Size in Map|increased Pack Size", s.MapPackSizeIncrease),
                ("@delirium_prefix:Magic Monsters|MapMagicPackIncrease|increased Magic Monsters", s.MapMagicPackIncrease),
                ("@delirium_prefix:Rare Monsters|MapRarePackIncrease|number of Rare Monsters|increased Rare Monsters", s.MapRarePackIncrease),
                ("@delirium_prefix:Gold found|MapDroppedGoldIncrease|Gold found", s.MapDroppedGoldIncrease),
                ("@delirium_prefix:Experience gain|MapExperienceGainIncrease|Experience gain", s.MapExperienceGainIncrease));
        }

        private bool IsEnabledBreachMod(TabletMod mod)
        {
            var s = this.Settings.BreachTabletSettings;
            return MatchFlag(mod.Id,
                ("@breach_suffix:Magic Monsters|BreachMagicMonsterIncrease|Breach Monsters in Map spawn", s.BreachMagicMonsterIncrease),
                ("@breach_suffix:additional Rare Monster|BreachRareMonsterIncrease|additional Rare Monster when Stabilised", s.BreachRareMonsterIncrease),
                ("@breach_suffix:Pack Size|BreachDensityIncrease|Breaches in Map have|Breach Monsters in Map", s.BreachDensityIncrease),
                ("BreachSpeedIncrease|open and close", s.BreachSpeedIncrease),
                ("BreachChestAdditional|Clasped Hand", s.BreachChestAdditional),
                ("@breach_suffix:Hiveblood|@breach_suffix:Wombgifts|BreachMonsterSplinterIncrease|Hiveblood|Wombgifts", s.BreachMonsterSplinterIncrease),
                ("Breach3AdditionalChance|three additional Breaches", s.Breach3AdditionalChance),
                ("BreachAdditionalChance|additional Breach", s.BreachAdditionalChance));
        }

        private bool IsEnabledRitualMod(TabletMod mod)
        {
            var s = this.Settings.RitualTabletSettings;
            return MatchFlag(mod.Id,
                ("@ritual_suffix:increased Tribute|RitualTributeIncrease|grant|increased Tribute", s.RitualTributeIncrease),
                ("@ritual_suffix:Rerolling Favours|RitualRerollCostIncrease|Rerolling Favours|reduced Tribute", s.RitualRerollCostDecrease),
                ("@ritual_suffix:Deferring Favours|RitualDeferCostIncrease|Deferring Favours|reduced Tribute", s.RitualDeferCostDecrease),
                ("@ritual_suffix:reappear|RitualDeferSpeed|reappear|sooner", s.RitualDeferSpeedIncrease),
                ("@ritual_suffix:additional time|RitualAdditionalReroll|rerolling Favours an additional time", s.RitualExtraReroll),
                ("@ritual_suffix:cost no Tribute|RitualChanceForNoCost|cost no Tribute", s.RitualFreeRerollChance),
                ("@ritual_suffix:chance to be Rare|RitualRareMonsters|increased chance to be Rare", s.RitualRareMonstersIncrease),
                ("@ritual_suffix:chance to be Magic|RitualMagicMonsters|increased chance to be Magic", s.RitualMagicMonstersIncrease),
                ("@ritual_suffix:Omens|RitualOmenChance|Omens", s.RitualOmensIncrease));
        }

        private bool IsEnabledOverseerMod(TabletMod mod)
        {
            var s = this.Settings.OverseerTabletSettings;
            return MatchFlag(mod.Id,
                ("@overseer_suffix:additional Strongbox|MapBossAdditionalStrongbox|additional Strongbox", s.MapBossStrongboxAdditional),
                ("@overseer_suffix:additional Shrine|MapBossAdditionalShrine|additional Shrine", s.MapBossShrineAdditional),
                ("@overseer_suffix:additional Essence|MapBossAdditionalEssence|additional Essence", s.MapBossEssenceAdditional),
                ("@overseer_suffix:Waystones|MapBossWaystoneChance|Waystones dropped by Map Bosses", s.MapBossWaystoneChance),
                ("@overseer_suffix:Experience|MapBossExperience|Map Bosses grant|Experience", s.MapBossExperienceIncrease),
                ("@overseer_suffix:Rarity of Items dropped by Map Bosses|MapBossRarity|Rarity of Items dropped by Map Bosses", s.MapBossRarityIncrease),
                ("@overseer_suffix:Quantity of Items dropped by Map Bosses|MapBossQuantity|Quantity of Items dropped by Map Bosses", s.MapBossQuantityIncrease));
        }

        private bool IsEnabledExpeditionMod(TabletMod mod)
        {
            var s = this.Settings.ExpeditionTabletSettings;
            return MatchFlag(mod.Id,
                ("ExpeditionArtifactIncrease|Artifacts", s.ExpeditionArtifactsIncrease),
                ("ExpeditionExplosionPlacement|Placement Range", s.ExpeditionPlacementRangeIncrease),
                ("ExpeditionRelicIncrease|Remnant", s.ExpeditionRemnantAdditional),
                ("ExpeditionExplosionRadius|Explosive Radius", s.ExpeditionRadiusIncrease),
                ("ExpeditionLogbookIncrease|Logbook", s.ExpeditionLogbookQuantityIncrease),
                ("ExpeditionRareMonsters|Rare Expedition Monsters", s.ExpeditionRaresIncrease),
                ("ExpeditionRelicModEffect|Effect of Remnants", s.ExpeditionRemnantEffectIncrease),
                ("ExpeditionRunicMonsters|Runic Monster", s.ExpeditionRunicMonstersIncrease));
        }

        private bool IsEnabledDeliriumMod(TabletMod mod)
        {
            var s = this.Settings.DeliriumTabletSettings;
            return MatchFlag(mod.Id,
                ("@delirium_suffix:Simulacrum Splinters|DeliriumMonsterSplinterIncrease|Simulacrum Splinters", s.DeliriumSplintersIncrease),
                ("DeliriumRewardProgressIncrease|Reward Progress", s.DeliriumProgressIncrease),
                ("@delirium_suffix:additional seconds|DeliriumFogDissipationDelay|lasts|additional seconds", s.DeliriumDurationIncrease),
                ("@delirium_suffix:dissipates|DeliriumFogPersistence|dissipates|slower", s.DeliriumDissipationDecrease),
                ("@delirium_suffix:faster with distance|DeliriumDifficultyIncrease|increases|faster", s.DeliriumDifficultyIncrease),
                ("@delirium_suffix:Delirium Monsters|DeliriumPackSizeIncrease|Delirium Monsters|Pack Size", s.DeliriumPackSizeIncrease),
                ("@delirium_suffix:Fracturing Mirrors|@delirium_suffix:MirrorShards|DeliriumDoodadsIncrease|Fracturing Mirrors|MirrorShards", s.DeliriumMirrorsIncrease),
                ("@delirium_suffix:pauses the Delirium Mirror Timer|DeliriumRareMonsterPause|pauses the Delirium Mirror Timer", s.DeliriumPauseOnRareKills),
                ("@delirium_suffix:Unique Bosses|DeliriumBossChance|spawn Unique Bosses", s.DeliriumBossChanceIncrease),
                ("DeliriumAdditionalRewardType|additional Reward type", s.DeliriumRewardTypeAdditionalChance));
        }

        private bool IsEnabledPrecursorMod(TabletMod mod)
        {
            var s = this.Settings.PrecursorTabletSettings;
            return MatchFlag(mod.Id,
                ("MapDroppedMapsIncrease|Waystones", s.PrecursorWaystonesIncrease),
                ("@irradiated_suffix:additional Rare Modifier|MapRareMonstersAdditionalModifier|additional Rare Modifier", s.PrecursorRareModifierChance),
                ("@irradiated_suffix:Shrine|MapAdditionalShrine|chance to contain Shrines|additional Shrine", s.PrecursorShrineChance),
                ("@irradiated_suffix:Strongbox|MapAdditionalStrongbox|chance to contain Strongboxes|additional Strongbox", s.PrecursorStrongboxChance),
                ("@irradiated_suffix:Essence|MapAdditionalEssence|chance to contain Essences|additional Essence", s.PrecursorEssenceChance),
                ("@irradiated_suffix:additional random Modifier|MapAdditionalModifier|additional random Modifier", s.PrecursorModifierAdditional));
        }

        private bool MatchFlag(string id, params (string Key, bool Enabled)[] flags) =>
            flags.Any(flag => flag.Enabled && flag.Key
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Any(candidate => this.MatchesCandidate(id, candidate)));

        private static bool Matches(string value, params string[] needles) =>
            needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

        private bool MatchesCandidate(string value, string candidate)
        {
            if (!candidate.StartsWith('@'))
            {
                return Matches(value, candidate) || TemplateMatches(value, candidate);
            }

            var separator = candidate.IndexOf(':');
            if (separator <= 1 || separator >= candidate.Length - 1)
            {
                return false;
            }

            var fileKey = candidate[1..separator];
            var hint = candidate[(separator + 1)..];
            if (!this.modTemplates.TryGetValue(fileKey, out var templates))
            {
                return false;
            }

            return templates
                .Where(template => Matches(template, hint) || TemplateMatches(template, hint))
                .Any(template => TemplateMatches(value, template));
        }

        private static bool TemplateMatches(string value, string template)
        {
            var templateTokens = Tokenize(template).ToArray();
            if (templateTokens.Length == 0)
            {
                return false;
            }

            var valueTokens = Tokenize(value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return templateTokens.All(valueTokens.Contains);
        }

        private static IEnumerable<string> Tokenize(string text)
        {
            var tokens = new List<string>();
            var current = new List<char>();
            foreach (var ch in text)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    current.Add(char.ToLowerInvariant(ch));
                }
                else if (current.Count > 0)
                {
                    AddToken(tokens, current);
                }
            }

            if (current.Count > 0)
            {
                AddToken(tokens, current);
            }

            return tokens;
        }

        private static void AddToken(List<string> tokens, List<char> current)
        {
            var token = new string(current.ToArray());
            current.Clear();
            if (token.Length > 1 && !token.All(char.IsDigit))
            {
                tokens.Add(token);
            }
        }

        private static int GetPrimaryValue(TabletMod mod)
        {
            if (!float.IsNaN(mod.Values.value0))
            {
                return (int)MathF.Round(mod.Values.value0);
            }

            if (!float.IsNaN(mod.Values.value1))
            {
                return (int)MathF.Round(mod.Values.value1);
            }

            return 0;
        }

        private static bool IsUsesMod(TabletMod mod)
        {
            var value = GetPrimaryValue(mod);
            return value is >= 1 and <= 20 &&
                   mod.Id.Contains("use", StringComparison.OrdinalIgnoreCase);
        }

        private static string DescribeTablet(Item item)
        {
            var mods = GetTabletMods(item);
            var rarity = GetRarity(item);
            var uses = mods.FirstOrDefault(IsUsesMod);
            var nonUseMods = mods.Count(mod => !IsUsesMod(mod));
            return uses.Id == null
                ? $"{rarity}, {nonUseMods} mods"
                : $"{rarity}, {GetPrimaryValue(uses)} uses, {nonUseMods} mods";
        }

        private static Item? ReadFreshItem(IntPtr itemAddress)
        {
            if (itemAddress == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Activator.CreateInstance(
                    typeof(Item),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new object[] { itemAddress },
                    null) as Item;
            }
            catch
            {
                return null;
            }
        }

        private readonly record struct FrameInfo(Vector2 Pos, Vector2 Size, Vector4 Color, string DebugText);

        private readonly record struct TabletMod(string Id, (float value0, float value1) Values);
    }

    /// <summary>
    ///     Resolves plugin references across GameHelper assembly names used by different builds.
    /// </summary>
#pragma warning disable CA2255
    public static class TabletsModuleInitializer
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        public static void Initialize()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var requestedName = new AssemblyName(args.Name).Name;
                if (requestedName is "GameHelper" or "GameHelper.App")
                {
                    return AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name is "GameHelper" or "GameHelper.App");
                }

                return null;
            };
        }
    }
#pragma warning restore CA2255
}
