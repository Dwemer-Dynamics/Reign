using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Court;
using ReignBeta.Events;
using ReignBeta.Government;
using ReignBeta.World;
using ReignPortraits;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Diplomacy.Campaign;
using ReignBeta.Government.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Runtime;
using ReignBeta.Platform.Settings;
using ReignBeta.Portraits.Integration;
using ReignBeta.Relationships.Campaign;
using ReignBeta.Relationships.Integration;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.Integration;
using ReignBeta.WorldSimulation.News;
using ReignBeta.WorldSimulation.Runtime;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
#endif

namespace ReignBeta.Platform.Integration
{
    public static partial class ReignServerClient
    {
        internal static JObject BuildHeroProfile(Hero hero)
        {
            Kingdom heroKingdom = hero?.Clan?.Kingdom;
            Hero sovereign = heroKingdom?.Leader;
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            Hero playerSovereign = playerKingdom?.Leader;
            JObject profile = new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["campaignLabel"] = ReignCampaignIdentity.CurrentCampaignLabel(),
                ["heroStringId"] = hero?.StringId ?? string.Empty,
                ["mainHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["mainHeroName"] = Hero.MainHero?.Name?.ToString() ?? string.Empty,
                ["playerClanId"] = Clan.PlayerClan?.StringId ?? string.Empty,
                ["playerClanName"] = Clan.PlayerClan?.Name?.ToString() ?? string.Empty,
                ["playerKingdomId"] = Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty,
                ["playerKingdomName"] = Clan.PlayerClan?.Kingdom?.InformalName?.ToString() ?? string.Empty,
                ["characterObjectId"] = hero?.CharacterObject?.StringId ?? string.Empty,
                ["name"] = hero?.Name?.ToString() ?? string.Empty,
                ["cultureId"] = hero?.Culture?.StringId ?? string.Empty,
                ["clanId"] = hero?.Clan?.StringId ?? string.Empty,
                ["clanName"] = hero?.Clan?.Name?.ToString() ?? string.Empty,
                ["clanTier"] = hero?.Clan?.Tier ?? 0,
                ["clanRenown"] = hero?.Clan?.Renown ?? 0f,
                ["clanFiefCount"] = hero?.Clan?.Fiefs?.Count ?? 0,
                ["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? hero?.MapFaction?.StringId ?? string.Empty,
                ["kingdomName"] = hero?.Clan?.Kingdom?.InformalName?.ToString() ?? hero?.MapFaction?.Name?.ToString() ?? string.Empty,
                ["currentSettlementId"] = hero?.CurrentSettlement?.StringId ?? string.Empty,
                ["currentSettlementName"] = hero?.CurrentSettlement?.Name?.ToString() ?? string.Empty,
                ["homeSettlementId"] = hero?.HomeSettlement?.StringId ?? string.Empty,
                ["homeSettlementName"] = hero?.HomeSettlement?.Name?.ToString() ?? string.Empty,
                ["partyId"] = hero?.PartyBelongedTo?.StringId ?? string.Empty,
                ["occupation"] = hero?.Occupation.ToString() ?? string.Empty,
                ["age"] = hero?.Age ?? 0f,
                ["isFemale"] = hero?.IsFemale ?? false,
                ["isAlive"] = hero?.IsAlive ?? false,
                ["isPrisoner"] = hero?.IsPrisoner ?? false,
                ["isLord"] = hero?.IsLord ?? false,
                ["isRuler"] = hero != null && hero.Clan?.Kingdom?.Leader == hero,
                ["isClanLeader"] = hero != null && hero.Clan?.Leader == hero,
                ["isNotable"] = hero?.IsNotable ?? false,
                ["isWanderer"] = hero?.IsWanderer ?? false,
                ["governorOfSettlementId"] = hero?.GovernorOf?.Settlement?.StringId ?? string.Empty,
                ["governorOfSettlementName"] = hero?.GovernorOf?.Settlement?.Name?.ToString() ?? string.Empty,
                ["currentSettlementId"] = hero?.CurrentSettlement?.StringId ?? string.Empty,
                ["currentPartyId"] = hero?.PartyBelongedTo?.StringId ?? string.Empty,
                ["isChild"] = hero?.IsChild ?? false,
                ["isPregnant"] = hero?.IsPregnant ?? false,
                ["pregnancyContexts"] = ReignServices.Family?.PregnancyContextsFor(hero) ?? new JArray(),
                ["childrenCount"] = hero?.Children?.Count ?? 0,
                ["childrenIds"] = new JArray((hero?.Children ?? new List<Hero>()).Where(child => child != null).Select(child => child.StringId)),
                ["childrenNames"] = new JArray((hero?.Children ?? new List<Hero>()).Where(child => child != null).Select(child => child.Name?.ToString() ?? child.StringId)),
                ["bodyWeight"] = hero == null ? 50f : hero.Weight * 100f,
                ["bodyBuild"] = hero == null ? 50f : hero.Build * 100f,
                ["spouseId"] = hero?.Spouse?.StringId ?? string.Empty,
                ["spouseName"] = hero?.Spouse?.Name?.ToString() ?? string.Empty,
                ["fatherId"] = hero?.Father?.StringId ?? string.Empty,
                ["fatherName"] = hero?.Father?.Name?.ToString() ?? string.Empty,
                ["motherId"] = hero?.Mother?.StringId ?? string.Empty,
                ["motherName"] = hero?.Mother?.Name?.ToString() ?? string.Empty,
                ["childrenIds"] = new JArray(hero?.Children?.Select(child => child.StringId) ?? Enumerable.Empty<string>()),
                ["childrenNames"] = new JArray(hero?.Children?.Select(child => child.Name?.ToString() ?? string.Empty) ?? Enumerable.Empty<string>()),
                ["encyclopediaText"] = hero?.EncyclopediaText?.ToString() ?? string.Empty,
                ["nativeEncyclopediaText"] = GetNativeEncyclopediaText(hero),
                ["narrativeVersion"] = ReignServices.CampaignPreparation?.NarrativeVersion ?? 0,
                ["relationToPlayer"] = hero != null && Hero.MainHero != null && hero != Hero.MainHero ? hero.GetRelation(Hero.MainHero) : 0,
                ["sovereignHeroStringId"] = sovereign?.StringId ?? string.Empty,
                ["sovereignName"] = sovereign?.Name?.ToString() ?? string.Empty,
                ["relationToSovereign"] = hero != null && sovereign != null && hero != sovereign
                    ? hero.GetRelation(sovereign) : 0,
                ["playerSovereignHeroStringId"] = playerSovereign?.StringId ?? string.Empty,
                ["playerSovereignName"] = playerSovereign?.Name?.ToString() ?? string.Empty,
                ["sharesPlayerSovereign"] = sovereign != null && sovereign == playerSovereign,
                ["traits"] = BuildTraitProfile(hero),
                ["attributes"] = new JObject { ["endurance"] = hero?.GetAttributeValue(DefaultCharacterAttributes.Endurance) ?? 0 },
                ["skills"] = BuildSkillProfile(hero),
                ["wealth"] = BuildWealthProfile(hero),
                ["inventory"] = BuildInventoryProfile(hero),
                ["appearance"] = BuildAppearanceProfile(hero)
            };
            JObject wanderer = ReignServices.WandererPopulation?.Metadata(hero);
            if (wanderer != null) profile["wandererIdentity"] = wanderer;
            JObject resident = ReignServices.EncounteredResidents?.Metadata(hero);
            if (resident != null)
            {
                profile["encounteredResident"] = resident;
                // Residence is public context, not formal access to a ruler's/clan's private knowledge.
                profile["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? string.Empty;
                profile["kingdomName"] = hero?.Clan?.Kingdom?.InformalName?.ToString() ?? string.Empty;
                profile["nativeEncyclopediaText"] = profile["encyclopediaText"] = hero.Name + " is a "
                    + System.Text.RegularExpressions.Regex.Replace((string)resident["occupation"] ?? "resident", "([a-z])([A-Z])", "$1 $2").ToLowerInvariant()
                    + " from " + (string)resident["homeSettlementName"] + ".";
            }
            JObject tavern = ReignServices.TavernHouse?.Metadata(hero);
            if (tavern != null)
            {
                profile["tavernHouse"] = tavern;
                // The authored biography/personality is native canon, including after recruitment.
                // Existing server memories and constructed traits remain separate evolving state.
                string biography = tavern.Value<string>("biography") ?? string.Empty;
                string personality = tavern.Value<string>("personality") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(personality) && biography.IndexOf(personality, StringComparison.Ordinal) < 0)
                    biography = (biography + " " + personality).Trim();
                if (!string.IsNullOrWhiteSpace(biography)) profile["nativeEncyclopediaText"] = biography;
                profile["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? string.Empty;
                profile["kingdomName"] = hero?.Clan?.Kingdom?.InformalName?.ToString() ?? string.Empty;
            }
            var governmentProfile = ReignServices.Government;
            profile["appliedPoliticalProfile"] = governmentProfile?.PoliticalProfileForServer(hero?.StringId);
            profile["governmentOfficeTitle"] = governmentProfile?.GovernmentOfficeTitle(hero?.StringId) ?? "";
            profile["governmentOfficeKingdomId"] = governmentProfile?.GetOfficeKingdomId(hero?.StringId) ?? "";
            profile["politicalTimelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main";
            profile["politicalInterval"] = governmentProfile?.CurrentPoliticalInterval ?? -1;
            return profile;
        }

        private static bool IsFinanceSnapshotEligible(Hero hero)
        {
            return hero != null
                && hero.IsAlive
                && !hero.IsChild
                && hero.Age >= 18f
                && (hero.IsLord || hero == Hero.MainHero || hero.Clan?.Leader == hero);
        }

        private static JObject BuildFinanceSnapshotEntry(Hero hero)
        {
            return new JObject
            {
                ["heroStringId"] = hero?.StringId ?? string.Empty,
                ["name"] = hero?.Name?.ToString() ?? string.Empty,
                ["clanId"] = hero?.Clan?.StringId ?? string.Empty,
                ["clanName"] = hero?.Clan?.Name?.ToString() ?? string.Empty,
                ["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? string.Empty,
                ["isLord"] = hero?.IsLord ?? false,
                ["isRuler"] = hero != null && hero.Clan?.Kingdom?.Leader == hero,
                ["isClanLeader"] = hero != null && hero.Clan?.Leader == hero,
                ["wealth"] = BuildWealthProfile(hero)
            };
        }

        internal static JObject BuildIdentityRosterEntry(Hero hero)
        {
            return new JObject
            {
                ["encounteredResident"] = ReignServices.EncounteredResidents?.Metadata(hero),
                ["heroStringId"] = hero?.StringId ?? string.Empty,
                ["name"] = hero?.Name?.ToString() ?? string.Empty,
                ["occupation"] = hero?.Occupation.ToString() ?? string.Empty,
                ["isAlive"] = hero?.IsAlive ?? false,
                ["isLord"] = hero?.IsLord ?? false,
                ["isRuler"] = hero != null && hero.Clan?.Kingdom?.Leader == hero,
                ["isClanLeader"] = hero != null && hero.Clan?.Leader == hero,
                ["isMercenaryClan"] = hero?.Clan?.IsUnderMercenaryService ?? false,
                ["governmentOfficeTitle"] = ReignServices.Government?.GovernmentOfficeTitle(hero?.StringId) ?? "",
                ["governmentOfficeKingdomId"] = ReignServices.Government?.GetOfficeKingdomId(hero?.StringId) ?? "",
                ["isNotable"] = hero?.IsNotable ?? false,
                ["isWanderer"] = hero?.IsWanderer ?? false,
                ["governorOfSettlementId"] = hero?.GovernorOf?.Settlement?.StringId ?? string.Empty,
                ["governorOfSettlementName"] = hero?.GovernorOf?.Settlement?.Name?.ToString() ?? string.Empty,
                ["isAdult"] = hero != null && hero.IsAlive && !hero.IsChild && hero.Age >= 18f,
                ["isChild"] = hero?.IsChild ?? false,
                ["isPlayer"] = hero == Hero.MainHero,
                ["isFemale"] = hero?.IsFemale ?? false,
                ["sex"] = hero?.IsFemale == true ? "female" : "male",
                ["clanTier"] = hero?.Clan?.Tier ?? 0,
                ["currentCharm"] = hero?.GetSkillValue(DefaultSkills.Charm) ?? 0,
                ["clanId"] = hero?.Clan?.StringId ?? string.Empty,
                // Identity synchronization is about formal kingdom membership. MapFaction can
                // group notables and transient heroes under settlement factions and previously
                // caused enormous false same-kingdom acquaintance networks.
                ["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? string.Empty,
                ["sovereignHeroStringId"] = hero?.Clan?.Kingdom?.Leader?.StringId ?? string.Empty,
                ["sovereignName"] = hero?.Clan?.Kingdom?.Leader?.Name?.ToString() ?? string.Empty,
                ["spouseId"] = hero?.Spouse?.StringId ?? string.Empty,
                ["spouseName"] = hero?.Spouse?.Name?.ToString() ?? string.Empty,
                ["fatherId"] = hero?.Father?.StringId ?? string.Empty,
                ["fatherName"] = hero?.Father?.Name?.ToString() ?? string.Empty,
                ["motherId"] = hero?.Mother?.StringId ?? string.Empty,
                ["motherName"] = hero?.Mother?.Name?.ToString() ?? string.Empty,
                ["childrenIds"] = new JArray((hero?.Children ?? new List<Hero>()).Where(x => x != null).Select(x => x.StringId)),
                ["childrenNames"] = new JArray((hero?.Children ?? new List<Hero>()).Where(x => x != null).Select(x => x.Name?.ToString() ?? x.StringId))
            };
        }

        internal static JObject BuildNativePortraitSnapshot(Hero hero)
        {
            JObject row = BuildPortraitRosterEntry(hero);
            row["schema"] = "reign-native-portrait-snapshot-v1";
            row["campaignId"] = GetCampaignId();
            row["age"] = hero.Age;
            row["bodyWeight"] = hero.Weight;
            row["bodyBuild"] = hero.Build;
            JObject tavern = ReignServices.TavernHouse?.Metadata(hero);
            if (tavern != null)
            {
                row["portraitSourceProfile"] = "ai_source_resident_full_outfit_v1";
                row["tavernHouse"] = tavern;
            }
            var resident = ReignServices.EncounteredResidents?.Find(hero);
            if (resident != null)
            {
                var colors = ReignServices.EncounteredResidents.PortraitColors(hero);
                row["clothingColor1"] = colors.Item1;
                row["clothingColor2"] = colors.Item2;
            }
            return row;
        }

        private static JObject BuildPortraitRosterEntry(Hero hero)
        {
            CharacterObject character = hero?.CharacterObject;
            return new JObject
            {
                ["encounteredResident"] = ReignServices.EncounteredResidents?.Metadata(hero),
                ["heroStringId"] = hero?.StringId ?? string.Empty,
                ["characterObjectId"] = character?.StringId ?? string.Empty,
                ["name"] = hero?.Name?.ToString() ?? character?.Name?.ToString() ?? string.Empty,
                ["cultureId"] = hero?.Culture?.StringId ?? character?.Culture?.StringId ?? string.Empty,
                ["occupation"] = hero?.Occupation.ToString() ?? string.Empty,
                ["isAlive"] = hero?.IsAlive ?? false,
                ["isFemale"] = hero?.IsFemale ?? character?.IsFemale ?? false,
                ["isLord"] = hero?.IsLord ?? false,
                ["isNotable"] = hero?.IsNotable ?? false,
                ["isWanderer"] = hero?.IsWanderer ?? false,
                ["bodyKey"] = hero == null ? string.Empty : StaticBodyKey(hero.StaticBodyProperties),
                ["age"] = hero?.Age ?? 30f,
                ["bodyWeight"] = hero?.Weight ?? .5f,
                ["bodyBuild"] = hero?.Build ?? .5f,
                ["civilianEquipment"] = BuildPortraitCivilianEquipment(hero),
                ["portraitCacheKey"] = AIPortraits.CharacterCacheId.ForHero(hero) ?? string.Empty
            };
        }

        private static JArray BuildPortraitCivilianEquipment(Hero hero)
        {
            var residents = ReignServices.EncounteredResidents;
            if (residents?.Find(hero) != null)
            {
                // A resident's actual worn outfit is authoritative even if a slot is empty.
                // Never fall through to a mechanics template or invented renderer clothing.
                Equipment worn = residents.PortraitEquipment(hero);
                return new JArray(Enumerable.Range(0, 10).Where(i => worn?[(EquipmentIndex)i].Item != null)
                    .Select(i => new JObject { ["slot"] = ((EquipmentIndex)i).ToString(),
                        ["itemId"] = worn[(EquipmentIndex)i].Item.StringId,
                        ["modifierId"] = worn[(EquipmentIndex)i].ItemModifier?.StringId ?? string.Empty }));
            }
            CharacterObject character = hero?.CharacterObject;
            foreach (Equipment equipment in new[]
            {
                hero?.CivilianEquipment,
                character?.FirstCivilianEquipment,
                character?.Equipment
            }.Where(candidate => candidate != null))
            {
                JArray items = new JArray();
                bool hasRenderableClothing = false;
                HashSet<int> visitedSlots = new HashSet<int>();
                foreach (EquipmentIndex index in Enum.GetValues(typeof(EquipmentIndex)))
                {
                    int slot = (int)index;
                    bool preserveEncountered = ReignServices.EncounteredResidents?.Find(hero) != null;
                    if (!visitedSlots.Add(slot) || slot < (preserveEncountered ? 0 : 5) || slot > 9)
                    {
                        continue;
                    }

                    try
                    {
                        ItemObject item = equipment[index].Item;
                        if (item == null || string.IsNullOrWhiteSpace(item.StringId))
                        {
                            continue;
                        }

                        items.Add(new JObject
                        {
                            ["slot"] = index.ToString(),
                            ["itemId"] = item.StringId,
                            ["modifierId"] = equipment[index].ItemModifier?.StringId ?? string.Empty
                        });
                        if (slot >= 6)
                        {
                            hasRenderableClothing = true;
                        }
                    }
                    catch
                    {
                        // EquipmentIndex contains aliases and version-specific sentinels.
                    }
                }

                if (hasRenderableClothing)
                {
                    return items;
                }
            }

            return new JArray();
        }

        private static bool IsPortraitRosterEligible(Hero hero)
        {
            return hero != null
                && hero.IsAlive
                && !hero.IsChild
                && hero.Age >= 18f
                && hero.CharacterObject != null
                && !string.IsNullOrWhiteSpace(hero.StringId);
        }

        private static string BuildPortraitRosterFingerprint(JArray characters)
        {
            StringBuilder builder = new StringBuilder();
            foreach (JObject character in (characters ?? new JArray()).OfType<JObject>()
                .OrderBy(x => x.Value<string>("heroStringId") ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append(character.Value<string>("heroStringId") ?? string.Empty).Append('|')
                    .Append(character.Value<string>("characterObjectId") ?? string.Empty).Append('|')
                    .Append(character.Value<string>("name") ?? string.Empty).Append('|')
                    .Append(character.Value<string>("cultureId") ?? string.Empty).Append('|')
                    .Append(character.Value<string>("occupation") ?? string.Empty).Append('|')
                    .Append(character.Value<bool?>("isFemale") == true ? '1' : '0').Append('|')
                    .Append(character.Value<bool?>("isLord") == true ? '1' : '0').Append('|')
                    .Append(character.Value<bool?>("isNotable") == true ? '1' : '0').Append('|')
                    .Append(character.Value<bool?>("isWanderer") == true ? '1' : '0').Append('|')
                    .Append(character.Value<string>("bodyKey") ?? string.Empty).Append('|');
                foreach (JObject item in (character["civilianEquipment"] as JArray ?? new JArray())
                    .OfType<JObject>()
                    .OrderBy(x => x.Value<string>("slot") ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                {
                    builder.Append(item.Value<string>("slot") ?? string.Empty).Append('=')
                        .Append(item.Value<string>("itemId") ?? string.Empty).Append(';');
                }
                builder.Append('|')
                    .Append(character.Value<string>("portraitCacheKey") ?? string.Empty)
                    .Append('\n');
            }

            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static string StaticBodyKey(StaticBodyProperties properties)
        {
            return properties.KeyPart1.ToString("X16") + properties.KeyPart2.ToString("X16")
                + properties.KeyPart3.ToString("X16") + properties.KeyPart4.ToString("X16")
                + properties.KeyPart5.ToString("X16") + properties.KeyPart6.ToString("X16")
                + properties.KeyPart7.ToString("X16") + properties.KeyPart8.ToString("X16");
        }

        internal static JObject BuildPlayerIdentityContext()
        {
            Hero player = Hero.MainHero;
            return new JObject
            {
                ["heroStringId"] = player?.StringId ?? string.Empty,
                ["name"] = player?.Name?.ToString() ?? string.Empty,
                ["isFemale"] = player?.IsFemale ?? false,
                ["isPrisoner"] = player?.IsPrisoner ?? false,
                ["isLord"] = player?.IsLord ?? false,
                ["isRuler"] = player != null && player.Clan?.Kingdom?.Leader == player,
                ["governmentOfficeTitle"] = ReignServices.Government?.GovernmentOfficeTitle(player?.StringId) ?? "",
                ["governmentOfficeKingdomId"] = ReignServices.Government?.GetOfficeKingdomId(player?.StringId) ?? "",
                ["isNotable"] = player?.IsNotable ?? false,
                ["isWanderer"] = player?.IsWanderer ?? false,
                ["occupation"] = player?.Occupation.ToString() ?? string.Empty,
                ["clanTier"] = player?.Clan?.Tier ?? 0,
                ["currentCharm"] = player?.GetSkillValue(DefaultSkills.Charm) ?? 0,
                ["clanId"] = player?.Clan?.StringId ?? string.Empty,
                ["clanName"] = player?.Clan?.Name?.ToString() ?? string.Empty,
                ["kingdomId"] = player?.Clan?.Kingdom?.StringId ?? string.Empty,
                ["kingdomName"] = player?.Clan?.Kingdom?.InformalName?.ToString() ?? string.Empty,
                ["governorOfSettlementId"] = player?.GovernorOf?.Settlement?.StringId ?? string.Empty,
                ["governorOfSettlementName"] = player?.GovernorOf?.Settlement?.Name?.ToString() ?? string.Empty,
                ["appearance"] = BuildAppearanceProfile(player)
            };
        }

        private static JObject BuildWealthProfile(Hero hero)
        {
            int gold = hero?.Gold ?? 0;
            int clanGold = 0;
            int clanTier = 0;
            float clanRenown = 0f;
            int clanFiefCount = 0;
            int? partyInventoryValue = null;
            string partyInventoryState = "not_applicable";
            MobileParty party = hero == Hero.MainHero ? MobileParty.MainParty : hero?.PartyBelongedTo;
            try
            {
                if (party != null)
                {
                    partyInventoryValue = party.ItemRoster?.TotalValue;
                    partyInventoryState = partyInventoryValue.HasValue ? "observed" : "unknown";
                }
            }
            catch
            {
                partyInventoryValue = null;
                partyInventoryState = "unknown";
            }

            try
            {
                clanGold = hero?.Clan?.Gold ?? 0;
                clanTier = hero?.Clan?.Tier ?? 0;
                clanRenown = hero?.Clan?.Renown ?? 0f;
                clanFiefCount = hero?.Clan?.Fiefs?.Count ?? 0;
            }
            catch
            {
                clanGold = 0;
                clanTier = 0;
                clanRenown = 0f;
                clanFiefCount = 0;
            }

            JObject wealth = new JObject
            {
                ["version"] = 3,
                ["dataState"] = "observed",
                ["source"] = "bannerlord_native",
                ["observedWorldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                ["observedUtc"] = DateTime.UtcNow.ToString("o"),
                ["gold"] = gold,
                ["personalWealthTier"] = WealthTier(gold),
                ["partyInventoryValue"] = partyInventoryValue.HasValue ? new JValue(partyInventoryValue.Value) : JValue.CreateNull(),
                ["partyInventoryState"] = partyInventoryState,
                ["visibleWealthTier"] = "unknown", // Deprecated: appearance is supplied separately.
                ["clanGold"] = clanGold,
                ["clanWealthTier"] = WealthTier(clanGold),
                ["clanTier"] = clanTier,
                ["clanRenown"] = clanRenown,
                ["clanFiefCount"] = clanFiefCount,
                ["clanSocialCredit"] = ClanSocialCredit(clanGold, clanTier, clanRenown, clanFiefCount, hero?.IsLord ?? false),
                ["canLeanOnClanReputation"] = clanGold >= 25000 || clanTier >= 3 || clanRenown >= 200f || clanFiefCount > 0 || (hero?.IsLord ?? false),
                ["economicCapacityKnown"] = true,
                ["wealthEvidenceBasis"] = "bannerlord_native",
                ["goldSemantics"] = hero?.Clan != null && gold == clanGold
                    ? "native_hero_wallet_matches_clan_treasury"
                    : "native_hero_wallet",
                ["notes"] = "Observed directly from the loaded Bannerlord campaign. Cargo does not describe visible dress or personal cash. An absent party is not an empty observed inventory. Hero.Gold and Clan.Gold are retained separately; Bannerlord may expose the same shared clan wallet through both values for clan members."
            };
            ReignTestHarness.Current
                .ApplyManipulationFixtureWealthEvidence(hero, wealth);
            return wealth;
        }

        private static JObject BuildNpcObservableWealthProfile(
            Hero hero)
        {
            JObject wealth = BuildWealthProfile(hero);
            ReignTestHarness.Current
                .ApplyNpcObservableWealthEvidence(hero, wealth);
            return wealth;
        }

        private static JObject BuildInventoryProfile(Hero hero)
        {
            MobileParty party = hero == Hero.MainHero ? MobileParty.MainParty : hero?.PartyBelongedTo;
            bool controlsPartyInventory = hero != null && (hero == Hero.MainHero || party?.LeaderHero == hero);
            ItemRoster roster = controlsPartyInventory ? party?.ItemRoster : null;
            JObject result = new JObject
            {
                ["source"] = party == null ? "none" : "party_item_roster",
                ["partyId"] = party?.StringId ?? string.Empty,
                ["controlsPartyInventory"] = controlsPartyInventory,
                ["totalValue"] = roster?.TotalValue ?? 0,
                ["tradeGoodsValue"] = roster?.TradeGoodsTotalValue ?? 0,
                ["totalFood"] = roster?.TotalFood ?? 0,
                ["foodVariety"] = roster?.FoodVariety ?? 0,
                ["mounts"] = roster?.NumberOfMounts ?? 0,
                ["packAnimals"] = roster?.NumberOfPackAnimals ?? 0,
                ["livestock"] = roster?.NumberOfLivestockAnimals ?? 0
            };

            JArray items = new JArray();
            if (roster != null)
            {
                IEnumerable<ItemRosterElement> ordered = roster.OrderByDescending(x => ItemValue(x) * Math.Max(1, x.Amount));
                IEnumerable<ItemRosterElement> selected = ordered;

                foreach (ItemRosterElement element in selected
                    .GroupBy(x => (x.EquipmentElement.Item?.StringId ?? string.Empty) + "|"
                        + (x.EquipmentElement.ItemModifier?.StringId ?? string.Empty), StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First()))
                {
                    ItemObject item = element.EquipmentElement.Item;
                    if (item == null || element.Amount <= 0)
                    {
                        continue;
                    }

                    string valueSource;
                    int appraisedValue = ReignValueService.EstimateItemValue(item, element.EquipmentElement, element.Amount, party, out valueSource);
                    items.Add(new JObject
                    {
                        ["itemId"] = item.StringId ?? string.Empty,
                        ["name"] = item.Name?.ToString() ?? item.StringId ?? string.Empty,
                        ["amount"] = element.Amount,
                        ["value"] = item.Value,
                        ["totalValue"] = item.Value * element.Amount,
                        ["appraisedValue"] = appraisedValue,
                        ["valueSource"] = valueSource,
                        ["type"] = item.Type.ToString(),
                        ["isFood"] = item.IsFood,
                        ["isTradeGood"] = item.IsTradeGood,
                        ["isMountable"] = item.IsMountable,
                        ["modifier"] = element.EquipmentElement.ItemModifier?.Name?.ToString() ?? string.Empty,
                        ["itemModifierId"] = element.EquipmentElement.ItemModifier?.StringId ?? string.Empty,
                        ["source"] = "party_inventory"
                    });
                }
            }

            JArray personal = ReignServices.PersonalItemService?.Snapshot(hero) ?? new JArray();
            foreach (JObject owned in personal.OfType<JObject>()) items.Add(owned.DeepClone());
            result["personalItems"] = personal;
            result["items"] = items;
            result["topItems"] = new JArray(items.Take(32).Select(x => x.DeepClone()));
            return result;
        }

        private static JObject BuildAppearanceProfile(Hero hero)
        {
            CharacterObject character = hero?.CharacterObject;
            Equipment civilian = hero?.CivilianEquipment
                ?? character?.FirstCivilianEquipment
                ?? character?.Equipment;
            Equipment battle = hero?.BattleEquipment ?? character?.Equipment;
            JArray civilianItems = BuildEquipmentArray(civilian, "civilian");
            JArray battleItems = BuildEquipmentArray(battle, "battle");
            int civilianValue = SumEquipmentValue(civilianItems);
            int battleValue = SumEquipmentValue(battleItems);
            int visibleCivilianValue = civilianItems.OfType<JObject>()
                .Where(x => !IsMountOrTack(x)).Sum(x => x.Value<int?>("value") ?? 0);
            int visibleBattleValue = battleItems.OfType<JObject>()
                .Where(x => !IsMountOrTack(x)).Sum(x => x.Value<int?>("value") ?? 0);
            int trueStatusScore = TrueStatusScore(hero);
            int visibleStatusScore = VisibleStatusScore(hero, visibleCivilianValue, visibleBattleValue, civilianItems);
            string presentation = ApparentStatusLabel(visibleStatusScore);
            string trueStatus = ApparentStatusLabel(trueStatusScore);
            return new JObject
            {
                ["source"] = "hero_equipment",
                ["trueStatusScore"] = trueStatusScore,
                ["trueStatusLabel"] = trueStatus,
                ["visibleStatusScore"] = visibleStatusScore,
                ["visibleStatusLabel"] = presentation,
                ["statusMismatch"] = visibleStatusScore - trueStatusScore,
                ["presentationEffect"] = PresentationEffect(trueStatusScore, visibleStatusScore),
                ["firstView"] = BuildFirstViewText(hero, civilianItems, presentation, trueStatus, visibleStatusScore - trueStatusScore),
                ["civilianEquipmentValue"] = civilianValue,
                ["battleEquipmentValue"] = battleValue,
                ["civilianEquipment"] = civilianItems,
                ["battleEquipment"] = battleItems
            };
        }

        private static JArray BuildEquipmentArray(Equipment equipment, string equipmentSet)
        {
            JArray items = new JArray();
            if (equipment == null)
            {
                return items;
            }

            HashSet<int> visitedSlots = new HashSet<int>();
            foreach (EquipmentIndex index in Enum.GetValues(typeof(EquipmentIndex)))
            {
                if (!visitedSlots.Add((int)index))
                {
                    continue;
                }

                try
                {
                    EquipmentElement element = equipment[index];
                    ItemObject item = element.Item;
                    if (item == null)
                    {
                        continue;
                    }

                    string valueSource;
                    int appraisedValue = ReignValueService.EstimateItemValue(item, element, 1, MobileParty.MainParty, out valueSource);
                    items.Add(new JObject
                    {
                        ["sourceEquipmentSet"] = equipmentSet ?? string.Empty,
                        ["slot"] = index.ToString(),
                        ["itemId"] = item.StringId ?? string.Empty,
                        ["name"] = item.Name?.ToString() ?? item.StringId ?? string.Empty,
                        ["value"] = item.Value,
                        ["appraisedValue"] = appraisedValue,
                        ["valueSource"] = valueSource,
                        ["type"] = item.Type.ToString(),
                        ["isWeapon"] = IsWeaponSlot(index),
                        ["isArmorOrClothing"] = IsArmorOrClothingSlot(index),
                        ["isMount"] = item.IsMountable,
                        ["modifier"] = element.ItemModifier?.Name?.ToString() ?? string.Empty
                    });
                }
                catch
                {
                    // Some EquipmentIndex values are sentinels on some Bannerlord versions.
                }
            }

            return items;
        }

        private static int ItemValue(ItemRosterElement element)
        {
            return element.EquipmentElement.Item?.Value ?? 0;
        }

        private static int SumEquipmentValue(JArray items)
        {
            int total = 0;
            foreach (JObject item in items.OfType<JObject>())
            {
                total += item.Value<int?>("value") ?? 0;
            }

            return total;
        }

        private static int TrueStatusScore(Hero hero)
        {
            if (hero == null)
            {
                return 0;
            }

            int score = 0;
            if (hero.IsLord) score += 2;
            if (hero.Clan != null && hero.Clan.Tier >= 4) score += 1;
            if (hero.Clan?.Kingdom != null) score += 1;
            if (hero.Gold > 80000) score += 1;
            if (hero.IsNotable) score += 1;
            if (hero.IsWanderer) score -= 1;
            if (hero.IsPrisoner) score -= 2;
            return ClampScore(score, -3, 5);
        }

        private static int VisibleStatusScore(Hero hero, int civilianValue, int battleValue, JArray civilianItems)
        {
            int score = 0;
            if (civilianValue >= 50000) score += 4;
            else if (civilianValue >= 20000) score += 3;
            else if (civilianValue >= 8000) score += 2;
            else if (civilianValue >= 2500) score += 1;
            else if (civilianValue <= 400) score -= 2;
            else if (civilianValue <= 1000) score -= 1;

            bool armed = civilianItems.OfType<JObject>().Any(x => x.Value<bool?>("isWeapon") == true && (x.Value<int?>("value") ?? 0) > 400);
            bool armored = civilianItems.OfType<JObject>().Any(x => x.Value<bool?>("isArmorOrClothing") == true && (x.Value<int?>("value") ?? 0) > 2000);
            if (armed) score += 1;
            if (armored) score += 1;
            if (battleValue > civilianValue * 3 && battleValue > 12000) score += 1;
            if (hero?.IsPrisoner == true) score -= 3;
            return ClampScore(score, -3, 5);
        }

        private static string BuildFirstViewText(Hero hero, JArray civilianItems, string visibleStatus, string trueStatus, int mismatch)
        {
            List<string> notable = civilianItems.OfType<JObject>()
                .Where(x => !IsMountOrTack(x))
                .OrderByDescending(x => x.Value<int?>("value") ?? 0)
                .Take(5)
                .Select(x => x.Value<string>("name"))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            string clothes = notable.Count == 0 ? "no notable visible gear" : string.Join(", ", notable);
            string twist = mismatch <= -2
                ? "Their clothes understate their true station."
                : mismatch >= 2
                    ? "Their presentation may make them seem more important than they are."
                    : "Their presentation broadly matches their station.";
            return (hero?.Name?.ToString() ?? "This person") + " appears " + visibleStatus + " at first glance, while their true station reads as " + trueStatus + ". Visible details: " + clothes + ". " + twist;
        }

        private static bool IsMountOrTack(JObject item)
        {
            return item?.Value<bool?>("isMount") == true
                || string.Equals(item?.Value<string>("type"), "HorseHarness", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWeaponSlot(EquipmentIndex index)
        {
            return index == EquipmentIndex.Weapon0 || index == EquipmentIndex.Weapon1 || index == EquipmentIndex.Weapon2 || index == EquipmentIndex.Weapon3;
        }

        private static bool IsArmorOrClothingSlot(EquipmentIndex index)
        {
            return index == EquipmentIndex.Head || index == EquipmentIndex.Body || index == EquipmentIndex.Cape || index == EquipmentIndex.Gloves || index == EquipmentIndex.Leg;
        }

        private static string WealthTier(int value)
        {
            if (value >= 200000) return "great wealth";
            if (value >= 80000) return "wealthy";
            if (value >= 25000) return "comfortable";
            if (value >= 5000) return "modest";
            if (value >= 1000) return "poor";
            return "destitute";
        }

        private static string ClanSocialCredit(int clanGold, int clanTier, float clanRenown, int clanFiefCount, bool isLord)
        {
            int score = 0;
            if (isLord) score += 1;
            if (clanGold >= 200000) score += 4;
            else if (clanGold >= 80000) score += 3;
            else if (clanGold >= 25000) score += 2;
            else if (clanGold >= 5000) score += 1;
            if (clanTier >= 5) score += 3;
            else if (clanTier >= 3) score += 2;
            else if (clanTier >= 1) score += 1;
            if (clanRenown >= 600f) score += 2;
            else if (clanRenown >= 200f) score += 1;
            if (clanFiefCount >= 3) score += 2;
            else if (clanFiefCount >= 1) score += 1;

            if (score >= 8) return "great-house backing";
            if (score >= 5) return "strong clan backing";
            if (score >= 3) return "credible clan backing";
            if (score >= 1) return "minor clan backing";
            return "little clan backing";
        }

        private static string ApparentStatusLabel(int score)
        {
            if (score >= 5) return "princely or ruling-class";
            if (score >= 3) return "noble or very wealthy";
            if (score >= 1) return "respectable or prosperous";
            if (score == 0) return "ordinary";
            if (score <= -2) return "poor or diminished";
            return "plain or low-status";
        }

        private static string PresentationEffect(int trueStatusScore, int visibleStatusScore)
        {
            int mismatch = visibleStatusScore - trueStatusScore;
            if (mismatch <= -3) return "major_understatement";
            if (mismatch <= -1) return "understated";
            if (mismatch >= 3) return "major_overstatement";
            if (mismatch >= 1) return "overstated";
            return "matched";
        }

        private static int ClampScore(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        internal static JObject BuildTraitProfile(Hero hero)
        {
            JObject traits = new JObject();
            if (hero == null)
            {
                return traits;
            }

            traits["valor"] = hero.GetTraitLevel(DefaultTraits.Valor);
            traits["generosity"] = hero.GetTraitLevel(DefaultTraits.Generosity);
            traits["honor"] = hero.GetTraitLevel(DefaultTraits.Honor);
            traits["mercy"] = hero.GetTraitLevel(DefaultTraits.Mercy);
            traits["calculating"] = hero.GetTraitLevel(DefaultTraits.Calculating);
            return traits;
        }

        internal static JObject BuildSkillProfile(Hero hero)
        {
            JObject skills = new JObject();
            if (hero == null)
            {
                return skills;
            }

            skills["oneHanded"] = hero.GetSkillValue(DefaultSkills.OneHanded);
            skills["twoHanded"] = hero.GetSkillValue(DefaultSkills.TwoHanded);
            skills["polearm"] = hero.GetSkillValue(DefaultSkills.Polearm);
            skills["bow"] = hero.GetSkillValue(DefaultSkills.Bow);
            skills["crossbow"] = hero.GetSkillValue(DefaultSkills.Crossbow);
            skills["throwing"] = hero.GetSkillValue(DefaultSkills.Throwing);
            skills["riding"] = hero.GetSkillValue(DefaultSkills.Riding);
            skills["athletics"] = hero.GetSkillValue(DefaultSkills.Athletics);
            skills["smithing"] = hero.GetSkillValue(DefaultSkills.Crafting);
            skills["scouting"] = hero.GetSkillValue(DefaultSkills.Scouting);
            skills["tactics"] = hero.GetSkillValue(DefaultSkills.Tactics);
            skills["roguery"] = hero.GetSkillValue(DefaultSkills.Roguery);
            skills["charm"] = hero.GetSkillValue(DefaultSkills.Charm);
            skills["leadership"] = hero.GetSkillValue(DefaultSkills.Leadership);
            skills["trade"] = hero.GetSkillValue(DefaultSkills.Trade);
            skills["steward"] = hero.GetSkillValue(DefaultSkills.Steward);
            skills["medicine"] = hero.GetSkillValue(DefaultSkills.Medicine);
            skills["engineering"] = hero.GetSkillValue(DefaultSkills.Engineering);
            return skills;
        }

        internal static JObject BuildLieSceneRisk(Hero claimant, Hero target)
        {
            int claimantTier = claimant?.Clan?.Tier ?? 0;
            int targetTier = target?.Clan?.Tier ?? 0;
            float claimantStrength = HeroPartyStrength(claimant);
            float targetStrength = HeroPartyStrength(target);
            double powerDisadvantage = 0d;
            if (claimantTier >= targetTier + 2) powerDisadvantage += 1d;
            if (claimantStrength > 0f && claimantStrength >= Math.Max(1f, targetStrength) * 2f) powerDisadvantage += 1d;
            if (target?.IsPrisoner == true) powerDisadvantage += 1d;
            return new JObject
            {
                ["claimantClanTier"] = claimantTier, ["targetClanTier"] = targetTier,
                ["claimantPartyStrength"] = claimantStrength, ["targetPartyStrength"] = targetStrength,
                ["targetIsPrisoner"] = target?.IsPrisoner ?? false, ["powerDisadvantage"] = Math.Min(2d, powerDisadvantage)
            };
        }

        private static float HeroPartyStrength(Hero hero)
        {
            try
            {
                MobileParty party = hero == Hero.MainHero ? MobileParty.MainParty : hero?.PartyBelongedTo;
                return party?.Party?.EstimatedStrength ?? 0f;
            }
            catch
            {
                return 0f;
            }
        }

        internal static string GetNativeEncyclopediaText(Hero hero)
        {
            if (hero == null || string.IsNullOrWhiteSpace(hero.StringId))
            {
                return string.Empty;
            }

            lock (NativeDescriptionLock)
            {
                if (NativeDescriptions.TryGetValue(hero.StringId, out string preserved))
                {
                    return preserved;
                }

                string current = hero.EncyclopediaText?.ToString() ?? string.Empty;
                NativeDescriptions[hero.StringId] = current;
                return current;
            }
        }

        private static string BuildSceneContext(Hero hero)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Conversation mode: individual one-to-one conversation.");
            if (TaleWorlds.CampaignSystem.Campaign.Current != null)
            {
                builder.AppendLine(ReignCalendarService.PromptRule);
            }
            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement ?? hero?.CurrentSettlement;
            if (settlement != null)
            {
                builder.AppendLine("Current settlement: " + settlement.Name + " (`" + settlement.StringId + "`)");
            }

            if (MobileParty.MainParty != null)
            {
                builder.AppendLine("Main party: " + (MobileParty.MainParty.Name?.ToString() ?? "the player's party"));
            }

            if (hero?.Clan != null)
            {
                builder.AppendLine("NPC clan: " + hero.Clan.Name);
            }

            if (hero?.Clan?.Kingdom != null)
            {
                builder.AppendLine("NPC kingdom: " + hero.Clan.Kingdom.InformalName);
            }

            return builder.ToString().Trim();
        }

        internal static string ResolveDialogueLocationId(Hero hero)
        {
            Settlement settlement = Settlement.CurrentSettlement
                ?? MobileParty.MainParty?.CurrentSettlement
                ?? hero?.CurrentSettlement
                ?? hero?.PartyBelongedTo?.CurrentSettlement;
            if (settlement != null)
            {
                return settlement.StringId ?? string.Empty;
            }

            // A remote audit can converse with a hero whose party is travelling on
            // the campaign map. Preserve a native party anchor instead of dropping
            // the turn's location entirely.
            return hero?.PartyBelongedTo?.StringId
                ?? MobileParty.MainParty?.StringId
                ?? string.Empty;
        }

        private static bool IsFixedNobleHero(Hero hero)
        {
            return hero != null
                && hero != Hero.MainHero
                && !string.IsNullOrWhiteSpace(hero.StringId)
                && (hero.IsLord || string.Equals(hero.Occupation.ToString(), "Lord", StringComparison.OrdinalIgnoreCase));
        }

        internal static void ApplyHeroEncyclopediaText(Hero hero, string encyclopediaText)
        {
            try
            {
                if (hero == null || string.IsNullOrWhiteSpace(encyclopediaText))
                {
                    return;
                }

                GetNativeEncyclopediaText(hero);

                string current = hero.EncyclopediaText?.ToString() ?? string.Empty;
                if (string.Equals(current.Trim(), encyclopediaText.Trim(), StringComparison.Ordinal))
                {
                    return;
                }

                hero.EncyclopediaText = new TextObject(encyclopediaText);
                ReignLog.Info("Applied Bannerlord Reign encyclopedia backstory hero=" + hero.StringId + " chars=" + encyclopediaText.Length);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Failed to apply encyclopedia backstory: " + ex.Message);
            }
        }
    }
}
