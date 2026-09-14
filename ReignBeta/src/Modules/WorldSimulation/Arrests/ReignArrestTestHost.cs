using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static readonly HashSet<string> ArrestTestProfiles = new HashSet<string>(
            new[] { "smoke", "feature", "save_prepare", "save_verify", "relationship",
                "evidence", "reputation", "town", "castle", "player_party", "surrender",
                "escape", "duel", "battle", "sovereignty", "evaluation", "cleanup" },
            StringComparer.OrdinalIgnoreCase);

        private static async Task<LiveCommandResult> ExecuteArrestTestAsync(JObject command)
        {
            Stopwatch timer = Stopwatch.StartNew();
            string profile = (command?.Value<string>("profile") ?? "smoke").Trim().ToLowerInvariant();
            if (!ArrestTestProfiles.Contains(profile))
                return LiveCommandResult.Failed("Unsupported arrest profile '" + profile + "'.");
            string fixtureRunId = SanitizeArrestFixtureId(command?.Value<string>("fixtureRunId")
                ?? "arrest_release_matrix");
            string targetHeroId = command?.Value<string>("targetHeroId") ?? string.Empty;
            string disposableSaveName = command?.Value<string>("disposableSaveName") ?? string.Empty;
            bool requireDisposableSave = command?.Value<bool?>("requireDisposableSave") == true;

            JObject data = await ReignMainThread.InvokeAsync(() =>
                BuildArrestTestSnapshot(profile, fixtureRunId, targetHeroId,
                    disposableSaveName, requireDisposableSave)).ConfigureAwait(false);
            timer.Stop();
            data["durationMilliseconds"] = timer.ElapsedMilliseconds;
            bool passed = data.Value<bool?>("passed") == true;
            return passed
                ? LiveCommandResult.Completed("Arrest " + profile + " profile passed.", data)
                : LiveCommandResult.Failed(data.Value<string>("error")
                    ?? "Arrest profile assertions failed.", data);
        }

        private static JObject BuildArrestTestSnapshot(string profile,
            string fixtureRunId, string targetHeroId, string disposableSaveName)
            
        {
            return BuildArrestTestSnapshot(profile, fixtureRunId, targetHeroId,
                disposableSaveName, false);
        }

        private static JObject BuildArrestTestSnapshot(string profile,
            string fixtureRunId, string targetHeroId, string disposableSaveName,
            bool requireDisposableSave)
        {
            ReignArrestCampaignBehavior behavior = ReignArrestCampaignBehavior.Instance;
            Hero target = string.IsNullOrWhiteSpace(targetHeroId)
                ? CharacterObject.OneToOneConversationCharacter?.HeroObject
                : Hero.FindFirst(hero => string.Equals(hero.StringId, targetHeroId,
                    StringComparison.OrdinalIgnoreCase));
            JObject nativeSave = ReignSaveSyncCampaignBehavior.NativeSaveObservation();
            string activeSaveName = ReignServerClient.ActiveNativeSaveName();
            bool destructive = requireDisposableSave || profile == "save_prepare" || profile == "cleanup"
                || profile == "town" || profile == "castle" || profile == "player_party"
                || profile == "surrender" || profile == "escape" || profile == "duel"
                || profile == "battle" || profile == "sovereignty";
            bool disposableArmed = !destructive || (!string.IsNullOrWhiteSpace(disposableSaveName)
                && string.Equals(activeSaveName, disposableSaveName,
                    StringComparison.OrdinalIgnoreCase));
            List<JObject> assertions = new List<JObject>();
            Action<string, bool, string> check = (name, passed, detail) => assertions.Add(
                new JObject { ["name"] = name, ["passed"] = passed, ["detail"] = detail });
            check("campaign_loaded", TaleWorlds.CampaignSystem.Campaign.Current != null && Hero.MainHero != null,
                "A loaded campaign and player hero are required.");
            check("behavior_registered", behavior != null,
                "The saved arrest campaign behavior must be registered.");
            check("explicit_disposable_save", disposableArmed,
                destructive ? "Fixture mutation requires the exact loaded disposable save name."
                    : "This profile is read-only.");

            int[,] justified = { { 0, -6 }, { 1, -3 }, { 2, 0 }, { 3, 8 } };
            int[,] unsupported = { { 0, -12 }, { 1, -20 }, { 2, -30 }, { 3, -45 } };
            int[] reputations = { -8, -15, -25, -40 };
            for (int i = 0; i < 4; i++)
            {
                ReignArrestChargeSeverity severity = (ReignArrestChargeSeverity)i;
                check("relationship_justified_" + severity,
                    ReignArrestCampaignBehavior.RelationshipDelta(severity, true) == justified[i, 1],
                    "Justified relationship cell is exact.");
                check("relationship_unsupported_" + severity,
                    ReignArrestCampaignBehavior.RelationshipDelta(severity, false) == unsupported[i, 1],
                    "Unsupported relationship cell is exact.");
                check("reputation_" + severity,
                    ReignArrestCampaignBehavior.ReputationValue(severity) == reputations[i],
                    "Accusation reputation severity value is exact.");
            }

            List<ReignArrestCase> fixtures = new List<ReignArrestCase>();
            if (behavior != null && profile == "save_prepare" && disposableArmed)
            {
                check("fixture_target_resolved", target != null,
                    "save_prepare requires targetHeroId or a current one-to-one conversation NPC.");
                if (target != null) fixtures = behavior.PrepareSerializationFixtures(fixtureRunId, target);
                check("all_phases_prepared", fixtures.Count == Enum.GetValues(typeof(ReignArrestPhase)).Length,
                    "Every saved arrest phase has a namespaced serialization fixture.");
            }
            else if (behavior != null && profile == "save_verify")
            {
                string prefix = "arrest_test_" + fixtureRunId + "_";
                fixtures = behavior.Cases.Where(item => item.CaseId.StartsWith(prefix,
                    StringComparison.OrdinalIgnoreCase)).ToList();
                check("all_phases_reloaded", fixtures.Count == Enum.GetValues(typeof(ReignArrestPhase)).Length,
                    "Every prepared arrest phase survived native save/load without duplication.");
                check("unique_case_ids", fixtures.Select(item => item.CaseId)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() == fixtures.Count,
                    "Reloaded cases remain unique and idempotent.");
                check("history_reloaded", fixtures.All(item => item.History != null
                    && item.History.Count > 0), "Fixture histories survived reload.");
            }
            else if (behavior != null && profile == "cleanup" && disposableArmed)
            {
                int removed = behavior.CleanupSerializationFixtures(fixtureRunId);
                check("fixtures_removed", !behavior.Cases.Any(item => item.CaseId.StartsWith(
                    "arrest_test_" + fixtureRunId + "_", StringComparison.OrdinalIgnoreCase)),
                    "Only exact namespaced arrest fixtures were removed (count=" + removed + ").");
            }

            JObject nativeAcceptance = new JObject();
            if (behavior != null && disposableArmed && IsNativeArrestProfile(profile))
                nativeAcceptance = RunNativeArrestAcceptance(profile, fixtureRunId,
                    target, behavior, check);

            ReignArrestCase[] cases = behavior?.Cases?.ToArray() ?? new ReignArrestCase[0];
            check("case_schema_version", cases.All(item => item.Version >= 1),
                "All loaded cases use a supported version.");
            check("case_ids_unique", cases.Select(item => item.CaseId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() == cases.Length,
                "Case identifiers are unique after normalization.");

            bool passedAll = assertions.All(item => item.Value<bool>("passed"));
            return new JObject
            {
                ["schemaVersion"] = 1,
                ["scenario"] = "arrest_" + profile,
                ["profile"] = profile,
                ["fixtureRunId"] = fixtureRunId,
                ["saveIdentifier"] = activeSaveName,
                ["disposableSaveArmed"] = disposableArmed,
                ["targetHeroId"] = target?.StringId ?? string.Empty,
                ["campaignId"] = ReignCampaignIdentity.CurrentCampaignId(),
                ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                ["prePostState"] = new JObject
                {
                    ["caseCount"] = cases.Length,
                    ["openCaseCount"] = cases.Count(item => item.IsOpen),
                    ["fixtures"] = new JArray(fixtures.Select(ArrestCaseTestJson))
                },
                ["nativeSave"] = nativeSave,
                ["nativeAcceptance"] = nativeAcceptance,
                ["assertions"] = new JArray(assertions),
                ["passed"] = passedAll,
                ["cleanupStatus"] = profile == "cleanup" && passedAll ? "clean" : "not_requested",
                ["error"] = passedAll ? string.Empty : "One or more structured arrest assertions failed."
            };
        }

        private static JObject ArrestCaseTestJson(ReignArrestCase item)
        {
            return item == null ? new JObject() : new JObject
            {
                ["caseId"] = item.CaseId,
                ["accusedHeroId"] = item.AccusedHeroStringId,
                ["phase"] = ReignArrestCampaignBehavior.PhaseName(item.Phase),
                ["severity"] = ReignArrestCampaignBehavior.SeverityName(item.Severity),
                ["causeEstablished"] = item.CauseEstablished,
                ["evidenceIds"] = item.EvidenceIdsCsv,
                ["relationshipDelta"] = item.RelationshipDeltaApplied,
                ["reputationTagId"] = item.ReputationTagId,
                ["historyCount"] = item.History?.Count ?? 0
            };
        }

        private static bool IsNativeArrestProfile(string profile)
        {
            return profile == "town" || profile == "castle" || profile == "player_party"
                || profile == "surrender" || profile == "escape" || profile == "duel"
                || profile == "battle" || profile == "sovereignty";
        }

        private static JObject RunNativeArrestAcceptance(string profile,
            string fixtureRunId, Hero requestedTarget, ReignArrestCampaignBehavior behavior,
            Action<string, bool, string> check)
        {
            JObject evidence = new JObject
            {
                ["profile"] = profile,
                ["fixtureRunId"] = fixtureRunId,
                ["nativeOperations"] = new JArray()
            };
            Hero target = profile == "battle"
                ? SelectBattleArrestFixtureHero(requestedTarget)
                : SelectArrestFixtureHero(requestedTarget, false);
            check("native_target_available", target != null,
                "A living adult free non-player lord is required for native custody proof.");
            if (target == null) return evidence;
            evidence["targetHeroId"] = target.StringId;
            try
            {
                if (profile == "town" || profile == "castle")
                    RunSettlementCustodyFixture(profile, fixtureRunId, target,
                        behavior, check, evidence);
                else if (profile == "player_party")
                    RunPlayerPartyCustodyFixture(fixtureRunId, target,
                        behavior, check, evidence);
                else if (profile == "surrender")
                    RunSurrenderFixture(fixtureRunId, target,
                        behavior, check, evidence);
                else if (profile == "duel")
                    RunDuelFixture(fixtureRunId, target,
                        behavior, check, evidence);
                else if (profile == "battle")
                    RunBattleCaptureFixture(fixtureRunId, target,
                        behavior, check, evidence);
                else if (profile == "escape")
                    RunEscapeFixture(fixtureRunId, target,
                        behavior, check, evidence);
                else if (profile == "sovereignty")
                    RunSovereigntyFixture(fixtureRunId, behavior, check, evidence);
            }
            catch (Exception ex)
            {
                check("native_fixture_exception", false, ex.ToString());
                evidence["exception"] = ex.ToString();
            }
            return evidence;
        }

        private static void RunSettlementCustodyFixture(string profile,
            string fixtureRunId, Hero target, ReignArrestCampaignBehavior behavior,
            Action<string, bool, string> check, JObject evidence)
        {
            Settlement settlement = Settlement.All.FirstOrDefault(item => item != null
                && !item.IsUnderSiege && (profile == "town" ? item.IsTown : item.IsCastle));
            check("native_" + profile + "_available", settlement != null,
                "A safe native " + profile + " is required.");
            if (settlement == null) return;
            Hero originalOwner = settlement.OwnerClan?.Leader;
            bool ownershipFixture = settlement.OwnerClan != Clan.PlayerClan;
            try
            {
                if (ownershipFixture)
                    ChangeOwnerOfSettlementAction.ApplyByGift(settlement, Hero.MainHero);
                check("native_" + profile + "_player_owned",
                    settlement.OwnerClan == Clan.PlayerClan,
                    "The guarded destination is player-owned before custody begins.");
                ReignArrestCase item = behavior.PrepareNativeAcceptanceFixture(fixtureRunId,
                    profile, target, ReignArrestPhase.AwaitingConfirmation,
                    ReignArrestContextKind.PlayerSettlement, settlement, true, true);
                TakePrisonerAction.Apply(settlement.Party, target);
                bool captured = item != null && item.Phase == ReignArrestPhase.Captured
                    && target.IsPrisoner && target.PartyBelongedToAsPrisoner == settlement.Party;
                check("native_" + profile + "_custody", captured,
                    "Native TakePrisonerAction placed the accused in the exact " + profile + " dungeon.");
                ((JArray)evidence["nativeOperations"]).Add(new JObject
                {
                    ["operation"] = "TakePrisonerAction.Apply",
                    ["destination"] = settlement.StringId,
                    ["destinationKind"] = profile,
                    ["captured"] = captured
                });
                CleanupNativeArrestFixture(behavior, item, target, check,
                    "native_" + profile + "_cleanup");
            }
            finally
            {
                if (ownershipFixture && originalOwner != null
                    && settlement.OwnerClan != originalOwner.Clan)
                    ChangeOwnerOfSettlementAction.ApplyByGift(settlement, originalOwner);
                check("native_" + profile + "_owner_restored",
                    !ownershipFixture || originalOwner == null
                        || settlement.OwnerClan == originalOwner.Clan,
                    "Temporary destination ownership was restored exactly.");
            }
        }

        private static void RunPlayerPartyCustodyFixture(string fixtureRunId,
            Hero target, ReignArrestCampaignBehavior behavior,
            Action<string, bool, string> check, JObject evidence)
        {
            ReignArrestCase item = behavior.PrepareNativeAcceptanceFixture(fixtureRunId,
                "playerparty", target, ReignArrestPhase.AwaitingConfirmation,
                ReignArrestContextKind.PlayerParty, null, true, true);
            TakePrisonerAction.Apply(PartyBase.MainParty, target);
            bool captured = item != null && item.Phase == ReignArrestPhase.Captured
                && target.IsPrisoner && target.PartyBelongedToAsPrisoner == PartyBase.MainParty;
            check("native_player_party_custody", captured,
                "Native TakePrisonerAction placed the accused in player-party custody.");
            ((JArray)evidence["nativeOperations"]).Add(new JObject
            {
                ["operation"] = "TakePrisonerAction.Apply",
                ["destination"] = MobileParty.MainParty?.StringId ?? string.Empty,
                ["destinationKind"] = "player_party",
                ["captured"] = captured
            });
            CleanupNativeArrestFixture(behavior, item, target, check,
                "native_player_party_cleanup");
        }

        private static void RunSurrenderFixture(string fixtureRunId,
            Hero target, ReignArrestCampaignBehavior behavior,
            Action<string, bool, string> check, JObject evidence)
        {
            ReignArrestCase refused = behavior.PrepareNativeAcceptanceFixture(fixtureRunId,
                "surrenderrefused", target, ReignArrestPhase.AwaitingConfirmation,
                ReignArrestContextKind.PartyEncounter, null, false, false);
            behavior.ConfirmArrest(ArrestFixtureAction(refused), target);
            check("native_surrender_refused", refused != null
                && refused.Phase == ReignArrestPhase.Refused && !target.IsPrisoner,
                "A refused field arrest causes no custody.");
            behavior.RemoveNativeAcceptanceFixture(refused?.CaseId);

            ReignArrestCase accepted = behavior.PrepareNativeAcceptanceFixture(fixtureRunId,
                "surrenderaccepted", target, ReignArrestPhase.AwaitingConfirmation,
                ReignArrestContextKind.PartyEncounter, null, true, true);
            behavior.ConfirmArrest(ArrestFixtureAction(accepted), target);
            bool captured = accepted != null && accepted.Phase == ReignArrestPhase.Captured
                && target.IsPrisoner && target.PartyBelongedToAsPrisoner == PartyBase.MainParty;
            check("native_surrender_accepted", captured,
                "An accepted field surrender enters native player-party custody.");
            ((JArray)evidence["nativeOperations"]).Add(new JObject
            {
                ["operation"] = "ConfirmArrest",
                ["refusalProducedCustody"] = false,
                ["acceptanceProducedCustody"] = captured
            });
            CleanupNativeArrestFixture(behavior, accepted, target, check,
                "native_surrender_cleanup");
        }

        private static void RunDuelFixture(string fixtureRunId,
            Hero target, ReignArrestCampaignBehavior behavior,
            Action<string, bool, string> check, JObject evidence)
        {
            ReignArrestCase item = behavior.PrepareNativeAcceptanceFixture(fixtureRunId,
                "duel", target, ReignArrestPhase.Refused,
                ReignArrestContextKind.PartyEncounter, null, false, true);
            behavior.MarkDuelPending(item?.CaseId, "arrest_test_duel_action");
            bool pending = item != null && item.Phase == ReignArrestPhase.DuelPending;
            behavior.CompleteArrestDuel(item?.CaseId, target, true);
            bool captured = pending && item.Phase == ReignArrestPhase.Captured
                && target.IsPrisoner && target.PartyBelongedToAsPrisoner == PartyBase.MainParty;
            check("native_nonlethal_duel_capture", captured,
                "The production nonlethal-duel completion seam captured the accused natively.");
            ((JArray)evidence["nativeOperations"]).Add(new JObject
            {
                ["operation"] = "CompleteArrestDuel",
                ["lethal"] = false,
                ["playerWon"] = true,
                ["captured"] = captured
            });
            CleanupNativeArrestFixture(behavior, item, target, check,
                "native_duel_cleanup");
        }

        private static void RunBattleCaptureFixture(string fixtureRunId,
            Hero target, ReignArrestCampaignBehavior behavior,
            Action<string, bool, string> check, JObject evidence)
        {
            Settlement anchor = Settlement.All.FirstOrDefault(settlement => settlement != null
                && settlement.IsFortification && !settlement.IsUnderSiege)
                ?? throw new InvalidOperationException(
                    "No safe fortification exists for the native arrest battle fixture.");
            MobileParty enemy = CreatePartyAgencyFixtureParty(target, anchor,
                fixtureRunId, "arrest_battle");
            CampaignVec2 playerPosition = MobileParty.MainParty.Position;
            Settlement playerSettlement = MobileParty.MainParty.CurrentSettlement
                ?? Settlement.CurrentSettlement;
            MapEvent mapEvent = null;
            ReignArrestCase item = null;
            bool actualMapBattle = false;
            bool playerVictory = false;
            bool captured = false;
            bool escaped = false;
            try
            {
                if (MobileParty.MainParty.CurrentSettlement != null)
                    LeaveSettlementAction.ApplyForParty(MobileParty.MainParty);
                enemy.Position = MobileParty.MainParty.Position;
                item = behavior.PrepareNativeAcceptanceFixture(fixtureRunId,
                    "battle", target, ReignArrestPhase.BattlePending,
                    ReignArrestContextKind.PartyEncounter, null, false, true);
                StartBattleAction.ApplyStartBattle(MobileParty.MainParty, enemy);
                mapEvent = PartyBase.MainParty.MapEvent ?? enemy.MapEvent;
                actualMapBattle = mapEvent != null && mapEvent.IsPlayerMapEvent
                    && mapEvent.IsFieldBattle && !mapEvent.IsNavalMapEvent
                    && mapEvent.InvolvedParties.Any(party => party == PartyBase.MainParty)
                    && mapEvent.InvolvedParties.Any(party => party == enemy.Party);
                check("native_map_battle_created", actualMapBattle,
                    "Bannerlord created a real field MapEvent containing the player and accused parties.");
                if (!actualMapBattle) return;
                mapEvent.SetOverrideWinner(mapEvent.PlayerSide);
                mapEvent.DoSurrender(mapEvent.PlayerSide.GetOppositeSide());
                playerVictory = mapEvent.HasWinner
                    && mapEvent.WinningSide == mapEvent.PlayerSide
                    && mapEvent.Winner?.Parties.Any(
                        party => party.Party == PartyBase.MainParty) == true;
                check("native_map_battle_player_victory", playerVictory,
                    "The bounded native field battle ended with the player side as verified winner.");
                TakePrisonerAction.Apply(PartyBase.MainParty, target);
                captured = item != null && item.Phase == ReignArrestPhase.Captured
                    && target.IsPrisoner
                    && target.PartyBelongedToAsPrisoner == PartyBase.MainParty;
                check("native_battle_capture_event", captured,
                    "The BattlePending case consumed Bannerlord's native prisoner-taken event exactly once after a real map battle.");
                EndCaptivityAction.ApplyByEscape(target, Hero.MainHero, true);
                escaped = !target.IsPrisoner && item.Phase == ReignArrestPhase.Escaped;
                check("native_battle_escape_event", escaped,
                    "Bannerlord's native escape event moved the captured case to Escaped.");
            }
            finally
            {
                if (PlayerEncounter.Current != null) PlayerEncounter.Finish();
                if (item != null)
                {
                    behavior.ReleaseArrested(ArrestFixtureAction(item), target, true);
                    behavior.RemoveNativeAcceptanceFixture(item.CaseId);
                }
                if (target?.IsPrisoner == true)
                    EndCaptivityAction.ApplyByReleasedByChoice(target, Hero.MainHero);
                if (enemy?.IsActive == true && enemy.MapEvent == null)
                    DestroyPartyAction.Apply(null, enemy);
                MobileParty.MainParty.Position = playerSettlement?.GatePosition
                    ?? playerPosition;
                if (playerSettlement != null
                    && MobileParty.MainParty.CurrentSettlement == null
                    && MobileParty.MainParty.MapEvent == null)
                    EnterSettlementAction.ApplyForParty(
                        MobileParty.MainParty, playerSettlement);
            }
            ((JArray)evidence["nativeOperations"]).Add(new JObject
            {
                ["operation"] = "native_map_battle_capture_and_escape",
                ["mapEventType"] = mapEvent?.EventType.ToString() ?? string.Empty,
                ["playerVictory"] = playerVictory,
                ["captured"] = captured,
                ["escaped"] = escaped,
                ["actualMapBattleProven"] = actualMapBattle
            });
        }

        private static void RunEscapeFixture(string fixtureRunId,
            Hero foreignTarget, ReignArrestCampaignBehavior behavior,
            Action<string, bool, string> check, JObject evidence)
        {
            Hero protectedTarget = SelectProtectedArrestFixtureHero();
            check("native_protected_target_available", protectedTarget != null,
                "A free adult same-realm or player-clan hero is required for protected custody.");
            if (protectedTarget != null)
            {
                ReignArrestCase protectedCase = behavior.PrepareNativeAcceptanceFixture(fixtureRunId,
                    "protectedescape", protectedTarget, ReignArrestPhase.BattlePending,
                    ReignArrestContextKind.PlayerParty, null, false, true);
                TakePrisonerAction.Apply(PartyBase.MainParty, protectedTarget);
                bool protectedBefore = behavior.IsProtectedCustody(protectedTarget);
                EndCaptivityAction.ApplyByEscape(protectedTarget, Hero.MainHero, true);
                bool blocked = protectedBefore && protectedTarget.IsPrisoner
                    && protectedCase.Phase == ReignArrestPhase.Captured;
                check("native_protected_escape_blocked", blocked,
                    "The Harmony guard blocked native automatic escape from protected custody.");
                CleanupNativeArrestFixture(behavior, protectedCase, protectedTarget, check,
                    "native_protected_escape_cleanup");
                ((JArray)evidence["nativeOperations"]).Add(new JObject
                {
                    ["operation"] = "EndCaptivityAction.ApplyByEscape",
                    ["protected"] = true,
                    ["blocked"] = blocked
                });
            }

            ReignArrestCase foreignCase = behavior.PrepareNativeAcceptanceFixture(fixtureRunId,
                "foreignescape", foreignTarget, ReignArrestPhase.BattlePending,
                ReignArrestContextKind.PlayerParty, null, false, true);
            TakePrisonerAction.Apply(PartyBase.MainParty, foreignTarget);
            bool foreignProtected = behavior.IsProtectedCustody(foreignTarget);
            EndCaptivityAction.ApplyByEscape(foreignTarget, Hero.MainHero, true);
            bool escaped = !foreignProtected && !foreignTarget.IsPrisoner
                && foreignCase.Phase == ReignArrestPhase.Escaped;
            check("native_foreign_escape_allowed", escaped,
                "Foreign custody retained Bannerlord's ordinary native escape behavior.");
            behavior.ReleaseArrested(ArrestFixtureAction(foreignCase), foreignTarget, true);
            behavior.RemoveNativeAcceptanceFixture(foreignCase?.CaseId);
            ((JArray)evidence["nativeOperations"]).Add(new JObject
            {
                ["operation"] = "EndCaptivityAction.ApplyByEscape",
                ["protected"] = false,
                ["escaped"] = escaped
            });
        }

        private static void RunSovereigntyFixture(string fixtureRunId,
            ReignArrestCampaignBehavior behavior, Action<string, bool, string> check,
            JObject evidence)
        {
            Settlement settlement = Settlement.All.FirstOrDefault(item => item != null
                && item.IsFortification && !item.IsUnderSiege && item.OwnerClan?.Leader != null);
            Hero target = SelectProtectedArrestFixtureHero();
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            Clan recipientClan = Clan.All.FirstOrDefault(clan => clan != null
                && clan != Clan.PlayerClan && clan != target?.Clan
                && !clan.IsEliminated && clan.Leader != null && clan.Leader.IsAlive
                && (playerKingdom == null || clan.Kingdom != playerKingdom));
            check("native_sovereignty_fixture_available", settlement != null
                && target != null && recipientClan != null,
                "A fortification, protected target, and alternate living clan are required.");
            if (settlement == null || target == null || recipientClan == null) return;
            Hero originalOwner = settlement.OwnerClan.Leader;
            try
            {
                if (settlement.OwnerClan != Clan.PlayerClan)
                    ChangeOwnerOfSettlementAction.ApplyByGift(settlement, Hero.MainHero);
                ReignArrestCase item = behavior.PrepareNativeAcceptanceFixture(fixtureRunId,
                    "sovereignty", target, ReignArrestPhase.BattlePending,
                    ReignArrestContextKind.PlayerSettlement, settlement, false, true);
                TakePrisonerAction.Apply(settlement.Party, target);
                bool protectedBefore = behavior.IsProtectedCustody(target);
                ChangeOwnerOfSettlementAction.ApplyByGift(settlement, recipientClan.Leader);
                bool custodyStayedWithSettlement = target.IsPrisoner
                    && target.PartyBelongedToAsPrisoner == settlement.Party;
                bool protectedAfter = behavior.IsProtectedCustody(target);
                bool caseMigrated = item.Phase == ReignArrestPhase.Captured
                    && string.Equals(item.LastOutcome, "sovereignty_custody_migrated",
                        StringComparison.OrdinalIgnoreCase);
                bool migrated = protectedBefore && custodyStayedWithSettlement && !protectedAfter
                    && caseMigrated;
                check("native_sovereignty_custody_migration", migrated,
                    "Custody stayed with the settlement while player-protection ended after sovereignty changed.");
                ((JArray)evidence["nativeOperations"]).Add(new JObject
                {
                    ["operation"] = "ChangeOwnerOfSettlementAction.ApplyByGift",
                    ["settlementId"] = settlement.StringId,
                    ["recipientClanId"] = recipientClan.StringId,
                    ["recipientKingdomId"] = recipientClan.Kingdom?.StringId ?? string.Empty,
                    ["targetClanId"] = target.Clan?.StringId ?? string.Empty,
                    ["targetKingdomId"] = target.Clan?.Kingdom?.StringId ?? string.Empty,
                    ["protectedBefore"] = protectedBefore,
                    ["custodyStayedWithSettlement"] = custodyStayedWithSettlement,
                    ["caseMigrated"] = caseMigrated,
                    ["casePhase"] = ReignArrestCampaignBehavior.PhaseName(item.Phase),
                    ["caseLastOutcome"] = item.LastOutcome ?? string.Empty,
                    ["protectedAfter"] = protectedAfter,
                    ["protectionReclassified"] = protectedBefore && !protectedAfter
                });
                ChangeOwnerOfSettlementAction.ApplyByGift(settlement, Hero.MainHero);
                CleanupNativeArrestFixture(behavior, item, target, check,
                    "native_sovereignty_cleanup");
            }
            finally
            {
                if (target?.IsPrisoner == true)
                    EndCaptivityAction.ApplyByReleasedByChoice(target, Hero.MainHero);
                if (originalOwner != null && settlement.OwnerClan != originalOwner.Clan)
                    ChangeOwnerOfSettlementAction.ApplyByGift(settlement, originalOwner);
                check("native_sovereignty_owner_restored",
                    originalOwner == null || settlement.OwnerClan == originalOwner.Clan,
                    "The exact original settlement owner was restored.");
            }
        }

        private static void CleanupNativeArrestFixture(ReignArrestCampaignBehavior behavior,
            ReignArrestCase item, Hero target, Action<string, bool, string> check,
            string assertionName)
        {
            if (item != null)
                behavior.ReleaseArrested(ArrestFixtureAction(item), target, true);
            if (target?.IsPrisoner == true)
                EndCaptivityAction.ApplyByReleasedByChoice(target, Hero.MainHero);
            bool removed = item == null || behavior.RemoveNativeAcceptanceFixture(item.CaseId);
            check(assertionName, removed && target?.IsPrisoner != true,
                "The exact run-owned native case was rescinded, released, and removed.");
        }

        private static ReignWorldActionRecord ArrestFixtureAction(ReignArrestCase item)
        {
            return new ReignWorldActionRecord
            {
                Source = "arrest_test",
                ActorHeroStringId = Hero.MainHero?.StringId ?? string.Empty,
                TargetHeroStringId = item?.AccusedHeroStringId ?? string.Empty,
                Reason = "guarded arrest acceptance fixture",
                TermsJson = new JObject { ["caseId"] = item?.CaseId ?? string.Empty }
                    .ToString(Newtonsoft.Json.Formatting.None)
            };
        }

        private static Hero SelectArrestFixtureHero(Hero preferred, bool sameRealm)
        {
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            Func<Hero, bool> eligible = hero => hero != null && hero != Hero.MainHero
                && hero.IsActive && hero.IsAlive && !hero.IsChild && !hero.IsPrisoner
                && hero.Clan != null && hero.IsLord
                && (!sameRealm || playerKingdom != null && hero.Clan.Kingdom == playerKingdom);
            if (eligible(preferred)) return preferred;
            IEnumerable<Hero> rows = Hero.AllAliveHeroes.Where(eligible);
            if (!sameRealm && playerKingdom != null)
                rows = rows.OrderBy(hero => hero.Clan?.Kingdom == playerKingdom ? 1 : 0);
            return rows.OrderBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }

        private static Hero SelectProtectedArrestFixtureHero()
        {
            Hero playerClan = Hero.AllAliveHeroes.FirstOrDefault(hero => hero != null
                && hero != Hero.MainHero && hero.IsActive && !hero.IsChild && !hero.IsPrisoner
                && hero.Clan == Clan.PlayerClan);
            return playerClan ?? SelectArrestFixtureHero(null, true);
        }

        private static Hero SelectBattleArrestFixtureHero(Hero preferred)
        {
            Func<Hero, bool> eligible = hero => hero != null && hero != Hero.MainHero
                && hero.IsActive && hero.IsAlive && !hero.IsChild && !hero.IsPrisoner
                && hero.IsLord && hero.Clan != null && hero.PartyBelongedTo == null
                && hero.CompanionOf == null && hero.GovernorOf == null
                && hero.CanLeadParty();
            if (eligible(preferred)) return preferred;
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            return Hero.AllAliveHeroes.Where(eligible)
                .OrderBy(hero => playerKingdom != null && hero.Clan?.Kingdom == playerKingdom ? 1 : 0)
                .ThenBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static string SanitizeArrestFixtureId(string value)
        {
            string safe = new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
            if (safe.Length == 0) safe = "arrestrelease";
            return safe.Length <= 24 ? safe : safe.Substring(safe.Length - 24);
        }
    }
}
