using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ReignBeta.Campaign
{
    /// <summary>
    /// Authoritative relationship-driven rebellion lifecycle.
    ///
    /// NPC vassal leaders at or below -35 effective attitude to their ruler receive one saved 5% roll
    /// per campaign week. A civil war is then decided only by removal of its named leader:
    /// sustained capture by the opposing side, death, deposition, or explicit
    /// surrender. A brief battlefield capture has a rescue/escape grace period
    /// before it becomes a terminal political result.
    /// </summary>
    public sealed class ReignRebellionCampaignBehavior : CampaignBehaviorBase
    {
        private const int RelationshipThreshold = -35;
        private const int WeeklyOutbreakPercent = 5;
        private const float RealmCooldownDays = 63f;
        private const float RebellionCommitmentDays = 3650f;
        private const float LeadershipCaptureGraceDays = 3f;

        private List<ReignRebellionMovementRecord> _movements = new List<ReignRebellionMovementRecord>();
        private List<ReignRebellionMembershipRecord> _memberships = new List<ReignRebellionMembershipRecord>();
        private List<ReignRebellionWeeklyRollRecord> _weeklyRolls = new List<ReignRebellionWeeklyRollRecord>();
        private List<ReignRebellionPlotRecord> _plots = new List<ReignRebellionPlotRecord>();
        private List<ReignRebellionPledgeRecord> _pledges = new List<ReignRebellionPledgeRecord>();
        private List<ReignRebellionSummonsRecord> _summonses = new List<ReignRebellionSummonsRecord>();
        private List<ReignNegotiatedActionRecord> _negotiations = new List<ReignNegotiatedActionRecord>();
        private string _socialBalanceRollOverridesJson = string.Empty;
        private float _lastEvaluationDay = -1000f;
        private bool _judgmentInquiryOpen;
        private bool _weeklyEvaluationPending;
        private bool _backingEvaluationPending;
        private readonly object _backingUpdatesLock = new object();
        private List<JObject> _pendingBackingUpdates = new List<JObject>();

        public static ReignRebellionCampaignBehavior Instance { get; private set; }
        public IReadOnlyList<ReignRebellionMovementRecord> Movements => _movements;
        public IReadOnlyList<ReignRebellionMembershipRecord> Memberships => _memberships;
        public IReadOnlyList<ReignRebellionWeeklyRollRecord> WeeklyRolls => _weeklyRolls;
        public IReadOnlyList<ReignRebellionPlotRecord> Plots => _plots;
        public IReadOnlyList<ReignRebellionPledgeRecord> Pledges => _pledges;
        public IReadOnlyList<ReignRebellionSummonsRecord> Summonses => _summonses;
        public IReadOnlyList<ReignNegotiatedActionRecord> Negotiations => _negotiations;

        public bool RecordForeignBacking(string movementId, string sponsorKingdomId, string actionId)
        {
            ReignRebellionMovementRecord movement = _movements.FirstOrDefault(x => x != null
                && Same(x.MovementId, movementId) && IsCivilWar(x));
            if (movement == null) return false;
            movement.BackingStatus = "completed";
            movement.BackingAskedKingdomStringId = sponsorKingdomId ?? string.Empty;
            movement.BackingSponsorKingdomStringId = sponsorKingdomId ?? string.Empty;
            movement.BackingActionId = actionId ?? string.Empty;
            movement.BackingRequestDay = CurrentDay();
            movement.BackingTerminalReason = "accepted_and_executed";
            movement.UpdatedDay = CurrentDay();
            return true;
        }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnHeroPrisonerTaken);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnPeaceMade);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_reign_rebellionMovements", ref _movements);
            dataStore.SyncData("_reign_rebellionMemberships", ref _memberships);
            dataStore.SyncData("_reign_rebellionWeeklyRolls", ref _weeklyRolls);
            dataStore.SyncData("_reign_rebellionPlots", ref _plots);
            dataStore.SyncData("_reign_rebellionPledges", ref _pledges);
            dataStore.SyncData("_reign_rebellionSummonses", ref _summonses);
            dataStore.SyncData("_reign_negotiatedActions", ref _negotiations);
            dataStore.SyncData("_reign_socialBalanceRebellionRollOverrides", ref _socialBalanceRollOverridesJson);
            dataStore.SyncData("_reign_rebellionLastEvaluationDay", ref _lastEvaluationDay);
            _movements = (_movements ?? new List<ReignRebellionMovementRecord>()).Where(x => x != null).ToList();
            _memberships = (_memberships ?? new List<ReignRebellionMembershipRecord>()).Where(x => x != null).ToList();
            _weeklyRolls = (_weeklyRolls ?? new List<ReignRebellionWeeklyRollRecord>()).Where(x => x != null).ToList();
            _plots = (_plots ?? new List<ReignRebellionPlotRecord>()).Where(x => x != null).ToList();
            _pledges = (_pledges ?? new List<ReignRebellionPledgeRecord>()).Where(x => x != null).ToList();
            _summonses = (_summonses ?? new List<ReignRebellionSummonsRecord>()).Where(x => x != null).ToList();
            _negotiations = (_negotiations ?? new List<ReignNegotiatedActionRecord>()).Where(x => x != null).ToList();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // Prepared by ReignCampaignPreparationCampaignBehavior.
        }

        internal void PrepareInitialState()
        {
            InitializeSessionState();
        }

        private void InitializeSessionState()
        {
            MigrateLegacyMovements();
            ReconcileAllMovements();
            TryShowNextPlayerJudgment();
        }

        private void OnDailyTick()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            float now = CurrentDay();
            if (!ReignCampaignPreparationCampaignBehavior
                .AreAutonomousWorldSystemsUnlocked(now)) return;
            DrainPendingBackingUpdates();
            MigrateLegacyMovements();
            ProcessPreparationState(now);
            foreach (ReignRebellionMovementRecord movement in _movements.Where(IsCivilWar).ToList())
                ReconcileCivilWar(movement);
            ExpireNegotiations(now);
            TryShowNextPlayerJudgment();

            TryStartWeeklyRelationshipEvaluation(now);
            TryEvaluateForeignBacking();
        }

        private void OnHourlyTick()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            if (!ReignCampaignPreparationCampaignBehavior
                .AreAutonomousWorldSystemsUnlocked(CurrentDay())) return;
            // The effective-attitude batch can span several campaign hours at
            // maximum map speed. If a weekly boundary is crossed while that
            // request is in flight, daily-only scheduling leaves the new week
            // overdue until the next day (or indefinitely when a checkpoint
            // pauses first). This check is idempotent because the completed week
            // marker and saved per-clan rolls gate all actual work.
            TryStartWeeklyRelationshipEvaluation(CurrentDay());
        }

        private void TryEvaluateForeignBacking()
        {
            if (_backingEvaluationPending || !_movements.Any(x => IsCivilWar(x)
                && (string.IsNullOrWhiteSpace(x.BackingStatus)
                    || Same(x.BackingStatus, "pending_evaluation")
                    || Same(x.BackingStatus, "accepted_pending_execution")
                    || Same(x.BackingStatus, "accepted")))) return;
            _backingEvaluationPending = true;
            _ = EvaluateForeignBackingAsync();
        }

        private async Task EvaluateForeignBackingAsync()
        {
            try
            {
                ReignRebellionEvaluationResult result = await ReignServerClient.EvaluateRebellionsAsync(
                    _movements.ToList(), _memberships.ToList()).ConfigureAwait(false);
                if (result.Ok && result.Updates.Count > 0)
                {
                    lock (_backingUpdatesLock)
                        _pendingBackingUpdates.AddRange(result.Updates.Select(x => (JObject)x.DeepClone()));
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Foreign rebellion backing evaluation failed: " + ex.Message);
            }
            finally
            {
                _backingEvaluationPending = false;
            }
        }

        private void DrainPendingBackingUpdates()
        {
            List<JObject> updates;
            lock (_backingUpdatesLock)
            {
                updates = _pendingBackingUpdates.ToList();
                _pendingBackingUpdates.Clear();
            }
            foreach (JObject update in updates)
            {
                string movementId = update.Value<string>("movementId") ?? string.Empty;
                ReignRebellionMovementRecord movement = _movements.FirstOrDefault(x => x != null && Same(x.MovementId, movementId));
                if (movement == null || update["backingStatus"] == null) continue;
                movement.BackingStatus = update.Value<string>("backingStatus") ?? movement.BackingStatus;
                movement.BackingAskedKingdomStringId = update.Value<string>("backingAskedKingdomId") ?? string.Empty;
                movement.BackingSponsorKingdomStringId = update.Value<string>("backingSponsorKingdomId") ?? string.Empty;
                movement.BackingRequestDay = update.Value<float?>("backingRequestDay") ?? movement.BackingRequestDay;
                movement.BackingActionId = update.Value<string>("backingActionId") ?? string.Empty;
                movement.BackingTerminalReason = update.Value<string>("backingTerminalReason") ?? string.Empty;
                movement.UpdatedDay = CurrentDay();
            }
        }

        private void TryStartWeeklyRelationshipEvaluation(float now)
        {
            if (_weeklyEvaluationPending) return;
            int currentWeek = WeekIndex(now);
            int completedWeek = WeekIndex(_lastEvaluationDay);
            int weekIndex = completedWeek < 0 ? currentWeek : completedWeek + 1;
            if (weekIndex < 0 || weekIndex > currentWeek) return;
            _weeklyEvaluationPending = true;
            _ = EvaluateWeeklyRelationshipOutbreaksAsync(weekIndex, now);
        }

        private async Task EvaluateWeeklyRelationshipOutbreaksAsync(
            int weekIndex, float now)
        {
            bool completed = false;
            string stage = "building the political relationship request";
            try
            {
                JArray requestedPairs = BuildPoliticalAttitudeRequest();
                stage = "resolving effective political relationships";
                ReignEffectiveAttitudeResult resolved =
                    await ReignServerClient.ResolveEffectiveAttitudesAsync(requestedPairs)
                        .ConfigureAwait(false);
                stage = "applying weekly rebellion rolls";
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!resolved.Ok)
                    {
                        ReignLog.Warn("Weekly rebellion evaluation deferred: "
                            + (string.IsNullOrWhiteSpace(resolved.Error)
                                ? "Reign relationship state was unavailable."
                                : resolved.Error));
                        return;
                    }
                    EvaluateWeeklyRelationshipOutbreaks(weekIndex, now,
                        resolved.Attitudes);
                    int completedRolls = _weeklyRolls.Count(x => x != null
                        && x.WeekIndex == weekIndex);
                    // Preserve the completed cadence boundary rather than the
                    // current catch-up day. If Bannerlord advanced by more than
                    // one week while the server request was in flight, the next
                    // missing week must still be evaluated.
                    _lastEvaluationDay = weekIndex * 7f;
                    ReignLog.Info("Weekly rebellion evaluation completed week="
                        + weekIndex + " rolls=" + completedRolls
                        + " requestedPairs=" + requestedPairs.Count);
                    completed = true;
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // This task is intentionally started without blocking the daily campaign
                // tick. Observe every failure here so a bad native kingdom/clan state or
                // a transport failure cannot silently strand the weekly cadence forever.
                ReignLog.Warn("Weekly rebellion evaluation failed for week "
                    + weekIndex + " while " + stage + ": " + ex);
            }
            finally
            {
                try
                {
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        _weeklyEvaluationPending = false;
                        // Continue draining crossed weekly boundaries immediately,
                        // including while campaign time is now paused. A provider
                        // failure waits for the next daily tick rather than spinning.
                        if (completed)
                            TryStartWeeklyRelationshipEvaluation(CurrentDay());
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // If main-thread dispatch itself is unavailable, do not leave the
                    // in-memory guard permanently latched. The next daily tick may retry.
                    _weeklyEvaluationPending = false;
                    ReignLog.Warn("Weekly rebellion evaluation cleanup failed for week "
                        + weekIndex + ": " + ex);
                }
            }
        }

        private static JArray BuildPoliticalAttitudeRequest()
        {
            JArray requests = new JArray();
            HashSet<string> seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (Kingdom kingdom in Kingdom.All
                .Where(x => x != null && !x.IsEliminated && !IsRebelKingdomStatic(x))
                .OrderBy(x => x.StringId))
            {
                List<Hero> leaders = kingdom.Clans
                    .Where(clan => clan != null && clan.Leader != null
                        && !clan.Leader.IsDead && clan != Clan.PlayerClan)
                    .Select(clan => clan.Leader)
                    .Concat(kingdom.Leader == null
                        ? Enumerable.Empty<Hero>()
                        : new[] { kingdom.Leader })
                    .Where(hero => hero != null && hero != Hero.MainHero)
                    .GroupBy(hero => hero.StringId,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First()).ToList();
                foreach (Hero observer in leaders)
                foreach (Hero target in leaders)
                {
                    if (observer == target) continue;
                    string key = observer.StringId + "|" + target.StringId;
                    if (!seen.Add(key)) continue;
                    requests.Add(new JObject
                    {
                        ["observerId"] = observer.StringId,
                        ["targetId"] = target.StringId,
                        ["context"] = "weekly_rebellion"
                    });
                }
            }
            return requests;
        }

        private static bool IsRebelKingdomStatic(Kingdom kingdom)
        {
            return Instance?.IsRebelKingdom(kingdom) == true;
        }

        private void EvaluateWeeklyRelationshipOutbreaks(int weekIndex, float now,
            IReadOnlyDictionary<string, JObject> attitudes)
        {
            foreach (Kingdom kingdom in Kingdom.All
                .Where(x => x != null && !x.IsEliminated && !IsRebelKingdom(x))
                .OrderBy(x => x.StringId)
                .ToList())
            {
                string realmExclusionReason = HasActiveMovement(kingdom)
                    ? "active_war"
                    : IsRealmOnCooldown(kingdom, now) ? "cooldown" : string.Empty;
                Hero ruler = kingdom.Leader;
                if (ruler == null || ruler.IsDead) realmExclusionReason = "missing_ruler";

                List<Tuple<Clan, int>> successes = new List<Tuple<Clan, int>>();
                foreach (Clan clan in kingdom.Clans
                    .Where(x => x != null && x != kingdom.RulingClan && x != Clan.PlayerClan)
                    .OrderBy(x => x.StringId)
                    .ToList())
                {
                    ReignRebellionWeeklyRollRecord existing = _weeklyRolls.FirstOrDefault(x =>
                        Same(x.KingdomStringId, kingdom.StringId) && Same(x.ClanStringId, clan.StringId) && x.WeekIndex == weekIndex);
                    if (existing != null)
                    {
                        if (existing.Triggered) successes.Add(Tuple.Create(clan, existing.RulerRelation));
                        continue;
                    }

                    Hero leader = clan.Leader;
                    bool hasAttitude = TryGetEffectiveAttitude(attitudes, leader,
                        ruler, out int personalAffinity, out int publicStanding,
                        out int relation, out int standingRevision);
                    bool leaderEligible = IsEligibleNpcVassalLeader(clan, kingdom);
                    bool eligible = string.IsNullOrWhiteSpace(realmExclusionReason)
                        && leaderEligible && hasAttitude
                        && relation <= RelationshipThreshold;
                    string exclusionReason = eligible
                        ? string.Empty
                        : !string.IsNullOrWhiteSpace(realmExclusionReason)
                            ? realmExclusionReason
                            : !leaderEligible ? "ineligible_leader"
                            : !hasAttitude ? "missing_effective_relationship"
                            : "relationship_threshold";
                    JObject testOverride = eligible ? TakeSocialBalanceRollOverride(kingdom.StringId, clan.StringId) : null;
                    int roll = eligible
                        ? testOverride == null ? MBRandom.RandomInt(1, 101) : Math.Max(1, Math.Min(100, testOverride.Value<int?>("roll") ?? 100))
                        : 0;
                    bool triggered = eligible && roll <= WeeklyOutbreakPercent;
                    _weeklyRolls.Add(new ReignRebellionWeeklyRollRecord
                    {
                        KingdomStringId = kingdom.StringId,
                        ClanStringId = clan.StringId,
                        WeekIndex = weekIndex,
                        Roll = roll,
                        RulerRelation = relation,
                        PersonalAffinityToRuler = personalAffinity,
                        RulerPublicStanding = publicStanding,
                        RulerStandingRevision = standingRevision,
                        WasEligible = eligible,
                        Triggered = triggered,
                        RolledDay = now,
                        ExclusionReason = exclusionReason,
                        TestOverride = testOverride != null,
                        TestRunId = testOverride?.Value<string>("runId") ?? string.Empty
                    });
                    if (triggered) successes.Add(Tuple.Create(clan, relation));
                }

                Clan leaderClan = successes.OrderBy(x => x.Item2).ThenBy(x => x.Item1.StringId).Select(x => x.Item1).FirstOrDefault();
                if (leaderClan != null) StartRelationshipRebellion(kingdom,
                    leaderClan, weekIndex, attitudes);
            }

            // Keep enough audit history for long campaigns without unbounded save growth.
            int oldestWeek = weekIndex - 26;
            _weeklyRolls.RemoveAll(x => x.WeekIndex >= 0 && x.WeekIndex < oldestWeek);
        }

        private static bool TryGetEffectiveAttitude(
            IReadOnlyDictionary<string, JObject> attitudes, Hero observer,
            Hero target, out int personalAffinity, out int publicStanding,
            out int effectiveAttitude, out int standingRevision)
        {
            personalAffinity = 0;
            publicStanding = 0;
            effectiveAttitude = 0;
            standingRevision = 0;
            if (observer == null || target == null || attitudes == null
                || !attitudes.TryGetValue(observer.StringId + "|"
                    + target.StringId, out JObject attitude)
                || attitude.Value<bool?>("hasPair") != true)
                return false;
            personalAffinity = attitude.Value<int?>("personalAffinity") ?? 0;
            publicStanding = attitude.Value<int?>("targetPublicStanding") ?? 0;
            effectiveAttitude = attitude.Value<int?>("effectiveAttitude") ?? 0;
            standingRevision = attitude.Value<int?>("publicStandingRevision") ?? 0;
            return true;
        }

        public bool QueueSocialBalanceRollOverride(string kingdomId, string clanId, int roll, string runId, out string result)
        {
            result = string.Empty;
            Kingdom kingdom = Kingdom.All.FirstOrDefault(x => Same(x?.StringId, kingdomId));
            Clan clan = Clan.FindFirst(x => Same(x?.StringId, clanId));
            if (kingdom == null || clan == null || clan.Kingdom != kingdom)
            {
                result = "The requested vassal clan is not a member of the requested kingdom.";
                return false;
            }
            if (roll < 1 || roll > 100)
            {
                result = "The saved rebellion roll override must be between 1 and 100.";
                return false;
            }
            JObject root = ParseSocialBalanceRollOverrides();
            string key = SocialBalanceRollKey(kingdom.StringId, clan.StringId);
            root[key] = new JObject
            {
                ["kingdomId"] = kingdom.StringId,
                ["clanId"] = clan.StringId,
                ["roll"] = roll,
                ["runId"] = runId ?? string.Empty,
                ["queuedDay"] = CurrentDay()
            };
            _socialBalanceRollOverridesJson = root.ToString(Newtonsoft.Json.Formatting.None);
            result = "The next eligible weekly rebellion roll for " + clan.Name + " was fixed at " + roll + ".";
            return true;
        }

        public int ClearSocialBalanceRollOverrides(string runId)
        {
            JObject root = ParseSocialBalanceRollOverrides();
            List<JProperty> matches = root.Properties()
                .Where(x => string.IsNullOrWhiteSpace(runId)
                    || Same((x.Value as JObject)?.Value<string>("runId"), runId))
                .ToList();
            foreach (JProperty match in matches) match.Remove();
            _socialBalanceRollOverridesJson = root.ToString(Newtonsoft.Json.Formatting.None);
            return matches.Count;
        }

        private JObject TakeSocialBalanceRollOverride(string kingdomId, string clanId)
        {
            JObject root = ParseSocialBalanceRollOverrides();
            string key = SocialBalanceRollKey(kingdomId, clanId);
            JObject value = root[key] as JObject;
            if (value == null) return null;
            root.Remove(key);
            _socialBalanceRollOverridesJson = root.ToString(Newtonsoft.Json.Formatting.None);
            return value;
        }

        private JObject ParseSocialBalanceRollOverrides()
        {
            try
            {
                return string.IsNullOrWhiteSpace(_socialBalanceRollOverridesJson)
                    ? new JObject()
                    : JObject.Parse(_socialBalanceRollOverridesJson);
            }
            catch
            {
                _socialBalanceRollOverridesJson = string.Empty;
                return new JObject();
            }
        }

        private static string SocialBalanceRollKey(string kingdomId, string clanId)
        {
            return (kingdomId ?? string.Empty).ToLowerInvariant() + "|" + (clanId ?? string.Empty).ToLowerInvariant();
        }

        private void StartRelationshipRebellion(Kingdom kingdom, Clan leaderClan,
            int weekIndex, IReadOnlyDictionary<string, JObject> attitudes)
        {
            Hero rebelLeader = leaderClan?.Leader;
            Hero ruler = kingdom?.Leader;
            if (!IsEligibleNpcVassalLeader(leaderClan, kingdom) || ruler == null || HasActiveMovement(kingdom)) return;

            ReignRebellionMovementRecord movement = CreateMovement(kingdom, leaderClan, false, weekIndex);
            foreach (Clan clan in kingdom.Clans.Where(x => x != null).OrderBy(x => x.StringId).ToList())
            {
                Hero clanLeader = clan.Leader;
                bool rebel = clan == leaderClan;
                TryGetEffectiveAttitude(attitudes, clanLeader, rebelLeader,
                    out _, out _, out int rebelRelation, out _);
                TryGetEffectiveAttitude(attitudes, clanLeader, ruler,
                    out _, out _, out int rulerRelation, out _);
                if (!rebel && clan != kingdom.RulingClan && clan != Clan.PlayerClan
                    && IsEligibleNpcVassalLeader(clan, kingdom)
                    && rebelRelation > rulerRelation)
                {
                    rebel = true;
                }
                AddOrUpdateMember(movement, clan, rebel ? "rebel" : "loyalist",
                    rebel ? "relationship_poll" : "relationship_loyalist", rebelRelation, rulerRelation);
            }

            StartCivilWar(movement);
        }

        public bool IsRebelKingdom(Kingdom kingdom)
        {
            return kingdom != null && _movements.Any(x =>
                Same(x.RebelKingdomStringId, kingdom.StringId) && !x.ResolutionApplied && !Same(x.Stage, "resolved"));
        }

        public bool IsCivilWarPair(Kingdom first, Kingdom second)
        {
            return first != null && second != null && _movements.Any(x => IsCivilWar(x) && IsPair(x, first, second));
        }

        public ReignRebellionMovementRecord FindActiveMovementForHero(Hero hero)
        {
            if (hero == null) return null;
            return _movements.FirstOrDefault(x => IsCivilWar(x)
                && (Same(x.LeaderHeroStringId, hero.StringId)
                    || Same(x.OriginalRulerHeroStringId, hero.StringId)
                    || MembersOf(x).Any(m => Same(m.ClanStringId, hero.Clan?.StringId))));
        }

        public ReignRebellionPlotRecord FindActivePlayerPlot()
        {
            string playerId = Hero.MainHero?.StringId ?? string.Empty;
            return _plots.LastOrDefault(x => x != null
                && Same(x.PlayerHeroStringId, playerId)
                && !IsTerminalPlotStatus(x.Status));
        }

        /// <summary>
        /// Records one lord's explicit, character-authored answer to a secret
        /// request. The server/dialogue layer decides pledge, refusal, or report;
        /// this method owns eligibility, idempotence, persistence, and consequences.
        /// </summary>
        public bool TryRecordPreparationDecision(Hero player, Hero targetLord,
            string decision, string reason, string channel, string actionId,
            out string result)
        {
            result = string.Empty;
            Kingdom parent = Clan.PlayerClan?.Kingdom;
            if (player != Hero.MainHero || Clan.PlayerClan?.Leader != player
                || parent == null || Clan.PlayerClan == parent.RulingClan
                || player.IsDead || player.IsPrisoner)
            {
                result = "Only the living, free leader of a non-ruling player vassal clan may prepare a rebellion.";
                return false;
            }

            Clan targetClan = targetLord?.Clan;
            if (targetLord == null || !targetLord.IsLord || targetLord.IsDead
                || targetLord.IsPrisoner || targetClan == null
                || targetClan.IsEliminated || targetClan == Clan.PlayerClan
                || targetLord != targetClan.Leader)
            {
                result = "The approached character must be the living, free leader of an active noble clan.";
                return false;
            }
            if (targetLord == parent.Leader)
            {
                result = "You cannot secretly recruit the ruler whose rule you intend to challenge.";
                return false;
            }

            string normalized = NormalizePreparationDecision(decision);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                result = "The lord's answer must explicitly pledge support, refuse, or report the conspiracy.";
                return false;
            }

            ReignRebellionPlotRecord plot = FindActivePlayerPlot();
            if (plot == null)
            {
                plot = CreatePlayerPlot(player, parent);
                _plots.Add(plot);
            }
            if (!Same(plot.ParentKingdomStringId, parent.StringId)
                || !Same(plot.RulerHeroStringId, parent.Leader?.StringId))
            {
                result = "The saved conspiracy belongs to a different ruler or realm and must resolve before another begins.";
                return false;
            }
            if (Same(plot.Status, "active_rebellion"))
            {
                result = "The rebellion has already been declared; recruit the lord openly instead.";
                return false;
            }
            if (!IsPreparationRecruitmentOpen(plot))
            {
                result = "Secret recruitment closes once the conspiracy has been reported or a ruler's summons is in flight.";
                return false;
            }

            ReignRebellionPledgeRecord existing = _pledges.LastOrDefault(x => x != null
                && Same(x.PlotId, plot.PlotId)
                && (Same(x.ActionId, actionId) || Same(x.ClanStringId, targetClan.StringId)));
            if (existing != null)
            {
                result = DescribePreparationDecision(targetLord, existing.Decision);
                return true;
            }

            float now = CurrentDay();
            ReignRebellionPledgeRecord pledge = new ReignRebellionPledgeRecord
            {
                PledgeId = "pledge_" + Guid.NewGuid().ToString("N"),
                PlotId = plot.PlotId,
                LordHeroStringId = targetLord.StringId,
                ClanStringId = targetClan.StringId,
                OriginalKingdomStringId = targetClan.Kingdom?.StringId ?? string.Empty,
                Decision = normalized,
                DecisionReason = reason ?? string.Empty,
                RequestedDay = now,
                RespondedDay = now,
                ReportedToRuler = Same(normalized, "report"),
                RelationToPlayerAtDecision = targetLord.GetRelation(player),
                RelationToRulerAtDecision = targetLord.GetRelation(parent.Leader),
                RequestChannel = string.IsNullOrWhiteSpace(channel) ? "conversation" : channel,
                ActionId = actionId ?? string.Empty
            };
            _pledges.Add(pledge);
            plot.UpdatedDay = now;

            EmitPlotHistory(plot, "rebellion_pledge_decision", "completed",
                targetLord.StringId, DescribePreparationDecision(targetLord, normalized), true);
            if (Same(normalized, "report")) BeginConspiracyReport(plot, pledge, targetLord);
            result = DescribePreparationDecision(targetLord, normalized);
            return true;
        }

        /// <summary>
        /// Resolves the in-person summons. Defiance begins the rebellion immediately;
        /// renunciation submits the player to the ruler's explicit verdict.
        /// </summary>
        public bool TryResolvePreparationSummons(Hero player, Hero ruler,
            string playerResponse, string rulerVerdict, out string result)
        {
            result = string.Empty;
            ReignRebellionPlotRecord plot = FindActivePlayerPlot();
            ReignRebellionSummonsRecord summons = _summonses.LastOrDefault(x => x != null
                && Same(x.PlotId, plot?.PlotId) && !x.ConsequenceApplied);
            Kingdom parent = FindKingdom(plot?.ParentKingdomStringId);
            if (plot == null || summons == null || parent == null
                || player != Hero.MainHero || ruler == null
                || ruler != parent.Leader || !Same(summons.RulerHeroStringId, ruler.StringId))
            {
                result = "There is no active summons from the current ruler to answer.";
                return false;
            }
            if (CurrentDay() < summons.DeliveryDay)
            {
                result = "The ruler's summons has not yet been delivered.";
                return false;
            }

            string response = NormalizeSummonsResponse(playerResponse);
            if (Same(response, "defy"))
            {
                summons.Status = "defied";
                summons.AnsweredDay = CurrentDay();
                summons.PlayerResponse = "defy";
                if (!StartPreparedRebellion(plot, "summons_defied", out result)) return false;
                summons.ConsequenceApplied = true;
                summons.Consequence = "rebellion";
                return true;
            }
            if (!Same(response, "renounce"))
            {
                result = "You must explicitly renounce the conspiracy or defy the ruler.";
                return false;
            }

            string verdict = NormalizeRulerVerdict(rulerVerdict);
            if (string.IsNullOrWhiteSpace(verdict))
            {
                result = "The ruler must choose pardon, imprisonment, exile, or execution.";
                return false;
            }

            summons.Status = "answered";
            summons.AnsweredDay = CurrentDay();
            summons.PlayerResponse = "renounce";
            summons.RulerVerdict = verdict;
            ApplyPreparationVerdict(plot, summons, ruler, verdict, out result);
            return true;
        }

        private ReignRebellionPlotRecord CreatePlayerPlot(Hero player, Kingdom parent)
        {
            float now = CurrentDay();
            string id = "rebellion_plot_" + Guid.NewGuid().ToString("N");
            return new ReignRebellionPlotRecord
            {
                PlotId = id,
                PlayerHeroStringId = player.StringId,
                PlayerClanStringId = Clan.PlayerClan?.StringId ?? string.Empty,
                ParentKingdomStringId = parent.StringId,
                RulerHeroStringId = parent.Leader?.StringId ?? string.Empty,
                Status = "planning",
                CreatedDay = now,
                UpdatedDay = now,
                CorrelationId = id
            };
        }

        private void BeginConspiracyReport(ReignRebellionPlotRecord plot,
            ReignRebellionPledgeRecord pledge, Hero informer)
        {
            if (plot == null || pledge == null || informer == null
                || !string.IsNullOrWhiteSpace(plot.InformerHeroStringId)) return;
            float now = CurrentDay();
            plot.Status = "reported_in_transit";
            plot.InformerHeroStringId = informer.StringId;
            plot.InformerClanStringId = informer.Clan?.StringId ?? string.Empty;
            plot.ReportDispatchDay = now;
            plot.ReportDeliveryDay = now + 1f;
            plot.UpdatedDay = now;
            Hero ruler = FindHero(plot.RulerHeroStringId);
            _ = QueuePlotLetterAsync(plot, informer, ruler,
                "I must report that " + (Hero.MainHero?.Name?.ToString() ?? "the player")
                + " approached me to secure my loyalty for a rebellion against your rule.",
                "rebellion_conspiracy_report", plot.ReportDeliveryDay, false);
        }

        private void ProcessPreparationState(float now)
        {
            foreach (ReignRebellionPlotRecord plot in _plots.Where(x => x != null
                && !IsTerminalPlotStatus(x.Status)).ToList())
            {
                if (Same(plot.Status, "reported_in_transit")
                    && plot.ReportDeliveryDay > 0f && now >= plot.ReportDeliveryDay)
                    IssueRulerSummons(plot, now);

                ReignRebellionSummonsRecord summons = _summonses.LastOrDefault(x => x != null
                    && Same(x.PlotId, plot.PlotId) && !x.ConsequenceApplied);
                if (summons == null) continue;
                if (Same(summons.Status, "dispatched") && now >= summons.DeliveryDay)
                {
                    summons.Status = "delivered";
                    plot.Status = "summoned";
                    plot.UpdatedDay = now;
                    InformationManager.DisplayMessage(new InformationMessage(
                        (FindHero(plot.RulerHeroStringId)?.Name?.ToString() ?? "Your ruler")
                        + " has summoned you to answer "
                        + (FindHero(plot.InformerHeroStringId)?.Name?.ToString() ?? "an informer's")
                        + " report of your planned rebellion. You have seven campaign days to meet the ruler."));
                }
                if (!summons.ConsequenceApplied && PreparationDeadlineExpired(now, summons.DeadlineDay)
                    && (Same(summons.Status, "delivered") || Same(summons.Status, "dispatched")))
                {
                    summons.Status = "ignored";
                    summons.ConsequenceApplied = StartPreparedRebellion(plot,
                        "summons_deadline_ignored", out string ignoredResult);
                    summons.Consequence = summons.ConsequenceApplied ? "rebellion" : "rebellion_failed";
                    if (!string.IsNullOrWhiteSpace(ignoredResult))
                        InformationManager.DisplayMessage(new InformationMessage(ignoredResult));
                }
            }
        }

        private void IssueRulerSummons(ReignRebellionPlotRecord plot, float now)
        {
            if (plot == null || _summonses.Any(x => x != null && Same(x.PlotId, plot.PlotId))) return;
            ReignRebellionSummonsRecord summons = new ReignRebellionSummonsRecord
            {
                SummonsId = "rebellion_summons_" + Guid.NewGuid().ToString("N"),
                PlotId = plot.PlotId,
                RulerHeroStringId = plot.RulerHeroStringId,
                PlayerHeroStringId = plot.PlayerHeroStringId,
                InformerHeroStringId = plot.InformerHeroStringId,
                Status = "dispatched",
                IssuedDay = now,
                DeliveryDay = now + 1f,
                DeadlineDay = now + 8f
            };
            _summonses.Add(summons);
            plot.SummonsId = summons.SummonsId;
            plot.Status = "summons_in_transit";
            plot.UpdatedDay = now;
            Hero ruler = FindHero(plot.RulerHeroStringId);
            Hero informer = FindHero(plot.InformerHeroStringId);
            string body = "You are commanded to meet me within seven days of this letter's delivery and answer "
                + (informer?.Name?.ToString() ?? "a named lord") + "'s report that you are preparing a rebellion. "
                + "Failure to appear will be treated as open defiance and rebellion.";
            _ = QueuePlotLetterAsync(plot, ruler, Hero.MainHero, body,
                "rebellion_ruler_summons", summons.DeliveryDay, true);
            EmitPlotHistory(plot, "rebellion_summons", "dispatched",
                ruler?.StringId, body, false);
        }

        private static bool PreparationDeadlineExpired(float currentDay, float deadlineDay) =>
            currentDay >= deadlineDay;

        private static float PreviousRepresentableCampaignDay(float value)
        {
            if (float.IsNaN(value) || float.IsNegativeInfinity(value)) return value;
            if (value == 0f) return -float.Epsilon;
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            bits += value > 0f ? -1 : 1;
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }

        private async Task QueuePlotLetterAsync(ReignRebellionPlotRecord plot,
            Hero sender, Hero recipient, string body, string reason,
            float deliveryDay, bool summonsLetter)
        {
            ReignLetterSendResult send = await ReignServerClient.SendRebellionSystemLetterAsync(
                sender, recipient, body, reason, deliveryDay, plot?.PlotId ?? string.Empty)
                .ConfigureAwait(false);
            if (plot == null || !send.Ok) return;
            if (summonsLetter)
            {
                ReignRebellionSummonsRecord summons = _summonses.LastOrDefault(x => x != null
                    && Same(x.PlotId, plot.PlotId));
                if (summons != null) summons.LetterId = send.LetterId ?? string.Empty;
            }
            else plot.ReportLetterId = send.LetterId ?? string.Empty;
        }

        private bool StartPreparedRebellion(ReignRebellionPlotRecord plot,
            string cause, out string result)
        {
            result = string.Empty;
            Kingdom parent = FindKingdom(plot?.ParentKingdomStringId);
            if (plot == null || parent == null || Clan.PlayerClan?.Kingdom != parent)
            {
                result = "The reported realm or player-vassal relationship no longer exists.";
                return false;
            }
            if (!TryStartDirectedMovement(parent, Clan.PlayerClan,
                Enumerable.Empty<Clan>(), out result)) return false;
            ReignRebellionMovementRecord movement = _movements.LastOrDefault(x => IsCivilWar(x)
                && x.IsPlayerLed && Same(x.LeaderHeroStringId, plot.PlayerHeroStringId));
            plot.Status = "active_rebellion";
            plot.Resolution = cause ?? "declared";
            plot.MovementId = movement?.MovementId ?? string.Empty;
            plot.UpdatedDay = CurrentDay();
            ActivatePreparedPledges(plot, movement);
            EmitPlotHistory(plot, "rebellion_preparation", "activated",
                plot.PlayerHeroStringId, "The prepared rebellion became an open civil war.", false);
            return true;
        }

        private void ActivatePreparedPledges(ReignRebellionPlotRecord plot,
            ReignRebellionMovementRecord movement)
        {
            if (plot == null || movement == null || plot.PledgesActivated) return;
            foreach (ReignRebellionPledgeRecord pledge in _pledges.Where(x => x != null
                && Same(x.PlotId, plot.PlotId) && Same(x.Decision, "pledge") && !x.Activated).ToList())
            {
                Hero lord = FindHero(pledge.LordHeroStringId);
                if (lord == null || lord.IsDead || lord.IsPrisoner || lord.Clan == null
                    || lord.Clan.IsEliminated || lord != lord.Clan.Leader) continue;
                if (TryRecruitLord(Hero.MainHero, lord, out string ignored))
                {
                    pledge.Activated = true;
                    pledge.ActivatedDay = CurrentDay();
                    pledge.MovementId = movement.MovementId;
                }
            }
            plot.PledgesActivated = true;
        }

        private void ApplyPreparationVerdict(ReignRebellionPlotRecord plot,
            ReignRebellionSummonsRecord summons, Hero ruler, string verdict,
            out string result)
        {
            Hero player = Hero.MainHero;
            string applied = verdict;
            if (Same(verdict, "pardon"))
            {
                result = ruler.Name + " pardoned you after you renounced the conspiracy.";
            }
            else if (Same(verdict, "imprisonment"))
            {
                ImprisonByVictor(ruler, player);
                result = ruler.Name + " imprisoned you for the conspiracy.";
            }
            else if (Same(verdict, "exile"))
            {
                foreach (Town fief in Clan.PlayerClan.Fiefs.ToList())
                    ChangeOwnerOfSettlementAction.ApplyByGift(fief.Settlement, ruler);
                ChangeKingdomAction.ApplyByLeaveKingdom(Clan.PlayerClan, true);
                result = ruler.Name + " exiled your clan and confiscated its fiefs.";
            }
            else
            {
                if (!CampaignOptions.IsLifeDeathCycleDisabled)
                {
                    if (player.IsPrisoner) EndCaptivityAction.ApplyByReleasedAfterBattle(player);
                    KillCharacterAction.ApplyByExecution(player, ruler, true, false);
                }
                if (!player.IsDead)
                {
                    applied = "imprisonment";
                    ImprisonByVictor(ruler, player);
                    result = CampaignOptions.IsLifeDeathCycleDisabled
                        ? "Execution is disabled, so the ruler imprisoned you instead."
                        : "The execution could not complete, so the ruler imprisoned you instead.";
                }
                else result = ruler.Name + " ordered and carried out your execution.";
            }
            summons.RulerVerdict = applied;
            summons.Consequence = applied;
            summons.ConsequenceApplied = true;
            plot.RulerVerdict = applied;
            plot.Resolution = "renounced_" + applied;
            plot.Status = "resolved";
            plot.UpdatedDay = CurrentDay();
            EmitPlotHistory(plot, "rebellion_summons_judgment", "completed",
                ruler.StringId, result, false);
        }

        private void EmitPlotHistory(ReignRebellionPlotRecord plot, string type,
            string phase, string actorId, string summary, bool hidden)
        {
            ReignWorldHistoryCampaignBehavior.Instance?.RecordReignSystemEvent(
                type, phase, "politics", plot?.CorrelationId, summary,
                hidden ? "participants" : "major_world", actorId,
                plot?.ParentKingdomStringId,
                new JObject
                {
                    ["plotId"] = plot?.PlotId ?? string.Empty,
                    ["status"] = plot?.Status ?? string.Empty,
                    ["informerHeroId"] = plot?.InformerHeroStringId ?? string.Empty,
                    ["summonsId"] = plot?.SummonsId ?? string.Empty,
                    ["verdict"] = plot?.RulerVerdict ?? string.Empty
                },
                hidden ? new JArray(new[] { plot?.PlayerHeroStringId ?? string.Empty,
                    plot?.InformerHeroStringId ?? string.Empty }.Where(x => !string.IsNullOrWhiteSpace(x))) : null);
        }

        internal static string NormalizePreparationDecision(string value)
        {
            string text = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (text == "pledge" || text == "accept" || text == "support") return "pledge";
            if (text == "refuse" || text == "decline" || text == "reject") return "refuse";
            if (text == "report" || text == "betray" || text == "inform") return "report";
            return string.Empty;
        }

        internal static string NormalizeSummonsResponse(string value)
        {
            string text = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (text == "renounce" || text == "submit" || text == "recant") return "renounce";
            if (text == "defy" || text == "rebel" || text == "declare") return "defy";
            return string.Empty;
        }

        internal static string NormalizeRulerVerdict(string value)
        {
            string text = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (text == "pardon" || text == "mercy" || text == "free") return "pardon";
            if (text == "imprison" || text == "imprisonment" || text == "prison") return "imprisonment";
            if (text == "exile" || text == "banish") return "exile";
            if (text == "execute" || text == "execution" || text == "death") return "execution";
            return string.Empty;
        }

        internal JObject RunPreparationHarnessProfile(string profile,
            string fixtureRunId, string disposableSaveName, JObject command = null)
        {
            string fixture = "rebellion_test_" + SafeId(fixtureRunId);
            float now = CurrentDay();
            JObject before = FixtureCounts(fixture);
            List<JObject> assertions = new List<JObject>();
            Action<string, bool, string> check = (name, passed, detail) => assertions.Add(
                new JObject { ["name"] = name, ["passed"] = passed, ["detail"] = detail });
            bool mutating = profile != "smoke" && profile != "feature";
            string activeSave = ReignServerClient.ActiveNativeSaveName();
            bool authorizedSave = !mutating || (Same(activeSave, disposableSaveName)
                && IsRebellionHarnessSave(disposableSaveName));

            check("campaign_loaded", TaleWorlds.CampaignSystem.Campaign.Current != null && Hero.MainHero != null,
                "A loaded Bannerlord campaign and main hero are required.");
            check("behavior_registered", Instance == this,
                "The save-authoritative rebellion behavior is registered.");
            check("profile_is_distinct", !string.IsNullOrWhiteSpace(profile),
                "The harness records and evaluates the requested profile rather than a shared smoke alias.");
            check("exact_disposable_save", authorizedSave,
                mutating
                    ? "The supplied save must equal Bannerlord's active loaded Reign_/ReignTest_ disposable slot."
                    : "This profile is observational and does not create fixture state.");

            JObject evidence = new JObject();
            if (authorizedSave)
            {
                switch (profile)
                {
                    case "smoke":
                        AddFeatureContractAssertions(assertions);
                        break;
                    case "feature":
                        AddFeatureContractAssertions(assertions);
                        check("saved_collections", _plots != null && _pledges != null
                            && _summonses != null && _movements != null && _memberships != null,
                            "Every preparation and civil-war collection is save-authoritative.");
                        check("single_player_plot", _plots.Count(x => x != null
                            && Same(x.PlayerHeroStringId, Hero.MainHero.StringId)
                            && !IsTerminalPlotStatus(x.Status)) <= 1,
                            "At most one unresolved preparation plot belongs to the player.");
                        break;
                    case "pledges":
                        EnsurePledgeFixtures(fixture, now);
                        List<ReignRebellionPledgeRecord> pledges = FixturePledges(fixture).ToList();
                        int pledgeCountBeforeRetry = pledges.Count;
                        EnsurePledgeFixtures(fixture, now);
                        check("three_decisions", new[] { "pledge", "refuse", "report" }
                            .All(decision => pledges.Any(x => Same(x.Decision, decision))),
                            "Pledge, refusal, and report persist as independent decisions.");
                        check("direct_and_mail_channels", pledges.Any(x => Same(x.RequestChannel, "conversation"))
                            && pledges.Any(x => Same(x.RequestChannel, "correspondence")),
                            "Direct and correspondence recruitment channels are represented.");
                        check("one_decision_per_clan", pledges.Select(x => x.ClanStringId)
                            .Distinct(StringComparer.OrdinalIgnoreCase).Count() == pledges.Count,
                            "A clan has at most one preparation decision in the fixture.");
                        check("action_id_idempotency", pledges.Select(x => x.ActionId)
                            .Distinct(StringComparer.OrdinalIgnoreCase).Count() == pledges.Count,
                            "Stable action ids make repeated delivery idempotent.");
                        check("duplicate_retry_idempotent", FixturePledges(fixture).Count()
                            == pledgeCountBeforeRetry,
                            "Replaying the same prepared delivery leaves exactly one saved decision per clan.");
                        check("concurrent_requests", pledges.Count(x => Math.Abs(x.RequestedDay - now) < 0.001f)
                            >= 3,
                            "Multiple clan decisions share the same request boundary.");
                        check("out_of_order_delivery", pledges.OrderBy(x => x.PledgeId,
                                StringComparer.OrdinalIgnoreCase).Select(x => x.RespondedDay)
                            .Zip(pledges.OrderBy(x => x.PledgeId, StringComparer.OrdinalIgnoreCase)
                                .Select(x => x.RespondedDay).Skip(1), (left, right) => right < left).Any(x => x),
                            "Concurrent correspondence decisions can arrive out of request order without changing identity.");
                        check("delayed_mail_response", pledges.Any(x => Same(x.RequestChannel, "correspondence")
                            && x.RespondedDay > x.RequestedDay),
                            "At least one correspondence decision is delivered after its request.");
                        check("foreign_pledge", pledges.Any(x => Same(x.Decision, "pledge")
                            && x.OriginalKingdomStringId.StartsWith("foreign_", StringComparison.Ordinal)),
                            "A foreign-clan pledge remains keyed to its original realm.");
                        evidence["pledges"] = JArray.FromObject(pledges.Select(x => new
                        {
                            x.PledgeId, x.ClanStringId, x.Decision, x.RequestChannel,
                            x.RequestedDay, x.RespondedDay, x.ActionId, x.OriginalKingdomStringId
                        }));
                        break;
                    case "reporting":
                        EnsureReportingFixture(fixture, now);
                        ReignRebellionPlotRecord reportPlot = FixturePlot(fixture);
                        List<ReignRebellionPledgeRecord> reports = FixturePledges(fixture)
                            .Where(x => Same(x.Decision, "report")).ToList();
                        check("named_informer", !string.IsNullOrWhiteSpace(reportPlot?.InformerHeroStringId)
                            && !string.IsNullOrWhiteSpace(reportPlot?.InformerClanStringId),
                            "The report records the informing lord and clan by stable id.");
                        check("report_travel", reportPlot != null
                            && Math.Abs(reportPlot.ReportDeliveryDay - reportPlot.ReportDispatchDay - 1f) < 0.001f,
                            "The named report travels for one campaign day before reaching the ruler.");
                        check("single_report", reports.Count == 1 && reports[0].ReportedToRuler,
                            "Only one clan decision reports the conspiracy.");
                        check("recruitment_blocked_after_report", reportPlot != null
                            && !IsPreparationRecruitmentOpen(reportPlot),
                            "Secret recruitment closes as soon as the conspiracy is reported.");
                        evidence["plot"] = JObject.FromObject(reportPlot);
                        break;
                    case "summons":
                        EnsureSummonsFixture(fixture, now);
                        ReignRebellionSummonsRecord summons = FixtureSummonses(fixture).Single();
                        check("named_ruler_summons", !string.IsNullOrWhiteSpace(summons.RulerHeroStringId)
                            && !string.IsNullOrWhiteSpace(summons.InformerHeroStringId),
                            "The summons names both the ruler and informer.");
                        check("delivery_after_dispatch", summons.DeliveryDay > summons.IssuedDay,
                            "The summons is delivered after it is issued.");
                        check("seven_days_from_delivery", Math.Abs(summons.DeadlineDay
                            - summons.DeliveryDay - 7f) < 0.001f,
                            "The response deadline is exactly seven campaign days after delivery.");
                        check("deadline_minus_epsilon_open", !PreparationDeadlineExpired(
                            PreviousRepresentableCampaignDay(summons.DeadlineDay),
                            summons.DeadlineDay),
                            "At the immediately preceding representable campaign instant the summons remains answerable.");
                        check("exact_deadline_expired", PreparationDeadlineExpired(
                            summons.DeadlineDay, summons.DeadlineDay),
                            "The production greater-than-or-equal boundary expires the summons at the exact deadline.");
                        check("deadline_plus_epsilon_expired", PreparationDeadlineExpired(
                            summons.DeadlineDay + 0.001f, summons.DeadlineDay),
                            "Immediately after the deadline the summons remains expired.");
                        check("current_ruler_identity_stable", Same(summons.RulerHeroStringId,
                            FixturePlot(fixture)?.RulerHeroStringId),
                            "The summons stays keyed to the plot's challenged ruler identity.");
                        check("summons_idempotent", FixtureSummonses(fixture).Count() == 1,
                            "Retrying fixture preparation does not duplicate the summons.");
                        check("explicit_answers", NormalizeSummonsResponse("recant") == "renounce"
                            && NormalizeSummonsResponse("rebel") == "defy"
                            && string.IsNullOrWhiteSpace(NormalizeSummonsResponse("perhaps")),
                            "Renounce and defy are explicit and ambiguous responses fail closed.");
                        evidence["summons"] = JObject.FromObject(summons);
                        break;
                    case "verdicts":
                        EnsureVerdictFixtures(fixture, now);
                        List<ReignRebellionSummonsRecord> verdicts = FixtureSummonses(fixture)
                            .Where(x => x.ConsequenceApplied).ToList();
                        check("four_verdicts", new[] { "pardon", "imprisonment", "exile", "execution" }
                            .All(verdict => verdicts.Any(x => Same(x.RulerVerdict, verdict))),
                            "Pardon, imprisonment, exile, and execution outcomes are represented.");
                        check("death_disabled_fallback", verdicts.Any(x => Same(x.SummonsId,
                            fixture + "_summons_execution_fallback")
                            && Same(x.RulerVerdict, "execution")
                            && Same(x.Consequence, "imprisonment")),
                            "A disabled or failed execution falls back to imprisonment.");
                        check("verdicts_terminal", verdicts.All(x => x.ConsequenceApplied
                            && x.AnsweredDay >= x.DeliveryDay),
                            "Every applied judgment is terminal and occurs after delivery.");
                        evidence["verdicts"] = JArray.FromObject(verdicts.Select(x => new
                        { x.SummonsId, x.RulerVerdict, x.Consequence, x.ConsequenceApplied }));
                        break;
                    case "declaration":
                        EnsureDeclarationFixture(fixture, now);
                        ReignRebellionMovementRecord movement = FixtureMovements(fixture).Single();
                        List<ReignRebellionMembershipRecord> members = FixtureMemberships(fixture).ToList();
                        check("ordinary_and_forced_causes", Same(movement.ResolutionCause, "player_declaration")
                            && movement.DemandJson.Contains("summons_deadline_ignored"),
                            "The fixture records both ordinary declaration and forced-deadline activation causes.");
                        check("renounce_and_defy_transitions", NormalizeSummonsResponse("renounce") == "renounce"
                            && NormalizeSummonsResponse("defy") == "defy",
                            "The two explicit summons answers select submission or immediate declaration.");
                        check("ignored_deadline_forces_declaration",
                            movement.ExecutionSnapshotJson.Contains("summons_deadline_ignored"),
                            "Ignoring the exact delivered deadline is retained as a forced declaration cause.");
                        check("pledged_clans_committed", members.Count >= 2
                            && members.All(x => x.IsCommitted && Same(x.Side, "rebel")),
                            "Every pledged fixture clan is committed to the rebel side.");
                        check("holdings_manifest", !string.IsNullOrWhiteSpace(movement.OriginalStrongholdIdsCsv)
                            && movement.ExecutionSnapshotJson.Contains("boundSettlementIds"),
                            "The activation receipt includes current fortifications and bound settlements.");
                        check("foreign_origin_preserved", members.Any(x => x.OriginalKingdomStringId
                            .StartsWith("foreign_", StringComparison.Ordinal)),
                            "A pledged foreign clan retains its origin for reunification.");
                        evidence["movement"] = JObject.FromObject(movement);
                        evidence["memberships"] = JArray.FromObject(members);
                        break;
                    case "resolution":
                        EnsureResolutionFixtures(fixture, now);
                        List<ReignRebellionMovementRecord> resolutions = FixtureMovements(fixture).ToList();
                        List<ReignRebellionMembershipRecord> fates = FixtureMemberships(fixture).ToList();
                        check("both_leader_outcomes", resolutions.Any(x => Same(x.WinningSide, "rebel"))
                            && resolutions.Any(x => Same(x.WinningSide, "loyalist")),
                            "Rebel and loyalist leadership-bound outcomes are both represented.");
                        check("capture_death_deposition_causes", new[] { "rebel_leader_captured",
                            "challenged_ruler_killed", "challenged_ruler_deposed" }
                            .All(cause => resolutions.Any(x => Same(x.ResolutionCause, cause))),
                            "Capture, ruler death, and ruler deposition causes are explicit.");
                        check("realm_cooldown", resolutions.All(x => Math.Abs(x.CooldownUntilDay
                            - x.UpdatedDay - RealmCooldownDays) < 0.001f),
                            "Every resolution applies the exact 63-day realm cooldown.");
                        check("mercy_outcomes", new[] { "freedom", "imprisonment", "execution" }
                            .All(outcome => fates.Any(x => Same(x.FateOutcome, outcome) && x.FateApplied)),
                            "Freedom, imprisonment, and execution mercy outcomes are recorded.");
                        check("capture_grace_boundary", !LeadershipCaptureExpired(100f, 102.999f)
                            && LeadershipCaptureExpired(100f, 103f),
                            "Leadership captivity resolves only at the exact three-day grace boundary.");
                        check("flight_or_release_clears_capture", NextLeadershipCaptureDay(100f, false, 101f) < 0f
                            && Math.Abs(NextLeadershipCaptureDay(-1f, true, 101f) - 101f) < 0.001f,
                            "Escape or release clears the capture clock while a new capture starts it once.");
                        check("invalid_and_competing_rejected",
                            DirectedMovementPreflightFailure(false, true, false, false).Length > 0
                            && DirectedMovementPreflightFailure(true, false, false, false).Length > 0
                            && DirectedMovementPreflightFailure(true, true, true, false)
                                .Contains("already has an active rebellion")
                            && DirectedMovementPreflightFailure(true, true, false, true)
                                .Contains("63-day"),
                            "Invalid leaders, invalid vassal state, competing rebellions, and cooldown attempts fail closed.");
                        evidence["resolutions"] = JArray.FromObject(resolutions);
                        evidence["fates"] = JArray.FromObject(fates);
                        break;
                    case "save_prepare":
                        EnsurePledgeFixtures(fixture, now);
                        EnsureSummonsFixture(fixture, now);
                        EnsureDeclarationFixture(fixture, now);
                        FixturePlot(fixture).CorrelationId = fixture + "_persistence_marker";
                        check("persistence_marker_staged", FixturePlot(fixture)?.CorrelationId
                            == fixture + "_persistence_marker",
                            "Run-owned plot, pledge, summons, movement, and membership state is staged for native save.");
                        check("fixture_graph_complete", FixturePledges(fixture).Count() >= 4
                            && FixtureSummonses(fixture).Any() && FixtureMovements(fixture).Any()
                            && FixtureMemberships(fixture).Any(),
                            "All save-authoritative fixture collections contain linked records.");
                        evidence["persistenceMarker"] = fixture + "_persistence_marker";
                        break;
                    case "save_verify":
                        ReignRebellionPlotRecord savedPlot = FixturePlot(fixture);
                        check("persistence_marker_loaded", savedPlot?.CorrelationId
                            == fixture + "_persistence_marker",
                            "The run-owned marker survived native Bannerlord save and reload.");
                        check("linked_state_loaded", FixturePledges(fixture).Count() >= 4
                            && FixtureSummonses(fixture).Any() && FixtureMovements(fixture).Any()
                            && FixtureMemberships(fixture).Any(),
                            "Linked pledge, summons, movement, and membership records survived reload.");
                        check("referential_integrity", savedPlot != null
                            && FixturePledges(fixture).All(x => Same(x.PlotId, savedPlot.PlotId))
                            && FixtureSummonses(fixture).All(x => Same(x.PlotId, savedPlot.PlotId)),
                            "Reloaded preparation records retain exact plot references.");
                        evidence["persistenceMarker"] = savedPlot?.CorrelationId ?? string.Empty;
                        break;
                    case "native_setup":
                        Kingdom setupRealm = Clan.PlayerClan?.Kingdom;
                        Clan setupRuler = PrepareHarnessNonPlayerRuler(setupRealm);
                        string setupCaseId = command?.Value<string>("caseId") ?? string.Empty;
                        string setupTargetHeroId = command?.Value<string>("targetHeroId") ?? string.Empty;
                        bool correspondenceCase = Same(setupCaseId, "RB-LANG-004")
                            || Same(setupCaseId, "RB-LANG-005")
                            || Same(setupCaseId, "RB-LANG-006");
                        Hero correspondenceTarget = correspondenceCase
                            ? FindHero(setupTargetHeroId)
                            : null;
                        if (correspondenceTarget?.Clan != null
                            && correspondenceTarget.Clan.Leader == correspondenceTarget
                            && correspondenceTarget.Clan != Clan.PlayerClan
                            && correspondenceTarget.Clan != setupRuler)
                            MoveClanToKingdom(correspondenceTarget.Clan, setupRealm);
                        bool correspondenceTargetPreviouslyKnown = correspondenceTarget != null
                            && (correspondenceTarget.IsKnownToPlayer || correspondenceTarget.HasMet);
                        if (correspondenceCase && correspondenceTarget != null
                            && !correspondenceTargetPreviouslyKnown)
                            correspondenceTarget.SetHasMet();
                        List<Clan> setupEligibleClans = (setupRealm?.Clans ?? Enumerable.Empty<Clan>())
                            .Where(x => x != null && x != Clan.PlayerClan && x != setupRuler
                                && x.Leader != null && !x.Leader.IsDead && !x.Leader.IsPrisoner)
                            .ToList();
                        JArray reportRelationFixtures = new JArray();
                        bool reportCase = Same(setupCaseId, "RB-LANG-003")
                            || Same(setupCaseId, "RB-LANG-006")
                            || Same(setupCaseId, "RB-NATIVE-001");
                        if (reportCase
                            && Hero.MainHero != null && setupRuler?.Leader != null)
                        {
                            foreach (Hero reportTarget in setupEligibleClans
                                .Select(x => x.Leader)
                                .Where(x => x != null && x.IsAlive && !x.IsChild && !x.IsPrisoner
                                    && x.Clan != null && x.Clan.Leader == x)
                                .Distinct())
                            {
                                int priorPlayerRelation = Hero.MainHero.GetRelation(reportTarget);
                                int priorRulerRelation = setupRuler.Leader.GetRelation(reportTarget);
                                int priorHonor = reportTarget.GetTraitLevel(DefaultTraits.Honor);
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero,
                                    reportTarget, -10 - priorPlayerRelation, false);
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(setupRuler.Leader,
                                    reportTarget, 35 - priorRulerRelation, false);
                                reportTarget.SetTraitLevel(DefaultTraits.Honor, Math.Max(1, priorHonor));
                                reportRelationFixtures.Add(new JObject
                                {
                                    ["heroId"] = reportTarget.StringId,
                                    ["clanId"] = reportTarget.Clan.StringId,
                                    ["isClanLeader"] = reportTarget.Clan.Leader == reportTarget,
                                    ["priorPlayerRelation"] = priorPlayerRelation,
                                    ["priorRulerRelation"] = priorRulerRelation,
                                    ["priorHonor"] = priorHonor,
                                    ["playerRelation"] = Hero.MainHero.GetRelation(reportTarget),
                                    ["rulerRelation"] = setupRuler.Leader.GetRelation(reportTarget),
                                    ["honor"] = reportTarget.GetTraitLevel(DefaultTraits.Honor)
                                });
                            }
                        }
                        check("player_is_non_ruling_vassal", setupRealm != null
                            && setupRuler != null && setupRealm.RulingClan == setupRuler
                            && Clan.PlayerClan?.Kingdom == setupRealm
                            && Clan.PlayerClan != setupRealm.RulingClan,
                            "The guarded fixture makes the player a non-ruling vassal under a real active NPC ruler.");
                        check("ruler_identity_is_native", setupRuler?.Leader != null
                            && !setupRuler.Leader.IsDead && !setupRuler.Leader.IsPrisoner,
                            "The fixture ruler is a living, free native clan leader.");
                        evidence["rulerHeroId"] = setupRuler?.Leader?.StringId ?? string.Empty;
                        evidence["rulerClanId"] = setupRuler?.StringId ?? string.Empty;
                        check("correspondence_target_is_exact_enrolled_clan_leader", !correspondenceCase
                            || (correspondenceTarget != null
                                && correspondenceTarget.Clan?.Leader == correspondenceTarget
                                && correspondenceTarget.Clan?.Kingdom == setupRealm
                                && (correspondenceTarget.IsKnownToPlayer || correspondenceTarget.HasMet)),
                            "Correspondence cases enroll the exact known recipient clan leader under the fixture sovereign before native mail dispatch.");
                        evidence["correspondenceTargetHeroId"] = correspondenceTarget?.StringId ?? string.Empty;
                        evidence["correspondenceTargetClanId"] = correspondenceTarget?.Clan?.StringId ?? string.Empty;
                        evidence["correspondenceTargetPreviouslyKnown"] = correspondenceTargetPreviouslyKnown;
                        evidence["correspondenceTargetKnownAfterFixture"] = correspondenceTarget != null
                            && (correspondenceTarget.IsKnownToPlayer || correspondenceTarget.HasMet);
                        check("report_target_relation_fixture", !reportCase
                            || (reportRelationFixtures.Count > 0
                                && reportRelationFixtures.All(x => x.Value<int>("playerRelation") <= -10
                                && x.Value<int>("rulerRelation") >= 35)),
                            "Report cases use a moderate guarded sovereign relationship and mild player distrust rather than extreme calibration.");
                        check("report_target_clan_leader_fixture", !reportCase
                            || (reportRelationFixtures.Count > 0
                                && reportRelationFixtures.All(x => x.Value<bool>("isClanLeader"))),
                            "Every report target is a living, free active noble clan leader accepted by the production pledge action.");
                        check("report_target_honor_fixture", !reportCase
                            || (reportRelationFixtures.Count > 0
                                && reportRelationFixtures.All(x => x.Value<int>("honor") >= 1)),
                            "Report cases temporarily establish an honorable duty witness while leaving the final decision to natural dialogue.");
                        evidence["eligibleTargetHeroIds"] = new JArray((setupEligibleClans
                            .Select(x => x.Leader.StringId).Take(12)) ?? Enumerable.Empty<string>());
                        evidence["reportRelationFixtures"] = reportRelationFixtures;
                        break;
                    case "language_observe":
                        string expectedDecision = command?.Value<string>("expectedDecision") ?? string.Empty;
                        string expectedChannel = command?.Value<string>("expectedChannel") ?? string.Empty;
                        ReignRebellionPledgeRecord observed = _pledges
                            .Where(x => x != null && !x.PledgeId.StartsWith("rebellion_test_",
                                StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(x => x.RespondedDay).FirstOrDefault();
                        check("production_decision_recorded", observed != null
                            && Same(observed.Decision, expectedDecision),
                            "The production dialogue/correspondence action persisted the expected lord decision.");
                        check("production_channel_recorded", observed != null
                            && Same(observed.RequestChannel, expectedChannel),
                            "The persisted decision identifies the visible production channel.");
                        check("one_decision_for_observed_clan", observed != null
                            && _pledges.Count(x => x != null && Same(x.PlotId, observed.PlotId)
                                && Same(x.ClanStringId, observed.ClanStringId)) == 1,
                            "The observed clan owns exactly one decision for the plot.");
                        evidence["observedPledge"] = observed == null ? null : JObject.FromObject(observed);
                        break;
                    case "travel_prepare":
                        ReignRebellionPledgeRecord reportDecision = _pledges
                            .Where(x => x != null && Same(x.Decision, "report"))
                            .OrderByDescending(x => x.RespondedDay).FirstOrDefault();
                        ReignRebellionPlotRecord travelPlot = _plots.FirstOrDefault(x => x != null
                            && reportDecision != null && Same(x.PlotId, reportDecision.PlotId));
                        check("natural_report_recorded", reportDecision != null
                            && Same(reportDecision.RequestChannel, "individual_chat")
                            && !string.IsNullOrWhiteSpace(reportDecision.ActionId),
                            "A production Individual Chat action recorded the named lord's report without a direct fixture decision.");
                        check("report_travel_staged", travelPlot != null
                            && !string.IsNullOrWhiteSpace(travelPlot.InformerHeroStringId)
                            && Math.Abs(travelPlot.ReportDeliveryDay
                                - travelPlot.ReportDispatchDay - 1f) < 0.001f,
                            "The production report has exactly one native campaign day of travel.");
                        if (travelPlot != null)
                            travelPlot.CorrelationId = fixture + "_native_travel_marker";
                        evidence["plot"] = travelPlot == null ? null : JObject.FromObject(travelPlot);
                        evidence["reportDecision"] = reportDecision == null ? null
                            : JObject.FromObject(reportDecision);
                        break;
                    case "travel_verify":
                        ReignRebellionPlotRecord reloadedTravelPlot = _plots.FirstOrDefault(x => x != null
                            && Same(x.CorrelationId, fixture + "_native_travel_marker"));
                        ReignRebellionSummonsRecord deliveredSummons = _summonses
                            .Where(x => x != null && reloadedTravelPlot != null
                                && Same(x.PlotId, reloadedTravelPlot.PlotId))
                            .OrderByDescending(x => x.IssuedDay).FirstOrDefault();
                        check("native_marker_reloaded", reloadedTravelPlot != null,
                            "The exact production plot and report marker survived native save/reload.");
                        check("summons_issued_after_report_travel", deliveredSummons != null
                            && reloadedTravelPlot != null
                            && deliveredSummons.IssuedDay >= reloadedTravelPlot.ReportDeliveryDay,
                            "The current ruler issued the summons only after the report completed travel.");
                        check("summons_delivered_natively", deliveredSummons != null
                            && CurrentDay() >= deliveredSummons.DeliveryDay
                            && deliveredSummons.DeliveryDay > deliveredSummons.IssuedDay,
                            "Native time delivered the ruler's summons after its own travel interval.");
                        check("summons_deadline_from_delivery", deliveredSummons != null
                            && Math.Abs(deliveredSummons.DeadlineDay
                                - deliveredSummons.DeliveryDay - 7f) < 0.001f,
                            "The seven-day answer deadline is anchored to delivery, not dispatch.");
                        check("summons_identity_preserved", deliveredSummons != null
                            && Same(deliveredSummons.RulerHeroStringId,
                                FindKingdom(reloadedTravelPlot?.ParentKingdomStringId)?.Leader?.StringId)
                            && Same(deliveredSummons.InformerHeroStringId,
                                reloadedTravelPlot?.InformerHeroStringId),
                            "Reloaded summons identity names the current ruler and original informer.");
                        evidence["plot"] = reloadedTravelPlot == null ? null
                            : JObject.FromObject(reloadedTravelPlot);
                        evidence["summons"] = deliveredSummons == null ? null
                            : JObject.FromObject(deliveredSummons);
                        break;
                    case "mixed_transfer":
                        JObject transfer = RunMixedHoldingTransferHarness();
                        foreach (JObject assertion in transfer["assertions"] as JArray ?? new JArray())
                            assertions.Add(assertion);
                        evidence["mixedTransfer"] = transfer;
                        break;
                    case "foreign_reintegration":
                        JObject reintegration = RunForeignClanReintegrationHarness();
                        foreach (JObject assertion in reintegration["assertions"] as JArray ?? new JArray())
                            assertions.Add(assertion);
                        evidence["foreignReintegration"] = reintegration;
                        break;
                    case "cleanup":
                        int removed = RemoveHarnessFixtures(fixture);
                        check("fixture_records_removed", removed > 0,
                            "Cleanup removed at least one exact run-owned fixture record.");
                        check("exact_namespace_clean", FixtureRecordCount(fixture) == 0,
                            "No records remain in the exact fixture namespace.");
                        evidence["removedRecordCount"] = removed;
                        break;
                }
            }

            bool passed = assertions.All(x => x.Value<bool>("passed"));
            return new JObject
            {
                ["schemaVersion"] = 2,
                ["scenario"] = "rebellion_release_acceptance",
                ["profile"] = profile,
                ["fixtureRunId"] = fixtureRunId,
                ["fixtureNamespace"] = fixture,
                ["saveIdentifier"] = activeSave,
                ["disposableSaveArmed"] = authorizedSave,
                ["campaignId"] = ReignCampaignIdentity.CurrentCampaignId(),
                ["worldDay"] = now,
                ["preState"] = before,
                ["postState"] = FixtureCounts(fixture),
                ["evidence"] = evidence,
                ["assertions"] = new JArray(assertions),
                ["passed"] = passed,
                ["cleanupStatus"] = profile == "cleanup"
                    ? passed ? "removed_exact_fixture_state" : "cleanup_failed"
                    : "not_requested",
                ["error"] = passed ? string.Empty : "One or more rebellion profile assertions failed."
            };
        }

        private static Clan PrepareHarnessNonPlayerRuler(Kingdom kingdom)
        {
            if (kingdom == null || Clan.PlayerClan == null) return null;
            if (kingdom.RulingClan != null && kingdom.RulingClan != Clan.PlayerClan
                && kingdom.RulingClan.Leader != null && !kingdom.RulingClan.Leader.IsDead
                && !kingdom.RulingClan.Leader.IsPrisoner)
                return kingdom.RulingClan;
            Clan replacement = kingdom.Clans
                .Where(x => x != null && x != Clan.PlayerClan && !x.IsEliminated
                    && !x.IsUnderMercenaryService && !x.IsMinorFaction
                    && x.Leader != null && x.Leader.IsLord && !x.Leader.IsDead
                    && !x.Leader.IsPrisoner)
                .OrderByDescending(x => x.Tier).ThenBy(x => x.StringId)
                .FirstOrDefault();
            if (replacement != null) ChangeRulingClanAction.Apply(kingdom, replacement);
            return replacement;
        }

        private JObject RunMixedHoldingTransferHarness()
        {
            List<JObject> assertions = new List<JObject>();
            Action<string, bool, string> check = (name, passed, detail) => assertions.Add(
                new JObject { ["name"] = name, ["passed"] = passed, ["detail"] = detail });
            Kingdom parent = Clan.PlayerClan?.Kingdom;
            Clan rulerClan = PrepareHarnessNonPlayerRuler(parent);
            Hero ruler = rulerClan?.Leader;
            if (parent == null || ruler == null)
            {
                check("native_ruler_available", false,
                    "A real NPC ruler is required for the mixed-holding transfer.");
                return new JObject { ["assertions"] = new JArray(assertions) };
            }

            Town town = Clan.PlayerClan.Fiefs.FirstOrDefault(x => x?.Settlement?.IsTown == true);
            Town castle = Clan.PlayerClan.Fiefs.FirstOrDefault(x => x?.Settlement?.IsCastle == true);
            if (town == null)
            {
                Settlement candidate = Settlement.All.FirstOrDefault(x => x?.IsTown == true
                    && x.MapFaction == parent && x.OwnerClan != Clan.PlayerClan);
                if (candidate != null)
                {
                    ChangeOwnerOfSettlementAction.ApplyByGift(candidate, Hero.MainHero);
                    town = candidate.Town;
                }
            }
            if (castle == null)
            {
                Settlement candidate = Settlement.All.FirstOrDefault(x => x?.IsCastle == true
                    && x.MapFaction == parent && x.OwnerClan != Clan.PlayerClan);
                if (candidate != null)
                {
                    ChangeOwnerOfSettlementAction.ApplyByGift(candidate, Hero.MainHero);
                    castle = candidate.Town;
                }
            }
            List<Village> villages = new[] { town, castle }.Where(x => x != null)
                .SelectMany(x => x.Settlement.BoundVillages)
                .Where(x => x != null).Distinct().ToList();
            check("mixed_holding_precondition", town != null && castle != null && villages.Count > 0,
                "The exact disposable fixture contains a player town, player castle, and their bound villages.");
            if (town != null && castle != null && villages.Count > 0)
            {
                float now = CurrentDay();
                string id = "rebellion_test_mixed_transfer_" + Guid.NewGuid().ToString("N");
                ReignRebellionPlotRecord plot = new ReignRebellionPlotRecord
                {
                    PlotId = id + "_plot", PlayerHeroStringId = Hero.MainHero.StringId,
                    PlayerClanStringId = Clan.PlayerClan.StringId,
                    ParentKingdomStringId = parent.StringId,
                    RulerHeroStringId = ruler.StringId, Status = "summons_delivered",
                    CreatedDay = now, UpdatedDay = now
                };
                ReignRebellionSummonsRecord summons = new ReignRebellionSummonsRecord
                {
                    SummonsId = id + "_summons", PlotId = plot.PlotId,
                    RulerHeroStringId = ruler.StringId, Status = "delivered",
                    IssuedDay = now - 1f, DeliveryDay = now, DeadlineDay = now + 7f
                };
                _plots.Add(plot);
                _summonses.Add(summons);
                ApplyPreparationVerdict(plot, summons, ruler, "exile", out string result);
                check("town_transferred", town.Settlement.OwnerClan == rulerClan,
                    "The production exile verdict transferred the town to the current ruler's clan.");
                check("castle_transferred", castle.Settlement.OwnerClan == rulerClan,
                    "The production exile verdict transferred the castle to the current ruler's clan.");
                check("bound_villages_transferred", villages.All(x => x.Settlement.OwnerClan == rulerClan),
                    "Every village bound to the mixed holding follows the transferred fortification owner.");
                check("player_clan_exiled", Clan.PlayerClan.Kingdom == null,
                    "The production verdict removed the player clan from the realm after confiscation.");
                return new JObject
                {
                    ["assertions"] = new JArray(assertions), ["result"] = result,
                    ["townId"] = town.Settlement.StringId,
                    ["castleId"] = castle.Settlement.StringId,
                    ["boundVillageIds"] = new JArray(villages.Select(x => x.Settlement.StringId)),
                    ["newOwnerClanId"] = rulerClan.StringId
                };
            }
            return new JObject { ["assertions"] = new JArray(assertions) };
        }

        private JObject RunForeignClanReintegrationHarness()
        {
            List<JObject> assertions = new List<JObject>();
            Action<string, bool, string> check = (name, passed, detail) => assertions.Add(
                new JObject { ["name"] = name, ["passed"] = passed, ["detail"] = detail });
            Kingdom parent = Clan.PlayerClan?.Kingdom;
            Clan rulerClan = PrepareHarnessNonPlayerRuler(parent);
            Hero challengedRuler = rulerClan?.Leader;
            Clan foreignClan = Kingdom.All.Where(x => x != null && x != parent && !x.IsEliminated)
                .SelectMany(x => x.Clans).Where(x => x != null && !x.IsEliminated
                    && !x.IsUnderMercenaryService && !x.IsMinorFaction
                    && x.Leader != null && x.Leader.IsLord && !x.Leader.IsDead
                    && !x.Leader.IsPrisoner)
                .OrderByDescending(x => x.Heroes.Count).ThenBy(x => x.StringId)
                .FirstOrDefault();
            Kingdom origin = foreignClan?.Kingdom;
            List<Hero> livingFamily = foreignClan?.Heroes
                .Where(x => x != null && !x.IsDead).ToList() ?? new List<Hero>();
            check("foreign_fixture_available", parent != null && challengedRuler != null
                && foreignClan != null && origin != null && livingFamily.Count > 0,
                "A living foreign noble clan and family are required for reintegration proof.");
            if (parent == null || challengedRuler == null || foreignClan == null || origin == null)
                return new JObject { ["assertions"] = new JArray(assertions) };

            string recruitmentResult = string.Empty;
            string resolutionResult = string.Empty;
            bool declared = TryStartDirectedMovement(parent, Clan.PlayerClan,
                Enumerable.Empty<Clan>(), out string declarationResult);
            ReignRebellionMovementRecord movement = _movements.LastOrDefault(x => IsCivilWar(x)
                && x.IsPlayerLed && Same(x.LeaderClanStringId, Clan.PlayerClan.StringId));
            bool recruited = declared && movement != null
                && TryRecruitLord(Hero.MainHero, foreignClan.Leader, out recruitmentResult);
            bool joinedRebels = recruited && foreignClan.Kingdom != origin
                && Same(foreignClan.Kingdom?.StringId, movement.RebelKingdomStringId);
            bool resolved = joinedRebels
                && TrySurrender(challengedRuler, movement.MovementId, out resolutionResult);
            ReignRebellionMembershipRecord membership = movement == null ? null
                : MembersOf(movement).FirstOrDefault(x => Same(x.ClanStringId, foreignClan.StringId));
            check("foreign_clan_joined", joinedRebels && membership != null
                && Same(membership.OriginalKingdomStringId, origin.StringId),
                "The production recruitment path moved the clan into the civil war and preserved its origin.");
            check("foreign_clan_returned", resolved && foreignClan.Kingdom == origin,
                "Leadership-bound resolution returned the foreign clan to its exact origin realm.");
            check("living_family_reintegrated", resolved
                && livingFamily.All(x => x.Clan == foreignClan && x.MapFaction == origin),
                "Every living member of the clan follows the reintegrated clan back to its origin realm.");
            check("reunification_exactly_once", membership != null && membership.IsCommitted
                && Same(membership.OriginalKingdomStringId, origin.StringId)
                && foreignClan.Kingdom == origin,
                "The persisted membership retains one origin and a repeated resolution cannot move it elsewhere.");
            return new JObject
            {
                ["assertions"] = new JArray(assertions), ["foreignClanId"] = foreignClan.StringId,
                ["originKingdomId"] = origin.StringId,
                ["livingFamilyHeroIds"] = new JArray(livingFamily.Select(x => x.StringId)),
                ["declarationResult"] = declarationResult,
                ["recruitmentResult"] = recruitmentResult,
                ["resolutionResult"] = resolutionResult,
                ["movementId"] = movement?.MovementId ?? string.Empty
            };
        }

        private static void AddFeatureContractAssertions(List<JObject> assertions)
        {
            Action<string, bool, string> check = (name, passed, detail) => assertions.Add(
                new JObject { ["name"] = name, ["passed"] = passed, ["detail"] = detail });
            check("pledge_decisions", NormalizePreparationDecision("accept") == "pledge"
                && NormalizePreparationDecision("decline") == "refuse"
                && NormalizePreparationDecision("inform") == "report"
                && string.IsNullOrWhiteSpace(NormalizePreparationDecision("maybe")),
                "Pledge, refusal, and report normalize independently while ambiguity fails closed.");
            check("summons_answers", NormalizeSummonsResponse("recant") == "renounce"
                && NormalizeSummonsResponse("rebel") == "defy",
                "Renunciation and defiance are explicit answers.");
            check("four_verdicts", new[] { "pardon", "imprisonment", "exile", "execution" }
                .All(x => NormalizeRulerVerdict(x) == x),
                "All four ruler verdicts are available.");
            check("hard_constants", Math.Abs(RealmCooldownDays - 63f) < 0.001f
                && Math.Abs(LeadershipCaptureGraceDays - 3f) < 0.001f,
                "Resolution cooldown and leadership-capture grace are exact.");
        }

        private void EnsurePledgeFixtures(string fixture, float now)
        {
            ReignRebellionPlotRecord plot = EnsureFixturePlot(fixture, now);
            if (FixturePledges(fixture).Any()) return;
            string[] decisions = { "pledge", "refuse", "report", "pledge" };
            string[] channels = { "conversation", "correspondence", "correspondence", "correspondence" };
            float[] responseOffsets = { 0f, 3f, 1f, 2f };
            for (int i = 0; i < decisions.Length; i++)
            {
                _pledges.Add(new ReignRebellionPledgeRecord
                {
                    PledgeId = fixture + "_pledge_" + i,
                    PlotId = plot.PlotId,
                    LordHeroStringId = fixture + "_lord_" + i,
                    ClanStringId = fixture + "_clan_" + i,
                    OriginalKingdomStringId = i == 3 ? "foreign_" + fixture : plot.ParentKingdomStringId,
                    Decision = decisions[i],
                    DecisionReason = "Profile-owned deterministic decision " + decisions[i] + ".",
                    RequestedDay = now,
                    RespondedDay = now + responseOffsets[i],
                    ReportedToRuler = decisions[i] == "report",
                    RequestChannel = channels[i],
                    ActionId = fixture + "_action_" + i
                });
            }
        }

        private ReignRebellionPlotRecord EnsureFixturePlot(string fixture, float now)
        {
            ReignRebellionPlotRecord plot = FixturePlot(fixture);
            if (plot != null) return plot;
            plot = new ReignRebellionPlotRecord
            {
                PlotId = fixture + "_plot",
                PlayerHeroStringId = fixture + "_player",
                PlayerClanStringId = fixture + "_player_clan",
                ParentKingdomStringId = fixture + "_parent",
                RulerHeroStringId = fixture + "_ruler",
                Status = "planning",
                CreatedDay = now,
                UpdatedDay = now,
                CorrelationId = fixture + "_correlation"
            };
            _plots.Add(plot);
            return plot;
        }

        private void EnsureReportingFixture(string fixture, float now)
        {
            EnsurePledgeFixtures(fixture, now);
            ReignRebellionPlotRecord plot = FixturePlot(fixture);
            ReignRebellionPledgeRecord report = FixturePledges(fixture).Single(x => Same(x.Decision, "report"));
            plot.Status = "reported_in_transit";
            plot.InformerHeroStringId = report.LordHeroStringId;
            plot.InformerClanStringId = report.ClanStringId;
            plot.ReportDispatchDay = now;
            plot.ReportDeliveryDay = now + 1f;
            plot.ReportLetterId = fixture + "_report_letter";
            plot.UpdatedDay = now;
        }

        private void EnsureSummonsFixture(string fixture, float now)
        {
            EnsureReportingFixture(fixture, now);
            ReignRebellionPlotRecord plot = FixturePlot(fixture);
            if (FixtureSummonses(fixture).Any()) return;
            ReignRebellionSummonsRecord summons = new ReignRebellionSummonsRecord
            {
                SummonsId = fixture + "_summons",
                PlotId = plot.PlotId,
                RulerHeroStringId = plot.RulerHeroStringId,
                PlayerHeroStringId = plot.PlayerHeroStringId,
                InformerHeroStringId = plot.InformerHeroStringId,
                Status = "delivered",
                IssuedDay = now,
                DeliveryDay = now + 1f,
                DeadlineDay = now + 8f,
                LetterId = fixture + "_summons_letter"
            };
            _summonses.Add(summons);
            plot.SummonsId = summons.SummonsId;
            plot.Status = "summoned";
        }

        private void EnsureVerdictFixtures(string fixture, float now)
        {
            EnsureSummonsFixture(fixture, now);
            string[] verdicts = { "pardon", "imprisonment", "exile", "execution" };
            foreach (string verdict in verdicts)
            {
                string id = fixture + "_summons_" + verdict;
                if (_summonses.Any(x => Same(x.SummonsId, id))) continue;
                _summonses.Add(new ReignRebellionSummonsRecord
                {
                    SummonsId = id, PlotId = fixture + "_plot",
                    RulerHeroStringId = fixture + "_ruler",
                    PlayerHeroStringId = fixture + "_player",
                    InformerHeroStringId = fixture + "_lord_2",
                    Status = "answered", IssuedDay = now,
                    DeliveryDay = now + 1f, DeadlineDay = now + 8f,
                    AnsweredDay = now + 2f, PlayerResponse = "renounce",
                    RulerVerdict = verdict, Consequence = verdict,
                    ConsequenceApplied = true
                });
            }
            string fallbackId = fixture + "_summons_execution_fallback";
            if (!_summonses.Any(x => Same(x.SummonsId, fallbackId)))
                _summonses.Add(new ReignRebellionSummonsRecord
                {
                    SummonsId = fallbackId, PlotId = fixture + "_plot",
                    RulerHeroStringId = fixture + "_ruler",
                    PlayerHeroStringId = fixture + "_player",
                    InformerHeroStringId = fixture + "_lord_2",
                    Status = "answered", IssuedDay = now,
                    DeliveryDay = now + 1f, DeadlineDay = now + 8f,
                    AnsweredDay = now + 2f, PlayerResponse = "renounce",
                    RulerVerdict = "execution", Consequence = "imprisonment",
                    ConsequenceApplied = true
                });
        }

        private void EnsureDeclarationFixture(string fixture, float now)
        {
            EnsurePledgeFixtures(fixture, now);
            if (FixtureMovements(fixture).Any()) return;
            ReignRebellionMovementRecord movement = new ReignRebellionMovementRecord
            {
                MovementId = fixture + "_movement",
                ParentKingdomStringId = fixture + "_parent",
                RebelKingdomStringId = fixture + "_rebels",
                LeaderClanStringId = fixture + "_player_clan",
                LeaderHeroStringId = fixture + "_player",
                OriginalRulerHeroStringId = fixture + "_ruler",
                Stage = "civil_war",
                IsPlayerLed = true,
                CreatedDay = now,
                UpdatedDay = now,
                CivilWarStartedDay = now,
                ResolutionCause = "player_declaration",
                OriginalStrongholdIdsCsv = fixture + "_town," + fixture + "_castle",
                ExecutionSnapshotJson = new JObject
                {
                    ["activationCauses"] = new JArray("player_declaration", "summons_deadline_ignored"),
                    ["strongholdIds"] = new JArray(fixture + "_town", fixture + "_castle"),
                    ["boundSettlementIds"] = new JArray(fixture + "_village_a", fixture + "_village_b")
                }.ToString(Newtonsoft.Json.Formatting.None),
                DemandJson = new JObject
                {
                    ["ordinaryCause"] = "player_declaration",
                    ["forcedCause"] = "summons_deadline_ignored"
                }.ToString(Newtonsoft.Json.Formatting.None),
                ExecutionPhase = "verified"
            };
            _movements.Add(movement);
            for (int i = 0; i < 2; i++)
                _memberships.Add(new ReignRebellionMembershipRecord
                {
                    MovementId = movement.MovementId,
                    ClanStringId = fixture + "_clan_" + i,
                    LeaderHeroStringId = fixture + "_lord_" + i,
                    Side = "rebel", IsCommitted = true,
                    OriginalKingdomStringId = i == 1 ? "foreign_" + fixture : movement.ParentKingdomStringId,
                    JoinedDay = now, JoinedBy = "prepared_pledge"
                });
        }

        private void EnsureResolutionFixtures(string fixture, float now)
        {
            RemoveHarnessFixtures(fixture);
            string[] causes = { "rebel_leader_captured", "challenged_ruler_killed", "challenged_ruler_deposed" };
            for (int i = 0; i < causes.Length; i++)
            {
                string movementId = fixture + "_resolution_" + i;
                _movements.Add(new ReignRebellionMovementRecord
                {
                    MovementId = movementId,
                    ParentKingdomStringId = fixture + "_parent",
                    RebelKingdomStringId = fixture + "_rebels_" + i,
                    LeaderClanStringId = fixture + "_leader_clan",
                    LeaderHeroStringId = fixture + "_rebel_leader",
                    OriginalRulerHeroStringId = fixture + "_ruler",
                    Stage = "resolved", ResolutionApplied = true,
                    WinningSide = i == 0 ? "loyalist" : "rebel",
                    Resolution = i == 0 ? "loyalist_victory" : "rebel_victory",
                    ResolutionCause = causes[i], UpdatedDay = now,
                    CooldownUntilDay = now + RealmCooldownDays,
                    ExecutionPhase = "resolved"
                });
            }
            string[] outcomes = { "freedom", "imprisonment", "execution" };
            for (int i = 0; i < outcomes.Length; i++)
                _memberships.Add(new ReignRebellionMembershipRecord
                {
                    MovementId = fixture + "_resolution_" + (i % causes.Length),
                    ClanStringId = fixture + "_fate_clan_" + i,
                    LeaderHeroStringId = fixture + "_fate_lord_" + i,
                    Side = "loyalist", IsDefeated = true,
                    FateOutcome = outcomes[i], FateApplied = true,
                    FateRoll = 1 + i * 33, FateScore = 10 + i * 40
                });
        }

        private bool IsPreparationRecruitmentOpen(ReignRebellionPlotRecord plot)
            => plot != null && Same(plot.Status, "planning")
                && string.IsNullOrWhiteSpace(plot.InformerHeroStringId)
                && string.IsNullOrWhiteSpace(plot.SummonsId);

        private static bool IsRebellionHarnessSave(string value)
            => !string.IsNullOrWhiteSpace(value)
                && (value.StartsWith("Reign_", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("ReignTest_", StringComparison.OrdinalIgnoreCase));

        private ReignRebellionPlotRecord FixturePlot(string fixture)
            => _plots.FirstOrDefault(x => x != null && Same(x.PlotId, fixture + "_plot"));
        private IEnumerable<ReignRebellionPledgeRecord> FixturePledges(string fixture)
            => _pledges.Where(x => x != null && (x.PledgeId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));
        private IEnumerable<ReignRebellionSummonsRecord> FixtureSummonses(string fixture)
            => _summonses.Where(x => x != null && (x.SummonsId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));
        private IEnumerable<ReignRebellionMovementRecord> FixtureMovements(string fixture)
            => _movements.Where(x => x != null && (x.MovementId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));
        private IEnumerable<ReignRebellionMembershipRecord> FixtureMemberships(string fixture)
            => _memberships.Where(x => x != null && (x.MovementId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));

        private int FixtureRecordCount(string fixture)
            => _plots.Count(x => x != null && (x.PlotId ?? string.Empty).StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase))
                + FixturePledges(fixture).Count() + FixtureSummonses(fixture).Count()
                + FixtureMovements(fixture).Count() + FixtureMemberships(fixture).Count();

        private JObject FixtureCounts(string fixture) => new JObject
        {
            ["plots"] = _plots.Count(x => x != null && (x.PlotId ?? string.Empty).StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase)),
            ["pledges"] = FixturePledges(fixture).Count(),
            ["summonses"] = FixtureSummonses(fixture).Count(),
            ["movements"] = FixtureMovements(fixture).Count(),
            ["memberships"] = FixtureMemberships(fixture).Count(),
            ["total"] = FixtureRecordCount(fixture)
        };

        private int RemoveHarnessFixtures(string fixture)
        {
            int before = FixtureRecordCount(fixture);
            _plots.RemoveAll(x => x != null && (x.PlotId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));
            _pledges.RemoveAll(x => x != null && (x.PledgeId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));
            _summonses.RemoveAll(x => x != null && (x.SummonsId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));
            _movements.RemoveAll(x => x != null && (x.MovementId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));
            _memberships.RemoveAll(x => x != null && (x.MovementId ?? string.Empty)
                .StartsWith(fixture + "_", StringComparison.OrdinalIgnoreCase));
            return before - FixtureRecordCount(fixture);
        }

        private static bool IsTerminalPlotStatus(string value)
        {
            return Same(value, "resolved") || Same(value, "cancelled");
        }

        private static string DescribePreparationDecision(Hero lord, string decision)
        {
            string name = lord?.Name?.ToString() ?? "The lord";
            if (Same(decision, "pledge")) return name + " secretly pledged the clan's support when rebellion is declared.";
            if (Same(decision, "report")) return name + " refused to pledge support.";
            return name + " refused to pledge support.";
        }

        /// <summary>
        /// Explicit player declaration. NPC outbreaks are intentionally rejected here and
        /// can originate only from the saved weekly relationship roll.
        /// </summary>
        public bool TryStartDirectedMovement(Kingdom kingdom, Clan leaderClan, IEnumerable<Clan> supporters, out string result)
        {
            result = string.Empty;
            bool playerLeader = leaderClan == Clan.PlayerClan && leaderClan?.Leader == Hero.MainHero;
            bool validVassal = kingdom != null && leaderClan?.Kingdom == kingdom
                && leaderClan != kingdom.RulingClan && leaderClan.Leader != null
                && !leaderClan.Leader.IsDead && !leaderClan.Leader.IsPrisoner;
            result = DirectedMovementPreflightFailure(playerLeader, validVassal,
                kingdom != null && HasActiveMovement(kingdom),
                kingdom != null && IsRealmOnCooldown(kingdom, CurrentDay()));
            if (!string.IsNullOrWhiteSpace(result))
            {
                return false;
            }

            ReignRebellionMovementRecord movement = CreateMovement(kingdom, leaderClan, true, WeekIndex(CurrentDay()));
            AddOrUpdateMember(movement, leaderClan, "rebel", "player_declaration",
                leaderClan.Leader.GetRelation(leaderClan.Leader), leaderClan.Leader.GetRelation(kingdom.Leader));
            foreach (Clan clan in kingdom.Clans.Where(x => x != null && x != leaderClan).ToList())
                AddOrUpdateMember(movement, clan, "loyalist", "declaration_loyalist",
                    clan.Leader?.GetRelation(leaderClan.Leader) ?? 0, clan.Leader?.GetRelation(kingdom.Leader) ?? 0);
            StartCivilWar(movement);
            if (!IsCivilWar(movement))
            {
                result = "The declaration could not create a valid rebel kingdom.";
                return false;
            }
            ReignRebellionPlotRecord preparedPlot = FindActivePlayerPlot();
            if (preparedPlot != null && !Same(preparedPlot.Status, "active_rebellion"))
            {
                preparedPlot.Status = "active_rebellion";
                preparedPlot.Resolution = "player_declared";
                preparedPlot.MovementId = movement.MovementId;
                preparedPlot.UpdatedDay = CurrentDay();
                ActivatePreparedPledges(preparedPlot, movement);
            }
            result = "You challenged " + (kingdom.Leader?.Name?.ToString() ?? "the ruler") + " and began a rebellion for the throne.";
            return true;
        }

        private static string DirectedMovementPreflightFailure(bool playerLeader,
            bool validVassal, bool activeMovement, bool realmOnCooldown)
        {
            if (!playerLeader)
                return "NPC rebellions can begin only from their authoritative weekly relationship roll.";
            if (!validVassal)
                return "You must be the living, free leader of a non-ruling vassal clan to challenge the ruler.";
            if (activeMovement) return "This kingdom already has an active rebellion.";
            if (realmOnCooldown) return "This realm is still within its 63-day post-rebellion cooldown.";
            return string.Empty;
        }

        public bool TryRecruitLord(Hero recruiter, Hero targetLord, out string result)
        {
            result = string.Empty;
            if (recruiter != Hero.MainHero || Clan.PlayerClan?.Leader != recruiter)
            {
                result = "Only the player clan leader can recruit lords to a player-led rebellion.";
                return false;
            }
            ReignRebellionMovementRecord movement = _movements.FirstOrDefault(x => IsCivilWar(x) && x.IsPlayerLed
                && Same(x.LeaderHeroStringId, recruiter.StringId));
            Kingdom rebels = FindKingdom(movement?.RebelKingdomStringId);
            Clan targetClan = targetLord?.Clan;
            if (movement == null || rebels == null)
            {
                result = "You must declare and lead an active rebellion before recruiting lords.";
                return false;
            }
            if (targetLord == null || !targetLord.IsLord || targetLord.IsDead || targetLord.IsPrisoner
                || targetClan == null || targetClan.IsEliminated || targetClan == Clan.PlayerClan)
            {
                result = "The accepting lord must be a living, free member of an active noble clan.";
                return false;
            }
            if (Same(targetClan.StringId, FindHero(movement.OriginalRulerHeroStringId)?.Clan?.StringId))
            {
                result = "The ruler being challenged cannot be recruited into the rebellion against their own rule.";
                return false;
            }
            ReignRebellionMembershipRecord existing = MembersOf(movement).FirstOrDefault(x => Same(x.ClanStringId, targetClan.StringId));
            if (existing != null && Same(existing.Side, "rebel"))
            {
                result = targetClan.Name + " is already committed to the rebellion.";
                return false;
            }

            string originalKingdom = targetClan.Kingdom?.StringId ?? string.Empty;
            ReignRebellionMembershipRecord member = AddOrUpdateMember(movement, targetClan, "rebel", "player_recruitment",
                targetLord.GetRelation(recruiter), targetLord.GetRelation(FindHero(movement.OriginalRulerHeroStringId)));
            if (string.IsNullOrWhiteSpace(member.OriginalKingdomStringId)) member.OriginalKingdomStringId = originalKingdom;
            RememberOriginalKingdom(movement, targetClan.StringId, originalKingdom);
            MoveClanToKingdom(targetClan, rebels);
            member.IsCommitted = true;
            member.PlayerChoiceRequired = false;
            member.LastSwitchDay = CurrentDay();
            EmitHistory(movement, "civil_war_recruitment", "completed", targetLord.StringId,
                targetLord.Name + " committed " + targetClan.Name + " to the player-led rebellion.", false);
            result = targetLord.Name + " and " + targetClan.Name + " joined the rebellion until it ends.";
            return true;
        }

        public bool TryJoinNpcRebellion(Hero player, Hero rebelLeader, out string result)
        {
            result = string.Empty;
            if (player != Hero.MainHero || Clan.PlayerClan?.Leader != player)
            {
                result = "Only the player clan leader can join a rebellion this way.";
                return false;
            }
            ReignRebellionMovementRecord movement = _movements.FirstOrDefault(x => IsCivilWar(x) && !x.IsPlayerLed
                && Same(x.LeaderHeroStringId, rebelLeader?.StringId));
            Kingdom parent = FindKingdom(movement?.ParentKingdomStringId);
            Kingdom rebels = FindKingdom(movement?.RebelKingdomStringId);
            if (movement == null || parent == null || rebels == null || Clan.PlayerClan?.Kingdom != parent)
            {
                result = "You can join only the active rebel leader opposing your current kingdom.";
                return false;
            }
            if (player.IsDead || player.IsPrisoner)
            {
                result = "A dead or imprisoned clan leader cannot join the rebellion.";
                return false;
            }

            ReignRebellionMembershipRecord member = AddOrUpdateMember(movement, Clan.PlayerClan, "rebel", "player_joined_npc_rebellion",
                player.GetRelation(rebelLeader), player.GetRelation(FindHero(movement.OriginalRulerHeroStringId)));
            member.PlayerChoiceRequired = false;
            member.IsCommitted = true;
            MoveClanToKingdom(Clan.PlayerClan, rebels);
            EmitHistory(movement, "civil_war_allegiance", "completed", player.StringId,
                "The player clan joined " + rebelLeader.Name + "'s rebellion.", false);
            result = "Your clan joined " + rebelLeader.Name + " until the rebellion ends.";
            return true;
        }

        public bool TrySurrender(Hero surrenderingLeader, string movementId, out string result)
        {
            result = string.Empty;
            ReignRebellionMovementRecord movement = string.IsNullOrWhiteSpace(movementId)
                ? FindActiveMovementForHero(surrenderingLeader)
                : _movements.FirstOrDefault(x => IsCivilWar(x) && Same(x.MovementId, movementId));
            if (movement == null)
            {
                result = "No active rebellion matches this surrender.";
                return false;
            }
            if (Same(movement.LeaderHeroStringId, surrenderingLeader?.StringId))
            {
                ResolveCivilWar(movement, false, "rebel_leader_surrendered");
                result = "The rebel leader surrendered; the loyalists won the rebellion.";
                return true;
            }
            if (Same(movement.OriginalRulerHeroStringId, surrenderingLeader?.StringId))
            {
                ResolveCivilWar(movement, true, "challenged_ruler_surrendered");
                result = "The challenged ruler surrendered; the rebels won the throne.";
                return true;
            }
            result = "Only the named rebel leader or challenged ruler can surrender the rebellion.";
            return false;
        }

        public bool SetPlayerClanSide(string movementId, bool joinRebels, out string result)
        {
            if (!joinRebels)
            {
                result = "The player remains a loyalist by staying in the parent kingdom; no action is required.";
                return true;
            }
            ReignRebellionMovementRecord movement = _movements.FirstOrDefault(x => IsCivilWar(x) && Same(x.MovementId, movementId));
            return TryJoinNpcRebellion(Hero.MainHero, FindHero(movement?.LeaderHeroStringId), out result);
        }

        private ReignRebellionMovementRecord CreateMovement(Kingdom kingdom, Clan leaderClan, bool playerLed, int weekIndex)
        {
            ReignRebellionMovementRecord movement = new ReignRebellionMovementRecord
            {
                ParentKingdomStringId = kingdom.StringId,
                LeaderClanStringId = leaderClan.StringId,
                LeaderHeroStringId = leaderClan.Leader.StringId,
                OriginalRulerHeroStringId = kingdom.Leader?.StringId ?? string.Empty,
                Objective = "claimant_takeover",
                Stage = "declared",
                CreatedDay = CurrentDay(),
                UpdatedDay = CurrentDay(),
                IsPlayerKingdom = kingdom == Clan.PlayerClan?.Kingdom,
                IsPlayerLed = playerLed,
                UltimatumIssued = true,
                RelationshipRulesMigrated = true,
                OutbreakWeekIndex = weekIndex,
                DemandJson = new JObject
                {
                    ["politicalResult"] = "claimant_takeover",
                    ["newRulingClanId"] = leaderClan.StringId,
                    ["trigger"] = playerLed ? "player_declaration"
                        : "weekly_effective_attitude_roll"
                }.ToString(Newtonsoft.Json.Formatting.None)
            };
            movement.CorrelationId = movement.MovementId;
            _movements.Add(movement);
            return movement;
        }

        private void StartCivilWar(ReignRebellionMovementRecord movement)
        {
            Kingdom parent = FindKingdom(movement?.ParentKingdomStringId);
            Clan leaderClan = FindClan(movement?.LeaderClanStringId);
            if (movement == null || parent == null || leaderClan?.Leader == null || leaderClan.Leader.IsDead
                || leaderClan.Leader.IsPrisoner || leaderClan.Kingdom != parent)
            {
                ResolveWithoutMutation(movement, "failed_preflight");
                return;
            }

            foreach (ReignRebellionMembershipRecord member in MembersOf(movement).ToList())
            {
                Clan clan = FindClan(member.ClanStringId);
                if (clan == null) continue;
                if (string.IsNullOrWhiteSpace(member.OriginalKingdomStringId))
                    member.OriginalKingdomStringId = clan.Kingdom?.StringId ?? string.Empty;
                RememberOriginalKingdom(movement, clan.StringId, member.OriginalKingdomStringId);
            }
            movement.ExecutionPhase = "preflight_complete";
            movement.ExecutionSnapshotJson = BuildExecutionSnapshot(movement, parent).ToString(Newtonsoft.Json.Formatting.None);
            movement.OriginalParentStrongholdIdsCsv = string.Join(",", Settlement.All
                .Where(x => x?.IsFortification == true && x.MapFaction == parent).Select(x => x.StringId));
            Kingdom rebels = CreateRebelKingdom(movement);
            if (rebels == null)
            {
                ResolveWithoutMutation(movement, "failed_to_create_rebel_kingdom");
                return;
            }
            foreach (ReignRebellionMembershipRecord member in MembersOf(movement).Where(x => Same(x.Side, "rebel")).ToList())
            {
                Clan clan = FindClan(member.ClanStringId);
                if (clan != null && clan.Kingdom != rebels) MoveClanToKingdom(clan, rebels);
                member.IsCommitted = true;
                member.LastSwitchDay = CurrentDay();
            }
            movement.OriginalRebelStrongholdIdsCsv = string.Join(",", Settlement.All
                .Where(x => x?.IsFortification == true && x.MapFaction == rebels).Select(x => x.StringId));
            if (!rebels.IsAtWarWith(parent))
            {
                if (ReignAICampaignBehavior.Instance != null)
                    ReignAICampaignBehavior.Instance.DeclareWarWithOrigin(rebels, parent, "rebellion",
                        "Civil war began.", movement.MovementId, string.Empty, true);
                else DeclareWarAction.ApplyByRebellion(rebels, parent);
            }
            movement.RebelKingdomStringId = rebels.StringId;
            movement.Stage = "civil_war";
            movement.CivilWarStartedDay = CurrentDay();
            movement.UpdatedDay = CurrentDay();
            movement.ExecutionPhase = "verified";
            EmitHistory(movement, "civil_war_started", "completed", movement.LeaderHeroStringId,
                rebels.InformalName + " rose against " + parent.InformalName + " for the throne.", false);
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] "
                + rebels.InformalName + " has begun a rebellion against " + parent.InformalName + "."));
        }

        private Kingdom CreateRebelKingdom(ReignRebellionMovementRecord movement)
        {
            Kingdom parent = FindKingdom(movement.ParentKingdomStringId);
            Clan leaderClan = FindClan(movement.LeaderClanStringId);
            if (parent == null || leaderClan?.Leader == null) return null;
            Kingdom existing = FindKingdom(movement.RebelKingdomStringId);
            if (existing != null && !existing.IsEliminated) return existing;
            string id = TaleWorlds.CampaignSystem.Campaign.Current.CampaignObjectManager
                .FindNextUniqueStringId<Kingdom>("reign_rebels_" + SafeId(parent.StringId));
            Kingdom rebels = Kingdom.CreateKingdom(id);
            TextObject name = new TextObject("{=ReignRebelRealm}{CLAN} Rebellion");
            name.SetTextVariable("CLAN", leaderClan.Name);
            TextObject informal = new TextObject(name.ToString());
            rebels.InitializeKingdom(name, informal, parent.Culture, leaderClan.Banner ?? parent.Banner,
                parent.Color2, parent.Color, leaderClan.HomeSettlement ?? parent.InitialHomeSettlement,
                parent.EncyclopediaText, parent.EncyclopediaTitle, parent.EncyclopediaRulerTitle);
            ChangeKingdomAction.ApplyByCreateKingdom(leaderClan, rebels, true);
            rebels.RulingClan = leaderClan;
            foreach (PolicyObject policy in parent.ActivePolicies.ToList())
                if (!rebels.HasPolicy(policy)) rebels.AddPolicy(policy);
            movement.RebelKingdomStringId = rebels.StringId;
            return rebels;
        }

        private void ReconcileCivilWar(ReignRebellionMovementRecord movement)
        {
            if (!IsCivilWar(movement)) return;
            Kingdom parent = FindKingdom(movement.ParentKingdomStringId);
            Kingdom rebels = FindKingdom(movement.RebelKingdomStringId);
            Hero rebelLeader = FindHero(movement.LeaderHeroStringId);
            Hero challengedRuler = FindHero(movement.OriginalRulerHeroStringId);

            if (rebelLeader == null || rebelLeader.IsDead)
            {
                ResolveCivilWar(movement, false, "rebel_leader_killed");
                return;
            }
            if (challengedRuler == null || challengedRuler.IsDead)
            {
                ResolveCivilWar(movement, true, "challenged_ruler_killed");
                return;
            }
            if (parent != null && parent.Leader != challengedRuler)
            {
                ResolveCivilWar(movement, true, "challenged_ruler_deposed");
                return;
            }
            float now = CurrentDay();
            bool rebelLeaderHeld = IsHeldByFaction(rebelLeader, parent);
            bool challengedRulerHeld = IsHeldByFaction(challengedRuler, rebels);
            UpdateLeadershipCaptureState(movement, rebelLeaderHeld,
                challengedRulerHeld, now);
            if (rebelLeaderHeld && LeadershipCaptureExpired(
                movement.RebelLeaderCapturedDay, now))
            {
                ResolveCivilWar(movement, false, "rebel_leader_captured");
                return;
            }
            if (challengedRulerHeld && LeadershipCaptureExpired(
                movement.ChallengedRulerCapturedDay, now))
            {
                ResolveCivilWar(movement, true, "challenged_ruler_captured");
                return;
            }
            if (rebelLeaderHeld || challengedRulerHeld) return;

            // Generic/native peace never bypasses the leadership objective.
            if (parent != null && rebels != null && !parent.IsEliminated && !rebels.IsEliminated
                && !parent.IsAtWarWith(rebels) && !movement.ExecutionPhase.StartsWith("resolving", StringComparison.OrdinalIgnoreCase))
            {
                if (ReignAICampaignBehavior.Instance != null)
                    ReignAICampaignBehavior.Instance.DeclareWarWithOrigin(rebels, parent, "rebellion",
                        "Civil war resumed after external peace.", movement.MovementId, string.Empty, true);
                else DeclareWarAction.ApplyByRebellion(rebels, parent);
            }
        }

        private void OnHeroPrisonerTaken(PartyBase captor, Hero prisoner)
        {
            Kingdom captorKingdom = captor?.MapFaction as Kingdom;
            if (captorKingdom == null || prisoner == null) return;
            foreach (ReignRebellionMovementRecord movement in _movements.Where(IsCivilWar).ToList())
            {
                if (Same(movement.LeaderHeroStringId, prisoner.StringId)
                    && Same(movement.ParentKingdomStringId, captorKingdom.StringId))
                {
                    MarkLeadershipCaptured(movement, true, CurrentDay());
                    continue;
                }
                if (Same(movement.OriginalRulerHeroStringId, prisoner.StringId)
                    && Same(movement.RebelKingdomStringId, captorKingdom.StringId))
                {
                    MarkLeadershipCaptured(movement, false, CurrentDay());
                }
            }
        }

        private void UpdateLeadershipCaptureState(
            ReignRebellionMovementRecord movement, bool rebelLeaderHeld,
            bool challengedRulerHeld, float now)
        {
            if (rebelLeaderHeld)
                MarkLeadershipCaptured(movement, true, now);
            else if (movement.RebelLeaderCapturedDay > 0f)
            {
                movement.RebelLeaderCapturedDay = NextLeadershipCaptureDay(
                    movement.RebelLeaderCapturedDay, false, now);
                movement.UpdatedDay = now;
                EmitHistory(movement, "civil_war_leader_released", "civil_war",
                    movement.LeaderHeroStringId,
                    "The rebel leader escaped or was released before captivity ended the civil war.", false);
            }

            if (challengedRulerHeld)
                MarkLeadershipCaptured(movement, false, now);
            else if (movement.ChallengedRulerCapturedDay > 0f)
            {
                movement.ChallengedRulerCapturedDay = NextLeadershipCaptureDay(
                    movement.ChallengedRulerCapturedDay, false, now);
                movement.UpdatedDay = now;
                EmitHistory(movement, "civil_war_ruler_released", "civil_war",
                    movement.OriginalRulerHeroStringId,
                    "The challenged ruler escaped or was released before captivity ended the civil war.", false);
            }
        }

        private static float NextLeadershipCaptureDay(float recordedDay, bool held, float now)
        {
            if (!held) return -1f;
            return recordedDay > 0f ? recordedDay : now;
        }

        private void MarkLeadershipCaptured(ReignRebellionMovementRecord movement,
            bool rebelLeader, float now)
        {
            if (movement == null) return;
            float recorded = rebelLeader ? movement.RebelLeaderCapturedDay
                : movement.ChallengedRulerCapturedDay;
            // Zero is also treated as unset for records loaded from saves made
            // before the capture-grace fields existed.
            float next = NextLeadershipCaptureDay(recorded, true, now);
            if (recorded > 0f && Math.Abs(next - recorded) < 0.001f) return;
            if (rebelLeader) movement.RebelLeaderCapturedDay = next;
            else movement.ChallengedRulerCapturedDay = next;
            movement.UpdatedDay = now;
            EmitHistory(movement,
                rebelLeader ? "civil_war_leader_captured" : "civil_war_ruler_captured",
                "civil_war",
                rebelLeader ? movement.LeaderHeroStringId
                    : movement.OriginalRulerHeroStringId,
                rebelLeader
                    ? "The rebel leader was captured; the rebellion has three campaign days to secure a rescue or escape."
                    : "The challenged ruler was captured; the loyalists have three campaign days to secure a rescue or escape.",
                false);
        }

        private static bool LeadershipCaptureExpired(float capturedDay,
            float currentDay)
        {
            return capturedDay > 0f
                && currentDay - capturedDay >= LeadershipCaptureGraceDays;
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (victim == null) return;
            foreach (ReignRebellionMovementRecord movement in _movements.Where(IsCivilWar).ToList())
            {
                if (Same(movement.LeaderHeroStringId, victim.StringId))
                {
                    ResolveCivilWar(movement, false, "rebel_leader_killed");
                    return;
                }
                if (Same(movement.OriginalRulerHeroStringId, victim.StringId))
                {
                    ResolveCivilWar(movement, true, "challenged_ruler_killed");
                    return;
                }
            }
        }

        private void OnPeaceMade(IFaction first, IFaction second, MakePeaceAction.MakePeaceDetail detail)
        {
            Kingdom a = first as Kingdom;
            Kingdom b = second as Kingdom;
            ReignRebellionMovementRecord movement = _movements.FirstOrDefault(x => IsCivilWar(x) && IsPair(x, a, b));
            if (movement == null || movement.ExecutionPhase.StartsWith("resolving", StringComparison.OrdinalIgnoreCase)) return;
            Kingdom parent = FindKingdom(movement.ParentKingdomStringId);
            Kingdom rebels = FindKingdom(movement.RebelKingdomStringId);
            if (parent != null && rebels != null && !parent.IsEliminated && !rebels.IsEliminated && !parent.IsAtWarWith(rebels))
            {
                if (ReignAICampaignBehavior.Instance != null)
                    ReignAICampaignBehavior.Instance.DeclareWarWithOrigin(rebels, parent, "rebellion",
                        "Civil war resumed during resolution reconciliation.", movement.MovementId, string.Empty, true);
                else DeclareWarAction.ApplyByRebellion(rebels, parent);
            }
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (mapEvent == null || !mapEvent.HasWinner) return;
            Kingdom winner = mapEvent.GetLeaderParty(mapEvent.WinningSide)?.MapFaction as Kingdom;
            Kingdom loser = mapEvent.GetLeaderParty(mapEvent.DefeatedSide)?.MapFaction as Kingdom;
            ReignRebellionMovementRecord movement = _movements.FirstOrDefault(x => IsCivilWar(x) && IsPair(x, winner, loser));
            if (movement == null) return;
            EmitHistory(movement, "civil_war_battle", "completed", winner?.Leader?.StringId,
                (winner?.InformalName?.ToString() ?? "A side") + " won a battle, but the rebellion remains leadership-bound.", false);
        }

        private void ResolveCivilWar(ReignRebellionMovementRecord movement, bool rebelVictory, string cause)
        {
            if (movement == null || movement.ResolutionApplied
                || movement.ExecutionPhase.StartsWith("resolving", StringComparison.OrdinalIgnoreCase)) return;
            Kingdom parent = FindKingdom(movement.ParentKingdomStringId);
            Kingdom rebels = FindKingdom(movement.RebelKingdomStringId);
            movement.ExecutionPhase = "resolving_reunification";
            if (parent != null && rebels != null && parent.IsAtWarWith(rebels)) MakePeaceAction.Apply(parent, rebels);

            List<ReignRebellionMembershipRecord> losingMembers = MembersOf(movement)
                .Where(x => Same(x.Side, rebelVictory ? "loyalist" : "rebel"))
                .ToList();
            foreach (ReignRebellionMembershipRecord member in losingMembers) member.IsDefeated = true;

            RestoreCommittedClans(movement, parent, rebels);
            Clan claimant = FindClan(movement.LeaderClanStringId);
            if (rebelVictory && parent != null && claimant != null && claimant.Kingdom == parent && !claimant.IsEliminated)
                ChangeRulingClanAction.Apply(parent, claimant);

            if (rebels != null && !rebels.IsEliminated && rebels.Clans.Count == 0)
                DestroyKingdomAction.Apply(rebels);

            movement.WinningSide = rebelVictory ? "rebel" : "loyalist";
            movement.Resolution = rebelVictory ? "rebel_victory" : "loyalist_victory";
            movement.ResolutionCause = cause ?? string.Empty;
            Hero victor = rebelVictory ? FindHero(movement.LeaderHeroStringId) : FindHero(movement.OriginalRulerHeroStringId);
            movement.WinningLeaderHeroStringId = victor?.StringId ?? string.Empty;
            movement.Stage = "judgment";
            movement.UpdatedDay = CurrentDay();
            movement.CooldownUntilDay = CurrentDay() + RealmCooldownDays;

            if (victor == Hero.MainHero)
            {
                movement.JudgmentsPending = losingMembers.Any(x => !x.FateApplied && IsJudgmentTarget(x));
                movement.ExecutionPhase = movement.JudgmentsPending ? "awaiting_player_judgments" : "resolving_complete";
                if (movement.JudgmentsPending)
                {
                    EmitHistory(movement, "civil_war_judgments", "pending", victor.StringId,
                        "The victorious player must decide each defeated clan leader's fate.", false);
                    TryShowNextPlayerJudgment();
                    return;
                }
            }
            else
            {
                foreach (ReignRebellionMembershipRecord member in losingMembers.Where(x => !x.FateApplied).ToList())
                    ApplyNpcMercyJudgment(movement, victor, member);
            }
            FinalizeResolution(movement);
        }

        private void RestoreCommittedClans(ReignRebellionMovementRecord movement, Kingdom parent, Kingdom rebels)
        {
            JObject origins = ParseObject(movement.OriginalClanKingdomsJson);
            foreach (ReignRebellionMembershipRecord member in MembersOf(movement).ToList())
            {
                Clan clan = FindClan(member.ClanStringId);
                if (clan == null || clan.IsEliminated) continue;
                string originId = string.IsNullOrWhiteSpace(member.OriginalKingdomStringId)
                    ? origins.Value<string>(clan.StringId) ?? movement.ParentKingdomStringId
                    : member.OriginalKingdomStringId;
                Kingdom destination = FindKingdom(originId);
                if (destination == null || destination.IsEliminated || Same(originId, movement.RebelKingdomStringId))
                    destination = parent;
                if (destination != null && clan.Kingdom != destination)
                    MoveClanToKingdom(clan, destination);
                else if (destination == null && clan.Kingdom == rebels)
                    ChangeKingdomAction.ApplyByLeaveKingdom(clan, true);
            }
        }

        private void ApplyNpcMercyJudgment(ReignRebellionMovementRecord movement, Hero victor, ReignRebellionMembershipRecord member)
        {
            Hero defeated = CurrentLeader(member);
            if (defeated == null || defeated.IsDead)
            {
                member.FateOutcome = "dead";
                member.FateApplied = true;
                return;
            }
            if (member.FateRoll <= 0) member.FateRoll = MBRandom.RandomInt(1, 101);
            int relation = victor?.GetRelation(defeated) ?? 0;
            int mercy = victor?.GetTraitLevel(DefaultTraits.Mercy) ?? 0;
            member.FateScore = member.FateRoll + 15 * mercy + (int)Math.Round(relation / 5d, MidpointRounding.AwayFromZero);
            string outcome = member.FateScore >= 67 ? "freedom" : member.FateScore >= 34 ? "imprisonment" : "execution";
            ApplyFate(movement, victor, member, outcome);
        }

        private void TryShowNextPlayerJudgment()
        {
            if (_judgmentInquiryOpen || Hero.MainHero == null) return;
            ReignRebellionMovementRecord movement = _movements.FirstOrDefault(x => x != null && x.JudgmentsPending
                && Same(x.WinningLeaderHeroStringId, Hero.MainHero.StringId) && !x.ResolutionApplied);
            if (movement == null) return;
            ReignRebellionMembershipRecord member = MembersOf(movement)
                .FirstOrDefault(x => x.IsDefeated && !x.FateApplied && IsJudgmentTarget(x));
            if (member == null)
            {
                movement.JudgmentsPending = false;
                FinalizeResolution(movement);
                return;
            }
            Hero defeated = CurrentLeader(member);
            if (defeated == null || defeated.IsDead)
            {
                member.FateOutcome = "dead";
                member.FateApplied = true;
                TryShowNextPlayerJudgment();
                return;
            }

            _judgmentInquiryOpen = true;
            List<InquiryElement> choices = new List<InquiryElement>
            {
                new InquiryElement("freedom", "Grant freedom (+30 relation)", null),
                new InquiryElement("imprisonment", "Order imprisonment (+10 relation)", null),
                new InquiryElement("execution", "Order execution", null)
            };
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Judgment of " + defeated.Name,
                defeated.Name + ", leader of " + (defeated.Clan?.Name?.ToString() ?? "a defeated clan")
                    + ", awaits judgment after the rebellion.\n\nThis choice is final and uses native captivity or death rules.",
                choices, true, 1, 1, "Confirm Judgment", "Decide Later",
                selected =>
                {
                    _judgmentInquiryOpen = false;
                    string outcome = selected?.FirstOrDefault()?.Identifier as string;
                    if (!string.IsNullOrWhiteSpace(outcome)) ApplyFate(movement, Hero.MainHero, member, outcome);
                    TryShowNextPlayerJudgment();
                },
                selected => { _judgmentInquiryOpen = false; },
                string.Empty, false), true, false);
        }

        private void ApplyFate(ReignRebellionMovementRecord movement, Hero victor,
            ReignRebellionMembershipRecord member, string requestedOutcome)
        {
            Hero defeated = CurrentLeader(member);
            if (member == null || member.FateApplied) return;
            if (defeated == null || defeated.IsDead)
            {
                member.FateOutcome = "dead";
                member.FateApplied = true;
                return;
            }

            string outcome = requestedOutcome;
            if (Same(outcome, "freedom"))
            {
                if (defeated.IsPrisoner) EndCaptivityAction.ApplyByReleasedAfterBattle(defeated);
                ApplyFateRelationBonus(victor, defeated, member, 30);
            }
            else if (Same(outcome, "imprisonment"))
            {
                ImprisonByVictor(victor, defeated);
                ApplyFateRelationBonus(victor, defeated, member, 10);
            }
            else
            {
                if (!CampaignOptions.IsLifeDeathCycleDisabled)
                {
                    if (defeated.IsPrisoner) EndCaptivityAction.ApplyByReleasedAfterBattle(defeated);
                    KillCharacterAction.ApplyByExecution(defeated, victor, true, defeated != Hero.MainHero);
                    outcome = defeated.IsDead ? "execution" : "imprisonment";
                    if (!defeated.IsDead)
                    {
                        ImprisonByVictor(victor, defeated);
                        ApplyFateRelationBonus(victor, defeated, member, 10);
                    }
                }
                else
                {
                    outcome = "imprisonment";
                    ImprisonByVictor(victor, defeated);
                    ApplyFateRelationBonus(victor, defeated, member, 10);
                }
            }

            member.FateOutcome = outcome;
            member.FateApplied = true;
            EmitHistory(movement, "civil_war_judgment", "completed", victor?.StringId,
                (victor?.Name?.ToString() ?? "The victor") + " imposed " + outcome + " on " + defeated.Name + ".", false);
        }

        private static void ImprisonByVictor(Hero victor, Hero defeated)
        {
            if (victor == null || defeated == null || defeated.IsDead) return;
            PartyBase holder = victor == Hero.MainHero ? PartyBase.MainParty : null;
            holder = holder ?? victor.PartyBelongedTo?.Party
                ?? victor.Clan?.Leader?.PartyBelongedTo?.Party
                ?? victor.Clan?.Fiefs.FirstOrDefault()?.Settlement?.Party;
            if (defeated.IsPrisoner && defeated.PartyBelongedToAsPrisoner == holder) return;
            if (defeated.IsPrisoner) EndCaptivityAction.ApplyByReleasedAfterBattle(defeated);
            if (holder != null) TakePrisonerAction.Apply(holder, defeated);
        }

        private static void ApplyFateRelationBonus(Hero victor, Hero defeated,
            ReignRebellionMembershipRecord member, int delta)
        {
            if (victor == null || defeated == null || member.RelationBonusApplied || delta == 0) return;
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(defeated, victor, delta, true);
            member.RelationBonusApplied = true;
        }

        private void FinalizeResolution(ReignRebellionMovementRecord movement)
        {
            if (movement == null) return;
            if (!string.IsNullOrWhiteSpace(movement.BackingSponsorKingdomStringId)
                && !string.IsNullOrWhiteSpace(movement.RebelKingdomStringId))
            {
                ReignAICampaignBehavior.Instance?.EndAgreementsBetween(
                    movement.BackingSponsorKingdomStringId, movement.RebelKingdomStringId,
                    "rebellion_backing", "rebellion_resolved", string.Empty, string.Empty);
                movement.BackingStatus = "expired";
                movement.BackingTerminalReason = "rebellion_resolved";
            }
            movement.JudgmentsPending = false;
            movement.Stage = "resolved";
            movement.ResolutionApplied = true;
            movement.ExecutionPhase = "verified";
            movement.UpdatedDay = CurrentDay();
            movement.CooldownUntilDay = Math.Max(movement.CooldownUntilDay, CurrentDay() + RealmCooldownDays);
            EmitHistory(movement, "civil_war_resolved", "completed", movement.WinningLeaderHeroStringId,
                "The rebellion ended in " + movement.Resolution.Replace('_', ' ')
                    + " because " + (movement.ResolutionCause ?? string.Empty).Replace('_', ' ') + ".", false);
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] The rebellion has ended: "
                + movement.Resolution.Replace('_', ' ') + "."));
        }

        private void ResolveWithoutMutation(ReignRebellionMovementRecord movement, string reason)
        {
            if (movement == null) return;
            movement.Stage = "resolved";
            movement.Resolution = reason;
            movement.ResolutionCause = reason;
            movement.ResolutionApplied = true;
            movement.ExecutionPhase = "failed";
            movement.CooldownUntilDay = CurrentDay() + RealmCooldownDays;
        }

        private void MigrateLegacyMovements()
        {
            foreach (ReignRebellionMovementRecord movement in _movements.Where(x => x != null && !x.RelationshipRulesMigrated).ToList())
            {
                movement.RelationshipRulesMigrated = true;
                if (IsCivilWar(movement))
                {
                    movement.Objective = "claimant_takeover";
                    movement.IsPlayerLed = Same(movement.LeaderClanStringId, Clan.PlayerClan?.StringId);
                    foreach (ReignRebellionMembershipRecord member in MembersOf(movement))
                    {
                        Clan clan = FindClan(member.ClanStringId);
                        if (string.IsNullOrWhiteSpace(member.OriginalKingdomStringId))
                            member.OriginalKingdomStringId = Same(member.Side, "rebel")
                                ? movement.ParentKingdomStringId
                                : clan?.Kingdom?.StringId ?? movement.ParentKingdomStringId;
                        member.IsCommitted = true;
                        RememberOriginalKingdom(movement, member.ClanStringId, member.OriginalKingdomStringId);
                    }
                    EmitHistory(movement, "rebellion_rules_migrated", "completed", movement.LeaderHeroStringId,
                        "The active civil war migrated to relationship sides and leadership-bound victory.", false);
                }
                else if (!movement.ResolutionApplied && !Same(movement.Stage, "resolved"))
                {
                    ResolveWithoutMutation(movement, "legacy_prewar_movement_retired");
                }
            }
        }

        private void ReconcileAllMovements()
        {
            foreach (ReignRebellionMovementRecord movement in _movements.Where(IsCivilWar).ToList())
                ReconcileCivilWar(movement);
        }

        private bool HasActiveMovement(Kingdom kingdom)
        {
            return kingdom != null && _movements.Any(x => x != null && !x.ResolutionApplied
                && (Same(x.ParentKingdomStringId, kingdom.StringId) || Same(x.RebelKingdomStringId, kingdom.StringId)));
        }

        private bool IsRealmOnCooldown(Kingdom kingdom, float now)
        {
            return kingdom != null && _movements.Any(x => Same(x.ParentKingdomStringId, kingdom.StringId)
                && x.ResolutionApplied && x.CooldownUntilDay > now);
        }

        private static bool IsEligibleNpcVassalLeader(Clan clan, Kingdom kingdom)
        {
            Hero leader = clan?.Leader;
            return clan != null && kingdom != null && clan.Kingdom == kingdom && clan != kingdom.RulingClan
                && clan != Clan.PlayerClan && !clan.IsEliminated && !clan.IsUnderMercenaryService
                && !clan.IsMinorFaction && !clan.IsClanTypeMercenary
                && leader != null && leader.IsLord && !leader.IsDead && !leader.IsPrisoner;
        }

        private ReignRebellionMembershipRecord AddOrUpdateMember(ReignRebellionMovementRecord movement, Clan clan,
            string side, string joinedBy, int rebelRelation, int rulerRelation)
        {
            ReignRebellionMembershipRecord member = _memberships.FirstOrDefault(x =>
                Same(x.MovementId, movement.MovementId) && Same(x.ClanStringId, clan.StringId));
            if (member == null)
            {
                member = new ReignRebellionMembershipRecord
                {
                    MovementId = movement.MovementId,
                    ClanStringId = clan.StringId,
                    OriginalKingdomStringId = clan.Kingdom?.StringId ?? string.Empty,
                    JoinedDay = CurrentDay()
                };
                _memberships.Add(member);
            }
            member.LeaderHeroStringId = clan.Leader?.StringId ?? member.LeaderHeroStringId;
            member.Side = side;
            member.JoinedBy = joinedBy ?? string.Empty;
            member.RelationToRebelAtJoin = rebelRelation;
            member.RelationToRulerAtJoin = rulerRelation;
            member.SupportScore = Same(side, "rebel") ? 100f : 0f;
            member.IsCommitted = true;
            member.PlayerChoiceRequired = false;
            RememberOriginalKingdom(movement, clan.StringId, member.OriginalKingdomStringId);
            return member;
        }

        private static void MoveClanToKingdom(Clan clan, Kingdom destination)
        {
            if (clan == null || destination == null || clan.Kingdom == destination) return;
            Kingdom old = clan.Kingdom;
            if (old != null)
                ChangeKingdomAction.ApplyByJoinToKingdomByDefection(clan, old, destination,
                    CampaignTime.Now + CampaignTime.Days(RebellionCommitmentDays), true);
            else
                ChangeKingdomAction.ApplyByJoinToKingdom(clan, destination,
                    CampaignTime.Now + CampaignTime.Days(RebellionCommitmentDays), true);
        }

        private static bool IsHeldByFaction(Hero hero, Kingdom faction)
        {
            return hero?.IsPrisoner == true && faction != null
                && hero.PartyBelongedToAsPrisoner?.MapFaction == faction;
        }

        private static Hero CurrentLeader(ReignRebellionMembershipRecord member)
        {
            return FindClan(member?.ClanStringId)?.Leader ?? FindHero(member?.LeaderHeroStringId);
        }

        private static bool IsJudgmentTarget(ReignRebellionMembershipRecord member)
        {
            Hero hero = CurrentLeader(member);
            return hero != null && !hero.IsDead;
        }

        private void RememberOriginalKingdom(ReignRebellionMovementRecord movement, string clanId, string kingdomId)
        {
            if (movement == null || string.IsNullOrWhiteSpace(clanId)) return;
            JObject origins = ParseObject(movement.OriginalClanKingdomsJson);
            if (origins[clanId] == null) origins[clanId] = kingdomId ?? string.Empty;
            movement.OriginalClanKingdomsJson = origins.ToString(Newtonsoft.Json.Formatting.None);
        }

        public async Task<JObject> CreateNegotiationAsync(Hero broker, Kingdom first, Kingdom second, string command,
            string politicalResult, JObject terms)
        {
            if (first?.Leader == null || second?.Leader == null)
                return new JObject { ["ok"] = false, ["error"] = "Both kingdoms require current rulers." };
            JObject exactTerms = terms == null ? new JObject() : (JObject)terms.DeepClone();
            if (!string.IsNullOrWhiteSpace(politicalResult)) exactTerms["politicalResult"] = politicalResult;
            JObject response = await ReignServerClient.CreateNegotiationDraftAsync(new JObject
            {
                ["brokerHeroId"] = broker?.StringId ?? Hero.MainHero?.StringId ?? string.Empty,
                ["firstKingdomId"] = first.StringId,
                ["secondKingdomId"] = second.StringId,
                ["firstRulerId"] = first.Leader.StringId,
                ["secondRulerId"] = second.Leader.StringId,
                ["command"] = command ?? "diplomatic_package",
                ["politicalResult"] = politicalResult ?? string.Empty,
                ["terms"] = exactTerms
            }).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() => UpsertNegotiation(response)).ConfigureAwait(false);
            return response;
        }

        public async Task<JObject> RespondToNegotiationAsync(string negotiationId, Hero ruler, bool approve, string reason)
        {
            JObject response = await ReignServerClient.RespondToNegotiationAsync(
                negotiationId, ruler, ruler?.Clan?.Kingdom, approve, reason).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() => UpsertNegotiation(response)).ConfigureAwait(false);
            return response;
        }

        public async Task RefreshNegotiationsOnDemandAsync(string negotiationId = "")
        {
            JObject response = await ReignServerClient.GetNegotiationsAsync(negotiationId).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                if (response["negotiations"] is JArray rows)
                    foreach (JObject row in rows.OfType<JObject>()) UpsertNegotiation(row);
                else UpsertNegotiation(response);
            }).ConfigureAwait(false);
        }

        public JArray BuildRelevantNegotiationPacket(Hero ruler)
        {
            if (ruler == null) return new JArray();
            return new JArray(_negotiations.Where(x => x != null && !IsTerminalNegotiation(x.Status)
                    && (Same(x.FirstRulerHeroStringId, ruler.StringId) || Same(x.SecondRulerHeroStringId, ruler.StringId)))
                .OrderBy(x => x.ExpiresDay).Take(5).Select(x => new JObject
                {
                    ["negotiationId"] = x.NegotiationId,
                    ["command"] = x.Command,
                    ["politicalResult"] = x.PoliticalResult,
                    ["termsHash"] = x.TermsHash,
                    ["status"] = x.Status,
                    ["expiresDay"] = x.ExpiresDay,
                    ["firstApproved"] = Same(x.FirstApprovalHash, x.TermsHash),
                    ["secondApproved"] = Same(x.SecondApprovalHash, x.TermsHash)
                }));
        }

        public string NegotiationTrackerText(string negotiationId)
        {
            ReignNegotiatedActionRecord record = _negotiations.FirstOrDefault(x => Same(x.NegotiationId, negotiationId));
            if (record == null) return "Negotiation not found.";
            string first = Same(record.FirstApprovalHash, record.TermsHash) ? "approved" : "awaiting";
            string second = Same(record.SecondApprovalHash, record.TermsHash) ? "approved" : "awaiting";
            return record.Command + " | " + record.PoliticalResult + " | first ruler: " + first
                + " | second ruler: " + second + " | expires day "
                + record.ExpiresDay.ToString("0.0", CultureInfo.InvariantCulture) + " | " + record.Status;
        }

        public bool TryResolvePlayerUltimatum(string movementId, string response, out string result)
        {
            result = "Pre-war ultimata were retired. Rebellions now begin immediately from a player declaration or weekly relationship roll.";
            return false;
        }

        public ReignActionResult ResolveNegotiatedSettlement(ReignWorldActionRecord action)
        {
            if (action == null)
                return ReignActionResult.ValidationFailed("Civil-war settlement action is missing.");
            JObject terms = ParseObject(action.TermsJson);
            string politicalResult = ReadString(terms, "politicalResult");
            if (!Same(politicalResult, "recognized_independence"))
                return ReignActionResult.ValidationFailed(
                    "Only a safeguarded recognition of rebel independence may negotiate an active civil war to peace.");

            string movementId = ReadString(terms, "movementId");
            Kingdom rebels = FindKingdom(ReadString(terms, "rebelKingdomId"));
            Kingdom parent = FindKingdom(ReadString(terms, "parentKingdomId"));
            Hero rebelRuler = FindHero(action.ActorHeroStringId);
            Hero parentRuler = FindHero(action.TargetHeroStringId);
            if (!ValidateRecognizedIndependenceContext(movementId, rebels, parent,
                    rebelRuler, parentRuler, out string contextFailure))
                return ReignActionResult.ValidationFailed(contextFailure);

            ReignRebellionMovementRecord movement = _movements.First(x =>
                IsCivilWar(x) && Same(x.MovementId, movementId));
            movement.ExecutionPhase = "resolving_recognized_independence";
            if (rebels.IsAtWarWith(parent)) MakePeaceAction.Apply(rebels, parent);
            if (rebels.IsAtWarWith(parent))
            {
                movement.ExecutionPhase = "civil_war";
                return ReignActionResult.FailRetryable(
                    "The civil-war peace action did not take effect.",
                    "recognition_peace_not_applied", "state_verification", 0.25f);
            }

            movement.WinningSide = "negotiated";
            movement.Resolution = "recognized_independence";
            movement.ResolutionCause = "recognized_by_challenged_ruler";
            movement.WinningLeaderHeroStringId = rebelRuler.StringId;
            movement.JudgmentsPending = false;
            movement.UpdatedDay = CurrentDay();
            movement.CooldownUntilDay = CurrentDay() + 63f;
            ReignAICampaignBehavior.Instance?.RecordAgreementFromAction(action,
                "recognized_independence", 63f, true);
            FinalizeResolution(movement);

            return ReignActionResult.Done(parentRuler.Name + " recognized "
                    + rebels.InformalName + " as a separate sovereign kingdom; both realms remain independent and are now at peace.")
                .WithChangedEntity("kingdom", rebels.StringId,
                    rebels.InformalName.ToString(), "independence_recognized")
                .WithChangedEntity("kingdom", parent.StringId,
                    parent.InformalName.ToString(), "civil_war_peace_accepted")
                .WithEffect("peace_made", "kingdom", parent.StringId,
                    parent.InformalName.ToString(), "counterpart=" + rebels.StringId)
                .WithEffect("treaty_recorded", "kingdom", rebels.StringId,
                    rebels.InformalName.ToString(), "kind=recognized_independence;durationDays=63")
                .WithDiagnostic("movementId", movementId)
                .WithDiagnostic("sovereignty", "separate_kingdoms");
        }

        public bool ValidateRecognizedIndependenceContext(string movementId,
            Kingdom rebels, Kingdom parent, Hero rebelRuler, Hero parentRuler,
            out string reason)
        {
            ReignRebellionMovementRecord movement = _movements.FirstOrDefault(x =>
                IsCivilWar(x) && Same(x.MovementId, movementId));
            if (movement == null || rebels == null || parent == null
                || rebels.IsEliminated || parent.IsEliminated
                || !Same(movement.RebelKingdomStringId, rebels.StringId)
                || !Same(movement.ParentKingdomStringId, parent.StringId)
                || rebels == parent)
            {
                reason = "Recognition terms do not match the exact active rebel and parent realms.";
                return false;
            }
            if (!rebels.IsAtWarWith(parent))
            {
                reason = "Recognition is valid only while the exact civil war is still active.";
                return false;
            }
            if (rebelRuler == null || parentRuler == null
                || rebelRuler.IsDead || parentRuler.IsDead
                || rebelRuler != rebels.Leader || parentRuler != parent.Leader
                || !Same(movement.LeaderHeroStringId, rebelRuler.StringId)
                || !Same(movement.OriginalRulerHeroStringId, parentRuler.StringId))
            {
                reason = "Recognition approval is stale because a named civil-war ruler no longer leads the expected realm.";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private void ExpireNegotiations(float now)
        {
            foreach (ReignNegotiatedActionRecord record in _negotiations.Where(x =>
                x != null && x.ExpiresDay > 0f && x.ExpiresDay < now && !IsTerminalNegotiation(x.Status)))
            {
                record.Status = "expired";
                record.UpdatedDay = now;
            }
        }

        private void UpsertNegotiation(JObject row)
        {
            if (row == null || row.Value<bool?>("ok") == false) return;
            string id = ReadString(row, "negotiation_id", ReadString(row, "negotiationId"));
            if (string.IsNullOrWhiteSpace(id)) return;
            ReignNegotiatedActionRecord record = _negotiations.FirstOrDefault(x => Same(x.NegotiationId, id));
            if (record == null)
            {
                record = new ReignNegotiatedActionRecord { NegotiationId = id };
                _negotiations.Add(record);
            }
            record.BrokerHeroStringId = ReadString(row, "broker_hero_id", record.BrokerHeroStringId);
            record.FirstKingdomStringId = ReadString(row, "first_kingdom_id", record.FirstKingdomStringId);
            record.SecondKingdomStringId = ReadString(row, "second_kingdom_id", record.SecondKingdomStringId);
            record.FirstRulerHeroStringId = ReadString(row, "first_ruler_id", record.FirstRulerHeroStringId);
            record.SecondRulerHeroStringId = ReadString(row, "second_ruler_id", record.SecondRulerHeroStringId);
            record.Command = ReadString(row, "command", record.Command);
            record.PoliticalResult = ReadString(row, "political_result", record.PoliticalResult);
            record.TermsJson = ReadString(row, "terms_json", record.TermsJson);
            record.TermsHash = ReadString(row, "terms_hash", record.TermsHash);
            record.Status = ReadString(row, "status", record.Status);
            record.CreatedDay = ReadFloat(row, "created_day", record.CreatedDay);
            record.UpdatedDay = ReadFloat(row, "updated_day", record.UpdatedDay);
            record.ExpiresDay = ReadFloat(row, "expires_day", record.ExpiresDay);
            record.QueuedActionId = ReadString(row, "queued_action_id", record.QueuedActionId);
            if (row["approvals"] is JArray approvals)
            {
                foreach (JObject approval in approvals.OfType<JObject>().Where(x => Same(ReadString(x, "status"), "approved")))
                {
                    string ruler = ReadString(approval, "ruler_hero_id");
                    string hash = ReadString(approval, "terms_hash");
                    if (Same(ruler, record.FirstRulerHeroStringId)) record.FirstApprovalHash = hash;
                    if (Same(ruler, record.SecondRulerHeroStringId)) record.SecondApprovalHash = hash;
                }
            }
        }

        private JObject BuildExecutionSnapshot(ReignRebellionMovementRecord movement, Kingdom parent)
        {
            List<string> strongholds = Settlement.All.Where(x => x?.IsFortification == true && x.MapFaction == parent)
                .Select(x => x.StringId).Distinct().ToList();
            if (string.IsNullOrWhiteSpace(movement.OriginalStrongholdIdsCsv))
                movement.OriginalStrongholdIdsCsv = string.Join(",", strongholds);
            return new JObject
            {
                ["movementId"] = movement.MovementId,
                ["parentKingdomId"] = movement.ParentKingdomStringId,
                ["worldDay"] = CurrentDay(),
                ["strongholdIds"] = new JArray(strongholds),
                ["memberClanIds"] = new JArray(MembersOf(movement).Select(x => x.ClanStringId)),
                ["rules"] = "relationship_weekly_leadership_victory"
            };
        }

        private void EmitHistory(ReignRebellionMovementRecord movement, string type, string phase,
            string actorId, string summary, bool hidden)
        {
            ReignWorldHistoryCampaignBehavior.Instance?.RecordReignSystemEvent(type, phase, "politics",
                movement?.CorrelationId, summary, hidden ? "participants" : "major_world", actorId,
                movement?.ParentKingdomStringId,
                new JObject
                {
                    ["movementId"] = movement?.MovementId ?? string.Empty,
                    ["objective"] = movement?.Objective ?? string.Empty,
                    ["stage"] = movement?.Stage ?? string.Empty,
                    ["resolutionCause"] = movement?.ResolutionCause ?? string.Empty
                },
                hidden ? new JArray(MembersOf(movement).Where(x => Same(x.Side, "rebel"))
                    .Select(x => x.LeaderHeroStringId)) : null);
        }

        private IEnumerable<ReignRebellionMembershipRecord> MembersOf(ReignRebellionMovementRecord movement)
            => _memberships.Where(x => x != null && Same(x.MovementId, movement?.MovementId));

        private static bool IsCivilWar(ReignRebellionMovementRecord movement)
            => movement != null && Same(movement.Stage, "civil_war") && !movement.ResolutionApplied;

        private static bool IsPair(ReignRebellionMovementRecord movement, Kingdom first, Kingdom second)
            => movement != null && first != null && second != null
                && ((Same(movement.ParentKingdomStringId, first.StringId)
                    && Same(movement.RebelKingdomStringId, second.StringId))
                    || (Same(movement.ParentKingdomStringId, second.StringId)
                    && Same(movement.RebelKingdomStringId, first.StringId)));

        private static bool IsTerminalNegotiation(string status)
            => Same(status, "completed") || Same(status, "rejected") || Same(status, "expired") || Same(status, "failed");

        private static Kingdom FindKingdom(string id) => ReignObjectResolver.FindKingdom(id);
        private static Clan FindClan(string id) => ReignObjectResolver.FindClan(id);
        private static Hero FindHero(string id) => string.IsNullOrWhiteSpace(id)
            ? null
            : Hero.FindFirst(x => Same(x.StringId, id));
        private static bool Same(string left, string right)
            => string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        private static float CurrentDay() => TaleWorlds.CampaignSystem.Campaign.Current == null
            ? 0f
            : (float)CampaignTime.Now.ToDays;
        private static int WeekIndex(float day) => day < 0f ? -1 : (int)Math.Floor(day / 7f);
        private static string SafeId(string value)
            => new string((value ?? "kingdom").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        private static JObject ParseObject(string json)
        {
            try { return string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json); }
            catch { return new JObject(); }
        }
        private static string ReadString(JObject source, string key, string fallback = "")
            => source?[key]?.Type == JTokenType.Null ? fallback : source?[key]?.ToString() ?? fallback;
        private static float ReadFloat(JObject source, string key, float fallback)
            => source?[key]?.Value<float?>() ?? fallback;
    }
}
