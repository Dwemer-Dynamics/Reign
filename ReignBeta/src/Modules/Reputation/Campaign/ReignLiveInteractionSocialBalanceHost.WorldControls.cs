using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.Integration;
using ReignBeta.Diplomacy.UI;
using ReignBeta.Platform.Integration;
using ReignBeta.Rebellion.Campaign;
using ReignBeta.Reputation.Campaign;
using ReignBeta.WorldSimulation.Integration;
using ReignBeta.WorldSimulation.Runtime;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static LiveCommandResult SocialBalanceSnapshot(JObject command)
        {
            List<string> focusIds = (command?["heroIds"] as JArray ?? new JArray())
                .Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            IEnumerable<Hero> heroes = Hero.AllAliveHeroes.Where(IsSocialBalanceAdult);
            if (focusIds.Count > 0)
                heroes = heroes.Where(x => focusIds.Contains(x.StringId, StringComparer.OrdinalIgnoreCase));
            JArray heroRows = new JArray(heroes.OrderBy(x => x.StringId).Select(hero =>
            {
                Kingdom kingdom = hero.Clan?.Kingdom;
                bool ruler = kingdom?.Leader == hero;
                bool clanLeader = hero.Clan?.Leader == hero;
                return new JObject
                {
                    ["heroId"] = hero.StringId ?? string.Empty,
                    ["name"] = hero.Name?.ToString() ?? string.Empty,
                    ["clanId"] = hero.Clan?.StringId ?? string.Empty,
                    ["kingdomId"] = kingdom?.StringId ?? string.Empty,
                    ["isPlayer"] = hero == Hero.MainHero,
                    ["isRuler"] = ruler,
                    ["isClanLeader"] = clanLeader,
                    ["isRegularLord"] = hero.IsLord && !ruler && !clanLeader,
                    ["isNotable"] = hero.IsNotable,
                    ["isWanderer"] = hero.IsWanderer,
                    ["charm"] = hero.GetSkillValue(DefaultSkills.Charm),
                    ["isPrisoner"] = hero.IsPrisoner
                };
            }));
            JArray settlements = new JArray(Settlement.All
                .Where(x => x?.Town != null)
                .OrderBy(x => x.StringId)
                .Select(settlement =>
                {
                    Town town = settlement.Town;
                    return new JObject
                    {
                        ["settlementId"] = settlement.StringId,
                        ["name"] = settlement.Name?.ToString() ?? string.Empty,
                        ["kingdomId"] = settlement.MapFaction?.StringId ?? string.Empty,
                        ["isTown"] = settlement.IsTown,
                        ["isCastle"] = settlement.IsCastle,
                        ["prosperity"] = town.Prosperity,
                        ["prosperityChange"] = town.ProsperityChange,
                        ["foodStocks"] = town.FoodStocks,
                        ["foodCapacity"] = town.FoodStocksUpperLimit(),
                        ["foodChange"] = town.FoodChange,
                        ["loyalty"] = town.Loyalty,
                        ["loyaltyChange"] = town.LoyaltyChange,
                        ["security"] = town.Security,
                        ["securityChange"] = town.SecurityChange,
                        ["underSiege"] = town.IsUnderSiege,
                        ["rebellious"] = town.InRebelliousState
                    };
                }));
            JArray villages = new JArray(Settlement.All
                .Where(x => x?.Village != null)
                .OrderBy(x => x.StringId)
                .Select(settlement => new JObject
                {
                    ["villageId"] = settlement.StringId,
                    ["name"] = settlement.Name?.ToString() ?? string.Empty,
                    ["kingdomId"] = settlement.MapFaction?.StringId ?? string.Empty,
                    ["boundSettlementId"] = settlement.Village?.Bound?.StringId ?? string.Empty,
                    ["state"] = settlement.Village?.VillageState.ToString() ?? string.Empty,
                    ["hearth"] = settlement.Village?.Hearth ?? 0f,
                    ["hearthChange"] = settlement.Village?.HearthChange ?? 0f,
                    ["deserted"] = settlement.Village?.IsDeserted == true
                }));
            JObject data = new JObject
            {
                ["campaignId"] = ReignCampaignIdentity.CurrentCampaignId(),
                ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                ["worldDay"] = CampaignTime.Now.ToDays,
                ["heroes"] = heroRows,
                ["settlements"] = settlements,
                ["villages"] = villages,
                ["rebellion"] = SocialBalanceRebellionSnapshot(),
                ["harness"] = ReignServices.SocialBalanceHarness?.Snapshot() ?? new JObject()
            };
            return LiveCommandResult.Completed("Captured a native social-balance snapshot.", data);
        }

        private static LiveCommandResult SocialBalanceSetTown(JObject command)
        {
            Settlement settlement = Settlement.Find(command.Value<string>("settlementId") ?? string.Empty);
            Town town = settlement?.Town;
            if (town == null) return LiveCommandResult.Failed("A town or castle settlement is required.");
            JObject before = TownMetrics(town);
            if (command["prosperity"] != null) town.Prosperity = ClampFloat(command.Value<float>("prosperity"), 0f, 1000000f);
            if (command["foodStocks"] != null) town.FoodStocks = ClampFloat(command.Value<float>("foodStocks"), -10000f, 100000f);
            if (command["loyalty"] != null) town.Loyalty = ClampFloat(command.Value<float>("loyalty"), 0f, 100f);
            if (command["security"] != null) town.Security = ClampFloat(command.Value<float>("security"), 0f, 100f);
            JObject after = TownMetrics(town);
            ReignServices.SocialBalanceHarness?.RecordOverride("town_metrics",
                new JObject { ["settlementId"] = settlement.StringId, ["before"] = before, ["after"] = after });
            return LiveCommandResult.Completed("Applied bounded settlement metrics in the enrolled disposable save.",
                new JObject { ["settlementId"] = settlement.StringId, ["before"] = before, ["after"] = after });
        }

        private static LiveCommandResult SocialBalanceSetVillage(JObject command)
        {
            Settlement settlement = Settlement.Find(command.Value<string>("villageId") ?? string.Empty);
            Village village = settlement?.Village;
            if (village == null) return LiveCommandResult.Failed("A village settlement is required.");
            JObject before = VillageMetrics(village);
            if (command["hearth"] != null) village.Hearth = ClampFloat(command.Value<float>("hearth"), 0f, 100000f);
            string state = (command.Value<string>("state") ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(state))
            {
                if (state == "normal") ChangeVillageStateAction.ApplyBySettingToNormal(settlement);
                else if (state == "looted") village.VillageState = Village.VillageStates.Looted;
                else return LiveCommandResult.Failed("Supported village states are normal and looted.");
            }
            JObject after = VillageMetrics(village);
            ReignServices.SocialBalanceHarness?.RecordOverride("village_metrics",
                new JObject { ["villageId"] = settlement.StringId, ["before"] = before, ["after"] = after });
            return LiveCommandResult.Completed("Applied bounded village metrics in the enrolled disposable save.",
                new JObject { ["villageId"] = settlement.StringId, ["before"] = before, ["after"] = after });
        }

        private static LiveCommandResult SocialBalanceSetHero(JObject command)
        {
            Hero hero = FindSocialBalanceHero(command.Value<string>("heroId"));
            if (hero == null || !IsSocialBalanceAdult(hero)) return LiveCommandResult.Failed("A living adult hero is required.");
            JObject before = SocialBalanceHeroMetrics(hero);
            if (command["charm"] != null)
                hero.SetSkillValue(DefaultSkills.Charm, Math.Max(0, Math.Min(1023, command.Value<int>("charm"))));
            JObject after = SocialBalanceHeroMetrics(hero);
            ReignServices.SocialBalanceHarness?.RecordOverride("hero_metrics",
                new JObject { ["heroId"] = hero.StringId, ["before"] = before, ["after"] = after });
            return LiveCommandResult.Completed("Applied bounded hero metrics in the enrolled disposable save.",
                new JObject { ["heroId"] = hero.StringId, ["before"] = before, ["after"] = after });
        }

        private static LiveCommandResult SocialBalanceSetRelation(JObject command)
        {
            Hero observer = FindSocialBalanceHero(command.Value<string>("observerId"));
            Hero subject = FindSocialBalanceHero(command.Value<string>("subjectId"));
            if (!IsSocialBalanceAdult(observer) || !IsSocialBalanceAdult(subject) || observer == subject)
                return LiveCommandResult.Failed("Two distinct living adult heroes are required.");
            int prior = observer.GetRelation(subject);
            int value = Math.Max(-100, Math.Min(100, command.Value<int?>("value") ?? prior));
            observer.SetPersonalRelation(subject, value);
            ReignServices.SocialBalanceHarness?.RecordOverride("native_relation",
                new JObject { ["observerId"] = observer.StringId, ["subjectId"] = subject.StringId, ["before"] = prior, ["after"] = value });
            return LiveCommandResult.Completed("Set the enrolled save's native relationship value.",
                new JObject { ["observerId"] = observer.StringId, ["subjectId"] = subject.StringId, ["before"] = prior, ["after"] = observer.GetRelation(subject) });
        }

        private static LiveCommandResult SocialBalanceSetRuler(JObject command)
        {
            Kingdom kingdom = FindSocialBalanceKingdom(command.Value<string>("kingdomId"));
            Clan clan = Clan.FindFirst(x => string.Equals(x?.StringId, command.Value<string>("clanId"), StringComparison.OrdinalIgnoreCase));
            if (kingdom == null || clan == null || clan.Kingdom != kingdom || clan.IsEliminated)
                return LiveCommandResult.Failed("The replacement ruling clan must be an active member of the kingdom.");
            string before = kingdom.RulingClan?.StringId ?? string.Empty;
            ChangeRulingClanAction.Apply(kingdom, clan);
            ReignServices.SocialBalanceHarness?.RecordOverride("ruling_clan",
                new JObject { ["kingdomId"] = kingdom.StringId, ["before"] = before, ["after"] = kingdom.RulingClan?.StringId ?? string.Empty });
            return LiveCommandResult.Completed("Changed the ruler in the enrolled disposable save.",
                new JObject { ["kingdomId"] = kingdom.StringId, ["beforeClanId"] = before, ["afterClanId"] = kingdom.RulingClan?.StringId ?? string.Empty });
        }

        private static LiveCommandResult SocialBalanceChangeClan(JObject command)
        {
            Clan clan = Clan.FindFirst(x => string.Equals(x?.StringId, command.Value<string>("clanId"), StringComparison.OrdinalIgnoreCase));
            Kingdom destination = FindSocialBalanceKingdom(command.Value<string>("kingdomId"));
            if (clan == null || clan.IsEliminated || clan == Clan.PlayerClan)
                return LiveCommandResult.Failed("A living non-player clan is required.");
            string before = clan.Kingdom?.StringId ?? string.Empty;
            if (destination == null) ChangeKingdomAction.ApplyByLeaveKingdom(clan, true);
            else if (clan.Kingdom != destination)
                ChangeKingdomAction.ApplyByJoinToKingdom(clan, destination, CampaignTime.Zero, true);
            ReignServices.SocialBalanceHarness?.RecordOverride("clan_kingdom",
                new JObject { ["clanId"] = clan.StringId, ["before"] = before, ["after"] = clan.Kingdom?.StringId ?? string.Empty });
            return LiveCommandResult.Completed("Changed clan membership in the enrolled disposable save.",
                new JObject { ["clanId"] = clan.StringId, ["beforeKingdomId"] = before, ["afterKingdomId"] = clan.Kingdom?.StringId ?? string.Empty });
        }

        private static LiveCommandResult SocialBalanceChangeOwner(JObject command)
        {
            Settlement settlement = Settlement.Find(command.Value<string>("settlementId") ?? string.Empty);
            Hero owner = FindSocialBalanceHero(command.Value<string>("ownerHeroId"));
            if (settlement?.Town == null || owner?.Clan == null || owner.IsDead)
                return LiveCommandResult.Failed("A fortification and living clan member owner are required.");
            string before = settlement.OwnerClan?.StringId ?? string.Empty;
            ChangeOwnerOfSettlementAction.ApplyByGift(settlement, owner);
            ReignServices.SocialBalanceHarness?.RecordOverride("settlement_owner",
                new JObject { ["settlementId"] = settlement.StringId, ["before"] = before, ["after"] = settlement.OwnerClan?.StringId ?? string.Empty });
            return LiveCommandResult.Completed("Changed settlement ownership in the enrolled disposable save.",
                new JObject { ["settlementId"] = settlement.StringId, ["beforeClanId"] = before, ["afterClanId"] = settlement.OwnerClan?.StringId ?? string.Empty });
        }

        private static LiveCommandResult SocialBalanceTimeControl(
            JObject command,
            string contextLabel = "enrolled disposable save")
        {
            TaleWorlds.CampaignSystem.Campaign campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign == null) return LiveCommandResult.Failed("No campaign is loaded.");
            string mode = (command.Value<string>("timeMode") ?? "stop").Trim().ToLowerInvariant();
            bool settlementWaitRequested = command.Value<bool?>("settlementWait") == true;
            bool? autoAcknowledgeRequested =
                command.Value<bool?>("autoAcknowledgeDiplomacyAnnouncements");
            GameMenu waitMenu = campaign.CurrentMenuContext?.GameMenu;
            if (settlementWaitRequested && waitMenu?.IsWaitActive != true)
            {
                Settlement settlement = Hero.MainHero?.CurrentSettlement;
                if (settlement == null || (!settlement.IsTown && !settlement.IsCastle))
                    return LiveCommandResult.Failed(
                        "Safe settlement waiting requires the player to be inside a town or castle.");

                if (waitMenu == null)
                    return LiveCommandResult.Failed(
                        "Safe settlement waiting requires an active Bannerlord settlement menu.");
                if (!waitMenu.IsWaitMenu)
                {
                    int waitOptionIndex = -1;
                    List<string> optionIds = new List<string>();
                    for (int index = 0; index < waitMenu.MenuItemAmount; index++)
                    {
                        string optionId = waitMenu.GetMenuOptionIdString(index) ?? string.Empty;
                        optionIds.Add(optionId);
                        if (optionId.Equals("town_wait", StringComparison.OrdinalIgnoreCase)
                            || optionId.IndexOf("wait", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            waitOptionIndex = index;
                            break;
                        }
                    }
                    if (waitOptionIndex < 0)
                        return LiveCommandResult.Failed(
                            "The current settlement menu has no native wait option. Options: "
                            + string.Join(", ", optionIds));

                    GameMenuOption waitOption = waitMenu.GetGameMenuOption(waitOptionIndex);
                    if (waitOption == null)
                        return LiveCommandResult.Failed(
                            "Bannerlord returned an empty native settlement-wait option.");
                    waitOption.RunConsequence(campaign.CurrentMenuContext);
                }
                waitMenu = campaign.CurrentMenuContext?.GameMenu;
                if (waitMenu == null || !waitMenu.IsWaitMenu)
                    return LiveCommandResult.Failed(
                        "Bannerlord did not enter its native settlement-wait menu.");
                if (!waitMenu.IsWaitActive) waitMenu.StartWait();
            }
            if (mode == "stop") campaign.TimeControlMode = CampaignTimeControlMode.Stop;
            else if (mode == "play") campaign.TimeControlMode = CampaignTimeControlMode.StoppablePlay;
            else if (mode == "fast" && settlementWaitRequested)
            {
                // Bannerlord's native settlement wait advances only through the
                // UnstoppableFastForward mode established by StartWait(). A prior
                // stop command deliberately leaves IsWaitActive set while pausing
                // the clock, so restart the native wait even when it is already
                // marked active instead of replacing its mode with a stoppable one.
                waitMenu = campaign.CurrentMenuContext?.GameMenu;
                if (waitMenu == null || !waitMenu.IsWaitMenu)
                    return LiveCommandResult.Failed(
                        "Bannerlord left its native settlement-wait menu before fast-forward could begin.");
                waitMenu.StartWait();
            }
            else if (mode == "fast") campaign.TimeControlMode = CampaignTimeControlMode.StoppableFastForward;
            else return LiveCommandResult.Failed("timeMode must be stop, play, or fast.");
            if (autoAcknowledgeRequested.HasValue)
                _autoAcknowledgeDiplomacyAnnouncements = autoAcknowledgeRequested.Value;
            return LiveCommandResult.Completed("Changed campaign time control for the " + contextLabel + ".",
                new JObject
                {
                    ["timeMode"] = campaign.TimeControlMode.ToString(),
                    ["worldDay"] = CampaignTime.Now.ToDays,
                    ["settlementWaitRequested"] = settlementWaitRequested,
                    ["settlementWaitActive"] = campaign.CurrentMenuContext?.GameMenu?.IsWaitActive == true,
                    ["settlementId"] = Hero.MainHero?.CurrentSettlement?.StringId ?? string.Empty,
                    ["autoAcknowledgeDiplomacyAnnouncements"] = _autoAcknowledgeDiplomacyAnnouncements
                });
        }

        private static LiveCommandResult SocialBalanceAcknowledgeDiplomacyAnnouncement()
        {
            bool acknowledged = ReignDiplomacyAnnouncementScreenManager
                .TryAcknowledgeForLiveHarness(false, out string eventId);
            if (acknowledged)
            {
                ReignServices.SocialBalanceHarness?.RecordOverride(
                    "diplomacy_announcement_acknowledged",
                    new JObject { ["eventId"] = eventId });
            }
            return LiveCommandResult.Completed(
                acknowledged
                    ? "Acknowledged the active Reign diplomacy announcement."
                    : "No Reign diplomacy announcement was open.",
                new JObject
                {
                    ["acknowledged"] = acknowledged,
                    ["eventId"] = eventId
                });
        }

        private static LiveCommandResult SocialBalanceRecordOutcome(JObject command)
        {
            Hero subject = FindSocialBalanceHero(command.Value<string>("subjectId"));
            string archetype = command.Value<string>("archetypeId") ?? string.Empty;
            string summary = command.Value<string>("incidentDescription") ?? string.Empty;
            if (!IsSocialBalanceAdult(subject) || string.IsNullOrWhiteSpace(archetype) || summary.Trim().Length < 10)
                return LiveCommandResult.Failed("A living adult subject, archetypeId, and factual incidentDescription are required.");
            JObject evidence = command["evidence"] as JObject ?? new JObject();
            evidence["socialBalanceTestRunId"] = (command["enrollment"] as JObject)?.Value<string>("runId") ?? string.Empty;
            evidence["socialBalanceCaseId"] = command.Value<string>("caseId") ?? string.Empty;
            JArray counters = command["counterOnlyTagIds"] as JArray;
            string sourceKey = command.Value<string>("sourceKey")
                ?? "social-balance-" + Guid.NewGuid().ToString("N");
            ReignServices.WorldHistory?.RecordSocialOutcome(
                archetype, subject, command.Value<string>("role") ?? "subject", sourceKey, summary,
                evidence, counters, command.Value<bool?>("forceExposure") == true,
                command.Value<bool?>("forcePromotion") == true);
            ReignServices.SocialBalanceHarness?.RecordOverride("social_outcome",
                new JObject { ["subjectId"] = subject.StringId, ["archetypeId"] = archetype, ["sourceKey"] = sourceKey });
            return LiveCommandResult.Completed("Queued deterministic social outcome evidence through World History.",
                new JObject { ["subjectId"] = subject.StringId, ["archetypeId"] = archetype, ["sourceKey"] = sourceKey });
        }

        private static LiveCommandResult SocialBalanceSetLocation(JObject command)
        {
            Hero hero = FindSocialBalanceHero(command.Value<string>("heroId"));
            Settlement settlement = Settlement.Find(command.Value<string>("settlementId") ?? string.Empty);
            if (!IsSocialBalanceAdult(hero) || settlement == null)
                return LiveCommandResult.Failed("A living adult hero and valid settlement are required.");
            string before = hero.CurrentSettlement?.StringId
                ?? hero.PartyBelongedTo?.CurrentSettlement?.StringId
                ?? string.Empty;
            TeleportHeroAction.ApplyImmediateTeleportToSettlement(hero, settlement);
            string after = hero.CurrentSettlement?.StringId
                ?? hero.PartyBelongedTo?.CurrentSettlement?.StringId
                ?? string.Empty;
            ReignServices.SocialBalanceHarness?.RecordOverride("hero_location",
                new JObject
                {
                    ["heroId"] = hero.StringId, ["beforeSettlementId"] = before,
                    ["afterSettlementId"] = after
                });
            return LiveCommandResult.Completed("Teleported the hero inside the enrolled disposable save.",
                new JObject
                {
                    ["heroId"] = hero.StringId, ["beforeSettlementId"] = before,
                    ["afterSettlementId"] = after
                });
        }

        private static LiveCommandResult SocialBalanceCreateChild(JObject command)
        {
            ReignFamilyCampaignBehavior family = ReignServices.Family;
            Hero mother = FindSocialBalanceHero(command.Value<string>("motherId"));
            Hero biologicalFather = FindSocialBalanceHero(command.Value<string>("biologicalFatherId"));
            Hero legalFather = FindSocialBalanceHero(command.Value<string>("legalFatherId")) ?? biologicalFather;
            if (family == null)
                return LiveCommandResult.Failed("The Reign family behavior is unavailable.");
            if (!family.TryCreateSocialBalanceChild(
                mother, biologicalFather, legalFather,
                command.Value<bool?>("publiclyKnown") == true,
                command.Value<bool?>("female") == true,
                out Hero child, out string error))
                return LiveCommandResult.Failed(error);
            ReignServices.SocialBalanceHarness?.RecordOverride("child_created",
                new JObject
                {
                    ["childId"] = child.StringId,
                    ["motherId"] = mother.StringId,
                    ["biologicalFatherId"] = biologicalFather.StringId,
                    ["legalFatherId"] = legalFather.StringId,
                    ["publiclyKnown"] = command.Value<bool?>("publiclyKnown") == true
                });
            return LiveCommandResult.Completed("Created controlled family evidence inside the enrolled disposable save.",
                new JObject
                {
                    ["childId"] = child.StringId,
                    ["name"] = child.Name?.ToString() ?? string.Empty,
                    ["motherId"] = mother.StringId,
                    ["biologicalFatherId"] = biologicalFather.StringId,
                    ["legalFatherId"] = legalFather.StringId
                });
        }

        private static LiveCommandResult SocialBalanceQueueRebellionRoll(JObject command)
        {
            ReignRebellionCampaignBehavior rebellion = ReignServices.Rebellion;
            if (rebellion == null) return LiveCommandResult.Failed("The rebellion behavior is unavailable.");
            string runId = (command["enrollment"] as JObject)?.Value<string>("runId") ?? string.Empty;
            if (!rebellion.QueueSocialBalanceRollOverride(command.Value<string>("kingdomId"),
                command.Value<string>("clanId"), command.Value<int?>("roll") ?? 100, runId, out string result))
                return LiveCommandResult.Failed(result);
            ReignServices.SocialBalanceHarness?.RecordOverride("rebellion_roll",
                new JObject { ["kingdomId"] = command.Value<string>("kingdomId"), ["clanId"] = command.Value<string>("clanId"), ["roll"] = command.Value<int?>("roll") ?? 100 });
            return LiveCommandResult.Completed(result);
        }

        private static LiveCommandResult SocialBalanceResolveRebellion(JObject command)
        {
            ReignRebellionCampaignBehavior rebellion = ReignServices.Rebellion;
            Hero surrendering = FindSocialBalanceHero(command.Value<string>("surrenderingHeroId"));
            if (rebellion == null || surrendering == null)
                return LiveCommandResult.Failed("The rebellion behavior and surrendering leader are required.");
            if (!rebellion.TrySurrender(surrendering, command.Value<string>("movementId") ?? string.Empty, out string result))
                return LiveCommandResult.Failed(result);
            return LiveCommandResult.Completed(result, SocialBalanceRebellionSnapshot());
        }

        private static LiveCommandResult SocialBalanceClearOverrides(JObject command)
        {
            string runId = (command["enrollment"] as JObject)?.Value<string>("runId") ?? string.Empty;
            int cleared = ReignServices.Rebellion?.ClearSocialBalanceRollOverrides(runId) ?? 0;
            return LiveCommandResult.Completed("Cleared pending test-only native overrides.",
                new JObject { ["rebellionRollOverridesCleared"] = cleared });
        }

        private static bool TryGetDurableSocialBalanceResult(JObject command, out LiveCommandResult result)
        {
            result = null;
            string operation = command?.Value<string>("operation") ?? string.Empty;
            string commandId = command?.Value<string>("commandId") ?? string.Empty;
            if (!operation.StartsWith("social_", StringComparison.OrdinalIgnoreCase)
                || ReignServices.SocialBalanceHarness == null
                || !ReignServices.SocialBalanceHarness.TryGetReceipt(commandId, out JObject stored))
                return false;
            result = new LiveCommandResult
            {
                Status = stored.Value<string>("status") ?? "completed",
                Message = stored.Value<string>("message") ?? string.Empty,
                Error = stored.Value<string>("error") ?? string.Empty,
                Data = stored["data"] as JObject ?? new JObject(),
                CorrelationIds = (stored["correlationIds"] as JArray ?? new JArray()).Values<string>().ToList()
            };
            return true;
        }

        private static void RememberDurableSocialBalanceResult(JObject command, LiveCommandResult result)
        {
            string operation = command?.Value<string>("operation") ?? string.Empty;
            if (!operation.StartsWith("social_", StringComparison.OrdinalIgnoreCase) || result == null) return;
            ReignServices.SocialBalanceHarness?.RememberReceipt(
                command.Value<string>("commandId") ?? string.Empty,
                new JObject
                {
                    ["status"] = result.Status ?? "completed",
                    ["message"] = result.Message ?? string.Empty,
                    ["error"] = result.Error ?? string.Empty,
                    ["data"] = result.Data ?? new JObject(),
                    ["correlationIds"] = new JArray(result.CorrelationIds ?? new List<string>())
                });
        }

        private static JObject SocialBalanceRebellionSnapshot()
        {
            ReignRebellionCampaignBehavior rebellion = ReignServices.Rebellion;
            return new JObject
            {
                ["weeklyRolls"] = new JArray((rebellion?.WeeklyRolls ?? new List<ReignBeta.World.ReignRebellionWeeklyRollRecord>())
                    .Select(x => new JObject
                    {
                        ["kingdomId"] = x.KingdomStringId, ["clanId"] = x.ClanStringId,
                        ["weekIndex"] = x.WeekIndex, ["roll"] = x.Roll,
                        ["rulerRelation"] = x.RulerRelation, ["eligible"] = x.WasEligible,
                        ["triggered"] = x.Triggered, ["testOverride"] = x.TestOverride,
                        ["testRunId"] = x.TestRunId ?? string.Empty
                    })),
                ["movements"] = new JArray((rebellion?.Movements ?? new List<ReignBeta.World.ReignRebellionMovementRecord>())
                    .Select(x => new JObject
                    {
                        ["movementId"] = x.MovementId, ["parentKingdomId"] = x.ParentKingdomStringId,
                        ["rebelKingdomId"] = x.RebelKingdomStringId, ["leaderHeroId"] = x.LeaderHeroStringId,
                        ["rulerHeroId"] = x.OriginalRulerHeroStringId, ["stage"] = x.Stage,
                        ["winningSide"] = x.WinningSide, ["resolutionCause"] = x.ResolutionCause,
                        ["resolved"] = x.ResolutionApplied
                    }))
            };
        }

        private static JObject TownMetrics(Town town)
        {
            return new JObject
            {
                ["prosperity"] = town?.Prosperity ?? 0f, ["foodStocks"] = town?.FoodStocks ?? 0f,
                ["loyalty"] = town?.Loyalty ?? 0f, ["security"] = town?.Security ?? 0f
            };
        }

        private static JObject VillageMetrics(Village village)
        {
            return new JObject
            {
                ["hearth"] = village?.Hearth ?? 0f,
                ["state"] = village?.VillageState.ToString() ?? string.Empty,
                ["deserted"] = village?.IsDeserted == true
            };
        }

        private static JObject SocialBalanceHeroMetrics(Hero hero)
        {
            return new JObject
            {
                ["heroId"] = hero?.StringId ?? string.Empty,
                ["charm"] = hero?.GetSkillValue(DefaultSkills.Charm) ?? 0,
                ["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? string.Empty
            };
        }

        private static Hero FindSocialBalanceHero(string heroId)
        {
            return string.IsNullOrWhiteSpace(heroId) ? null
                : Hero.FindFirst(x => string.Equals(x?.StringId, heroId, StringComparison.OrdinalIgnoreCase));
        }

        private static Kingdom FindSocialBalanceKingdom(string kingdomId)
        {
            return string.IsNullOrWhiteSpace(kingdomId) ? null
                : Kingdom.All.FirstOrDefault(x => string.Equals(x?.StringId, kingdomId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSocialBalanceAdult(Hero hero)
        {
            if (hero == null || !hero.IsAlive || hero.IsChild) return false;
            float adultAge = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.AgeModel?.HeroComesOfAge ?? 18f;
            return hero.Age >= adultAge && (hero == Hero.MainHero || hero.IsLord || hero.IsNotable || hero.IsWanderer || hero.Clan != null);
        }

        private static float ClampFloat(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
