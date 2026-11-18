using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Platform;
using ReignBeta.UI;
using ReignBeta.UI.ViewModels;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using ReignBeta.Dialogue.UI;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignActionGauntlet
    {
        private static string ExpectedValidationFailure(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            return result != null && result.ResultCode == "validation_failed" ? string.Empty : "Expected terminal validation failure.";
        }

        private static string WarStarted(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "War action failed.";
            }

            Kingdom actor = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Kingdom target = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            return actor != null && target != null && actor.IsAtWarWith(target) ? string.Empty : "Expected kingdoms to be at war.";
        }

        private static string PeaceMade(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Peace action failed.";
            }

            Kingdom actor = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Kingdom target = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            return actor != null && target != null && !actor.IsAtWarWith(target) ? string.Empty : "Expected kingdoms to be at peace.";
        }

        private static string TreatyRecorded(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Treaty action failed.";
            }

            return HasEffect(result, "treaty_recorded") || after.Value<int>("agreementCount") > before.Value<int>("agreementCount")
                ? string.Empty
                : "Expected a treaty/agreement record.";
        }

        private static string TreatyBroken(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Break treaty action failed.";
            }

            return HasEffect(result, "treaty_broken") ? string.Empty : "Expected treaty_broken effect.";
        }

        private static string PrisonerOrTreatyEffect(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Prisoner diplomacy action failed.";
            }

            return HasEffect(result, "prisoners_released") || HasEffect(result, "treaty_recorded") ? string.Empty : "Expected prisoner release or treaty effect.";
        }

        private static string PrisonerOrGoldEffect(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Ransom action failed.";
            }

            return HasEffect(result, "prisoners_released") || HasEffect(result, "gold_transfer") ? string.Empty : "Expected prisoner or gold effect.";
        }

        private static string GoldOrTreatyEffect(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Gold/treaty action failed.";
            }

            return HasEffect(result, "gold_transfer") || HasEffect(result, "treaty_recorded") ? string.Empty : "Expected gold transfer or treaty effect.";
        }

        private static string SettlementTransferredToActor(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Settlement transfer action failed.";
            }

            Settlement settlement = ReignObjectResolver.FindSettlement(action.TargetSettlementStringId);
            Kingdom actor = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            return settlement != null && actor != null && settlement.MapFaction == actor ? string.Empty : "Expected target settlement to transfer to actor kingdom.";
        }

        private static string SettlementTransferredToTarget(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Return settlement action failed.";
            }

            Settlement settlement = ReignObjectResolver.FindSettlement(action.TargetSettlementStringId);
            Kingdom target = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            return settlement != null && target != null && settlement.MapFaction == target ? string.Empty : "Expected target settlement to transfer to target kingdom.";
        }

        private static string FullSurrenderTransferredLand(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Full surrender failed.";
            }

            return HasEffect(result, "settlement_transferred") || after.Value<int>("targetKingdomFortifications") < before.Value<int>("targetKingdomFortifications")
                ? string.Empty
                : "Expected at least one surrendered settlement.";
        }

        private static string WarStartedAgainstThird(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Pay-to-join-war action failed.";
            }

            Kingdom target = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            Kingdom enemy = ReignObjectResolver.FindKingdom(ReadTerm(action.TermsJson, "enemyKingdomId"));
            return target != null && enemy != null && target.IsAtWarWith(enemy) ? string.Empty : "Expected target kingdom to join war against third kingdom.";
        }

        private static string DiplomaticPackageEffect(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Diplomatic package failed.";
            }

            if (!HasEffect(result, "diplomatic_package"))
            {
                return "Expected diplomatic_package effect.";
            }

            return ReignActionReceiptFormatter.ValidatePromisedEffects(action, result, out string reason)
                ? string.Empty
                : reason;
        }

        private static string VerifiedTransactionCompleted(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            return ReignActionReceiptFormatter.ValidatePromisedEffects(action, result, out string reason)
                ? string.Empty
                : reason;
        }

        private static string CapturePlanProgressed(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Capture plan failed.";
            }

            return action.PlanStage != ReignStrategicPlanStage.None && action.PlanStage != ReignStrategicPlanStage.Failed
                ? string.Empty
                : "Expected capture plan stage to advance.";
        }

        private static string ClanKingdomChanged(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Clan kingdom action failed.";
            }

            return before.Value<string>("actionActorClanKingdom") != after.Value<string>("actionActorClanKingdom") || HasEffect(result, "clan_defected")
                ? string.Empty
                : "Expected clan kingdom to change.";
        }

        private static string ClanExiled(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Exile clan action failed.";
            }

            return string.IsNullOrWhiteSpace(after.Value<string>("actionActorClanKingdom")) ? string.Empty : "Expected clan to have no kingdom after exile.";
        }

        private static string ClanRestored(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Restore clan action failed.";
            }

            return after.Value<string>("actionActorClanKingdom") == action.ActorKingdomStringId ? string.Empty : "Expected clan to be restored to actor kingdom.";
        }

        private static string RulingClanChanged(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Ruling clan action failed.";
            }

            return after.Value<string>("actorKingdomRulingClan") == action.ActorClanStringId || HasEffect(result, "ruling_clan_changed")
                ? string.Empty
                : "Expected actor clan to become ruling clan.";
        }

        private static string RebellionStarted(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Rebellion action failed.";
            }

            return HasEffect(result, "rebellion_started") || before.Value<string>("actionActorClanKingdom") != after.Value<string>("actionActorClanKingdom")
                ? string.Empty
                : "Expected rebellion to start or claimant clan kingdom to change.";
        }

        private static bool HasEffect(ReignActionResult result, string effectType)
        {
            return result?.Effects != null && result.Effects.Any(x => x != null
                && x.TryGetValue("effectType", out string value)
                && string.Equals(value, effectType, StringComparison.OrdinalIgnoreCase));
        }

        private static string ReadTerm(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            try
            {
                JObject obj = JObject.Parse(json);
                return obj[key]?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SuccessAny(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            return result != null && result.Success ? string.Empty : result?.Message ?? "No execution result.";
        }

        private static Assertion MovementOrder(string expectedDetail)
        {
            return (c, action, result, before, after) =>
            {
                if (result == null || !result.Success)
                {
                    return result?.Message ?? "Movement action produced no execution result.";
                }

                bool exactEffect = result.Effects != null && result.Effects.Any(effect => effect != null
                    && effect.TryGetValue("effectType", out string effectType)
                    && string.Equals(effectType, "movement_order", StringComparison.OrdinalIgnoreCase)
                    && effect.TryGetValue("detail", out string detail)
                    && string.Equals(detail, expectedDetail, StringComparison.OrdinalIgnoreCase));
                bool changed = result.ChangedEntities != null && result.ChangedEntities.Any(change => change != null
                    && change.TryGetValue("changeType", out string changeType)
                    && string.Equals(changeType, "movement_order_changed", StringComparison.OrdinalIgnoreCase));
                return exactEffect && changed ? string.Empty : "Expected native movement effect " + expectedDetail + " and movement_order_changed.";
            };
        }

        private static Assertion NativeChange(string expectedChangeType)
        {
            return (c, action, result, before, after) =>
            {
                if (result == null || !result.Success)
                {
                    return result?.Message ?? "Native action produced no execution result.";
                }

                bool changed = result.ChangedEntities != null && result.ChangedEntities.Any(change => change != null
                    && change.TryGetValue("changeType", out string changeType)
                    && string.Equals(changeType, expectedChangeType, StringComparison.OrdinalIgnoreCase));
                return changed ? string.Empty : "Expected verified native change " + expectedChangeType + ", but only an intent/pending result was produced.";
            };
        }

        private static Assertion NativeChangeOrEffect(string expectedType)
        {
            return (c, action, result, before, after) =>
            {
                if (result == null || !result.Success)
                {
                    return result?.Message ?? "Native action produced no execution result.";
                }

                bool changed = result.ChangedEntities != null && result.ChangedEntities.Any(change => change != null
                    && change.TryGetValue("changeType", out string changeType)
                    && string.Equals(changeType, expectedType, StringComparison.OrdinalIgnoreCase));
                bool effect = result.Effects != null && result.Effects.Any(row => row != null
                    && row.TryGetValue("effectType", out string effectType)
                    && string.Equals(effectType, expectedType, StringComparison.OrdinalIgnoreCase));
                return changed || effect ? string.Empty : "Expected verified native change/effect " + expectedType + ".";
            };
        }

        private static string GoldChanged(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Gold action failed.";
            }

            return (int)before["playerGold"] != (int)after["playerGold"] || (int)before["actorGold"] != (int)after["actorGold"]
                ? string.Empty
                : "Expected hero gold to change.";
        }

        private static string ItemChanged(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Item action failed.";
            }

            return (int)after["targetGrain"] > (int)before["targetGrain"] ? string.Empty : "Expected target party grain to increase.";
        }

        private static string WorkshopChanged(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Workshop action failed.";
            }

            return string.Equals((string)after["workshopOwner"], Hero.MainHero.StringId, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : "Expected workshop owner to become player.";
        }

        private static string PrisonerReleased(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Prisoner action failed.";
            }

            return after.Value<bool>("prisonerIsPrisoner") ? "Expected prisoner to be released." : string.Empty;
        }

        private static string HeroKilled(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Kill action failed.";
            }

            return after.Value<bool>("targetDead") ? string.Empty : "Expected target hero to be dead.";
        }

        private static string CreatePartyAssert(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Create party failed.";
            }

            return after.Value<bool>("actorHasParty") ? string.Empty : "Expected actor to have a party or an explicit no-op result.";
        }

        private static string PlayerMercenaryStarted(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Mercenary start failed.";
            }

            return Clan.PlayerClan != null && Clan.PlayerClan.IsUnderMercenaryService ? string.Empty : "Expected player clan to enter mercenary service.";
        }

        private static string PlayerMercenaryEnded(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Mercenary dismissal failed.";
            }

            return Clan.PlayerClan != null && !Clan.PlayerClan.IsUnderMercenaryService ? string.Empty : "Expected player clan to leave mercenary service.";
        }

        private static string PlayerVassalStarted(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Vassalage start failed.";
            }

            return Clan.PlayerClan?.Kingdom != null && !Clan.PlayerClan.IsUnderMercenaryService ? string.Empty : "Expected player clan to become a vassal.";
        }

        private static string PlayerVassalEnded(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Vassalage dismissal failed.";
            }

            return Clan.PlayerClan?.Kingdom == null ? string.Empty : "Expected player clan to leave kingdom.";
        }

        private static string ClanJoinedKingdom(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Join kingdom failed.";
            }

            Clan clan = ReignObjectResolver.FindClan(action.ActorClanStringId);
            Kingdom kingdom = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            if (clan != null && kingdom != null && clan.Kingdom == kingdom)
            {
                c.LastJoinedClan = clan;
                return string.Empty;
            }

            return "Expected clan to join target kingdom.";
        }

        private static string ClanLeftKingdom(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Leave kingdom failed.";
            }

            Clan clan = ReignObjectResolver.FindClan(action.ActorClanStringId);
            return clan != null && clan.Kingdom == null ? string.Empty : "Expected clan to leave kingdom.";
        }

        private static string ClanMercenaryStarted(GauntletContext c, ReignWorldActionRecord action, ReignActionResult result, JObject before, JObject after)
        {
            if (result == null || !result.Success)
            {
                return result?.Message ?? "Hire mercenary clan failed.";
            }

            Clan clan = ReignObjectResolver.FindClan(action.TargetClanStringId) ?? ReignObjectResolver.FindClan(action.ActorClanStringId);
            return clan != null && clan.Kingdom != null ? string.Empty : "Expected clan to enter kingdom service.";
        }

        private static GauntletContext BuildContext(string correlationId)
        {
            Settlement arena = FindArenaSettlement();
            Hero actor = FindActiveLord(null, null);
            Hero target = FindActiveLord(actor, null);
            Hero partyless = FindPartylessLord();
            Hero clanTransfer = FindClanTransferHero(actor, target, partyless);
            Settlement hostileFort = FindHostileFortification(actor) ?? FindAnyFortificationNotOwnedBy(actor?.PartyBelongedTo?.MapFaction);
            Settlement hostileVillage = FindHostileVillage(actor) ?? FindAnyVillageNotOwnedBy(actor?.PartyBelongedTo?.MapFaction);
            return new GauntletContext
            {
                CorrelationId = correlationId,
                ArenaSettlement = arena,
                ActorHero = actor,
                TargetHero = target,
                PartylessHero = partyless,
                SurrenderHero = FindSurrenderHero(actor, target),
                ClanTransferHero = clanTransfer,
                PrisonerHero = FindPrisonerCandidate(actor, target),
                KillTarget = FindKillTarget(actor, target),
                HostileFortification = hostileFort,
                HostileVillage = hostileVillage,
                Workshop = FindWorkshop(),
                TargetKingdom = FindTargetKingdom(),
                TargetClan = FindTargetClan(clanTransfer?.Clan ?? actor?.Clan),
                JoinClan = FindJoinClan(),
                MercenaryClan = FindMercenaryClan()
            };
        }

        private static ReignWorldActionRecord Action(
            ReignWorldActionType type,
            GauntletContext context,
            string label,
            Hero actor = null,
            Hero target = null,
            Settlement settlement = null,
            JObject terms = null,
            Clan actorClan = null,
            Clan targetClan = null,
            Kingdom targetKingdom = null)
        {
            return new ReignWorldActionRecord
            {
                Type = type,
                Source = "gauntlet:" + context.CorrelationId,
                ActorHeroStringId = actor?.StringId ?? string.Empty,
                TargetHeroStringId = target?.StringId ?? string.Empty,
                ActorClanStringId = actorClan?.StringId ?? actor?.Clan?.StringId ?? string.Empty,
                TargetClanStringId = targetClan?.StringId ?? target?.Clan?.StringId ?? string.Empty,
                ActorKingdomStringId = actor?.Clan?.Kingdom?.StringId ?? actorClan?.Kingdom?.StringId ?? string.Empty,
                TargetKingdomStringId = targetKingdom?.StringId ?? target?.Clan?.Kingdom?.StringId ?? string.Empty,
                TargetSettlementStringId = settlement?.StringId ?? string.Empty,
                TermsJson = terms == null ? string.Empty : terms.ToString(Formatting.None),
                Reason = "ReignBeta Action Gauntlet: " + label,
                RequiresAcceptance = false,
                MaxAttempts = 1,
                MinimumTroops = 0
            };
        }

        private static Hero ResolveSnapshotHero(string heroId, params Hero[] candidates)
        {
            if (!string.IsNullOrWhiteSpace(heroId))
            {
                Hero found = ReignObjectResolver.FindHero(heroId);
                if (found != null)
                {
                    return found;
                }

                foreach (Hero candidate in candidates)
                {
                    if (candidate != null && string.Equals(candidate.StringId, heroId, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }

                return null;
            }

            foreach (Hero candidate in candidates)
            {
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static JObject Snapshot(GauntletContext c, ReignWorldActionRecord action)
        {
            Hero actor = ResolveSnapshotHero(action.ActorHeroStringId, c.ActorHero, Hero.MainHero, c.TargetHero, c.KillTarget, c.PrisonerHero);
            Hero target = ResolveSnapshotHero(action.TargetHeroStringId, c.TargetHero, c.KillTarget, c.PrisonerHero, Hero.MainHero);
            MobileParty actorParty = actor?.PartyBelongedTo;
            MobileParty targetParty = target?.PartyBelongedTo;
            Workshop workshop = c.Workshop;
            Hero prisoner = c.PrisonerHero;
            Clan actorActionClan = ReignObjectResolver.FindClan(action.ActorClanStringId);
            Clan targetActionClan = ReignObjectResolver.FindClan(action.TargetClanStringId);
            Kingdom actorKingdom = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Kingdom targetKingdom = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            Settlement targetSettlement = ReignObjectResolver.FindSettlement(action.TargetSettlementStringId);

            return new JObject
            {
                ["actorHeroId"] = actor?.StringId ?? string.Empty,
                ["targetHeroId"] = target?.StringId ?? string.Empty,
                ["targetHeroName"] = target?.Name?.ToString() ?? string.Empty,
                ["playerGold"] = Hero.MainHero?.Gold ?? 0,
                ["actorGold"] = actor?.Gold ?? 0,
                ["targetGold"] = target?.Gold ?? 0,
                ["actorPartyId"] = actorParty?.StringId ?? string.Empty,
                ["actorHasParty"] = actorParty != null,
                ["actorPosition"] = actorParty == null ? string.Empty : actorParty.GetPosition2D.ToString(),
                ["targetPartyId"] = targetParty?.StringId ?? string.Empty,
                ["actorGrain"] = actorParty == null || DefaultItems.Grain == null ? 0 : actorParty.ItemRoster.GetItemNumber(DefaultItems.Grain),
                ["targetGrain"] = targetParty == null || DefaultItems.Grain == null ? 0 : targetParty.ItemRoster.GetItemNumber(DefaultItems.Grain),
                ["workshopOwner"] = workshop?.Owner?.StringId ?? string.Empty,
                ["prisonerHero"] = prisoner?.StringId ?? string.Empty,
                ["prisonerIsPrisoner"] = prisoner != null && prisoner.IsPrisoner,
                ["targetDead"] = target != null && target.IsDead,
                ["playerClanKingdom"] = Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty,
                ["playerClanMercenary"] = Clan.PlayerClan != null && Clan.PlayerClan.IsUnderMercenaryService,
                ["actorClanKingdom"] = actor?.Clan?.Kingdom?.StringId ?? string.Empty,
                ["actionActorClanId"] = actorActionClan?.StringId ?? string.Empty,
                ["actionActorClanKingdom"] = actorActionClan?.Kingdom?.StringId ?? string.Empty,
                ["actionActorClanMercenary"] = actorActionClan != null && actorActionClan.IsUnderMercenaryService,
                ["actionTargetClanId"] = targetActionClan?.StringId ?? string.Empty,
                ["actionTargetClanKingdom"] = targetActionClan?.Kingdom?.StringId ?? string.Empty,
                ["actionTargetClanMercenary"] = targetActionClan != null && targetActionClan.IsUnderMercenaryService,
                ["actorKingdomId"] = actorKingdom?.StringId ?? string.Empty,
                ["targetKingdomId"] = targetKingdom?.StringId ?? string.Empty,
                ["actorKingdomAtWarWithTarget"] = actorKingdom != null && targetKingdom != null && actorKingdom.IsAtWarWith(targetKingdom),
                ["actorKingdomRulingClan"] = actorKingdom?.RulingClan?.StringId ?? string.Empty,
                ["targetKingdomRulingClan"] = targetKingdom?.RulingClan?.StringId ?? string.Empty,
                ["actorKingdomEliminated"] = actorKingdom != null && actorKingdom.IsEliminated,
                ["targetKingdomEliminated"] = targetKingdom != null && targetKingdom.IsEliminated,
                ["actorLeaderGold"] = actorKingdom?.Leader?.Gold ?? 0,
                ["targetLeaderGold"] = targetKingdom?.Leader?.Gold ?? 0,
                ["targetSettlementId"] = targetSettlement?.StringId ?? string.Empty,
                ["targetSettlementOwnerKingdom"] = targetSettlement?.MapFaction?.StringId ?? string.Empty,
                ["targetKingdomFortifications"] = targetKingdom == null ? 0 : Settlement.All.Count(x => x != null && x.IsFortification && x.MapFaction == targetKingdom),
                ["agreementCount"] = CountActiveAgreements(action)
            };
        }

        private static int CountActiveAgreements(ReignWorldActionRecord action)
        {
            IReadOnlyList<ReignDiplomaticAgreementRecord> agreements = ReignServices.AI?.Agreements;
            if (agreements == null || action == null)
            {
                return 0;
            }

            return agreements.Count(x => x != null
                && x.IsActive
                && SamePair(x.ActorKingdomStringId, x.TargetKingdomStringId, action.ActorKingdomStringId, action.TargetKingdomStringId)
                && SameOptional(x.ActorClanStringId, action.ActorClanStringId)
                && SameOptional(x.TargetClanStringId, action.TargetClanStringId)
                && SameOptional(x.TargetHeroStringId, action.TargetHeroStringId)
                && SameOptional(x.TargetSettlementStringId, action.TargetSettlementStringId));
        }

        private static bool SamePair(string a1, string a2, string b1, string b2)
        {
            return (string.Equals(a1 ?? string.Empty, b1 ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a2 ?? string.Empty, b2 ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                || (string.Equals(a1 ?? string.Empty, b2 ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a2 ?? string.Empty, b1 ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }

        private static bool SameOptional(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
            {
                return true;
            }

            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsurePlayerResources(List<string> notes)
        {
            Hero player = Hero.MainHero;
            if (player == null || MobileParty.MainParty == null)
            {
                return;
            }

            EnsureHeroGold(player, 250000);
            notes?.Add("player gold ensured");

            CharacterObject cataphract = MBObjectManager.Instance.GetObject<CharacterObject>("imperial_elite_cataphract");
            if (cataphract != null && MobileParty.MainParty.MemberRoster.TotalManCount < 150)
            {
                MobileParty.MainParty.MemberRoster.AddToCounts(cataphract, 150);
                notes?.Add("player troops ensured");
            }

            if (DefaultItems.Grain != null && MobileParty.MainParty.ItemRoster.GetItemNumber(DefaultItems.Grain) < 300)
            {
                MobileParty.MainParty.ItemRoster.AddToCounts(DefaultItems.Grain, 300);
                notes?.Add("player grain ensured");
            }

            ItemObject horse = FindTradeHorseItem();
            if (horse != null && MobileParty.MainParty.ItemRoster.GetItemNumber(horse) < 20)
            {
                MobileParty.MainParty.ItemRoster.AddToCounts(horse, 20);
                notes?.Add("player horses ensured");
            }
        }

        private static void EnsureActorResources(Hero hero, List<string> notes)
        {
            if (hero == null)
            {
                return;
            }

            EnsureHeroGold(hero, 50000);
            MobileParty party = hero.PartyBelongedTo;
            if (party != null)
            {
                CharacterObject cataphract = MBObjectManager.Instance.GetObject<CharacterObject>("imperial_elite_cataphract");
                if (cataphract != null && party.MemberRoster.TotalManCount < 40)
                {
                    party.MemberRoster.AddToCounts(cataphract, 40);
                }

                if (DefaultItems.Grain != null && party.ItemRoster.GetItemNumber(DefaultItems.Grain) < 50)
                {
                    party.ItemRoster.AddToCounts(DefaultItems.Grain, 50);
                }

                ItemObject horse = FindTradeHorseItem();
                if (horse != null && party.ItemRoster.GetItemNumber(horse) < 10)
                {
                    party.ItemRoster.AddToCounts(horse, 10);
                }
            }

            notes?.Add(hero.Name + " resources ensured");
        }

        private static ItemObject FindTradeHorseItem()
        {
            return MBObjectManager.Instance.GetObject<ItemObject>("imperial_charger")
                ?? MBObjectManager.Instance.GetObject<ItemObject>("sumpter_horse")
                ?? MBObjectManager.Instance.GetObjectTypeList<ItemObject>().FirstOrDefault(x => x != null && x.HorseComponent != null);
        }

        private static void EnsureHeroGold(Hero hero, int minimumGold)
        {
            if (hero != null && hero.Gold < minimumGold)
            {
                GiveGoldAction.ApplyBetweenCharacters(null, hero, minimumGold - hero.Gold, false);
            }
        }

        private static void EnsureWar(IFaction left, IFaction right, List<string> notes)
        {
            if (left == null || right == null || left == right || left.IsAtWarWith(right))
            {
                return;
            }

            DeclareWarAction.ApplyByDefault(left, right);
            notes?.Add("war declared between " + left.Name + " and " + right.Name);
        }

        private static void TeleportParty(MobileParty party, Settlement settlement)
        {
            if (party == null || settlement == null)
            {
                return;
            }

            party.SetPositionAfterMapChange(settlement.GatePosition);
            party.SetMoveModeHold();
        }

        private static Settlement FindArenaSettlement()
        {
            return Settlement.All.FirstOrDefault(x => x != null && x.IsTown && string.Equals(x.StringId, "town_EW2", StringComparison.OrdinalIgnoreCase))
                ?? Settlement.All.FirstOrDefault(x => x != null && x.IsTown && x.Name != null && x.Name.ToString().IndexOf("Zeonica", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? Settlement.All.FirstOrDefault(x => x != null && x.IsTown);
        }

        private static Hero FindActiveLord(Hero except, IFaction faction)
        {
            IEnumerable<Hero> heroes = Hero.AllAliveHeroes.Where(x => x != null
                && x != Hero.MainHero
                && x != except
                && x.IsLord
                && !x.IsPrisoner
                && x.PartyBelongedTo != null
                && !x.PartyBelongedTo.IsMainParty
                && x.PartyBelongedTo.IsActive
                && x.PartyBelongedTo.MapEvent == null
                && x.PartyBelongedTo.BesiegedSettlement == null);

            if (faction != null)
            {
                heroes = heroes.Where(x => x.PartyBelongedTo.MapFaction == faction);
            }

            return heroes.OrderByDescending(SafeActiveLordScore).FirstOrDefault();
        }

        private static float SafeActiveLordScore(Hero hero)
        {
            try
            {
                MobileParty party = hero?.PartyBelongedTo;
                if (party == null)
                {
                    return 0f;
                }

                return party.MemberRoster?.TotalManCount ?? 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static Hero FindPartylessLord()
        {
            return Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                && x != Hero.MainHero
                && x.IsLord
                && !x.IsPrisoner
                && x.PartyBelongedTo == null
                && x.Clan != null
                && !x.Clan.IsEliminated);
        }

        private static Hero FindSurrenderHero(params Hero[] excluded)
        {
            IFaction playerFaction = MobileParty.MainParty?.MapFaction;
            return Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                && x != Hero.MainHero
                && !excluded.Contains(x)
                && !x.IsPrisoner
                && x.PartyBelongedTo != null
                && x.PartyBelongedTo.IsActive
                && !x.PartyBelongedTo.IsMainParty
                && x.PartyBelongedTo.LeaderHero == x
                && x.PartyBelongedTo.MapEvent == null
                && x.PartyBelongedTo.BesiegedSettlement == null
                && playerFaction != null
                && x.PartyBelongedTo.MapFaction != null
                && x.PartyBelongedTo.MapFaction != playerFaction
                && playerFaction.IsAtWarWith(x.PartyBelongedTo.MapFaction));
        }

        private static Hero FindClanTransferHero(params Hero[] excluded)
        {
            return Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                && x != Hero.MainHero
                && !excluded.Contains(x)
                && !x.IsPrisoner
                && x.CompanionOf == null
                && x.Clan != null
                && !x.Clan.IsEliminated
                && x.Clan.Leader != x
                && x.PartyBelongedTo == null
                && x.GovernorOf == null);
        }

        private static Hero FindPrisonerCandidate(Hero actor, Hero target)
        {
            return Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                && x != Hero.MainHero
                && x != actor
                && x != target
                && !x.IsDead
                && !x.IsPrisoner
                && x.IsLord
                && x.Clan?.Leader != x)
                ?? Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                    && x != Hero.MainHero
                    && x != actor
                    && x != target
                    && !x.IsDead
                    && !x.IsPrisoner
                    && x.IsWanderer
                    && !x.IsPlayerCompanion);
        }

        private static Hero FindKillTarget(Hero actor, Hero target)
        {
            return Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                && x != Hero.MainHero
                && x != actor
                && x != target
                && !x.IsDead
                && !x.IsPrisoner
                && x.IsWanderer
                && !x.IsPlayerCompanion)
                ?? Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                    && x != Hero.MainHero
                    && x != actor
                    && x != target
                    && !x.IsDead
                    && !x.IsPrisoner
                    && x.IsLord
                    && x.Clan?.Leader != x
                    && x.PartyBelongedTo == null);
        }

        private static Settlement FindHostileFortification(Hero actor)
        {
            IFaction faction = actor?.PartyBelongedTo?.MapFaction;
            return Settlement.All.FirstOrDefault(x => x != null
                && x.IsFortification
                && x.MapFaction != null
                && faction != null
                && faction.IsAtWarWith(x.MapFaction)
                && !x.IsUnderSiege);
        }

        private static Settlement FindHostileVillage(Hero actor)
        {
            IFaction faction = actor?.PartyBelongedTo?.MapFaction;
            return Settlement.All.FirstOrDefault(x => x != null
                && x.IsVillage
                && x.MapFaction != null
                && faction != null
                && faction.IsAtWarWith(x.MapFaction));
        }

        private static Settlement FindAnyFortificationNotOwnedBy(IFaction faction)
        {
            return Settlement.All.FirstOrDefault(x => x != null && x.IsFortification && !x.IsUnderSiege && x.MapFaction != null && x.MapFaction != faction);
        }

        private static Settlement FindAnyVillageNotOwnedBy(IFaction faction)
        {
            return Settlement.All.FirstOrDefault(x => x != null && x.IsVillage && x.MapFaction != null && x.MapFaction != faction);
        }

        private static Workshop FindWorkshop()
        {
            foreach (Town town in Town.AllTowns.Where(x => x != null))
            {
                Workshop workshop = town.Workshops?.FirstOrDefault(x => x != null && x.Owner != null && x.WorkshopType != null);
                if (workshop != null)
                {
                    return workshop;
                }
            }

            return null;
        }

        private static Kingdom FindTargetKingdom()
        {
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            return Kingdom.All.FirstOrDefault(x => x != null && !x.IsEliminated && x != playerKingdom)
                ?? Kingdom.All.FirstOrDefault(x => x != null && !x.IsEliminated);
        }

        private static Clan FindTargetClan(Clan except)
        {
            return Clan.All.FirstOrDefault(x => x != null && x != except && !x.IsEliminated && !x.IsClanTypeMercenary && x.Leader != null);
        }

        private static Clan FindJoinClan()
        {
            Kingdom target = FindTargetKingdom();
            return Clan.All.FirstOrDefault(x => x != null
                && x != Clan.PlayerClan
                && !x.IsEliminated
                && !x.IsClanTypeMercenary
                && x.Kingdom != target
                && x.Leader != null)
                ?? Clan.All.FirstOrDefault(x => x != null && x != Clan.PlayerClan && !x.IsEliminated && x.Kingdom != null && x.Leader != null);
        }

        private static Clan FindMercenaryClan()
        {
            return Clan.All.FirstOrDefault(x => x != null && x != Clan.PlayerClan && !x.IsEliminated && x.IsClanTypeMercenary && x.Leader != null);
        }
    }
}
