using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Bannerlord.ReignCourtAppearance;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Court
{
    internal sealed class ReignCourtNobleCampaignBehavior : CampaignBehaviorBase
    {
        private const string CourtNoblePrefix = "reign_court_";
        private const string HouseholdClanPrefix = "reign_house_";
        private const int HouseholdClanMigrationVersion = 1;
        private const int MinimumFaceSliderValue = -25;
        private const int MaximumFaceSliderValue = 24;
        private const float MinimumFaceWeight = 0.375f;
        private const float MaximumFaceWeight = 0.62f;
        private static readonly string[] CourtRoleSuffixes =
        {
            "_father",
            "_mother",
            "_child_1",
            "_child_2",
            "_child_3",
            "_child_4"
        };
        private static readonly FieldInfo OriginClanField =
            typeof(Hero).GetField("_originClan", BindingFlags.Instance | BindingFlags.NonPublic);
        private int _householdClanMigrationVersion;

        internal static ReignCourtNobleCampaignBehavior Instance { get; private set; }
        internal static bool IsHouseholdClanMigrationInProgress { get; private set; }

        public ReignCourtNobleCampaignBehavior()
        {
            Instance = this;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnNewGameCreatedPartialFollowUpEndEvent.AddNonSerializedListener(this, OnNewGameCreatedPartialFollowUpEnd);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.CanHeroLeadPartyEvent.AddNonSerializedListener(this, OnCanHeroLeadParty);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData(
                "_reignCourtNobles_householdClanMigrationVersion",
                ref _householdClanMigrationVersion);
        }

        internal static bool IsCourtNoble(Hero hero)
        {
            return hero != null && IsCourtNobleId(hero.StringId);
        }

        internal static bool TryRestoreHouseholdClan(Hero hero)
        {
            if (!IsCourtNoble(hero)) return false;

            string settlementId = GetHomeSettlementId(hero.StringId);
            string expectedClanId = HouseholdClanPrefix + settlementId;
            Clan expectedClan = Clan.All.FirstOrDefault(clan =>
                clan != null
                && string.Equals(
                    clan.StringId,
                    expectedClanId,
                    StringComparison.OrdinalIgnoreCase));
            if (expectedClan == null) return false;

            try
            {
                hero.Clan = expectedClan;
                OriginClanField?.SetValue(hero, expectedClan);
                return hero.Clan == expectedClan;
            }
            catch (Exception ex)
            {
                ReignBeta.Integration.ReignLog.Warn(
                    "Could not restore court-house clan for "
                    + hero.StringId + ": " + ex.Message);
                return false;
            }
        }

        internal void EnsureHouseholdClansMigrated()
        {
            // This migration establishes the authored court households once. After that,
            // normal campaign play may change clan leaders, kingdoms, and fief ownership.
            // Re-running the new-campaign assertions on load would reject legitimate mature
            // saves (for example, after a court house acquires its first settlement).
            if (_householdClanMigrationVersion >= HouseholdClanMigrationVersion)
            {
                return;
            }

            List<Hero> roster = Hero.AllAliveHeroes
                .Concat(Hero.DeadOrDisabledHeroes)
                .Where(IsCourtNoble)
                .GroupBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(hero => hero.StringId, StringComparer.Ordinal)
                .ToList();
            if (roster.Count != 720)
            {
                throw new InvalidOperationException(
                    "Court-house migration expected 720 existing heroes but found " + roster.Count + ".");
            }

            Dictionary<string, Clan> expectedClans = Clan.All
                .Where(clan => clan != null
                    && clan.StringId.StartsWith(HouseholdClanPrefix, StringComparison.Ordinal))
                .ToDictionary(clan => clan.StringId, clan => clan, StringComparer.OrdinalIgnoreCase);
            if (expectedClans.Count != 120)
            {
                throw new InvalidOperationException(
                    "Court-house migration expected 120 landless clans but found "
                    + expectedClans.Count + ".");
            }
            if (OriginClanField == null)
            {
                throw new MissingFieldException(
                    typeof(Hero).FullName,
                    "_originClan");
            }

            List<HouseholdClanMove> moves = new List<HouseholdClanMove>(roster.Count);
            foreach (Hero hero in roster)
            {
                string settlementId = GetHomeSettlementId(hero.StringId);
                string expectedClanId = HouseholdClanPrefix + settlementId;
                if (string.IsNullOrWhiteSpace(settlementId)
                    || !expectedClans.TryGetValue(expectedClanId, out Clan expectedClan))
                {
                    throw new InvalidOperationException(
                        "Court-house migration could not resolve the expected clan for "
                        + hero.StringId + ".");
                }
                if (expectedClan.Kingdom == null || expectedClan.Culture != hero.Culture)
                {
                    throw new InvalidOperationException(
                        "Court-house migration found an invalid kingdom or culture for "
                        + expectedClanId + ".");
                }
                moves.Add(new HouseholdClanMove
                {
                    Hero = hero,
                    ExpectedClan = expectedClan,
                    PreviousClan = hero.Clan,
                    PreviousOriginClan = hero.OriginClan
                });
            }

            foreach (IGrouping<Clan, HouseholdClanMove> household in moves.GroupBy(move => move.ExpectedClan))
            {
                if (household.Count() != 6)
                {
                    throw new InvalidOperationException(
                        "Court-house migration expected six authored heroes for "
                        + household.Key.StringId + " but found " + household.Count() + ".");
                }
                Hero expectedLeader = household
                    .Select(move => move.Hero)
                    .SingleOrDefault(hero => hero.StringId.EndsWith("_father", StringComparison.Ordinal));
                if (expectedLeader == null || household.Key.Leader != expectedLeader)
                {
                    throw new InvalidOperationException(
                        "Court-house migration found the wrong clan leader for "
                        + household.Key.StringId + ".");
                }
                if (household.Key.Fiefs.Count > 0)
                {
                    throw new InvalidOperationException(
                        "New court house unexpectedly owns a settlement: "
                        + household.Key.StringId + ".");
                }
            }

            bool alreadyAligned = moves.All(move =>
                move.Hero.Clan == move.ExpectedClan
                && move.Hero.OriginClan == move.ExpectedClan);
            if (alreadyAligned)
            {
                _householdClanMigrationVersion = HouseholdClanMigrationVersion;
                ApplyAuthoredNames(roster);
                return;
            }

            IsHouseholdClanMigrationInProgress = true;
            try
            {
                foreach (HouseholdClanMove move in moves)
                {
                    if (move.Hero.Clan != move.ExpectedClan)
                    {
                        move.Hero.Clan = move.ExpectedClan;
                    }
                    OriginClanField.SetValue(move.Hero, move.ExpectedClan);
                }
                ApplyAuthoredNames(roster);
                _householdClanMigrationVersion = HouseholdClanMigrationVersion;
                ReignBeta.Integration.ReignLog.Info(
                    "Migrated 720 existing court nobles into 120 independent landless clans.");
            }
            catch
            {
                foreach (HouseholdClanMove move in moves.AsEnumerable().Reverse())
                {
                    if (move.Hero.Clan != move.PreviousClan)
                    {
                        move.Hero.Clan = move.PreviousClan;
                    }
                    OriginClanField.SetValue(move.Hero, move.PreviousOriginClan);
                }
                throw;
            }
            finally
            {
                IsHouseholdClanMigrationInProgress = false;
            }
        }

        private static void ApplyAuthoredNames(IEnumerable<Hero> heroes)
        {
            foreach (Hero hero in heroes)
            {
                ApplyAuthoredName(hero);
            }
        }

        private sealed class HouseholdClanMove
        {
            internal Hero Hero { get; set; }
            internal Clan ExpectedClan { get; set; }
            internal Clan PreviousClan { get; set; }
            internal Clan PreviousOriginClan { get; set; }
        }

        private static bool IsCourtNobleId(string heroId)
        {
            if (string.IsNullOrWhiteSpace(heroId) || !heroId.StartsWith(CourtNoblePrefix)) return false;
            return CourtRoleSuffixes.Any(suffix => heroId.EndsWith(suffix));
        }

        private void OnNewGameCreatedPartialFollowUpEnd(CampaignGameStarter starter)
        {
            EnsureHouseholdClansMigrated();
            List<Hero> courtNobles = Hero.AllAliveHeroes.Where(IsCourtNoble).ToList();
            GenerateCourtNobleFaces(courtNobles);
            foreach (Hero hero in courtNobles)
            {
                PlaceAtHomeHolding(hero);
            }
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            EnsureHouseholdClansMigrated();
            GenerateCourtNobleFaces(Hero.AllAliveHeroes.Where(IsCourtNoble).ToList());
        }

        private static void GenerateCourtNobleFaces(List<Hero> courtNobles)
        {
            foreach (Hero hero in courtNobles)
            {
                ApplyAuthoredName(hero);
            }

            List<ReignCourtAppearanceInput> inputs = new List<ReignCourtAppearanceInput>();
            foreach (Hero hero in courtNobles.Where(hero => hero?.CharacterObject?.BodyPropertyRange != null))
            {
                int[] cultureHair = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.BodyPropertiesModel?.GetHairIndicesForCulture(
                    hero.CharacterObject.Race,
                    hero.IsFemale ? 1 : 0,
                    hero.Age,
                    hero.Culture) ?? Array.Empty<int>();
                inputs.Add(new ReignCourtAppearanceInput
                {
                    CharacterId = hero.StringId,
                    IsFemale = hero.IsFemale,
                    Race = hero.CharacterObject.Race,
                    Age = hero.Age,
                    Weight = hero.Weight,
                    Build = hero.Build,
                    Range = hero.CharacterObject.BodyPropertyRange,
                    CultureHairIndices = cultureHair,
                    MotherId = IsParentRole(hero) ? string.Empty : hero.Mother?.StringId ?? string.Empty,
                    FatherId = IsParentRole(hero) ? string.Empty : hero.Father?.StringId ?? string.Empty
                });
            }

            Dictionary<string, ReignCourtAppearanceResult> generated =
                ReignCourtAppearanceGenerator.Generate(inputs);
            foreach (Hero hero in courtNobles)
            {
                if (generated.TryGetValue(hero.StringId, out ReignCourtAppearanceResult appearance))
                {
                    hero.StaticBodyProperties = appearance.BodyProperties.StaticProperties;
                    if (!appearance.Unique)
                    {
                        ReignBeta.Integration.ReignLog.Warn(
                            "Using a non-unique constrained face fallback for court noble " + hero.StringId + ".");
                    }
                }
            }
        }

        private static void ApplyAuthoredName(Hero hero)
        {
            string fullName = hero?.CharacterObject?.Name?.ToString();
            if (string.IsNullOrWhiteSpace(fullName)) return;
            string firstName = fullName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fullName;
            hero.SetName(new TextObject(fullName), new TextObject(firstName));
        }

        private static bool IsParentRole(Hero hero)
        {
            return hero != null && (hero.StringId.EndsWith("_father") || hero.StringId.EndsWith("_mother"));
        }

        private static void AssignGeneratedFace(Hero hero, HashSet<string> usedFaces, bool inheritParents)
        {
            if (hero?.CharacterObject == null) return;
            int baseSeed = StableSeed(hero.StringId);
            BodyProperties fallback = default(BodyProperties);
            bool hasFallback = false;
            for (int attempt = 0; attempt < 64; attempt++)
            {
                int seed = unchecked(baseSeed + attempt * 7919);
                BodyProperties generated;
                if (inheritParents && hero.Mother != null && hero.Father != null)
                {
                    MBBodyProperty range = hero.CharacterObject.BodyPropertyRange;
                    generated = BodyProperties.GetRandomBodyProperties(
                        hero.CharacterObject.Race,
                        hero.IsFemale,
                        hero.Mother.BodyProperties,
                        hero.Father.BodyProperties,
                        1,
                        seed,
                        range.HairTags,
                        range.BeardTags,
                        range.TattooTags,
                        0.15f);
                }
                else
                {
                    generated = hero.CharacterObject.GetBodyProperties(null, seed);
                }

                generated = ConstrainGeneratedAppearance(hero, generated, seed, inheritParents);
                fallback = generated;
                hasFallback = true;

                string faceKey = StaticFaceKey(generated.StaticProperties);
                if (usedFaces.Add(faceKey))
                {
                    hero.StaticBodyProperties = generated.StaticProperties;
                    return;
                }
            }
            if (hasFallback)
            {
                hero.StaticBodyProperties = fallback.StaticProperties;
                ReignBeta.Integration.ReignLog.Warn("Using a non-unique constrained face fallback for court noble " + hero.StringId + ".");
                return;
            }
            ReignBeta.Integration.ReignLog.Warn("Could not produce a unique generated face for court noble " + hero.StringId + ".");
        }

        private static BodyProperties ConstrainGeneratedAppearance(Hero hero, BodyProperties generated, int seed, bool inheritedFromParents)
        {
            FaceGenerationParams parameters = FaceGenerationParams.Create();
            MBBodyProperties.GetParamsFromKey(ref parameters, generated, earsAreHidden: false, mouthHidden: false);
            int deformKeyCount = MBBodyProperties.GetFaceGenInstancesLength(
                hero.CharacterObject.Race,
                hero.IsFemale ? 1 : 0,
                (int)hero.Age);
            Random random = new Random(seed);

            for (int keyIndex = 0; keyIndex < deformKeyCount && keyIndex < parameters.KeyWeights.Length; keyIndex++)
            {
                DeformKeyData key = MBBodyProperties.GetDeformKeyData(
                    keyIndex,
                    hero.CharacterObject.Race,
                    hero.IsFemale ? 1 : 0,
                    (int)hero.Age);
                if (IsNonFaceKey(key.Id)) continue;

                parameters.KeyWeights[keyIndex] = inheritedFromParents
                    ? Clamp(parameters.KeyWeights[keyIndex] + random.Next(-3, 4) / 200f, MinimumFaceWeight, MaximumFaceWeight)
                    : SliderValueToWeight(random.Next(MinimumFaceSliderValue, MaximumFaceSliderValue + 1));
            }

            parameters.HeightMultiplier = inheritedFromParents
                ? Clamp(parameters.HeightMultiplier, MinimumFaceWeight, MaximumFaceWeight)
                : SliderValueToWeight(random.Next(MinimumFaceSliderValue, MaximumFaceSliderValue + 1));

            MBBodyProperties.EnforceConstraints(ref parameters);
            for (int keyIndex = 0; keyIndex < deformKeyCount && keyIndex < parameters.KeyWeights.Length; keyIndex++)
            {
                DeformKeyData key = MBBodyProperties.GetDeformKeyData(
                    keyIndex,
                    hero.CharacterObject.Race,
                    hero.IsFemale ? 1 : 0,
                    (int)hero.Age);
                if (!IsNonFaceKey(key.Id))
                {
                    parameters.KeyWeights[keyIndex] = Clamp(parameters.KeyWeights[keyIndex], MinimumFaceWeight, MaximumFaceWeight);
                }
            }
            parameters.HeightMultiplier = Clamp(parameters.HeightMultiplier, MinimumFaceWeight, MaximumFaceWeight);

            BodyProperties constrained = generated;
            MBBodyProperties.ProduceNumericKeyWithParams(parameters, earsAreHidden: false, mouthIsHidden: false, ref constrained);
            int hair = SelectNonBaldHair(hero, seed);
            TaleWorlds.Core.FaceGen.SetHair(ref constrained, hair, hero.IsFemale ? 0 : -1, -1);
            return constrained;
        }

        private static bool IsNonFaceKey(string keyId)
        {
            return string.Equals(keyId, "weight", StringComparison.OrdinalIgnoreCase)
                || string.Equals(keyId, "build", StringComparison.OrdinalIgnoreCase)
                || string.Equals(keyId, "height", StringComparison.OrdinalIgnoreCase)
                || string.Equals(keyId, "age", StringComparison.OrdinalIgnoreCase);
        }

        private static int SelectNonBaldHair(Hero hero, int seed)
        {
            HashSet<int> choices = new HashSet<int>();
            int gender = hero.IsFemale ? 1 : 0;
            int[] cultureHair = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.BodyPropertiesModel?.GetHairIndicesForCulture(
                hero.CharacterObject.Race,
                gender,
                hero.Age,
                hero.Culture);
            AddNonBaldHair(choices, cultureHair);

            string hairTags = hero.CharacterObject.BodyPropertyRange?.HairTags;
            if (!string.IsNullOrWhiteSpace(hairTags))
            {
                AddNonBaldHair(choices, TaleWorlds.Core.FaceGen.GetHairIndicesByTag(
                    hero.CharacterObject.Race,
                    gender,
                    hero.Age,
                    hairTags));
            }

            if (choices.Count == 0)
            {
                ReignBeta.Integration.ReignLog.Warn("No culture-valid non-bald hairstyle found for " + hero.StringId + "; using hairstyle 1.");
                return 1;
            }

            int[] ordered = choices.OrderBy(x => x).ToArray();
            return ordered[new Random(unchecked(seed ^ 0x5F3759DF)).Next(ordered.Length)];
        }

        private static void AddNonBaldHair(HashSet<int> choices, IEnumerable<int> indices)
        {
            if (indices == null) return;
            foreach (int index in indices)
            {
                if (index > 0) choices.Add(index);
            }
        }

        private static float SliderValueToWeight(int sliderValue)
        {
            return (sliderValue + 100) / 200f;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static int StableSeed(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char character in value ?? string.Empty)
                {
                    hash ^= character;
                    hash *= 16777619;
                }
                return (int)(hash & 0x7FFFFFFF);
            }
        }

        private static string StaticFaceKey(StaticBodyProperties properties)
        {
            return properties.KeyPart1.ToString("X16") + properties.KeyPart2.ToString("X16")
                + properties.KeyPart3.ToString("X16") + properties.KeyPart4.ToString("X16")
                + properties.KeyPart5.ToString("X16") + properties.KeyPart6.ToString("X16")
                + properties.KeyPart7.ToString("X16") + properties.KeyPart8.ToString("X16");
        }

        private void OnCanHeroLeadParty(Hero hero, ref bool result)
        {
            if (!result || !IsCourtNoble(hero)) return;
            if (hero.Clan != null
                && hero.Clan.StringId.StartsWith(HouseholdClanPrefix, StringComparison.Ordinal)
                && hero.Clan.Fiefs.Count == 0)
            {
                result = false;
                return;
            }
            if (HasAvailableRegularCommander(hero.Clan))
            {
                result = false;
            }
        }

        private static bool HasAvailableRegularCommander(Clan clan)
        {
            if (clan == null) return false;
            return clan.Heroes.Any(IsAvailableRegularCommander);
        }

        private static bool IsAvailableRegularCommander(Hero hero)
        {
            if (hero == null || IsCourtNoble(hero) || !hero.IsAlive || !hero.IsActive || hero.IsPrisoner) return false;
            if (hero.PartyBelongedTo != null || hero.PartyBelongedToAsPrisoner != null) return false;
            if (hero.CharacterObject == null || hero.CharacterObject.Occupation != Occupation.Lord) return false;
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || hero.Age < TaleWorlds.CampaignSystem.Campaign.Current.Models.AgeModel.HeroComesOfAge) return false;
            return hero.CanLeadParty();
        }

        private static void PlaceAtHomeHolding(Hero hero)
        {
            if (hero == null || !hero.IsAlive || !hero.IsActive || hero.IsPrisoner || hero.PartyBelongedTo != null) return;
            if (ReignCourtCampaignBehavior.Instance?.IsNobleVisitorReserved(hero) == true) return;
            if (ReignBeta.Government.ReignGovernmentCampaignBehavior.Instance?.IsGovernmentAttendanceReserved(hero) == true) return;
            Settlement target = FindCurrentHomeHolding(hero);
            if (target == null || hero.CurrentSettlement == target) return;
            TeleportHeroAction.ApplyImmediateTeleportToSettlement(hero, target);
        }

        private static Settlement FindCurrentHomeHolding(Hero hero)
        {
            string homeSettlementId = GetHomeSettlementId(hero.StringId);
            Settlement originalHome = string.IsNullOrWhiteSpace(homeSettlementId) ? null : Settlement.Find(homeSettlementId);
            Settlement clanFief = hero.Clan?.Fiefs.FirstOrDefault()?.Settlement;
            if (clanFief != null) return clanFief;
            if (originalHome != null
                && hero.Clan?.Kingdom != null
                && originalHome.OwnerClan?.Kingdom == hero.Clan.Kingdom)
            {
                return originalHome;
            }
            return null;
        }

        private static string GetHomeSettlementId(string heroId)
        {
            if (!IsCourtNobleId(heroId)) return null;
            foreach (string suffix in CourtRoleSuffixes)
            {
                if (heroId.EndsWith(suffix))
                {
                    return heroId.Substring(CourtNoblePrefix.Length, heroId.Length - CourtNoblePrefix.Length - suffix.Length);
                }
            }
            return null;
        }
    }

    internal static class ReignCourtNobleSafetyPatches
    {
        private const string HarmonyId =
            "com.bannerlordreign.reignbeta.court-noble-safety";
        private static Harmony _harmony;

        internal static void Apply()
        {
            if (_harmony != null) return;

            Type behaviorType = AccessTools.TypeByName(
                "TaleWorlds.CampaignSystem.CampaignBehaviors.TeleportationCampaignBehavior");
            MethodInfo target = behaviorType == null
                ? null
                : AccessTools.Method(
                    behaviorType,
                    "OnHeroComesOfAge",
                    new[] { typeof(Hero) });
            MethodInfo prefix = AccessTools.Method(
                typeof(ReignCourtNobleSafetyPatches),
                nameof(BeforeOnHeroComesOfAge));
            MethodInfo hourlyTick = behaviorType == null
                ? null
                : AccessTools.Method(behaviorType, "HourlyTick", Type.EmptyTypes);
            MethodInfo hourlyFinalizer = AccessTools.Method(
                typeof(ReignCourtNobleSafetyPatches),
                nameof(AfterHourlyTick));
            if (target == null || prefix == null || hourlyTick == null || hourlyFinalizer == null)
            {
                throw new MissingMethodException(
                    "Could not resolve Bannerlord's native teleport safety handlers.");
            }

            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            _harmony.Patch(hourlyTick, finalizer: new HarmonyMethod(hourlyFinalizer));
            ReignBeta.Integration.ReignLog.Info(
                "Court-noble and native teleport iteration safety patches loaded.");
        }

        internal static void Unapply()
        {
            if (_harmony == null) return;
            _harmony.UnpatchAll(HarmonyId);
            _harmony = null;
        }

        private static bool BeforeOnHeroComesOfAge(Hero hero)
        {
            if (hero == null) return false;
            if (hero.Clan != null) return true;

            if (ReignCourtNobleCampaignBehavior.TryRestoreHouseholdClan(hero))
            {
                ReignBeta.Integration.ReignLog.Warn(
                    "Restored missing court-house clan before native coming-of-age handling for "
                    + hero.StringId + ".");
                return true;
            }

            ReignBeta.Integration.ReignLog.Warn(
                "Skipped native coming-of-age teleport handling for clanless hero "
                + (hero.StringId ?? "unknown") + ".");
            return false;
        }

        private static Exception AfterHourlyTick(Exception __exception)
        {
            if (!(__exception is ArgumentOutOfRangeException)) return __exception;

            // Bannerlord iterates its delayed-teleport list backwards. Removing
            // the current entry can also remove another entry for the same hero,
            // leaving the loop's next index beyond the shortened list. The list
            // is already consistent at this point, so deferring its remaining
            // entries until the next hourly tick preserves native behavior while
            // preventing a recoverable bookkeeping race from ending the campaign.
            ReignBeta.Integration.ReignLog.Warn(
                "Recovered Bannerlord's delayed-teleport list after it changed size during HourlyTick; remaining teleports were deferred by one hour.");
            return null;
        }
    }
}
