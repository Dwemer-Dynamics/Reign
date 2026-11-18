using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.World;
using ReignBeta.Shared.Rulers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Rebellion.Integration;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.News;
using ReignBeta.WorldSimulation.Rulers;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Rebellion.Campaign
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
    public sealed partial class ReignRebellionCampaignBehavior : CampaignBehaviorBase
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

        public static ReignRebellionCampaignBehavior Instance => ReignServices.Rebellion;
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
            ReignServices.Rebellion = this;
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
            // Sealed saves skip initial world preparation. Repair their loaded
            // history here as well, before any new preparation request is read.
            if (dataStore.IsLoading) CloseResolvedPreparationPlots(_plots, _movements);
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
            CloseResolvedPreparationPlots(_plots, _movements);
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
                ReignRebellionEvaluationResult result = await ReignRebellionServerClient.EvaluateRebellionsAsync(
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
                    await ReignRebellionServerClient.ResolveEffectiveAttitudesAsync(requestedPairs)
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

        // Keep the preparation history, but do not let an already concluded war
        // permanently block the player's next preparation. Also repairs old saves.
        internal static int CloseResolvedPreparationPlots(
            IEnumerable<ReignRebellionPlotRecord> plots,
            IEnumerable<ReignRebellionMovementRecord> movements)
        {
            var resolved = (movements ?? Enumerable.Empty<ReignRebellionMovementRecord>())
                .Where(x => x != null && (x.ResolutionApplied || Same(x.Stage, "resolved"))).ToList();
            int closed = 0;
            foreach (var plot in plots ?? Enumerable.Empty<ReignRebellionPlotRecord>())
            {
                if (plot == null || !Same(plot.Status, "active_rebellion")
                    || string.IsNullOrWhiteSpace(plot.MovementId)) continue;
                var movement = resolved.FirstOrDefault(x => Same(x.MovementId, plot.MovementId)
                    && Same(x.LeaderHeroStringId, plot.PlayerHeroStringId)
                    && Same(x.LeaderClanStringId, plot.PlayerClanStringId));
                if (movement == null) continue;
                plot.Status = "resolved";
                plot.Resolution = !string.IsNullOrWhiteSpace(movement.Resolution)
                    ? movement.Resolution : "rebellion_resolved";
                plot.UpdatedDay = Math.Max(plot.UpdatedDay, movement.UpdatedDay);
                closed++;
            }
            return closed;
        }

        /// <summary>
        /// Checks the exact reviewed identities before the structured pledge can
        /// enter the existing preparation system; this read never creates a plot.
        /// </summary>
        public string StructuredPledgePreflightFailure(Hero player, Hero lord, string kingdomId, string rulerId)
        {
            Kingdom parent = Clan.PlayerClan?.Kingdom;
            if (player != Hero.MainHero || Clan.PlayerClan?.Leader != player || parent == null
                || Clan.PlayerClan == parent.RulingClan || player.IsDead || player.IsPrisoner)
                return "Only a living, free player vassal clan leader may seek support for a rebellion.";
            if (parent.StringId != kingdomId || parent.Leader?.StringId != rulerId)
                return "The ruler or realm differs from the rebellion terms that were reviewed.";
            if (lord == null || !lord.IsLord || lord.IsDead || lord.IsChild || lord.IsPrisoner
                || lord.Clan == null || lord.Clan.IsEliminated || lord.Clan == Clan.PlayerClan || lord.Clan.Leader != lord || lord == parent.Leader)
                return "Only the living, free leader of another noble clan can pledge support; the challenged ruler cannot.";
            var plot = FindActivePlayerPlot();
            if (plot == null) return "";
            if (!Same(plot.ParentKingdomStringId, kingdomId) || !Same(plot.RulerHeroStringId, rulerId))
                return "An unresolved preparation belongs to a different ruler or realm.";
            if (!IsPreparationRecruitmentOpen(plot)) return "Secret recruitment is closed; use the current rebellion or summons actions.";
            var prior = _pledges.LastOrDefault(x => x != null && Same(x.PlotId, plot.PlotId) && Same(x.ClanStringId, lord.Clan.StringId));
            return prior != null && !Same(prior.Decision, "pledge")
                ? "This clan has already refused or reported the current preparation; that saved decision has not changed." : "";
        }

        /// <summary>Records the lord's explicit pledge, refusal or report with native eligibility, persistence and idempotence.</summary>
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
            ReignLetterSendResult send = await ReignRebellionServerClient.SendRebellionSystemLetterAsync(
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
            ReignServices.WorldHistory?.RecordReignSystemEvent(
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

        public async Task<JObject> CreateNegotiationAsync(Hero broker, Kingdom first, Kingdom second, string command,
            string politicalResult, JObject terms)
        {
            if (first?.Leader == null || second?.Leader == null)
                return new JObject { ["ok"] = false, ["error"] = "Both kingdoms require current rulers." };
            JObject exactTerms = terms == null ? new JObject() : (JObject)terms.DeepClone();
            if (!string.IsNullOrWhiteSpace(politicalResult)) exactTerms["politicalResult"] = politicalResult;
            JObject response = await ReignRebellionServerClient.CreateNegotiationDraftAsync(new JObject
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
            JObject response = await ReignRebellionServerClient.RespondToNegotiationAsync(
                negotiationId, ruler, ruler?.Clan?.Kingdom, approve, reason).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() => UpsertNegotiation(response)).ConfigureAwait(false);
            return response;
        }

        public async Task RefreshNegotiationsOnDemandAsync(string negotiationId = "")
        {
            JObject response = await ReignRebellionServerClient.GetNegotiationsAsync(negotiationId).ConfigureAwait(false);
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
            ReignServices.AI?.RecordAgreementFromAction(action,
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
            ReignServices.WorldHistory?.RecordReignSystemEvent(type, phase, "politics",
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
