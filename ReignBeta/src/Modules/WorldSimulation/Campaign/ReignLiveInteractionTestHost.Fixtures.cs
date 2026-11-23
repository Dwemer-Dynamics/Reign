using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Court;
using ReignBeta.Events;
using ReignBeta.PartyAgency;
using ReignBeta.UI;
using ReignBeta.UI.ViewModels;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Dialogue.UI;
using ReignBeta.Diplomacy.Campaign;
using ReignBeta.Diplomacy.UI;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.Reputation.Campaign;
using ReignBeta.WorldSimulation.Integration;
using ReignBeta.WorldSimulation.PartyAgency;
using ReignBeta.WorldSimulation.Runtime;
using ReignBeta.WorldSimulation.UI;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
using ReignBeta.Court.UI;
#endif
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static async Task<LiveCommandResult> PreparePartyFixtureAsync(JObject command)
        {
            if (_individualVm != null || _partyVm != null || _eventVm != null)
                await CloseSessionAsync(false).ConfigureAwait(false);

            int desiredCount = Math.Max(2, Math.Min(
                5, command.Value<int?>("desiredCount") ?? 5));
            List<string> searches = ReadSearches(command);
            JObject result = await ReignMainThread.InvokeAsync(() =>
            {
                MobileParty party = MobileParty.MainParty;
                if (party == null)
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = "The main party is unavailable."
                    };

                List<Hero> globalPool = Hero.AllAliveHeroes
                    .Where(hero => IsLivingNpc(hero)
                        && !hero.IsPrisoner
                        && !hero.IsWounded)
                    .GroupBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
                List<Hero> requested = new List<Hero>();
                foreach (string search in searches)
                {
                    Hero match = RankHeroes(
                        globalPool.Where(hero => requested.All(existing =>
                            !string.Equals(existing.StringId, hero.StringId,
                                StringComparison.OrdinalIgnoreCase))),
                        search).FirstOrDefault();
                    if (match != null) requested.Add(match);
                }
                if (requested.Count == 0)
                {
                    requested = globalPool
                        .OrderBy(hero => hero.IsLord ? 1 : 0)
                        .ThenBy(hero => hero.IsWanderer ? 0 : 1)
                        .ThenBy(hero => hero.Name?.ToString() ?? hero.StringId,
                            StringComparer.OrdinalIgnoreCase)
                        .Take(12)
                        .ToList();
                }

                JArray added = new JArray();
                JArray rejected = new JArray();
                foreach (Hero hero in requested)
                {
                    if (ReignPartyChatSession.GetAvailableConversationHeroes().Count
                        >= desiredCount)
                        break;
                    if (hero.PartyBelongedTo == party)
                    {
                        added.Add(new JObject
                        {
                            ["heroId"] = hero.StringId,
                            ["name"] = hero.Name?.ToString() ?? hero.StringId,
                            ["status"] = "already_present"
                        });
                        continue;
                    }

                    try
                    {
                        string originSettlementId =
                            hero.CurrentSettlement?.StringId ?? string.Empty;
                        AddHeroToPartyAction.Apply(hero, party, false);
                        bool present = hero.PartyBelongedTo == party
                            || party.MemberRoster.GetTroopCount(
                                hero.CharacterObject) > 0;
                        if (!present)
                            throw new InvalidOperationException(
                                "Bannerlord did not place the hero in the main party.");
                        added.Add(new JObject
                        {
                            ["heroId"] = hero.StringId,
                            ["name"] = hero.Name?.ToString() ?? hero.StringId,
                            ["originSettlementId"] = originSettlementId,
                            ["status"] = "added"
                        });
                    }
                    catch (Exception ex)
                    {
                        rejected.Add(new JObject
                        {
                            ["heroId"] = hero.StringId,
                            ["name"] = hero.Name?.ToString() ?? hero.StringId,
                            ["error"] = ex.Message
                        });
                    }
                }

                List<Hero> eligible =
                    ReignPartyChatSession.GetAvailableConversationHeroes();
                return new JObject
                {
                    ["ok"] = eligible.Count >= desiredCount,
                    ["desiredCount"] = desiredCount,
                    ["eligibleCount"] = eligible.Count,
                    ["eligibleHeroIds"] = new JArray(
                        eligible.Select(hero => hero.StringId).Take(desiredCount)),
                    ["added"] = added,
                    ["rejected"] = rejected,
                    ["disposableSaveFixture"] = true
                };
            }).ConfigureAwait(false);

            return result.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed(
                    "The disposable qualification party is ready through Bannerlord's native party roster.",
                    result)
                : LiveCommandResult.Failed(
                    "Five party-chat-eligible heroes could not be prepared from the supplied fixture candidates.",
                    result);
        }

        private static async Task<LiveCommandResult> PrepareAuthorityFixtureAsync(
            JObject command)
        {
            if (command?.Value<bool?>("confirmDisposableCampaign") != true)
                return LiveCommandResult.Failed(
                    "prepare_authority_fixture requires confirmDisposableCampaign=true because native kingdom, clan, family, settlement, and party state are changed until the baseline save is reloaded.");
            if (_individualVm != null || _partyVm != null || _eventVm != null)
                await CloseSessionAsync(false).ConfigureAwait(false);

            JObject result = await ReignMainThread.InvokeAsync(() =>
            {
                try
                {
                    ReignBetaDebugActions.SetupPlayerKingdomTestScenario();
                    Hero player = Hero.MainHero;
                    Clan playerClan = Clan.PlayerClan;
                    Kingdom kingdom = playerClan?.Kingdom;
                    MobileParty party = MobileParty.MainParty;
                    Settlement seat = Settlement.CurrentSettlement
                        ?? party?.CurrentSettlement;
                    if (player == null || playerClan == null
                        || kingdom == null || party == null || seat == null
                        || seat.Town == null)
                        return new JObject
                        {
                            ["ok"] = false,
                            ["error"] =
                                "The native royal-court setup did not produce a player kingdom, main party, and occupied fortification."
                        };
                    if (kingdom.RulingClan != playerClan
                        || kingdom.Leader != player
                        || seat.OwnerClan != playerClan)
                        return new JObject
                        {
                            ["ok"] = false,
                            ["error"] =
                                "The native royal-court setup did not make the player the ruler and personal owner of the occupied fortification."
                        };

                    List<Hero> independentWanderers = Hero.AllAliveHeroes
                        .Where(hero => IsLivingNpc(hero)
                            && hero.IsWanderer
                            && !hero.IsPrisoner
                            && !hero.IsWounded
                            && hero.Clan != playerClan
                            && hero.CompanionOf != playerClan)
                        .OrderBy(hero =>
                            hero.PartyBelongedTo == null ? 0 : 1)
                        .ThenBy(hero =>
                            hero.CurrentSettlement == seat ? 0 : 1)
                        .ThenBy(hero =>
                            hero.Name?.ToString() ?? hero.StringId,
                            StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (independentWanderers.Count < 3)
                        return new JObject
                        {
                            ["ok"] = false,
                            ["error"] =
                                "Three adult independent wanderers are required for separate same-clan, immediate-family, and governor witnesses."
                        };

                    Hero clanWitness = independentWanderers[0];
                    Hero familyWitness = independentWanderers[1];
                    Hero governorWitness = independentWanderers[2];
                    clanWitness.Clan = playerClan;
                    AdoptHeroAction.Apply(familyWitness);
                    governorWitness.Clan = playerClan;

                    EnsureAuthorityFixturePartyMember(clanWitness, party);
                    EnsureAuthorityFixturePartyMember(familyWitness, party);

                    if (governorWitness.PartyBelongedTo != null)
                    {
                        governorWitness.PartyBelongedTo.MemberRoster
                            .AddToCounts(
                                governorWitness.CharacterObject, -1);
                    }
                    if (governorWitness.CurrentSettlement != seat)
                        EnterSettlementAction.ApplyForCharacterOnly(
                            governorWitness, seat);
                    ChangeGovernorAction.Apply(
                        seat.Town, governorWitness);

                    List<Hero> vassalWitnesses = kingdom.Clans
                        .Where(clan => clan != null
                            && clan != playerClan
                            && !clan.IsEliminated
                            && !clan.IsClanTypeMercenary
                            && !clan.IsUnderMercenaryService
                            && clan.Leader != null
                            && IsLivingNpc(clan.Leader)
                            && !clan.Leader.IsPrisoner)
                        .Select(clan => clan.Leader)
                        .Distinct()
                        .OrderBy(hero =>
                            hero.Name?.ToString() ?? hero.StringId,
                            StringComparer.OrdinalIgnoreCase)
                        .Take(2)
                        .ToList();
                    foreach (Hero witness in vassalWitnesses)
                        EnsureAuthorityFixturePartyMember(witness, party);

                    List<Kingdom> enemyKingdoms =
                        (kingdom.FactionsAtWarWith
                            ?? Enumerable.Empty<IFaction>())
                        .Where(faction =>
                            faction != null
                            && faction.IsKingdomFaction)
                        .Select(faction => faction as Kingdom)
                        .Where(enemy =>
                            enemy != null && enemy != kingdom
                            && !enemy.IsEliminated)
                        .GroupBy(enemy => enemy.StringId,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.First())
                        .OrderBy(enemy => enemy.StringId,
                            StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    List<Hero> partyWitnesses = new[]
                        {
                            familyWitness, clanWitness
                        }
                        .Concat(vassalWitnesses)
                        .Where(hero =>
                            hero?.PartyBelongedTo == party)
                        .Distinct()
                        .Take(5)
                        .ToList();

                    bool ready = familyWitness.Clan == playerClan
                        && (familyWitness.Father == player
                            || familyWitness.Mother == player)
                        && clanWitness.Clan == playerClan
                        && clanWitness.Father != player
                        && clanWitness.Mother != player
                        && seat.Town.Governor == governorWitness
                        && governorWitness.GovernorOf == seat.Town
                        && vassalWitnesses.Count >= 2
                        && partyWitnesses.Count >= 4;
                    return new JObject
                    {
                        ["ok"] = ready,
                        ["error"] = ready
                            ? string.Empty
                            : "One or more native authority-fixture invariants did not hold after setup.",
                        ["disposableSaveFixture"] = true,
                        ["requiresBaselineReload"] = true,
                        ["playerHeroId"] = player.StringId,
                        ["playerName"] =
                            player.Name?.ToString() ?? string.Empty,
                        ["playerClanId"] = playerClan.StringId,
                        ["playerClanTier"] = playerClan.Tier,
                        ["playerKingdomId"] = kingdom.StringId,
                        ["playerKingdomName"] =
                            kingdom.InformalName?.ToString()
                            ?? kingdom.Name?.ToString()
                            ?? string.Empty,
                        ["playerIsRuler"] =
                            kingdom.RulingClan == playerClan
                            && kingdom.Leader == player,
                        ["settlementId"] = seat.StringId,
                        ["settlementName"] =
                            seat.Name?.ToString() ?? string.Empty,
                        ["settlementOwnerClanId"] =
                            seat.OwnerClan?.StringId ?? string.Empty,
                        ["settlementOwnerClanName"] =
                            seat.OwnerClan?.Name?.ToString()
                            ?? string.Empty,
                        ["settlementOwnerHeroId"] =
                            seat.OwnerClan?.Leader?.StringId
                            ?? string.Empty,
                        ["settlementGovernorHeroId"] =
                            seat.Town.Governor?.StringId
                            ?? string.Empty,
                        ["settlementGovernorName"] =
                            seat.Town.Governor?.Name?.ToString()
                            ?? string.Empty,
                        ["familyObserver"] =
                            HeroTargetJson(familyWitness),
                        ["clanObserver"] =
                            HeroTargetJson(clanWitness),
                        ["governorObserver"] =
                            HeroTargetJson(governorWitness),
                        ["vassalObservers"] = new JArray(
                            vassalWitnesses.Select(
                                HeroTargetJson)),
                        ["partyObserverIds"] = new JArray(
                            partyWitnesses.Select(
                                hero => hero.StringId)),
                        ["currentEnemyKingdomCount"] =
                            enemyKingdoms.Count,
                        ["enemyKingdomIds"] = new JArray(
                            enemyKingdoms.Select(
                                enemy => enemy.StringId)),
                        ["nativeInvariant"] = new JObject
                        {
                            ["sameClanWitness"] =
                                clanWitness.Clan == playerClan,
                            ["immediateFamilyWitness"] =
                                familyWitness.Clan == playerClan
                                && (familyWitness.Father == player
                                    || familyWitness.Mother == player),
                            ["localGovernorWitness"] =
                                seat.Town.Governor
                                    == governorWitness,
                            ["subjectVassalWitnessCount"] =
                                vassalWitnesses.Count,
                            ["playerOwnsSettlement"] =
                                seat.OwnerClan == playerClan,
                            ["playerGovernsSettlement"] =
                                seat.Town.Governor == player,
                            ["playerRulesKingdom"] =
                                kingdom.RulingClan == playerClan
                                && kingdom.Leader == player
                        }
                    };
                }
                catch (Exception ex)
                {
                    ReignLog.Exception(
                        "Live authority fixture setup", ex);
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = ex.Message,
                        ["requiresBaselineReload"] = true
                    };
                }
            }).ConfigureAwait(false);

            return result.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed(
                    "The disposable sovereign, settlement, diplomacy, same-clan, and immediate-family authority fixture is ready.",
                    result)
                : LiveCommandResult.Failed(
                    result.Value<string>("error")
                        ?? "Authority fixture setup failed.",
                    result);
        }

        private static void EnsureAuthorityFixturePartyMember(
            Hero hero,
            MobileParty party)
        {
            if (hero == null || party == null
                || hero.PartyBelongedTo == party)
                return;
            AddHeroToPartyAction.Apply(
                hero, party, false);
            if (hero.PartyBelongedTo != party
                && party.MemberRoster.GetTroopCount(
                    hero.CharacterObject) <= 0)
                throw new InvalidOperationException(
                    "Bannerlord did not place authority witness "
                    + (hero.Name?.ToString()
                        ?? hero.StringId)
                    + " in the main party.");
        }

        private static async Task<LiveCommandResult> PrepareWildernessAsync(JObject command)
        {
            if (_individualVm != null || _partyVm != null || _eventVm != null)
                await CloseSessionAsync(false).ConfigureAwait(false);
            JObject result = await ReignMainThread.InvokeAsync(() =>
            {
                MobileParty party = MobileParty.MainParty;
                Settlement origin = Settlement.CurrentSettlement ?? party?.CurrentSettlement;
                if (party == null || origin == null)
                    return new JObject { ["ok"] = false, ["error"] = "The main party must be inside a settlement before wilderness setup." };
                List<Hero> requested = ResolveHeroesForMode("party_chat", ReadSearches(command))
                    .Where(hero => hero != null && hero != Hero.MainHero && hero.IsAlive && !hero.IsPrisoner && !hero.IsWounded)
                    .Take(5).ToList();
                if (requested.Count < 2)
                    return new JObject { ["ok"] = false, ["error"] = "At least two eligible temporary wilderness participants are required." };

                _wildernessOriginSettlementId = origin.StringId;
                TemporaryWildernessHeroIds.Clear();
                foreach (Hero hero in requested)
                {
                    if (hero.PartyBelongedTo != party)
                    {
                        AddHeroToPartyAction.Apply(hero, party, false);
                        TemporaryWildernessHeroIds.Add(hero.StringId);
                    }
                }
                LeaveSettlementAction.ApplyForParty(party);
                MapState mapState = Game.Current?.GameStateManager?.ActiveState as MapState;
                if (mapState?.AtMenu == true) mapState.ExitMenuMode();
                return new JObject
                {
                    ["ok"] = true,
                    ["originSettlementId"] = _wildernessOriginSettlementId,
                    ["temporaryHeroIds"] = new JArray(TemporaryWildernessHeroIds),
                    ["mainPartyHeroCount"] = ReignPartyChatSession.GetMainPartyHeroes().Count
                };
            }).ConfigureAwait(false);
            if (result.Value<bool?>("ok") != true)
                return LiveCommandResult.Failed(result.Value<string>("error") ?? "Wilderness setup failed.", result);

            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                bool ready = await ReignMainThread.InvokeAsync(() =>
                {
                    MapState state = Game.Current?.GameStateManager?.ActiveState as MapState;
                    return MobileParty.MainParty?.CurrentSettlement == null
                        && Settlement.CurrentSettlement == null
                        && state != null && !state.AtMenu
                        && TaleWorlds.CampaignSystem.Campaign.Current?.CurrentMenuContext == null;
                }).ConfigureAwait(false);
                if (ready) return LiveCommandResult.Completed("The disposable party is ready on the open map for production wilderness events.", result);
                await Task.Delay(250).ConfigureAwait(false);
            }
            return LiveCommandResult.Failed("The party left the settlement but did not reach an eligible open-map state.", result);
        }

        private static async Task<LiveCommandResult> RestoreSettlementAsync()
        {
            if (_individualVm != null || _partyVm != null || _eventVm != null)
                await CloseSessionAsync(false).ConfigureAwait(false);
            JObject result = await ReignMainThread.InvokeAsync(() =>
            {
                Settlement origin = Settlement.Find(_wildernessOriginSettlementId);
                MobileParty party = MobileParty.MainParty;
                if (origin == null || party == null)
                    return new JObject { ["ok"] = false, ["error"] = "The recorded wilderness origin settlement is unavailable." };
                if (party.CurrentSettlement == null) EnterSettlementAction.ApplyForParty(party, origin);
                foreach (string heroId in TemporaryWildernessHeroIds.ToList())
                {
                    Hero hero = Hero.FindFirst(value => value != null && value.StringId == heroId);
                    if (hero == null) continue;
                    if (hero.PartyBelongedTo == party) party.MemberRoster.AddToCounts(hero.CharacterObject, -1);
                    EnterSettlementAction.ApplyForCharacterOnly(hero, origin);
                }
                JArray restored = new JArray(TemporaryWildernessHeroIds);
                TemporaryWildernessHeroIds.Clear();
                return new JObject
                {
                    ["ok"] = true,
                    ["settlementId"] = origin.StringId,
                    ["restoredHeroIds"] = restored,
                    ["partyCurrentSettlementId"] = party.CurrentSettlement?.StringId ?? string.Empty
                };
            }).ConfigureAwait(false);
            return result.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed("The disposable wilderness party and origin settlement were restored.", result)
                : LiveCommandResult.Failed(result.Value<string>("error") ?? "Settlement restoration failed.", result);
        }

        private static async Task<LiveCommandResult> PrepareManipulationFixtureAsync(JObject command)
        {
            if (_individualVm != null || _partyVm != null || _eventVm != null)
                await CloseSessionAsync(false).ConfigureAwait(false);
            int desiredTier = Math.Max(0, Math.Min(6,
                command?.Value<int?>("playerClanTier") ?? 6));
            int desiredGold = Math.Max(0, Math.Min(100000000,
                command?.Value<int?>("playerGold") ?? (Hero.MainHero?.Gold ?? 0)));
            string wealthEvidence = (command?.Value<string>(
                "wealthEvidence") ?? "demonstrated")
                .Trim().ToLowerInvariant();
            if (wealthEvidence != "demonstrated"
                && wealthEvidence != "hidden"
                && wealthEvidence != "visible_presentation")
                return LiveCommandResult.Failed(
                    "wealthEvidence must be demonstrated, hidden, or visible_presentation.");
            JObject result = await ReignMainThread.InvokeAsync(() =>
            {
                Hero player = Hero.MainHero;
                Clan clan = Clan.PlayerClan;
                if (player == null || clan == null
                    || TaleWorlds.CampaignSystem.Campaign.Current?.Models?.ClanTierModel == null)
                    return new JObject { ["ok"] = false, ["error"] = "The loaded campaign has no available player clan." };
                if (!_manipulationFixtureCaptured)
                {
                    _manipulationFixtureOriginalRenown = clan.Renown;
                    _manipulationFixtureOriginalGold = player.Gold;
                    _manipulationFixtureCaptured = true;
                }
                int required = desiredTier <= 0
                    ? 0
                    : TaleWorlds.CampaignSystem.Campaign.Current.Models
                        .ClanTierModel.GetRequiredRenownForTier(desiredTier);
                SetManipulationFixtureRenown(
                    clan,
                    desiredTier <= 0 ? 0f : required + 1f);
                player.Gold = desiredGold;
                _manipulationFixtureEconomicCapacityKnown =
                    wealthEvidence == "demonstrated";
                _manipulationFixtureWealthEvidenceBasis =
                    wealthEvidence == "demonstrated"
                        ? "demonstrated_financial_capacity"
                        : wealthEvidence == "hidden"
                            ? "hidden_wallet_not_observable"
                            : "visible_presentation_only";
                return new JObject
                {
                    ["ok"] = clan.Tier == desiredTier,
                    ["error"] = clan.Tier == desiredTier
                        ? string.Empty
                        : "Bannerlord did not refresh the requested clan tier after the reversible renown change.",
                    ["originalRenown"] = _manipulationFixtureOriginalRenown,
                    ["originalGold"] = _manipulationFixtureOriginalGold,
                    ["requestedClanTier"] = desiredTier,
                    ["currentRenown"] = clan.Renown,
                    ["clanTier"] = clan.Tier,
                    ["tierVerified"] = clan.Tier == desiredTier,
                    ["playerGold"] = player.Gold,
                    ["wealthEvidenceBasis"] =
                        _manipulationFixtureWealthEvidenceBasis,
                    ["economicCapacityKnown"] =
                        _manipulationFixtureEconomicCapacityKnown,
                    ["playerHeroId"] = player.StringId,
                    ["playerName"] = player.Name?.ToString() ?? string.Empty
                };
            }).ConfigureAwait(false);
            return result.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed("The reversible court-intrigue opportunity fixture is active.", result)
                : LiveCommandResult.Failed(result.Value<string>("error") ?? "Manipulation fixture setup failed.", result);
        }

        private static async Task<LiveCommandResult> RestoreManipulationFixtureAsync()
        {
            JObject result = await ReignMainThread.InvokeAsync(() =>
            {
                Clan clan = Clan.PlayerClan;
                if (clan == null)
                    return new JObject { ["ok"] = false, ["error"] = "The loaded campaign has no available player clan." };
                if (_manipulationFixtureCaptured)
                {
                    SetManipulationFixtureRenown(
                        clan,
                        _manipulationFixtureOriginalRenown);
                    if (Hero.MainHero != null)
                        Hero.MainHero.Gold =
                            _manipulationFixtureOriginalGold;
                }
                JObject restored = new JObject
                {
                    ["ok"] = true,
                    ["restored"] = _manipulationFixtureCaptured,
                    ["renown"] = clan.Renown,
                    ["clanTier"] = clan.Tier,
                    ["gold"] = Hero.MainHero?.Gold ?? 0
                };
                _manipulationFixtureCaptured = false;
                _manipulationFixtureOriginalRenown = 0f;
                _manipulationFixtureOriginalGold = 0;
                _manipulationFixtureEconomicCapacityKnown = false;
                _manipulationFixtureWealthEvidenceBasis = string.Empty;
                return restored;
            }).ConfigureAwait(false);
            return result.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed("The court-intrigue opportunity fixture was restored.", result)
                : LiveCommandResult.Failed(result.Value<string>("error") ?? "Manipulation fixture restoration failed.", result);
        }

        private static void SetManipulationFixtureRenown(
            Clan clan,
            float renown)
        {
            if (clan == null) return;
            // Clan.Renown is a stored number, while Clan.Tier is a cached
            // private value. Directly assigning Renown therefore leaves the
            // production context on the old tier. ResetClanRenown and
            // AddRenown are the native paths that recalculate that cache and
            // dispatch the ordinary tier-change event without notifications.
            clan.ResetClanRenown();
            if (renown > 0f)
                clan.AddRenown(renown, shouldNotify: false);
        }

        public static void ApplyManipulationFixtureOpportunityEvidence(
            JObject opportunitySnapshot)
        {
            if (!_manipulationFixtureCaptured
                || opportunitySnapshot == null)
                return;
            JObject target = opportunitySnapshot["target"] as JObject;
            JObject wealth = target?["wealth"] as JObject;
            if (wealth == null) return;
            wealth["economicCapacityKnown"] =
                _manipulationFixtureEconomicCapacityKnown;
            wealth["wealthEvidenceBasis"] =
                _manipulationFixtureWealthEvidenceBasis;
        }

        public static void ApplyManipulationFixtureWealthEvidence(
            Hero subject,
            JObject wealth)
        {
            if (!_manipulationFixtureCaptured
                || subject != Hero.MainHero
                || wealth == null)
                return;
            wealth["economicCapacityKnown"] =
                _manipulationFixtureEconomicCapacityKnown;
            wealth["wealthEvidenceBasis"] =
                _manipulationFixtureWealthEvidenceBasis;
            if (_manipulationFixtureEconomicCapacityKnown)
                return;
            wealth["gold"] = -1;
            wealth["clanGold"] = -1;
            wealth["partyInventoryValue"] = -1;
            wealth["personalWealthTier"] =
                "not directly observed";
            wealth["clanWealthTier"] =
                "not directly observed";
            wealth["visibleWealthTier"] =
                "not directly observed";
            wealth["notes"] =
                "Wallet and treasury amounts are not observable evidence. "
                + "Use visible presentation, known rank, holdings, and "
                + "witnessed transactions only.";
        }

        public static void ApplyNpcObservableWealthEvidence(
            Hero subject,
            JObject wealth)
        {
            if (subject != Hero.MainHero || wealth == null)
                return;
            bool economicCapacityKnown =
                _manipulationFixtureCaptured
                && _manipulationFixtureEconomicCapacityKnown;
            wealth["economicCapacityKnown"] =
                economicCapacityKnown;
            wealth["wealthEvidenceBasis"] =
                _manipulationFixtureCaptured
                    ? _manipulationFixtureWealthEvidenceBasis
                    : "private_wallet_not_observable";
            if (economicCapacityKnown)
                return;
            wealth["gold"] = -1;
            wealth["clanGold"] = -1;
            wealth["partyInventoryValue"] = -1;
            wealth["personalWealthTier"] =
                "not directly observed";
            wealth["clanWealthTier"] =
                "not directly observed";
            wealth["visibleWealthTier"] =
                "not directly observed";
            wealth["notes"] =
                "Private wallet, inventory value, and treasury amounts are "
                + "not observable evidence. Use visible presentation, known "
                + "rank, public holdings, and witnessed transactions only.";
        }

        public static void ApplyNpcObservableClanEvidence(
            Hero subject,
            JObject clan)
        {
            if (subject != Hero.MainHero || clan == null)
                return;
            bool economicCapacityKnown =
                _manipulationFixtureCaptured
                && _manipulationFixtureEconomicCapacityKnown;
            clan["economicCapacityKnown"] =
                economicCapacityKnown;
            clan["wealthEvidenceBasis"] =
                _manipulationFixtureCaptured
                    ? _manipulationFixtureWealthEvidenceBasis
                    : "private_treasury_not_observable";
            if (economicCapacityKnown)
                return;
            clan["gold"] = -1;
            clan["wealthTier"] = "not directly observed";
            clan["notes"] =
                "Exact clan treasury is private. Public tier, renown, "
                + "influence, kingdom affiliation, and holdings remain "
                + "available when the observer knows the clan.";
        }
    }
}
