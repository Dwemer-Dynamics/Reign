using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignShared.Spymaster;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private const int OrganicLedgerMaxBatches = 48;
        private const int OrganicLedgerMaxObservations = 16;
        private const int OrganicLedgerMaxStoredJsonChars = 2048;
        private bool _spymasterAgentTickInFlight;

        public CourtOfficeAssignment ActiveSpymasterAssignment =>
            _offices.FirstOrDefault(x => x != null && x.IsActive && x.Office == ReignCourtOffice.Spymaster);

        public Hero ActiveSpymaster => FindHero(ActiveSpymasterAssignment?.HeroStringId);

        public ReignSpymasterState EnsureSpymasterState()
        {
            _spymasterState = _spymasterState ?? new ReignSpymasterState();
            _spymasterState.Missions = (_spymasterState.Missions ?? new List<ReignSpymasterMission>())
                .Where(x => x != null).OrderBy(x => x.StartedDay).ToList();
            _spymasterState.Effects = (_spymasterState.Effects ?? new List<ReignSpymasterSettlementEffect>())
                .Where(x => x != null).ToList();
            _spymasterState.ForeignAgents = (_spymasterState.ForeignAgents ?? new List<ReignForeignAgentRecord>())
                .Where(x => x != null).ToList();
            _spymasterState.ForeignAgentActions = (_spymasterState.ForeignAgentActions ?? new List<ReignForeignAgentActionRecord>())
                .Where(x => x != null).OrderBy(x => x.WorldDay).ToList();
            _spymasterState.AppliedForeignAgentActionIds = (_spymasterState.AppliedForeignAgentActionIds ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _spymasterState.OrganicTestLedgers = (_spymasterState.OrganicTestLedgers ?? new List<ReignSpymasterOrganicTestLedger>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.RunId)).GroupBy(x => x.RunId, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Last()).ToList();
            if (_spymasterState.OrganicTestLedgers.Count > 4)
                _spymasterState.OrganicTestLedgers = _spymasterState.OrganicTestLedgers
                    .Skip(_spymasterState.OrganicTestLedgers.Count - 4).ToList();
            foreach (ReignSpymasterOrganicTestLedger ledger in _spymasterState.OrganicTestLedgers)
            {
                ledger.InitialMissionIds = ledger.InitialMissionIds ?? new List<string>();
                ledger.InitialAgentHeroIds = ledger.InitialAgentHeroIds ?? new List<string>();
                ledger.InitialActionIds = ledger.InitialActionIds ?? new List<string>();
                ledger.InitialEffectIds = ledger.InitialEffectIds ?? new List<string>();
                ledger.MissionIds = (ledger.MissionIds ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                ledger.Batches = (ledger.Batches ?? new List<ReignSpymasterOrganicBatchRecord>()).Where(x => x != null).ToList();
                if (ledger.Batches.Count > OrganicLedgerMaxBatches)
                    ledger.Batches = ledger.Batches.Skip(ledger.Batches.Count - OrganicLedgerMaxBatches).ToList();
                foreach (ReignSpymasterOrganicBatchRecord batch in ledger.Batches)
                    batch.PreSnapshotJson = CompactOrganicStoredJson(batch.PreSnapshotJson, "batch_snapshot");
                ledger.ObservationJson = (ledger.ObservationJson ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => CompactOrganicStoredJson(x, "observation")).ToList();
                if (ledger.ObservationJson.Count > OrganicLedgerMaxObservations)
                    ledger.ObservationJson = ledger.ObservationJson.Skip(ledger.ObservationJson.Count - OrganicLedgerMaxObservations).ToList();
            }
            if (_spymasterState.Missions.Count > 250)
                _spymasterState.Missions = _spymasterState.Missions.Skip(_spymasterState.Missions.Count - 250).ToList();
            if (_spymasterState.ForeignAgentActions.Count > 250)
                _spymasterState.ForeignAgentActions = _spymasterState.ForeignAgentActions.Skip(_spymasterState.ForeignAgentActions.Count - 250).ToList();
            if (_spymasterState.AppliedForeignAgentActionIds.Count > 500)
                _spymasterState.AppliedForeignAgentActionIds = _spymasterState.AppliedForeignAgentActionIds.Skip(_spymasterState.AppliedForeignAgentActionIds.Count - 500).ToList();
            _spymasterState.Version = Math.Max(4, _spymasterState.Version);
            return _spymasterState;
        }

        private static string CompactOrganicStoredJson(string value, string kind)
        {
            string json = string.IsNullOrWhiteSpace(value) ? "{}" : value;
            if (json.Length <= OrganicLedgerMaxStoredJsonChars) return json;
            return new JObject
            {
                ["compacted"] = true,
                ["kind"] = kind ?? "snapshot",
                ["originalLength"] = json.Length
            }.ToString(Formatting.None);
        }

        public int ActiveSpymasterMissionCount => EnsureSpymasterState().Missions.Count(x => x.State == ReignSpymasterMissionState.Active);
        public int SpymasterMissionCapacity => ReignSpymasterBalance.MissionCapacity;

        public bool HasActiveSpymasterMissions(string heroStringId = null)
        {
            return EnsureSpymasterState().Missions.Any(x => x.State == ReignSpymasterMissionState.Active
                && (string.IsNullOrWhiteSpace(heroStringId) || string.Equals(x.SpymasterHeroStringId, heroStringId, StringComparison.OrdinalIgnoreCase)));
        }

        private bool CanChangeSpymasterOffice(out string error)
        {
            error = string.Empty;
            CourtOfficeAssignment incumbent = ActiveSpymasterAssignment;
            if (incumbent != null && HasActiveSpymasterMissions(incumbent.HeroStringId))
            {
                error = "The current Spymaster cannot be dismissed or replaced while an operation is active.";
                return false;
            }
            return true;
        }

        public ReignSpymasterMissionQuote GetSpymasterQuote(string missionType, Hero targetHero)
        {
            Hero spymaster = ActiveSpymaster;
            int roguery = spymaster?.GetSkillValue(DefaultSkills.Roguery) ?? 0;
            int tier = Math.Max(0, targetHero?.Clan?.Tier ?? 0);
            bool ruler = targetHero?.Clan?.Kingdom?.Leader == targetHero;
            return ReignSpymasterBalance.Quote(missionType, roguery, tier, ruler);
        }

        public ReignSpymasterMissionQuote GetSpymasterQuote(string missionType,
            string targetType, string targetId)
        {
            Hero targetHero = string.Equals(targetType, "person", StringComparison.OrdinalIgnoreCase)
                ? FindHero(targetId) : null;
            if (string.Equals(missionType, "assassinate_governor", StringComparison.OrdinalIgnoreCase))
                targetHero = FindSettlement(targetId)?.Town?.Governor;
            return GetSpymasterQuote(missionType, targetHero);
        }

        public string StartSpymasterMission(string missionType, string targetType, string targetId,
            string targetName, string socialItemId = null, string fabricatedText = null)
        {
            if (!IsRuleModeActive) return "Only a reigning ruler may direct the intelligence service.";
            Hero spymaster = ActiveSpymaster;
            if (spymaster == null) return "Appoint a Spymaster first.";
            if (!spymaster.IsAlive || !spymaster.IsActive || spymaster.IsPrisoner)
                return "The Spymaster must be alive, active, and free.";
            if (ActiveSpymasterMissionCount >= SpymasterMissionCapacity)
                return "The intelligence service is at capacity (" + SpymasterMissionCapacity + " active operations).";

            Hero targetHero = string.Equals(targetType, "person", StringComparison.OrdinalIgnoreCase)
                ? FindHero(targetId) : null;
            Settlement targetSettlement = string.Equals(targetType, "settlement", StringComparison.OrdinalIgnoreCase)
                ? FindSettlement(targetId) : null;
            if (string.Equals(missionType, "assassinate_governor", StringComparison.OrdinalIgnoreCase))
                targetHero = targetSettlement?.Town?.Governor;
            if (targetHero == Hero.MainHero && missionType.StartsWith("assassinate", StringComparison.OrdinalIgnoreCase))
                return "The ruler cannot order their own assassination.";
            if (missionType.StartsWith("assassinate", StringComparison.OrdinalIgnoreCase)
                && targetHero?.IsNotable == true && targetHero.Issue != null)
                return "A notable attached to an active issue cannot be targeted.";
            if (targetType == "person" && targetHero == null) return "That person is no longer available.";
            if (targetType == "settlement" && targetSettlement == null) return "That settlement is no longer available.";
            if (string.Equals(missionType, "assassinate_governor", StringComparison.OrdinalIgnoreCase)
                && (targetHero == null || !targetHero.IsAlive))
                return "That settlement has no living governor to target.";

            ReignSpymasterMissionQuote quote =
                GetSpymasterQuote(missionType, targetType, targetId);
            if ((Hero.MainHero?.Gold ?? 0) < quote.GoldCost)
                return "The treasury needs " + quote.GoldCost.ToString("N0") + " denars for this operation.";

            GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, quote.GoldCost, true);
            float day = CurrentDayFloat();
            ReignSpymasterMission mission = new ReignSpymasterMission
            {
                MissionId = ReignSpymasterTestRuntime.Active
                    ? "spytest_" + ReignSpymasterTestRuntime.RunId + "_" + Guid.NewGuid().ToString("N")
                    : "spy_" + Guid.NewGuid().ToString("N"),
                SpymasterHeroStringId = spymaster.StringId,
                MissionType = missionType ?? string.Empty,
                TargetType = targetType ?? string.Empty,
                TargetStringId = targetId ?? string.Empty,
                TargetName = targetName ?? targetId ?? "Unknown target",
                GoldCost = quote.GoldCost,
                Roguery = spymaster.GetSkillValue(DefaultSkills.Roguery),
                ClanTier = Math.Max(0, targetHero?.Clan?.Tier ?? 0),
                TargetIsRuler = targetHero != null && targetHero.Clan?.Kingdom?.Leader == targetHero,
                StartedDay = day,
                DueDay = day + quote.DurationDays,
                SuccessChance = quote.SuccessChance,
                DetectionChance = quote.DetectionChance,
                Harmful = quote.Harmful,
                State = ReignSpymasterMissionState.Active,
                SocialItemId = socialItemId ?? string.Empty,
                FabricatedText = fabricatedText ?? string.Empty,
                RequestSummary = BuildRequestSummary(missionType, targetName, quote)
            };
            mission.History.Add("Assigned on day " + day.ToString("0.00") + ": " + mission.RequestSummary);
            mission.SponsorKingdomStringId = targetHero?.Clan?.Kingdom?.StringId
                ?? targetSettlement?.OwnerClan?.Kingdom?.StringId ?? string.Empty;
            EnsureSpymasterState().Missions.Add(mission);
            StateChanged?.Invoke();
            _ = ReignSpymasterServerClient.RecordMissionMemoryAsync(mission, "assigned");
            return string.Empty;
        }

        private static string BuildRequestSummary(string type, string targetName, ReignSpymasterMissionQuote quote)
        {
            return (type ?? "operation").Replace('_', ' ') + " concerning " + (targetName ?? "the target")
                + "; cost " + quote.GoldCost.ToString("N0") + " denars; assessed success "
                + quote.SuccessChance.ToString("0") + "%; detection " + quote.DetectionChance.ToString("0") + "%.";
        }

        private void ProcessSpymasterDailyTick()
        {
            ReignSpymasterState state = EnsureSpymasterState();
            float day = CurrentDayFloat();
            state.Effects.RemoveAll(x => x.EndDay <= day);
            foreach (ReignSpymasterMission mission in state.Missions
                .Where(x => x.State == ReignSpymasterMissionState.Active).ToList())
            {
                Hero assigned = FindHero(mission.SpymasterHeroStringId);
                if (assigned == null || !assigned.IsAlive || !assigned.IsActive || assigned.IsPrisoner)
                {
                    mission.State = ReignSpymasterMissionState.Failed;
                    mission.ResolvedDay = day;
                    mission.ResultSummary = "The operation failed immediately because its assigned Spymaster became unavailable. No funds were recovered.";
                    AppendMissionHistory(mission, mission.ResultSummary, day);
                    _ = ReignSpymasterServerClient.RecordMissionMemoryAsync(mission, "resolved");
                }
                else if (mission.DueDay <= day)
                {
                    string invalidation = ValidateSpymasterMissionTarget(mission);
                    if (!string.IsNullOrWhiteSpace(invalidation))
                    {
                        ReignSpymasterTestRuntime.DiscardMissionOutcome(mission.MissionId);
                        mission.State = ReignSpymasterMissionState.Cancelled;
                        mission.ResolvedDay = day;
                        mission.ResultSummary = invalidation + " The prepaid funds were not recovered.";
                        AppendMissionHistory(mission, mission.ResultSummary, day);
                        _ = ReignSpymasterServerClient.RecordMissionMemoryAsync(mission, "resolved");
                    }
                    else ResolveSpymasterMission(mission, assigned, day);
                }
            }

            if (day - state.LastAgentRecruitmentDay >= 5f && !_spymasterAgentTickInFlight)
            {
                state.LastAgentRecruitmentDay = day;
                _ = TickForeignAgentsAsync(day);
            }
            StateChanged?.Invoke();
        }

        private void ResolveSpymasterMission(ReignSpymasterMission mission, Hero spymaster, float day)
        {
            float outcomeRoll;
            if (!ReignSpymasterTestRuntime.TryTakeMissionOutcome(mission.MissionId, out outcomeRoll))
                outcomeRoll = NextSpymasterRoll("mission_outcome");
            mission.OutcomeRoll = outcomeRoll * 100f;
            bool succeeded = mission.OutcomeRoll < mission.SuccessChance;
            mission.ResolvedDay = day;
            if (succeeded)
            {
                mission.State = ReignSpymasterMissionState.Succeeded;
                ResolveSuccessfulSpymasterMission(mission, spymaster, day);
                ReignSpymasterTestRuntime.DiscardMissionConsequence(mission.MissionId);
            }
            else
            {
                bool assassination = mission.MissionType.StartsWith("assassinate", StringComparison.OrdinalIgnoreCase);
                bool alwaysNoticed = mission.MissionType.StartsWith("person_", StringComparison.OrdinalIgnoreCase) || assassination;
                float detectionRoll = NextSpymasterRoll("mission_detection");
                mission.DetectionRoll = detectionRoll * 100f;
                mission.TargetNoticed = alwaysNoticed || mission.DetectionRoll < mission.DetectionChance;
                float attributionRoll = NextSpymasterRoll("mission_attribution");
                mission.AttributionRoll = attributionRoll * 100f;
                mission.PlayerIdentified = !assassination && mission.TargetNoticed
                    && mission.AttributionRoll < Math.Max(5f, Math.Min(95f, mission.DetectionChance * 0.7f));
                mission.State = mission.PlayerIdentified ? ReignSpymasterMissionState.Exposed : ReignSpymasterMissionState.Failed;
                mission.ResultSummary = mission.TargetNoticed
                    ? "The operation failed and the target discovered the inquiry" + (mission.PlayerIdentified ? ", tracing it to the ruler." : ". The sponsor remained concealed.")
                    : "The operation failed without alerting the target.";
                ApplyFailedMissionConsequences(mission, spymaster);
            }
            AppendMissionHistory(mission, mission.ResultSummary, day);
            _ = ReignSpymasterServerClient.RecordMissionMemoryAsync(mission, "resolved");
        }

        private void ResolveSuccessfulSpymasterMission(ReignSpymasterMission mission, Hero spymaster, float day)
        {
            switch (mission.MissionType)
            {
                case "land_intelligence":
                    ReignSettlementSupplySnapshot snapshot = ReignCourtSupplyService.CreateSnapshot(FindSettlement(mission.TargetStringId)?.Town);
                    mission.ReportJson = snapshot == null ? "{}" : JsonConvert.SerializeObject(snapshot);
                    mission.ResultSummary = snapshot == null
                        ? "The agents returned, but the settlement no longer exists."
                        : BuildSettlementReport(snapshot);
                    break;
                case "person_skills":
                case "person_relationships":
                case "person_rumors":
                    Hero person = FindHero(mission.TargetStringId);
                    mission.ResultSummary = BuildPersonReport(person, mission.MissionType);
                    mission.ReportJson = JsonConvert.SerializeObject(new { heroId = person?.StringId, report = mission.ResultSummary });
                    if (mission.MissionType == "person_rumors" && person != null)
                        _ = EnrichSocialReportAsync(mission, person);
                    break;
                case "counterintelligence":
                    int exposed = ExposeForeignAgentNetwork(day);
                    mission.ResultSummary = exposed == 0
                        ? "No foreign network was exposed; defensive watchers have been reinforced."
                        : exposed + " foreign agent" + (exposed == 1 ? " was" : "s were") + " exposed and neutralized.";
                    break;
                case "disrupt_food":
                case "disrupt_construction":
                case "disrupt_security":
                case "disrupt_loyalty":
                    ReignSpymasterEffectSpec effect = ReignSpymasterCore.Effect(mission.MissionType);
                    AddSettlementEffect(mission, day, effect.EffectType, (float)effect.Magnitude, (float)effect.DurationDays);
                    break;
                case "assassinate_governor":
                    ResolveAssassination(mission, FindSettlement(mission.TargetStringId)?.Town?.Governor);
                    break;
                case "assassinate_person":
                    ResolveAssassination(mission, FindHero(mission.TargetStringId));
                    break;
                default:
                    mission.ResultSummary = "The assigned operation was completed successfully.";
                    if (mission.MissionType.StartsWith("mitigate_", StringComparison.OrdinalIgnoreCase)
                        || mission.MissionType.StartsWith("fabricate_", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = ApplySocialOperationAndRecordAsync(mission);
                        ApplyPositiveSocialRelation(mission);
                    }
                    break;
            }
        }

        private void AddSettlementEffect(ReignSpymasterMission mission, float day, string type, float magnitude, float duration)
        {
            EnsureSpymasterState().Effects.Add(new ReignSpymasterSettlementEffect
            {
                SettlementStringId = mission.TargetStringId,
                EffectType = type,
                SourceKingdomStringId = Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty,
                AgentHeroStringId = mission.SpymasterHeroStringId,
                StartDay = day,
                EndDay = day + duration,
                Magnitude = magnitude
            });
            mission.ResultSummary = "The network successfully disrupted " + type + " in " + mission.TargetName
                + " for approximately " + duration.ToString("0") + " days.";
        }

        private void ResolveAssassination(ReignSpymasterMission mission, Hero victim)
        {
            if (victim == null || !victim.IsAlive)
            {
                mission.ResultSummary = "The target was already unavailable before the assassins arrived.";
                return;
            }
            if (victim.IsNotable && victim.Issue != null)
            {
                mission.ResultSummary = "The attempt was abandoned because the target became essential to an active issue.";
                return;
            }
            if (!victim.CanDie(KillCharacterAction.KillCharacterActionDetail.Murdered))
            {
                mission.ResultSummary = "The attempt reached the target, but campaign protections prevented the death.";
                return;
            }
            if (ReignSpymasterTestRuntime.Active && ReignSpymasterTestRuntime.SuppressIrreversibleNativeConsequences)
                ReignSpymasterTestRuntime.RecordNativeIntent("assassination", new JObject { ["victimHeroId"] = victim.StringId, ["missionId"] = mission.MissionId });
            else KillCharacterAction.ApplyByMurder(victim, null, true);
            mission.ResultSummary = victim.Name + " was assassinated. Native clan and kingdom succession has begun.";
        }

        private void ApplyFailedMissionConsequences(ReignSpymasterMission mission, Hero spymaster)
        {
            Hero target = ResolveSpymasterConsequenceTarget(mission);
            bool assassination = mission.MissionType.StartsWith("assassinate", StringComparison.OrdinalIgnoreCase);
            if (!assassination && mission.PlayerIdentified && target != null && target != Hero.MainHero
                && !string.Equals(mission.MissionType, "fabricate_positive", StringComparison.OrdinalIgnoreCase))
            {
                int loss = ReignSpymasterCore.RelationLoss(mission.ClanTier, mission.Harmful, mission.TargetIsRuler);
                if (ReignSpymasterTestRuntime.Active && ReignSpymasterTestRuntime.SuppressIrreversibleNativeConsequences)
                    ReignSpymasterTestRuntime.RecordNativeIntent("relation_and_pressure", new JObject { ["targetHeroId"] = target.StringId, ["loss"] = loss, ["missionId"] = mission.MissionId });
                else
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(target, Hero.MainHero, -loss, true);
                    _ = ReignSpymasterServerClient.ApplyExposurePressureAsync(mission, loss);
                }
            }

            if (!assassination) return;
            float consequenceRoll;
            if (!ReignSpymasterTestRuntime.TryTakeMissionConsequence(mission.MissionId, out consequenceRoll))
                consequenceRoll = NextSpymasterRoll("assassination_consequence");
            mission.ConsequenceRoll = consequenceRoll * 100f;
            if (consequenceRoll < 0.20f)
            {
                if (spymaster?.IsAlive == true && spymaster.CanDie(KillCharacterAction.KillCharacterActionDetail.Murdered))
                {
                    if (ReignSpymasterTestRuntime.Active && ReignSpymasterTestRuntime.SuppressIrreversibleNativeConsequences)
                        ReignSpymasterTestRuntime.RecordNativeIntent("spymaster_killed", new JObject { ["spymasterHeroId"] = spymaster.StringId, ["missionId"] = mission.MissionId });
                    else KillCharacterAction.ApplyByMurder(spymaster, target, true);
                }
                mission.ResultSummary += " The Spymaster was killed by the target's household.";
                ExposeSpymasterMission(mission, target, true);
            }
            else if (spymaster?.CanBecomePrisoner() == true)
            {
                PartyBase holder = ResolveSpymasterCaptivityHolder(mission, target);
                bool suppress = ReignSpymasterTestRuntime.Active && ReignSpymasterTestRuntime.SuppressIrreversibleNativeConsequences;
                if (ReignSpymasterTestRuntime.Active && ReignSpymasterTestRuntime.SuppressIrreversibleNativeConsequences)
                    ReignSpymasterTestRuntime.RecordNativeIntent("spymaster_captured", new JObject { ["spymasterHeroId"] = spymaster.StringId, ["holderAvailable"] = holder != null, ["missionId"] = mission.MissionId });
                else if (holder != null) TakePrisonerAction.Apply(holder, spymaster);
                bool captured = holder != null && (suppress || spymaster.IsPrisoner);
                mission.AttributionPending = captured;
                mission.PlayerIdentified = false;
                mission.State = ReignSpymasterMissionState.Failed;
                mission.ResultSummary += captured
                    ? " The Spymaster was captured before revealing the sponsor. A successful personal breakout can recover them without exposing the ruler; ransom or a failed rescue will expose everything."
                    : " The Spymaster evaded capture without revealing the sponsor.";
            }
        }

        private static PartyBase ResolveSpymasterCaptivityHolder(ReignSpymasterMission mission, Hero target)
        {
            PartyBase holder = target?.PartyBelongedTo?.Party ?? target?.CurrentSettlement?.Party;
            if (holder != null) return holder;

            if (string.Equals(mission?.TargetType, "settlement", StringComparison.OrdinalIgnoreCase))
            {
                holder = FindSettlement(mission.TargetStringId)?.Party;
                if (holder != null) return holder;
            }

            Settlement home = target?.HomeSettlement;
            if (home?.IsVillage == true) home = home.Village?.Bound;
            holder = home?.Party;
            if (holder != null) return holder;

            holder = target?.Clan?.Fiefs.FirstOrDefault(x => x?.Settlement?.Party != null)?.Settlement?.Party;
            if (holder != null) return holder;

            Hero ruler = target?.Clan?.Kingdom?.Leader;
            return ruler?.PartyBelongedTo?.Party ?? ruler?.CurrentSettlement?.Party;
        }

        private void ExposeSpymasterMission(ReignSpymasterMission mission, Hero target, bool almostCertainWar)
        {
            if (mission == null) return;
            mission.AttributionPending = false;
            mission.TargetNoticed = true;
            mission.PlayerIdentified = true;
            mission.State = ReignSpymasterMissionState.Exposed;
            int loss = ReignSpymasterCore.RelationLoss(mission.ClanTier, mission.Harmful, mission.TargetIsRuler);
            bool suppress = ReignSpymasterTestRuntime.Active && ReignSpymasterTestRuntime.SuppressIrreversibleNativeConsequences;
            if (suppress)
                ReignSpymasterTestRuntime.RecordNativeIntent("exposure", new JObject { ["targetHeroId"] = target?.StringId ?? string.Empty, ["relationLoss"] = loss, ["missionId"] = mission.MissionId });
            else
            {
                if (target != null && target != Hero.MainHero)
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(target, Hero.MainHero, -loss, true);
                _ = ReignSpymasterServerClient.ApplyExposurePressureAsync(mission, loss);
            }
            Kingdom foreign = target?.Clan?.Kingdom;
            Kingdom player = Clan.PlayerClan?.Kingdom;
            if (almostCertainWar && foreign != null && player != null && foreign != player
                && !FactionManager.IsAtWarAgainstFaction(foreign, player))
            {
                float warRoll = NextSpymasterRoll("exposure_war");
                if (warRoll < 0.90f)
                {
                    if (suppress)
                        ReignSpymasterTestRuntime.RecordNativeIntent("declare_war", new JObject { ["foreignKingdomId"] = foreign.StringId, ["playerKingdomId"] = player.StringId, ["missionId"] = mission.MissionId });
                    else DeclareWarAction.ApplyByKingdomDecision(foreign, player);
                    mission.ResultSummary += " The exposure caused war.";
                }
            }
        }

        public ReignSpymasterMission PendingCapturedSpymasterMission => EnsureSpymasterState().Missions
            .Where(x => x.AttributionPending && x.SpymasterHeroStringId == ActiveSpymasterAssignment?.HeroStringId)
            .OrderByDescending(x => x.ResolvedDay).FirstOrDefault();

        public bool CanAttemptSpymasterBreakout => ActiveSpymaster?.IsPrisoner == true
            && ActiveSpymaster.PartyBelongedToAsPrisoner != null && PendingCapturedSpymasterMission != null
            && Hero.MainHero?.IsPrisoner != true;

        public float GetSpymasterBreakoutChance()
        {
            Hero spymaster = ActiveSpymaster;
            ReignSpymasterMission mission = PendingCapturedSpymasterMission;
            if (spymaster?.PartyBelongedToAsPrisoner == null || mission == null) return 0f;
            float security = spymaster.PartyBelongedToAsPrisoner.Settlement?.Town?.Security ?? 50f;
            int roguery = Hero.MainHero?.GetSkillValue(DefaultSkills.Roguery) ?? 0;
            return (float)ReignSpymasterCore.BreakoutChance(roguery, security, mission.ClanTier);
        }

        public string AttemptSpymasterBreakout()
        {
            if (!CanAttemptSpymasterBreakout) return "No captured Spymaster is currently eligible for a personal breakout.";
            Hero spymaster = ActiveSpymaster;
            ReignSpymasterMission mission = PendingCapturedSpymasterMission;
            PartyBase holder = spymaster.PartyBelongedToAsPrisoner;
            Hero target = string.Equals(mission.MissionType, "assassinate_governor", StringComparison.OrdinalIgnoreCase)
                ? FindSettlement(mission.TargetStringId)?.Town?.Governor : FindHero(mission.TargetStringId);
            mission.BreakoutChance = GetSpymasterBreakoutChance();
            float breakoutRoll;
            if (!ReignSpymasterTestRuntime.TryTakeBreakoutOutcome(mission.MissionId, out breakoutRoll))
                breakoutRoll = NextSpymasterRoll("breakout");
            mission.BreakoutRoll = breakoutRoll * 100f;
            if (mission.BreakoutRoll < mission.BreakoutChance)
            {
                if (ReignSpymasterTestRuntime.Active && ReignSpymasterTestRuntime.SuppressIrreversibleNativeConsequences)
                    ReignSpymasterTestRuntime.RecordNativeIntent("breakout_success", new JObject { ["spymasterHeroId"] = spymaster.StringId, ["missionId"] = mission.MissionId });
                else EndCaptivityAction.ApplyByEscape(spymaster, Hero.MainHero, true);
                mission.AttributionPending = false;
                mission.ResultSummary += " The ruler personally broke the Spymaster out; both escaped and the sponsor remained concealed.";
                AppendMissionHistory(mission, "Breakout succeeded without exposure.", CurrentDayFloat());
                _ = ReignSpymasterServerClient.RecordMissionMemoryAsync(mission, "resolved");
                StateChanged?.Invoke();
                return string.Empty;
            }

            mission.ResultSummary += " The personal breakout failed. Both the ruler and Spymaster were captured, and the sponsor was conclusively exposed.";
            ExposeSpymasterMission(mission, target, true);
            AppendMissionHistory(mission, "Breakout failed; both the ruler and Spymaster were captured and the operation was exposed.", CurrentDayFloat());
            _ = ReignSpymasterServerClient.RecordMissionMemoryAsync(mission, "resolved");
            if (ReignSpymasterTestRuntime.Active && ReignSpymasterTestRuntime.SuppressIrreversibleNativeConsequences)
                ReignSpymasterTestRuntime.RecordNativeIntent("player_captured", new JObject { ["holderAvailable"] = holder != null, ["missionId"] = mission.MissionId });
            else if (holder != null) TakePrisonerAction.Apply(holder, Hero.MainHero);
            StateChanged?.Invoke();
            return "The breakout failed. Both the ruler and Spymaster are imprisoned, and the operation has been exposed.";
        }

        private void ApplyPositiveSocialRelation(ReignSpymasterMission mission)
        {
            if (!string.Equals(mission.MissionType, "fabricate_positive", StringComparison.OrdinalIgnoreCase)) return;
            Hero target = FindHero(mission.TargetStringId);
            if (target != null && target != Hero.MainHero)
            {
                if (ReignSpymasterTestRuntime.Active && ReignSpymasterTestRuntime.SuppressIrreversibleNativeConsequences)
                    ReignSpymasterTestRuntime.RecordNativeIntent("positive_relation", new JObject { ["targetHeroId"] = target.StringId, ["gain"] = 2, ["missionId"] = mission.MissionId });
                else ChangeRelationAction.ApplyRelationChangeBetweenHeroes(target, Hero.MainHero, 2, true);
            }
        }

        private async System.Threading.Tasks.Task EnrichSocialReportAsync(ReignSpymasterMission mission, Hero person)
        {
            try
            {
                JObject status = await ReignSpymasterServerClient.GetSocialStatusAsync(person.StringId).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (mission.State != ReignSpymasterMissionState.Succeeded) return;
                    JArray rumors = status?["rumors"] as JArray ?? new JArray();
                    JArray reputations = status?["reputations"] as JArray ?? new JArray();
                    string details = "Rumors: " + (rumors.Count == 0 ? "none confirmed" : string.Join("; ", rumors.Select(x => (string)x["description"] ?? (string)x["tag_id"])))
                        + ". Reputations: " + (reputations.Count == 0 ? "none recorded" : string.Join("; ", reputations.Select(x => (string)x["description"] ?? (string)x["tag_id"]))) + ".";
                    mission.ResultSummary = details;
                    mission.ReportJson = status.ToString(Formatting.None);
                    _ = ReignSpymasterServerClient.RecordMissionMemoryAsync(mission, "resolved");
                    StateChanged?.Invoke();
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { ReignLog.Warn("Spymaster social report could not be enriched: " + ex.Message); }
        }

        private static string BuildSettlementReport(ReignSettlementSupplySnapshot x)
        {
            string goods = x.Items == null || x.Items.Count == 0 ? "No market stock recorded"
                : string.Join(", ", x.Items.OrderByDescending(i => i.Count).Select(i => i.Name + " " + i.Count + " @ " + i.LocalPrice));
            goods += ". Ownership: " + x.OwnerClanName + " (" + x.OwnerKingdomName + "); governor " + x.GovernorName
                + "; loyalty change " + x.LoyaltyChange.ToString("+0.0;-0.0;0") + "/day; security change "
                + x.SecurityChange.ToString("+0.0;-0.0;0") + "/day; estimated garrison wages " + x.GarrisonWages.ToString("N0")
                + "; net income after estimated garrison wages " + x.DailyNetIncome.ToString("N0");
            return x.Name + " — prosperity " + x.Prosperity.ToString("0") + ", food " + x.FoodStocks.ToString("0")
                + "/" + x.FoodCapacity + " (" + x.FoodChange.ToString("+0.0;-0.0;0") + "/day), loyalty " + x.Loyalty.ToString("0")
                + ", security " + x.Security.ToString("0") + ", militia " + x.Militia.ToString("0")
                + " (" + x.MilitiaChange.ToString("+0.0;-0.0;0") + "/day), garrison " + x.GarrisonCount
                + " (" + x.GarrisonWounded + " wounded), villages " + x.VillageCount + " (" + x.RaidedVillageCount + " raided), market gold "
                + x.MarketGold.ToString("N0") + ", daily tax " + x.DailyTaxIncome.ToString("N0") + ", construction: " + (x.CurrentConstruction ?? "none")
                + " at " + x.CurrentConstructionProgress.ToString("0.0") + "%" + (x.IsBesieged ? ", UNDER SIEGE" : string.Empty)
                + ". Market: " + goods + ".";
        }

        private static string BuildPersonReport(Hero hero, string type)
        {
            if (hero == null) return "The subject could not be located.";
            if (type == "person_skills")
            {
                var rows = new[]
                {
                    Tuple.Create("One Handed", hero.GetSkillValue(DefaultSkills.OneHanded)),
                    Tuple.Create("Two Handed", hero.GetSkillValue(DefaultSkills.TwoHanded)),
                    Tuple.Create("Polearm", hero.GetSkillValue(DefaultSkills.Polearm)),
                    Tuple.Create("Bow", hero.GetSkillValue(DefaultSkills.Bow)),
                    Tuple.Create("Crossbow", hero.GetSkillValue(DefaultSkills.Crossbow)),
                    Tuple.Create("Throwing", hero.GetSkillValue(DefaultSkills.Throwing)),
                    Tuple.Create("Riding", hero.GetSkillValue(DefaultSkills.Riding)),
                    Tuple.Create("Athletics", hero.GetSkillValue(DefaultSkills.Athletics)),
                    Tuple.Create("Smithing", hero.GetSkillValue(DefaultSkills.Crafting)),
                    Tuple.Create("Roguery", hero.GetSkillValue(DefaultSkills.Roguery)),
                    Tuple.Create("Charm", hero.GetSkillValue(DefaultSkills.Charm)),
                    Tuple.Create("Leadership", hero.GetSkillValue(DefaultSkills.Leadership)),
                    Tuple.Create("Steward", hero.GetSkillValue(DefaultSkills.Steward)),
                    Tuple.Create("Tactics", hero.GetSkillValue(DefaultSkills.Tactics)),
                    Tuple.Create("Trade", hero.GetSkillValue(DefaultSkills.Trade)),
                    Tuple.Create("Scouting", hero.GetSkillValue(DefaultSkills.Scouting)),
                    Tuple.Create("Medicine", hero.GetSkillValue(DefaultSkills.Medicine)),
                    Tuple.Create("Engineering", hero.GetSkillValue(DefaultSkills.Engineering))
                };
                return hero.Name + " — " + string.Join(", ", rows.OrderByDescending(x => x.Item2).Select(x => x.Item1 + " " + x.Item2)) + ".";
            }
            if (type == "person_relationships")
            {
                float adultAge = TaleWorlds.CampaignSystem.Campaign.Current.Models.AgeModel.HeroComesOfAge;
                List<string> strongest = Hero.AllAliveHeroes.Where(x => x != hero && x.Age >= adultAge)
                    .Select(x => new { Hero = x, Value = hero.GetRelation(x) })
                    .Where(x => Math.Abs(x.Value) >= 10).OrderByDescending(x => Math.Abs(x.Value)).Take(8)
                    .Select(x => x.Hero.Name + " " + x.Value.ToString("+0;-0;0")).ToList();
                return hero.Name + " — relation with the ruler " + hero.GetRelation(Hero.MainHero).ToString("+0;-0;0")
                    + ". Strongest known ties: " + (strongest.Count == 0 ? "none" : string.Join(", ", strongest)) + ".";
            }
            return "The network is checking current rumors and established reputations concerning " + hero.Name + ".";
        }

        public float GetSpymasterFoodFactor(Town town) => GetSettlementEffect(town, "food");
        public float GetSpymasterConstructionFactor(Town town) => GetSettlementEffect(town, "construction");
        public float GetSpymasterSecurityDelta(Town town) => GetSettlementEffect(town, "security");
        public float GetSpymasterLoyaltyDelta(Town town) => GetSettlementEffect(town, "loyalty");

        private float GetSettlementEffect(Town town, string type)
        {
            if (town?.Settlement == null) return 0f;
            float day = CurrentDayFloat();
            float total = EnsureSpymasterState().Effects.Where(x => x.EndDay > day
                && string.Equals(x.SettlementStringId, town.Settlement.StringId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.EffectType, type, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Magnitude);
            return string.Equals(type, "food", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "construction", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(-0.90f, Math.Min(0f, total))
                : Math.Max(-10f, Math.Min(10f, total));
        }

        private int ExposeForeignAgentNetwork(float day)
        {
            List<ReignForeignAgentRecord> candidates = EnsureSpymasterState().ForeignAgents
                .Where(x => x.Activated && !x.Exposed).OrderBy(x => x.RecruitedDay)
                .Take(1 + Math.Max(0, ActiveSpymaster?.GetSkillValue(DefaultSkills.Roguery) ?? 0) / 100).ToList();
            foreach (ReignForeignAgentRecord agent in candidates)
            {
                // Preserve the exposed network as durable campaign history while
                // taking it out of the active pool. This makes identification
                // observable across save/load instead of deleting its evidence.
                agent.Activated = false;
                agent.Exposed = true;
                agent.ExposedDay = day;
                foreach (ReignForeignAgentActionRecord action in EnsureSpymasterState().ForeignAgentActions
                    .Where(x => string.Equals(x.AgentHeroStringId, agent.AgentHeroStringId, StringComparison.OrdinalIgnoreCase)))
                {
                    action.Detected = true;
                    action.SponsorAttributed = true;
                    action.Summary = "Counterintelligence exposed the agent and attributed the network to " + agent.SponsorKingdomStringId + ".";
                }
                _ = ReignSpymasterServerClient.ExposeForeignAgentAsync(agent.AgentHeroStringId, "player_counterintelligence");
            }
            return candidates.Count;
        }

        private void OnSpymasterPrisonerReleased(Hero hero, PartyBase party, IFaction capturerFaction,
            EndCaptivityDetail detail, bool showNotification)
        {
            if (detail == EndCaptivityDetail.Ransom) NotifyRansomedSpymaster(hero);
        }

        private void NotifyRansomedSpymaster(Hero hero)
        {
            if (hero == null) return;
            ReignSpymasterMission capturedMission = EnsureSpymasterState().Missions
                .Where(x => x.AttributionPending && string.Equals(x.SpymasterHeroStringId, hero.StringId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.ResolvedDay).FirstOrDefault();
            if (capturedMission != null)
            {
                Hero target = string.Equals(capturedMission.MissionType, "assassinate_governor", StringComparison.OrdinalIgnoreCase)
                    ? FindSettlement(capturedMission.TargetStringId)?.Town?.Governor : FindHero(capturedMission.TargetStringId);
                capturedMission.ResultSummary += " The ransom payment was traced to the ruler, exposing the entire operation.";
                AppendMissionHistory(capturedMission, "Ransom exposed the ruler and the complete operation.", CurrentDayFloat());
                ExposeSpymasterMission(capturedMission, target, true);
                _ = ReignSpymasterServerClient.RecordMissionMemoryAsync(capturedMission, "resolved");
                StateChanged?.Invoke();
            }
        }

        private async System.Threading.Tasks.Task TickForeignAgentsAsync(float day)
        {
            _spymasterAgentTickInFlight = true;
            try
            {
                JObject response = await ReignSpymasterServerClient.TickForeignAgentsAsync(BuildForeignAgentSnapshot(day)).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() =>
                {
                    ApplyForeignAgentResponse(response, day);
                    ReignSpymasterTestRuntime.RecordForeignAgentActionReceipt(response?["controlledAgentAction"] as JObject);
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { ReignLog.Warn("Foreign agent tick failed: " + ex.Message); }
            finally { _spymasterAgentTickInFlight = false; }
        }

        private async System.Threading.Tasks.Task ApplySocialOperationAndRecordAsync(ReignSpymasterMission mission)
        {
            try
            {
                JObject result = await ReignSpymasterServerClient.ApplySocialOperationAsync(mission).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() =>
                {
                    string outcome = (string)result?["outcome"];
                    if (!string.IsNullOrWhiteSpace(outcome))
                        mission.ResultSummary = "The social operation succeeded: " + outcome.Replace('_', ' ') + ".";
                    mission.ReportJson = result?.ToString(Formatting.None) ?? "{}";
                    _ = ReignSpymasterServerClient.RecordMissionMemoryAsync(mission, "resolved");
                    StateChanged?.Invoke();
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    mission.ResultSummary = "The field operation succeeded, but its social record could not be synchronized: " + ex.Message;
                    StateChanged?.Invoke();
                }).ConfigureAwait(false);
            }
        }

        private JObject BuildForeignAgentSnapshot(float day)
        {
            Kingdom player = Clan.PlayerClan?.Kingdom;
            Hero ruler = player?.Leader;
            JArray candidates = new JArray();
            float adultAge = TaleWorlds.CampaignSystem.Campaign.Current.Models.AgeModel.HeroComesOfAge;
            foreach (Hero hero in Hero.AllAliveHeroes.Where(x => IsEligibleForeignAgentCandidate(x, player, ruler, adultAge)))
            {
                Settlement home = hero.IsNotable ? ResolveNotableFortification(hero) : ResolveNobleFortification(hero, player);
                if (home == null || !home.IsFortification) continue;
                candidates.Add(new JObject
                {
                    ["heroId"] = hero.StringId,
                    ["name"] = hero.Name?.ToString() ?? hero.StringId,
                    ["isNotable"] = hero.IsNotable,
                    ["settlementId"] = home?.StringId ?? string.Empty,
                    ["nativeLoyaltyFallback"] = ruler == null ? 50 : hero.GetRelation(ruler)
                });
            }
            JArray enemies = new JArray(Kingdom.All.Where(x => x != null && x != player && !x.IsEliminated
                    && player != null && FactionManager.IsAtWarAgainstFaction(x, player))
                .Select(x => new JObject { ["kingdomId"] = x.StringId, ["rulerId"] = x.Leader?.StringId ?? string.Empty,
                    ["isAtWar"] = true }));
            JObject snapshot = new JObject { ["worldDay"] = day, ["playerKingdomId"] = player?.StringId ?? string.Empty,
                ["playerRulerId"] = ruler?.StringId ?? string.Empty, ["candidates"] = candidates, ["enemyKingdoms"] = enemies };
            JObject controlledAction = ReignSpymasterTestRuntime.PendingForeignAgentActionControl();
            if (controlledAction != null) snapshot["controlledAgentAction"] = controlledAction;
            return snapshot;
        }

        private bool IsEligibleForeignAgentCandidate(Hero hero, Kingdom player, Hero ruler, float adultAge)
        {
            if (hero == null || hero.Age < adultAge || player == null)
                return false;
            if (hero == Hero.MainHero || hero == ruler || hero == ActiveSpymaster)
                return false;
            return hero.IsLord && hero.Clan?.Kingdom == player
                || hero.IsNotable && hero.HomeSettlement?.MapFaction == player;
        }

        private void ApplyForeignAgentResponse(JObject response, float day)
        {
            if (response == null || response.Value<bool?>("ok") != true) return;
            ReignSpymasterState state = EnsureSpymasterState();
            foreach (JObject row in response["agents"] as JArray ?? new JArray())
            {
                string agentId = (string)row["agentHeroStringId"] ?? string.Empty;
                string sponsorId = (string)row["sponsorKingdomStringId"] ?? string.Empty;
                ReignForeignAgentRecord existing = state.ForeignAgents.FirstOrDefault(x => string.Equals(x.AgentHeroStringId, agentId, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    existing = new ReignForeignAgentRecord { AgentHeroStringId = agentId, SponsorKingdomStringId = sponsorId };
                    state.ForeignAgents.Add(existing);
                }
                existing.SponsorKingdomStringId = sponsorId;
                existing.SettlementStringId = (string)row["settlementStringId"] ?? string.Empty;
                existing.IsNotable = (bool?)row["isNotable"] == true;
                existing.Activated = (bool?)row["activated"] == true;
                existing.Exposed = false;
                existing.ExposedDay = -1f;
                existing.RecruitedDay = (float?)row["recruitedDay"] ?? existing.RecruitedDay;
                existing.NextActionDay = (float?)row["nextActionDay"] ?? day + 1f;
            }
            foreach (JObject row in response["actions"] as JArray ?? new JArray())
            {
                string actionId = (string)row["actionId"] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(actionId))
                    actionId = "legacy_agent_action_" + ((string)row["agentHeroStringId"] ?? string.Empty) + "_" + day.ToString("0.000") + "_" + ((string)row["effectType"] ?? "security");
                if (state.AppliedForeignAgentActionIds.Contains(actionId, StringComparer.OrdinalIgnoreCase)) continue;
                state.AppliedForeignAgentActionIds.Add(actionId);
                float duration = (float?)row["durationDays"] ?? 10f;
                float magnitude = (float?)row["magnitude"] ?? -1f;
                state.Effects.Add(new ReignSpymasterSettlementEffect { SettlementStringId = (string)row["settlementStringId"] ?? string.Empty,
                    EffectType = (string)row["effectType"] ?? "security", SourceKingdomStringId = (string)row["sponsorKingdomStringId"] ?? string.Empty,
                    AgentHeroStringId = (string)row["agentHeroStringId"] ?? string.Empty, StartDay = day,
                    EndDay = day + duration, Magnitude = magnitude });
                state.ForeignAgentActions.Add(new ReignForeignAgentActionRecord
                {
                    ActionId = actionId,
                    AgentHeroStringId = (string)row["agentHeroStringId"] ?? string.Empty,
                    SponsorKingdomStringId = (string)row["sponsorKingdomStringId"] ?? string.Empty,
                    SettlementStringId = (string)row["settlementStringId"] ?? string.Empty,
                    EffectType = (string)row["effectType"] ?? "security",
                    WorldDay = day,
                    DurationDays = duration,
                    Magnitude = magnitude,
                    Detected = (bool?)row["detected"] == true,
                    SponsorAttributed = (bool?)row["sponsorAttributed"] == true,
                    Summary = (string)row["summary"] ?? "A hidden foreign agent disrupted the settlement."
                });
            }
            StateChanged?.Invoke();
        }

        private static Settlement ResolveNotableFortification(Hero hero)
        {
            Settlement home = hero?.HomeSettlement;
            if (home?.IsVillage == true) return home.Village?.Bound;
            return home;
        }

        private static Settlement FindSettlement(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? null : Settlement.All.FirstOrDefault(x => string.Equals(x.StringId, id, StringComparison.OrdinalIgnoreCase));
        }
    }
}
