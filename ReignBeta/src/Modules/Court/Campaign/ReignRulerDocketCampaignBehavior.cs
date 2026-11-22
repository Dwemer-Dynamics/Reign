using System;
using Reign.Core.Contracts.Dialogue;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Campaign;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Persistence.Save;
using ReignBeta.Platform.Integration;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.Economy;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Court.Campaign
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private string _rulerDocketStateJson = string.Empty;
        private List<string> _rulerDocketStateChunks = new List<string>();
        private ReignRulerDocketState _rulerDocketState = new ReignRulerDocketState();
        private bool _docketRelationDeliveryInFlight;
        private bool _docketMemoryDeliveryInFlight;

        public ReignRulerDocketState RulerDocketState => EnsureRulerDocketState();
        public IReadOnlyList<ReignDocketPetition> DocketPetitions => EnsureRulerDocketState().Petitions
            .Where(x => x != null && x.IsPending)
            .OrderByDescending(x => (int)x.Severity)
            .ThenByDescending(x => x.NormalizedNeed)
            .ThenBy(x => x.PetitionId, StringComparer.Ordinal)
            .ToList();
        public IReadOnlyList<ReignNobleDocketMatter> DocketNobleMatters => EnsureRulerDocketState().NobleMatters
            .Where(x => x != null && x.IsPending)
            .OrderByDescending(x => (int)x.Severity)
            .ThenBy(x => x.ReceivedDay)
            .ThenBy(x => x.MatterId, StringComparer.Ordinal)
            .ToList();
        public ReignChancellorOffice Chancellor => EnsureRulerDocketState().Chancellor;

        public bool TryIssueRoyalProclamation(string exactText, out string receipt)
        {
            receipt = string.Empty;
            if (!HasRoyalCommandAccess)
            { receipt = "Royal proclamations may be issued only while holding Court in the capital."; return false; }
            if (string.IsNullOrWhiteSpace(exactText))
            { receipt = "A proclamation requires text."; return false; }
            string proclamationId = "royal_proclamation_" + Guid.NewGuid().ToString("N");
            ReignDocketHistoryRecord record = new ReignDocketHistoryRecord
            {
                RecordId = proclamationId,
                ReignId = CurrentReignId(),
                Type = "royal_proclamation",
                Outcome = "proclaimed",
                Day = CurrentDay(),
                PetitionerHeroId = Hero.MainHero?.StringId ?? string.Empty,
                PetitionerName = Hero.MainHero?.Name?.ToString() ?? string.Empty,
                Summary = exactText,
                Detail = "Issued verbatim as a globally known, immutable historical proclamation. It has no direct mechanical effect."
            };
            EnsureRulerDocketState().History.Add(record);
            ReignServices.WorldHistory?.RecordReignSystemEvent(
                "royal_proclamation", "completed", "politics", proclamationId,
                exactText, "major_world", Hero.MainHero?.StringId ?? string.Empty,
                Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty,
                new JObject
                {
                    ["proclamationId"] = proclamationId,
                    ["exactText"] = exactText,
                    ["mechanicalEffects"] = false,
                    ["immutable"] = true
                });
            receipt = "The proclamation was entered verbatim into globally known history.";
            StateChanged?.Invoke();
            return true;
        }

        public JObject BuildChancellorConversationContext(Hero conversationHero)
        {
            Hero ruler = Hero.MainHero;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (conversationHero == null || ruler == null || kingdom == null
                || kingdom.Leader != ruler)
                return new JObject();

            ReignChancellorOffice office = EnsureRulerDocketState().Chancellor;
            bool isIncumbent = !office.IsVacant
                && string.Equals(office.HeroId, conversationHero.StringId,
                    StringComparison.OrdinalIgnoreCase);
            bool candidateEligible = office.IsVacant
                && IsNormalChancellorCandidate(conversationHero, out _);
            if (!isIncumbent && !candidateEligible) return new JObject();

            Settlement capital = FindCurrentCapital();
            return new JObject
            {
                ["schema"] = "reign-chancellor-conversation-v1",
                ["enabled"] = true,
                ["mode"] = isIncumbent ? "incumbent" : "candidate",
                ["playerIsRuler"] = true,
                ["rulerHeroId"] = ruler.StringId ?? string.Empty,
                ["rulerName"] = ruler.Name?.ToString() ?? ruler.StringId,
                ["kingdomId"] = kingdom.StringId ?? string.Empty,
                ["kingdomName"] = kingdom.Name?.ToString() ?? kingdom.StringId,
                ["capitalSettlementId"] = capital?.StringId ?? string.Empty,
                ["capitalSettlementName"] = capital?.Name?.ToString() ?? string.Empty,
                ["conversationHeroId"] = conversationHero.StringId ?? string.Empty,
                ["officeVacant"] = office.IsVacant,
                ["candidateEligible"] = candidateEligible,
                ["defaultSalaryWhenUnstated"] = ReignRulerDocketRules.DefaultActiveChancellorSalary,
                ["playerGold"] = ruler.Gold,
                ["incumbentHeroId"] = office.HeroId ?? string.Empty,
                ["incumbentHeroName"] = office.HeroName ?? string.Empty,
                ["officeState"] = office.State.ToString(),
                ["activeDailySalary"] = office.Salary,
                ["emergency"] = office.Emergency,
                ["relinquishedDuties"] = isIncumbent
                    ? office.RelinquishedDuties ?? string.Empty
                    : DescribeChancellorDuties(conversationHero)
            };
        }

        internal ReignRulerDocketState EnsureRulerDocketState()
        {
            _rulerDocketState = _rulerDocketState ?? new ReignRulerDocketState();
            _rulerDocketState.Normalize();
            return _rulerDocketState;
        }

        private void PrepareRulerDocketForSave()
        {
            ReignRulerDocketState state = EnsureRulerDocketState();
            string json = JsonConvert.SerializeObject(state, Formatting.None);
            _rulerDocketStateChunks = ReignSavePayloadCodec.Encode(json);
            _rulerDocketStateJson = string.Empty;
        }

        private void RestoreRulerDocketAfterLoad()
        {
            try
            {
                ReignRulerDocketState state = ReignSavePayloadCodec.DecodeAuthoritativeState(
                    _rulerDocketStateChunks, _rulerDocketStateJson, JsonConvert.DeserializeObject<ReignRulerDocketState>,
                    () => new ReignRulerDocketState());
                state.Normalize();
                _rulerDocketState = state;
            }
            catch (Exception ex)
            {
                const string message = "Ruler docket save state could not be restored safely. Load a verified backup.";
                ReignLog.Warn(message + " " + ex.GetType().Name);
                throw new InvalidOperationException(message, ex);
            }

            MigrateLegacyDocketOnce();
            CollapseDuplicatePendingPetitioners(CurrentDay());
        }

        private void CollapseDuplicatePendingPetitioners(int day)
        {
            ReignRulerDocketState state = EnsureRulerDocketState();
            foreach (IGrouping<string, ReignDocketPetition> group in state.Petitions
                .Where(x => x?.IsPending == true && !string.IsNullOrWhiteSpace(x.PetitionerHeroId))
                .GroupBy(x => x.PetitionerHeroId, StringComparer.OrdinalIgnoreCase))
            {
                List<ReignDocketPetition> ordered = group
                    .OrderByDescending(x => x.Severity)
                    .ThenByDescending(x => x.NormalizedNeed)
                    .ThenBy(x => x.ReceivedDay)
                    .ThenBy(x => x.PetitionId, StringComparer.Ordinal)
                    .ToList();
                foreach (ReignDocketPetition duplicate in ordered.Skip(1))
                {
                    duplicate.State = ReignDocketPetitionState.Invalidated;
                    duplicate.DecidedDay = day;
                    duplicate.DecisionReason = "superseded_duplicate_petitioner";
                    AddPetitionHistory(duplicate, "invalidated",
                        "Retired during the one-pending-petition-per-petitioner migration; no player decision or mechanical effect was applied.");
                }
            }
        }

        internal void MigrateLegacyDocketOnce()
        {
            ReignRulerDocketState state = EnsureRulerDocketState();
            if (state.LegacyMigrationComplete) return;

            int day = CurrentDay();
            foreach (CourtMatter matter in (_matters ?? new List<CourtMatter>()).Where(IsLegacyPlaceholderMatter))
            {
                bool wasTerminal = matter.IsTerminal;
                if (!wasTerminal)
                {
                    matter.State = ReignCourtMatterState.Invalidated;
                    matter.InvalidReason = "Retired when the ruler petition docket replaced the placeholder docket.";
                    matter.ResolvedDay = day;
                    matter.ResolutionMode = "legacy_docket_migration";
                    matter.Revision++;
                }

                state.History.Add(new ReignDocketHistoryRecord
                {
                    RecordId = "docket_history_" + Guid.NewGuid().ToString("N"),
                    ReignId = CurrentReignId(),
                    Type = "legacy_matter",
                    Outcome = wasTerminal ? "legacy_completed" : "legacy_retired",
                    Day = day,
                    SettlementId = matter.SettlementStringId ?? string.Empty,
                    Summary = matter.Title ?? "Legacy court matter",
                    Detail = wasTerminal
                        ? "Completed under the former Court docket and retained as history only."
                        : "Unresolved placeholder matter retired without an outcome during docket migration."
                });
            }

            _agenda.RemoveAll(x => x == null || (_matters.FirstOrDefault(m => m.MatterId == x.MatterId) is CourtMatter matter
                && IsLegacyPlaceholderMatter(matter)));
            foreach (CourtRegentAssignment regent in (_regents ?? new List<CourtRegentAssignment>()).Where(x => x != null && x.IsActive))
            {
                regent.IsActive = false;
                regent.DismissedDay = day;
                regent.DismissalReason = "legacy_regent_replaced_by_chancellor";
                regent.Revision++;
                state.History.Add(new ReignDocketHistoryRecord
                {
                    RecordId = "docket_history_" + Guid.NewGuid().ToString("N"),
                    ReignId = CurrentReignId(),
                    Type = "legacy_regent",
                    Outcome = "legacy_retired",
                    Day = day,
                    ChancellorHeroId = regent.HeroStringId ?? string.Empty,
                    Summary = "The former Regent office was retired when the Chancellor office was established."
                });
            }
            state.LegacyMigrationComplete = true;
        }

        private static bool IsLegacyPlaceholderMatter(CourtMatter matter)
        {
            if (matter == null) return false;
            string key = matter.SourceKey ?? string.Empty;
            string[] prefixes =
            {
                "settlement_supply:", "office_vacancy:", "obligation:", "rebellion_ultimatum:",
                "prisoner_petition:", "war_council:", "ambient_court:", "host_interruption:"
            };
            return prefixes.Any(x => key.StartsWith(x, StringComparison.OrdinalIgnoreCase));
        }

        private void ProcessRulerDocketDailyTick(int day)
        {
            ReignRulerDocketState state = EnsureRulerDocketState();
            state.ActiveChancellorDays.RemoveAll(x => x < day - 29);
            ProcessRulerDocketMidnightRefusals(day);
            ProcessDocketCommitments(day);
            ProcessSoldierExpeditionReturns(day);
        }

        private void ProcessRulerDocketHourlyTick(int day, int hour)
        {
            ReignRulerDocketState state = EnsureRulerDocketState();
            TryProcessPendingDocketRelationshipAdjustments();
            TryProcessPendingDocketMemoryJobs();
            TryProcessPendingDocketWorldHistoryJobs();
            MaintainChancellorCaptivityLifecycle(day);
            if (hour >= 8) ProcessNobleInvestigations(day);
            ProcessCourtStayLeases(day, hour);
            double courtLifeNow = CampaignTime.Now.ToDays;
            ProcessInternationalCourtLife(courtLifeNow);
            ProcessFamilyVisits(courtLifeNow);
            ProcessNobleVisitorStays(courtLifeNow);
            ProcessPatronageCommissions(courtLifeNow);
            if (hour >= 8)
            {
                ProcessRulerDocketOfficeTick(day);
                GenerateRulerDocketForDay(day);
            }
        }

        internal void TryProcessPendingDocketRelationshipAdjustments()
        {
            if (_docketRelationDeliveryInFlight
                || !EnsureRulerDocketState().PendingRelationAdjustments.Any(x => x != null && !x.Applied))
                return;
            _docketRelationDeliveryInFlight = true;
            _ = ProcessPendingDocketRelationshipAdjustmentsAsync();
        }

        private async Task ProcessPendingDocketRelationshipAdjustmentsAsync()
        {
            ReignRulerDocketState deliveryState = EnsureRulerDocketState();
            try
            {
                List<ReignDirectionalRelationAdjustment> pending = deliveryState
                    .PendingRelationAdjustments.Where(x => x != null && !x.Applied)
                    .OrderBy(x => x.AdjustmentId, StringComparer.Ordinal).ToList();
                foreach (ReignDirectionalRelationAdjustment adjustment in pending)
                {
                    Task<JObject> request = null;
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        if (!ReferenceEquals(deliveryState, _rulerDocketState) || !ReferenceEquals(Instance, this)) return;
                        Hero observer = FindHero(adjustment.ObserverHeroId), ruler = FindHero(adjustment.SubjectHeroId);
                        if (observer == null || ruler == null)
                        {
                            if (DocketIdentityKnownDead(adjustment.ObserverHeroId) || DocketIdentityKnownDead(adjustment.SubjectHeroId))
                            { adjustment.Skipped = true; adjustment.Applied = true; adjustment.ReceiptId = "skipped_dead_identity_" + adjustment.AdjustmentId; }
                            return;
                        }
                        request = ReignCorrespondenceClient.ApplyRulerDocketDirectionalDeltaAsync(adjustment.AdjustmentId,
                            observer, ruler, adjustment.Delta, adjustment.Reason);
                    }).ConfigureAwait(false);
                    if (request == null) continue;
                    JObject response = await request.ConfigureAwait(false);
                    if (response.Value<bool?>("ok") != true)
                    {
                        ReignLog.Warn("Ruler docket relationship adjustment " + adjustment.AdjustmentId
                            + " was not accepted: " + (response.Value<string>("error") ?? "unknown error"));
                        continue;
                    }

                    string receiptId = response.Value<string>("eventId") ?? adjustment.AdjustmentId;
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        if (!ReferenceEquals(deliveryState, _rulerDocketState) || !ReferenceEquals(Instance, this)) return;
                        adjustment.Applied = true;
                        adjustment.ReceiptId = receiptId;
                    }).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Ruler docket relationship delivery failed and will retry: " + ex.Message);
            }
            finally
            {
                await ReignMainThread.InvokeAsync(() => _docketRelationDeliveryInFlight = false).ConfigureAwait(false);
            }
        }

        internal void TryProcessPendingDocketMemoryJobs()
        {
            if (_docketMemoryDeliveryInFlight
                || !EnsureRulerDocketState().PendingMemoryJobs.Any(x => x != null && !x.Applied))
                return;
            _docketMemoryDeliveryInFlight = true;
            _ = ProcessPendingDocketMemoryJobsAsync();
        }

        private async Task ProcessPendingDocketMemoryJobsAsync()
        {
            ReignRulerDocketState deliveryState = EnsureRulerDocketState();
            try
            {
                foreach (ReignDocketMemoryJob job in deliveryState.PendingMemoryJobs
                    .Where(x => x != null && !x.Applied)
                    .OrderBy(x => x.JobId, StringComparer.Ordinal).ToList())
                {
                    Task<JObject> request = null;
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        if (!ReferenceEquals(deliveryState, _rulerDocketState) || !ReferenceEquals(Instance, this)) return;
                        Hero hero = FindHero(job.HeroId);
                        if (hero == null)
                        {
                            job.LastError = "The remembering hero is not currently resolvable.";
                            if (DocketIdentityKnownDead(job.HeroId))
                            { job.Skipped = true; job.Applied = true; job.ReceiptId = "skipped_dead_identity_" + job.JobId; job.LastError = "The remembering hero died before delivery."; }
                            return;
                        }
                        if (job.EventType == "noble_visitor_invitation")
                        {
                            // Legacy queued invitations asserted agreement from prose. Keep the
                            // job and its original payload, without sending it as a new fact.
                            job.Skipped = true;
                            job.Applied = true;
                            job.ReceiptId = "skipped_unconfirmed_invitation_" + job.JobId;
                            job.LastError = "The legacy invitation had no verified agreement.";
                            return;
                        }
                        request = ReignCorrespondenceClient.RecordRulerDocketMemoryAsync(job.JobId, hero, job.EventType, job.Summary, job.WorldDay);
                    }).ConfigureAwait(false);
                    if (request == null) continue;
                    JObject response = await request.ConfigureAwait(false);
                    if (response.Value<bool?>("ok") != true)
                    {
                        await ReignMainThread.InvokeAsync(() =>
                        { if (ReferenceEquals(deliveryState, _rulerDocketState)) job.LastError = response.Value<string>("error") ?? "The memory server refused the job."; }).ConfigureAwait(false);
                        continue;
                    }
                    string receiptId = response.Value<string>("eventId") ?? job.JobId;
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        if (!ReferenceEquals(deliveryState, _rulerDocketState) || !ReferenceEquals(Instance, this)) return;
                        job.Applied = true;
                        job.ReceiptId = receiptId;
                        job.LastError = string.Empty;
                        ReignDocketHistoryRecord record = deliveryState.History
                            .LastOrDefault(x => x.ChancellorHeroId == job.HeroId
                                && string.IsNullOrWhiteSpace(x.LinkedMemoryId));
                        if (record != null) record.LinkedMemoryId = receiptId;
                    }).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Emergency Chancellor memory delivery failed and will retry: " + ex.Message);
            }
            finally
            {
                await ReignMainThread.InvokeAsync(() => _docketMemoryDeliveryInFlight = false).ConfigureAwait(false);
            }
        }

        internal void TryProcessPendingDocketWorldHistoryJobs()
        {
            ReignBeta.WorldSimulation.Campaign.ReignWorldHistoryCampaignBehavior history =
                ReignServices.WorldHistory;
            if (history == null || !history.TimelineReady) return;
            foreach (ReignDocketWorldHistoryJob job in EnsureRulerDocketState().PendingWorldHistoryJobs
                .Where(x => x != null && !x.Applied)
                .OrderBy(x => x.JobId, StringComparer.Ordinal).ToList())
            {
                history.RecordReignSystemEvent("emergency_chancellor_authority",
                    job.Phase, "government", job.CorrelationId, job.Summary,
                    "major_world", job.ChancellorHeroId, job.KingdomId,
                    new Newtonsoft.Json.Linq.JObject
                    {
                        ["jobId"] = job.JobId,
                        ["rulerHeroId"] = job.RulerHeroId,
                        ["chancellorHeroId"] = job.ChancellorHeroId,
                        ["startDay"] = job.StartDay,
                        ["endDay"] = job.EndDay,
                        ["outcome"] = job.Outcome
                    }, new Newtonsoft.Json.Linq.JArray(job.RulerHeroId));
                job.Applied = true;
                ReignDocketHistoryRecord record = EnsureRulerDocketState().History
                    .LastOrDefault(x => x.ChancellorHeroId == job.ChancellorHeroId
                        && string.IsNullOrWhiteSpace(x.LinkedWorldHistoryEventId));
                if (record != null) record.LinkedWorldHistoryEventId = job.JobId;
            }
        }

        private void RefusePetitionWithoutEffects(ReignDocketPetition petition, int day, string reason)
        {
            if (petition == null || !petition.IsPending) return;
            petition.State = ReignDocketPetitionState.Refused;
            petition.DecidedDay = day;
            petition.DecisionReason = reason ?? string.Empty;
            ReignRulerDocketState state = EnsureRulerDocketState();
            state.Cooldowns.Add(new ReignDocketCooldown
            {
                TargetSettlementId = petition.TargetSettlementId,
                Kind = petition.Kind,
                UntilDay = day + ReignRulerDocketRules.DeniedRepeatCooldownDays
            });
            AddPetitionHistory(petition, "refused", "The ruler refused the petition.");
        }

        internal void GenerateRulerDocketForDay(int day)
        {
            ReignRulerDocketState state = EnsureRulerDocketState();
            // Loading after midnight can reach the hourly generation route before
            // Bannerlord raises the daily tick. Always close yesterday's audience
            // first; the persisted day marker makes this safe and idempotent.
            ProcessRulerDocketMidnightRefusals(day);
            if (state.LastGeneratedDay >= day) return;
            state.LastGeneratedDay = day;
            CollapseDuplicatePendingPetitioners(day);
            RecordDocketSettlementSamples(day);
            state.Cooldowns.RemoveAll(x => x == null || x.UntilDay <= day);
            if (state.Chancellor.SuppressesPetitions || Hero.MainHero?.IsPrisoner == true) return;

            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (kingdom == null) return;
            List<NativePetitionCandidate> native = new List<NativePetitionCandidate>();
            foreach (Settlement townSettlement in Settlement.All.Where(x => x?.Town != null && x.OwnerClan?.Kingdom == kingdom))
                BuildTownPetitionCandidates(townSettlement, day, native);

            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            string timelineId = ReignServices.WorldHistory?.TimelineId ?? "main";
            int replies = Math.Min(ReignCourtLifeRules.MaximumDailyEntries, DueInternationalReplyCount(day));
            PromoteInternationalReplies(day, replies);
            int count = ReignCourtLifeRules.OrdinarySlots(campaignId, timelineId, day, replies);
            string seed = campaignId + "|" + timelineId + "|" + day;
            int notableCount = 0;
            for (int slot = 0; slot < count; slot++)
            {
                ReignCourtLifeMatter life = null;
                switch (ReignCourtLifeRules.SourceForSlot(campaignId, timelineId, day, slot))
                {
                    case ReignDocketSource.Notable: notableCount++; break;
                    case ReignDocketSource.DomesticNoble:
                        life = TryCreateCourtJealousyMatterForSlot(campaignId, timelineId, day, slot);
                        if (life == null) TryCreateNobleMatterForSlot(campaignId, timelineId, day, slot);
                        break;
                    case ReignDocketSource.International: life = TryCreateInternationalMatterForSlot(campaignId, timelineId, day, slot); break;
                    case ReignDocketSource.Family: life = TryCreateFamilyVisitMatterForSlot(campaignId, timelineId, day, slot); break;
                    case ReignDocketSource.Patronage: life = TryCreatePatronageMatterForSlot(campaignId, timelineId, day, slot); break;
                    case ReignDocketSource.Visitor: life = TryCreateNobleVisitorMatterForSlot(campaignId, timelineId, day, slot); break;
                }
                if (life != null && !state.CourtLifeMatters.Any(x => x.MatterId == life.MatterId)) state.CourtLifeMatters.Add(life);
            }
            Dictionary<string, NativePetitionCandidate> byId = native.ToDictionary(x => x.Score.CandidateId, StringComparer.Ordinal);
            var pendingPetitioners = new HashSet<string>(state.Petitions
                .Where(x => x?.IsPending == true && !string.IsNullOrWhiteSpace(x.PetitionerHeroId))
                .Select(x => x.PetitionerHeroId), StringComparer.OrdinalIgnoreCase);
            foreach (ReignPetitionCandidateScore selected in ReignRulerDocketRules.SelectCandidates(
                native.Where(x => x.Petitioner != null && !pendingPetitioners.Contains(x.Petitioner.StringId))
                    .Select(x => x.Score), notableCount, seed))
            {
                if (byId.TryGetValue(selected.CandidateId, out NativePetitionCandidate candidate)
                    && candidate.Petitioner != null
                    && pendingPetitioners.Add(candidate.Petitioner.StringId))
                    state.Petitions.Add(CreatePetitionSnapshot(candidate, campaignId, timelineId, day));
            }
        }

        internal void BuildTownPetitionCandidates(Settlement townSettlement, int day, List<NativePetitionCandidate> output)
        {
            Town town = townSettlement?.Town;
            if (town == null) return;
            foreach (Village village in town.Villages.Where(x => x?.Settlement != null))
            {
                ReignPetitionSeverity food = ReignRulerDocketRules.FoodSeverity(village.Hearth);
                AddCandidate(output, townSettlement, village.Settlement, ReignPetitionKind.Food, food,
                    village.Hearth, Math.Max(0d, (400d - village.Hearth) / 400d), 0d, day);

                ReignDocketSettlementSample sample = LatestSample(village.Settlement.StringId, day);
                ReignPetitionSeverity villageGold = ReignRulerDocketRules.VillageGoldSeverity(village.Hearth,
                    sample?.ActualVillageOutput ?? 0d, sample?.HealthyVillageOutput ?? 0d);
                AddCandidate(output, townSettlement, village.Settlement, ReignPetitionKind.VillageGold, villageGold,
                    sample?.ActualVillageOutput ?? 0d,
                    sample?.HealthyVillageOutput > 0d ? 1d - (sample.ActualVillageOutput / sample.HealthyVillageOutput) : 0d,
                    sample?.HealthyVillageOutput ?? 0d, day);
            }

            double trend = ThreeDayProsperityTrend(townSettlement.StringId, day);
            ReignPetitionSeverity gold = ReignRulerDocketRules.TownGoldSeverity(town.Prosperity, trend);
            AddCandidate(output, townSettlement, townSettlement, ReignPetitionKind.TownGold, gold,
                town.Prosperity, Math.Max(0d, (5000d - town.Prosperity) / 5000d), 0d, day);
            ReignPetitionSeverity soldiers = ReignRulerDocketRules.SoldierSeverity(town.Security, townSettlement.IsUnderSiege);
            AddCandidate(output, townSettlement, townSettlement, ReignPetitionKind.Soldiers, soldiers,
                town.Security, Math.Max(0d, (80d - town.Security) / 80d), 0d, day);
        }

        private void AddCandidate(List<NativePetitionCandidate> output, Settlement parentTown, Settlement target,
            ReignPetitionKind kind, ReignPetitionSeverity severity, double needValue, double normalizedNeed,
            double expectedValue, int day)
        {
            if (severity == ReignPetitionSeverity.None || target == null || parentTown == null) return;
            ReignRulerDocketState state = EnsureRulerDocketState();
            if (state.Cooldowns.Any(x => x.UntilDay > day && x.Kind == kind
                && string.Equals(x.TargetSettlementId, target.StringId, StringComparison.OrdinalIgnoreCase))) return;
            if (state.Commitments.Any(x => x.Active && x.Kind == kind
                && string.Equals(x.TargetSettlementId, target.StringId, StringComparison.OrdinalIgnoreCase))) return;
            Hero petitioner = SelectPetitioner(parentTown, target, kind, day);
            if (petitioner == null) return;
            output.Add(new NativePetitionCandidate
            {
                ParentTown = parentTown,
                Target = target,
                Petitioner = petitioner,
                NeedValue = needValue,
                ExpectedValue = expectedValue,
                Score = new ReignPetitionCandidateScore
                {
                    CandidateId = parentTown.StringId + ":" + target.StringId + ":" + kind,
                    TownId = parentTown.StringId,
                    Kind = kind,
                    Severity = severity,
                    NormalizedNeed = Math.Max(0d, Math.Min(1d, normalizedNeed))
                }
            });
        }

        internal Hero SelectPetitioner(Settlement parentTown, Settlement target, ReignPetitionKind kind, int day)
        {
            Hero governor = parentTown.Town?.Governor;
            if (IsEligibleRepresentative(governor)) return governor;
            return target.Notables.Concat(parentTown.Notables)
                .Where(IsEligiblePetitioner).Distinct()
                .OrderBy(x => StableCourtOrder(x.StringId + "|" + kind, day)).FirstOrDefault();
        }

        private static bool IsEligiblePetitioner(Hero hero)
        {
            return IsEligibleRepresentative(hero) && hero.IsNotable
                && hero.Occupation != Occupation.GangLeader;
        }

        private static bool IsEligibleRepresentative(Hero hero)
        {
            return hero != null && hero.IsAlive && hero.IsActive && !hero.IsPrisoner;
        }

        internal ReignDocketPetition CreatePetitionSnapshot(NativePetitionCandidate candidate, string campaignId, string timelineId, int day)
        {
            ReignPetitionTerms terms = ReignRulerDocketRules.Terms(candidate.Score.Kind, candidate.Score.Severity);
            double effect = candidate.Score.Kind == ReignPetitionKind.Food ? terms.HearthDaily
                : candidate.Score.Kind == ReignPetitionKind.TownGold ? terms.ProsperityDaily
                : candidate.Score.Kind == ReignPetitionKind.VillageGold ? terms.VillageOutputFactor : terms.SecurityDaily;
            return new ReignDocketPetition
            {
                PetitionId = "petition_" + Guid.NewGuid().ToString("N"), CampaignId = campaignId, TimelineId = timelineId,
                ReignId = CurrentReignId(), ReceivedDay = day, Kind = candidate.Score.Kind, Severity = candidate.Score.Severity,
                PetitionerHeroId = candidate.Petitioner.StringId, PetitionerName = candidate.Petitioner.Name?.ToString() ?? "Notable",
                TargetSettlementId = candidate.Target.StringId, TargetSettlementName = candidate.Target.Name?.ToString() ?? "Settlement",
                ParentTownId = candidate.ParentTown.StringId, ParentTownName = candidate.ParentTown.Name?.ToString() ?? "Town",
                NeedValue = candidate.NeedValue, HealthyExpectedValue = candidate.ExpectedValue, NormalizedNeed = candidate.Score.NormalizedNeed,
                GoldCost = terms.Gold, FoodStockCost = terms.FoodStock, SoldierCount = terms.Soldiers, DurationDays = terms.DurationDays,
                RequesterRelationDelta = terms.RequesterRelation, AssociatedRelationDelta = terms.AssociatedRelation,
                DailyEffect = effect, DangerLabel = candidate.Score.Severity == ReignPetitionSeverity.Minor ? "Low" : candidate.Score.Severity == ReignPetitionSeverity.Serious ? "Serious" : "Severe",
                ProblemSummary = PetitionProblemSummary(candidate.Score.Kind, candidate.Score.Severity, candidate.Target.Name?.ToString()),
                TermsHash = ReignCourtTerms.Hash(candidate.Score.Kind + "|" + candidate.Score.Severity + "|" + terms.Gold + "|" + terms.FoodStock + "|" + terms.Soldiers + "|" + terms.DurationDays)
            };
        }

        private void RecordDocketSettlementSamples(int day)
        {
            ReignRulerDocketState state = EnsureRulerDocketState();
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (kingdom == null) return;
            state.SettlementSamples.RemoveAll(x => x.Day < day - 4 || x.Day == day);
            foreach (Settlement settlement in Settlement.All.Where(x => x?.OwnerClan?.Kingdom == kingdom && (x.Town != null || x.Village != null)))
            {
                Village village = settlement.Village;
                double healthy = HealthyVillageProduction(village);
                double actual = ReignVillageProductionCalculatorModel.TotalProduction(village, false);
                state.SettlementSamples.Add(new ReignDocketSettlementSample
                {
                    SettlementId = settlement.StringId, Day = day, Prosperity = settlement.Town?.Prosperity ?? 0d,
                    FoodStocks = settlement.Town?.FoodStocks ?? 0d, Security = settlement.Town?.Security ?? 0d,
                    Hearth = village?.Hearth ?? 0d, ActualVillageOutput = actual, HealthyVillageOutput = healthy
                });
            }
        }

        private ReignDocketSettlementSample LatestSample(string settlementId, int day) => EnsureRulerDocketState().SettlementSamples
            .Where(x => x.Day <= day && string.Equals(x.SettlementId, settlementId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Day).FirstOrDefault();

        private double ThreeDayProsperityTrend(string settlementId, int day)
        {
            List<ReignDocketSettlementSample> samples = EnsureRulerDocketState().SettlementSamples
                .Where(x => x.Day >= day - 3 && x.Day <= day && string.Equals(x.SettlementId, settlementId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Day).ToList();
            return samples.Count < 2 ? 0d : samples.Last().Prosperity - samples.First().Prosperity;
        }

        public ReignPetitionDecisionQuote GetRulerPetitionQuote(string petitionId)
        {
            ReignDocketPetition petition = EnsureRulerDocketState().Petitions.FirstOrDefault(x => x.PetitionId == petitionId);
            ReignPetitionDecisionQuote quote = new ReignPetitionDecisionQuote { PetitionId = petitionId ?? string.Empty };
            if (petition == null || !petition.IsPending)
            {
                quote.Error = "The petition is no longer pending.";
                return quote;
            }
            if (!ValidatePetitionIdentity(petition, out string identityError))
            {
                quote.Error = identityError;
                return quote;
            }
            Settlement capital = FindCurrentCapital();
            if (capital?.Town == null)
            {
                quote.Error = "A valid kingdom capital is required.";
                return quote;
            }

            quote.Valid = true;
            quote.DirectGoldCost = petition.GoldCost;
            quote.FoodStockCost = petition.FoodStockCost;
            quote.SoldierCount = petition.SoldierCount;
            switch (petition.Kind)
            {
                case ReignPetitionKind.Food:
                    quote.CanGrantDirect = capital.Town.FoodStocks + 0.001f >= petition.FoodStockCost;
                    int grainPrice = Math.Max(0, capital.Town.GetItemPrice(DefaultItems.Grain));
                    quote.GoldSubstituteCost = ReignRulerDocketRules.FoodGoldSubstitute(petition.FoodStockCost, grainPrice);
                    quote.CanGrantWithGold = Hero.MainHero?.Gold >= quote.GoldSubstituteCost;
                    if (quote.CanGrantDirect && capital.Town.FoodStocks - petition.FoodStockCost <= 0f)
                        quote.Warning = "This grant will drain the capital's food stocks into shortage.";
                    break;
                case ReignPetitionKind.Soldiers:
                    quote.CanGrantDirect = TryBuildSoldierManifest(capital, petition, out List<ReignSoldierManifestEntry> manifest,
                        out int healthy, out int reserve, out int replacement, out int wages);
                    quote.HealthyGarrison = healthy;
                    quote.RequiredGarrisonReserve = reserve;
                    quote.GoldSubstituteCost = ReignRulerDocketRules.SoldierGoldSubstitute(replacement, wages);
                    quote.CanGrantWithGold = Hero.MainHero?.Gold >= quote.GoldSubstituteCost;
                    break;
                default:
                    quote.CanGrantDirect = Hero.MainHero?.Gold >= petition.GoldCost;
                    quote.CanGrantWithGold = false;
                    break;
            }
            return quote;
        }

        public bool TryDecideRulerPetition(string petitionId, ReignDocketGrantMethod method, bool refuse, out string receipt, ReignXpOptions xpOptions = null)
        {
            receipt = string.Empty;
            ReignDocketPetition petition = EnsureRulerDocketState().Petitions.FirstOrDefault(x => x.PetitionId == petitionId);
            if (petition == null || !petition.IsPending) { receipt = "The petition is no longer pending."; return false; }
            if (!HasRoyalCommandAccess) { receipt = "Petitions may be decided only while holding Court in the capital."; return false; }
            if (!ValidatePetitionIdentity(petition, out string identityError))
            {
                InvalidatePetition(petition, identityError);
                receipt = identityError;
                return false;
            }
            if (refuse)
            {
                RefusePetitionWithoutEffects(petition, CurrentDay(), "explicit_refusal");
                QueuePetitionRelationChanges(petition, false);
                receipt = "Petition refused.";
                ReignBeta.Dialogue.Campaign.ReignXpCampaignBehavior.Instance?.Award("petition:" + petitionId, ReignXpSkill.Leadership, ReignXpRules.PetitionXp, xpOptions, ReignXpReason.PetitionDecision);
                StateChanged?.Invoke();
                return true;
            }

            ReignPetitionDecisionQuote quote = GetRulerPetitionQuote(petitionId);
            if (!quote.Valid) { receipt = quote.Error; return false; }
            Settlement capital = FindCurrentCapital();
            List<ReignSoldierManifestEntry> manifest = null;
            if (method == ReignDocketGrantMethod.Direct && !quote.CanGrantDirect)
            {
                receipt = "The direct grant is no longer affordable.";
                return false;
            }
            if (method == ReignDocketGrantMethod.GoldSubstitute && !quote.CanGrantWithGold)
            {
                receipt = "The gold substitute is no longer affordable.";
                return false;
            }
            if (method == ReignDocketGrantMethod.GoldSubstitute
                && petition.Kind != ReignPetitionKind.Food && petition.Kind != ReignPetitionKind.Soldiers)
            {
                receipt = "This petition has no gold substitute.";
                return false;
            }
            if (petition.Kind == ReignPetitionKind.Soldiers && method == ReignDocketGrantMethod.Direct
                && !TryBuildSoldierManifest(capital, petition, out manifest, out _, out _, out _, out _))
            {
                receipt = "The capital garrison can no longer supply the requested regulars while retaining its reserve.";
                return false;
            }

            if (method == ReignDocketGrantMethod.GoldSubstitute)
                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, quote.GoldSubstituteCost, true);
            else if (petition.Kind == ReignPetitionKind.Food)
                capital.Town.FoodStocks -= petition.FoodStockCost;
            else if (petition.Kind == ReignPetitionKind.Soldiers)
                DispatchSoldierExpedition(capital, petition, manifest);
            else if (petition.GoldCost > 0)
                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, petition.GoldCost, true);

            petition.State = ReignDocketPetitionState.Granted;
            petition.GrantMethod = method;
            petition.DecidedDay = CurrentDay();
            petition.DecisionReason = method == ReignDocketGrantMethod.GoldSubstitute ? "gold_substitute" : "direct_grant";
            ReignDocketCommitment commitment = new ReignDocketCommitment
            {
                CommitmentId = "commitment_" + Guid.NewGuid().ToString("N"), PetitionId = petition.PetitionId,
                Kind = petition.Kind, Severity = petition.Severity, TargetSettlementId = petition.TargetSettlementId,
                TargetSettlementName = petition.TargetSettlementName, StartDay = CurrentDay(),
                EndDay = CurrentDay() + petition.DurationDays, DailyEffect = petition.DailyEffect, Active = true
            };
            EnsureRulerDocketState().Commitments.Add(commitment);
            ApplyCommitmentDailyEffect(commitment, CurrentDay());
            QueuePetitionRelationChanges(petition, true);
            AddPetitionHistory(petition, "granted", method == ReignDocketGrantMethod.GoldSubstitute
                ? "The ruler funded the validated gold substitute." : "The ruler granted the requested aid directly.");
            receipt = "Petition granted. The commitment is now active.";
            ReignBeta.Dialogue.Campaign.ReignXpCampaignBehavior.Instance?.Award("petition:" + petitionId, ReignXpSkill.Leadership, ReignXpRules.PetitionXp, xpOptions, ReignXpReason.PetitionDecision);
            StateChanged?.Invoke();
            return true;
        }

        public void AttachRulerPetitionPresentation(string petitionId,
            string transcriptId, string sceneAssetPath)
        {
            ReignRulerDocketState state = EnsureRulerDocketState();
            ReignDocketPetition petition = state.Petitions.FirstOrDefault(x => x?.PetitionId == petitionId);
            if (petition == null) return;
            if (!string.IsNullOrWhiteSpace(transcriptId)) petition.TranscriptId = transcriptId;
            if (!string.IsNullOrWhiteSpace(sceneAssetPath)) petition.SceneAssetPath = sceneAssetPath;
            foreach (ReignDocketHistoryRecord history in state.History.Where(x => x?.PetitionId == petitionId))
            {
                if (!string.IsNullOrWhiteSpace(transcriptId)) history.TranscriptId = transcriptId;
                if (!string.IsNullOrWhiteSpace(sceneAssetPath)) history.SceneAssetPath = sceneAssetPath;
            }
            StateChanged?.Invoke();
        }

        private void ProcessRulerDocketMidnightRefusals(int day)
        {
            ReignRulerDocketState state = EnsureRulerDocketState();
            if (state.LastMidnightDecisionDay >= day) return;
            state.LastMidnightDecisionDay = day;

            List<ReignDocketPetition> overdue = state.Petitions
                .Where(x => x != null
                    && x.IsPending
                    && x.ReceivedDay < day)
                .OrderBy(x => x.ReceivedDay)
                .ThenBy(x => x.PetitionId, StringComparer.Ordinal)
                .ToList();
            foreach (ReignDocketPetition petition in overdue)
            {
                RefusePetitionWithoutEffects(petition, day,
                    "midnight_automatic_refusal");
                QueuePetitionRelationChanges(petition, false);
            }

            if (overdue.Count > 0)
            {
                ReignLog.Info("Automatically refused " + overdue.Count
                    + " unresolved ruler docket petition(s) at midnight for day "
                    + day + ".");
                StateChanged?.Invoke();
            }
        }

        private bool ValidatePetitionIdentity(ReignDocketPetition petition, out string error)
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Settlement target = Settlement.Find(petition.TargetSettlementId);
            Hero petitioner = FindHero(petition.PetitionerHeroId);
            if (kingdom == null) { error = "The ruler no longer has a kingdom."; return false; }
            if (FindCurrentCapital()?.Town == null) { error = "The designated capital is no longer valid."; return false; }
            if (target == null || target.OwnerClan?.Kingdom != kingdom) { error = "The petition's settlement has left the kingdom."; return false; }
            if (petitioner == null || !petitioner.IsAlive || !petitioner.IsActive || petitioner.IsPrisoner)
            { error = "The original petitioner is no longer able to appear; the petition was not reassigned."; return false; }
            error = string.Empty;
            return true;
        }

        private void InvalidatePetition(ReignDocketPetition petition, string reason)
        {
            petition.State = ReignDocketPetitionState.Invalidated;
            petition.DecidedDay = CurrentDay();
            petition.DecisionReason = reason ?? string.Empty;
            AddPetitionHistory(petition, "invalidated", reason);
        }

        private bool TryBuildSoldierManifest(Settlement capital, ReignDocketPetition petition,
            out List<ReignSoldierManifestEntry> manifest, out int healthy, out int reserve,
            out int replacementValue, out int fullTermWages)
        {
            manifest = new List<ReignSoldierManifestEntry>();
            healthy = 0; reserve = 0; replacementValue = 0; fullTermWages = 0;
            MobileParty garrison = capital?.Town?.GarrisonParty;
            if (garrison?.MemberRoster == null || petition == null || petition.SoldierCount <= 0) return false;
            List<TroopRosterElement> candidates = garrison.MemberRoster.GetTroopRoster()
                .Where(x => x.Character != null && !x.Character.IsHero && x.Number > x.WoundedNumber)
                .OrderBy(x => StableCourtOrder(x.Character.StringId + "|" + petition.PetitionId, petition.ReceivedDay))
                .ToList();
            healthy = candidates.Sum(x => x.Number - x.WoundedNumber);
            reserve = ReignRulerDocketRules.MinimumHealthyGarrisonAfterDispatch(healthy);
            if (healthy - petition.SoldierCount < reserve) return false;
            int remaining = petition.SoldierCount;
            foreach (TroopRosterElement element in candidates)
            {
                int count = Math.Min(remaining, element.Number - element.WoundedNumber);
                if (count <= 0) continue;
                manifest.Add(new ReignSoldierManifestEntry
                { CharacterId = element.Character.StringId, CharacterName = element.Character.Name?.ToString() ?? "Regular", Count = count });
                int recruit = (int)Math.Ceiling(Math.Max(0f, TaleWorlds.CampaignSystem.Campaign.Current.Models.PartyWageModel
                    .GetTroopRecruitmentCost(element.Character, Hero.MainHero, false).ResultNumber));
                replacementValue = SaturatingAdd(replacementValue, recruit * count);
                fullTermWages = SaturatingAdd(fullTermWages, ReignBeta.WorldSimulation.Economy.ReignWarEconomyOptions.BaseTroopWage(element.Character) * count * petition.DurationDays);
                remaining -= count;
                if (remaining == 0) break;
            }
            return remaining == 0;
        }

        private void DispatchSoldierExpedition(Settlement capital, ReignDocketPetition petition, List<ReignSoldierManifestEntry> manifest)
        {
            MobileParty garrison = capital.Town.GarrisonParty;
            foreach (ReignSoldierManifestEntry entry in manifest)
            {
                TroopRosterElement element = garrison.MemberRoster.GetTroopRoster()
                    .First(x => string.Equals(x.Character.StringId, entry.CharacterId, StringComparison.Ordinal));
                garrison.MemberRoster.AddToCounts(element.Character, -entry.Count);
            }
            string expeditionId = "expedition_" + Guid.NewGuid().ToString("N");
            int casualties = ReignRulerDocketRules.CasualtyCount(expeditionId, petition.SoldierCount,
                ReignRulerDocketRules.Terms(ReignPetitionKind.Soldiers, petition.Severity).CasualtyPercentMaximum);
            petition.HiddenCasualtyCount = casualties;
            int remainingCasualties = casualties;
            foreach (ReignSoldierManifestEntry entry in manifest.OrderBy(x => StableCourtOrder(x.CharacterId + "|casualty|" + expeditionId, 0)))
            {
                entry.Casualties = Math.Min(entry.Count, remainingCasualties);
                remainingCasualties -= entry.Casualties;
            }
            EnsureRulerDocketState().Expeditions.Add(new ReignSoldierExpedition
            {
                ExpeditionId = expeditionId, PetitionId = petition.PetitionId,
                OriginalOwnerClanId = capital.OwnerClan?.StringId ?? string.Empty, OriginalCapitalId = capital.StringId,
                TargetSettlementId = petition.TargetSettlementId, DepartureDay = CurrentDay(),
                ReturnDay = CurrentDay() + petition.DurationDays, HiddenCasualtyCount = casualties,
                DangerLabel = petition.DangerLabel, Manifest = manifest
            });
        }

        private void QueuePetitionRelationChanges(ReignDocketPetition petition, bool granted)
        {
            ReignRulerDocketState state = EnsureRulerDocketState();
            string rulerId = Hero.MainHero?.StringId ?? string.Empty;
            int requesterDelta = granted ? petition.RequesterRelationDelta : -petition.RequesterRelationDelta;
            state.PendingRelationAdjustments.Add(new ReignDirectionalRelationAdjustment
            {
                AdjustmentId = "docket_relation_" + Guid.NewGuid().ToString("N"), ObserverHeroId = petition.PetitionerHeroId,
                SubjectHeroId = rulerId, Delta = requesterDelta, Reason = "petition_" + (granted ? "granted" : "refused")
            });
            if (!granted) return;
            Settlement target = Settlement.Find(petition.TargetSettlementId);
            IEnumerable<Hero> associated = target == null ? Enumerable.Empty<Hero>() : target.Notables;
            foreach (Hero notable in associated.Where(x => x != null && x.StringId != petition.PetitionerHeroId))
            {
                state.PendingRelationAdjustments.Add(new ReignDirectionalRelationAdjustment
                {
                    AdjustmentId = "docket_relation_" + Guid.NewGuid().ToString("N"), ObserverHeroId = notable.StringId,
                    SubjectHeroId = rulerId, Delta = petition.AssociatedRelationDelta, Reason = "associated_petition_granted"
                });
            }
        }

        private static int SaturatingAdd(int first, int second)
        {
            long result = (long)first + second;
            return result >= int.MaxValue ? int.MaxValue : result <= int.MinValue ? int.MinValue : (int)result;
        }

        public float GetDocketVillageProductionFactor(Village village)
        {
            if (village?.Settlement == null) return 0f;
            return (float)EnsureRulerDocketState().Commitments.Where(x => x.Active
                    && x.Kind == ReignPetitionKind.VillageGold
                    && string.Equals(x.TargetSettlementId, village.Settlement.StringId, StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.DailyEffect);
        }

        private void ProcessDocketCommitments(int day)
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            foreach (ReignDocketCommitment commitment in EnsureRulerDocketState().Commitments.Where(x => x.Active).ToList())
            {
                Settlement target = Settlement.Find(commitment.TargetSettlementId);
                if (target == null || target.OwnerClan?.Kingdom != kingdom)
                {
                    commitment.Active = false;
                    commitment.EndReason = "location_left_kingdom";
                    AddCommitmentHistory(commitment, day, "The supported settlement left the kingdom; the modifier ended without a refund.");
                    continue;
                }
                if (day >= commitment.EndDay)
                {
                    commitment.Active = false;
                    commitment.EndReason = "term_completed";
                    AddCommitmentHistory(commitment, day, "The promised support term was completed.");
                    continue;
                }
                ApplyCommitmentDailyEffect(commitment, day);
            }
        }

        private void ApplyCommitmentDailyEffect(ReignDocketCommitment commitment, int day)
        {
            if (commitment == null || !commitment.Active || commitment.LastAppliedDay >= day) return;
            Settlement target = Settlement.Find(commitment.TargetSettlementId);
            if (target == null) return;
            switch (commitment.Kind)
            {
                case ReignPetitionKind.Food:
                    if (target.Village != null) {
                        float beforeHearth = target.Village.Hearth;
                        target.Village.Hearth = MathF.Max(0f, target.Village.Hearth + (float)commitment.DailyEffect);
                        ReignBeta.WorldSimulation.Economy.ReignWarEconomyTransactions.RecordHearthGrant("paid_petition", target.Village, beforeHearth);
                    }
                    break;
                case ReignPetitionKind.TownGold:
                    if (target.Town != null) target.Town.Prosperity = MathF.Max(0f, target.Town.Prosperity + (float)commitment.DailyEffect);
                    break;
                case ReignPetitionKind.Soldiers:
                    if (target.Town != null) target.Town.Security = MBMath.ClampFloat(target.Town.Security + (float)commitment.DailyEffect, 0f, 100f);
                    break;
            }
            commitment.LastAppliedDay = day;
        }

        private void ProcessSoldierExpeditionReturns(int day)
        {
            foreach (ReignSoldierExpedition expedition in EnsureRulerDocketState().Expeditions
                .Where(x => x != null && !x.Returned && x.ReturnDay <= day).ToList())
            {
                Settlement destination = ResolveExpeditionReturnDestination(expedition);
                MobileParty garrison = destination?.Town?.GarrisonParty;
                if (garrison?.MemberRoster == null)
                {
                    expedition.ReturnStatus = "returning";
                    continue;
                }
                int returned = 0;
                foreach (ReignSoldierManifestEntry entry in expedition.Manifest ?? new List<ReignSoldierManifestEntry>())
                {
                    int survivors = Math.Max(0, entry.Count - entry.Casualties);
                    CharacterObject character = MBObjectManager.Instance.GetObject<CharacterObject>(entry.CharacterId);
                    if (character == null || survivors <= 0) continue;
                    garrison.MemberRoster.AddToCounts(character, survivors);
                    returned += survivors;
                }
                expedition.Returned = true;
                expedition.ReturnSettlementId = destination.StringId;
                expedition.ReturnStatus = "returned";
                EnsureRulerDocketState().History.Add(new ReignDocketHistoryRecord
                {
                    RecordId = "docket_history_" + Guid.NewGuid().ToString("N"), ReignId = CurrentReignId(),
                    Type = "soldier_expedition", Outcome = "completed", PetitionId = expedition.PetitionId,
                    Day = day, SettlementId = destination.StringId, SettlementName = destination.Name?.ToString() ?? "Fortification",
                    Summary = "The petition expedition returned.",
                    Detail = returned + " soldiers returned; " + expedition.HiddenCasualtyCount + " were lost."
                });
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + returned
                    + " petition troops returned to " + destination.Name + "; " + expedition.HiddenCasualtyCount + " were lost."));
            }
        }

        private Settlement ResolveExpeditionReturnDestination(ReignSoldierExpedition expedition)
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Settlement originalOwnerFortification = Settlement.All.Where(x => x?.IsFortification == true
                    && string.Equals(x.OwnerClan?.StringId, expedition.OriginalOwnerClanId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.StringId).FirstOrDefault();
            if (originalOwnerFortification?.Town?.GarrisonParty != null) return originalOwnerFortification;
            Settlement capital = FindCurrentCapital();
            if (capital?.Town?.GarrisonParty != null && capital.OwnerClan?.Kingdom == kingdom) return capital;
            Settlement target = Settlement.Find(expedition.TargetSettlementId) ?? Settlement.Find(expedition.OriginalCapitalId);
            return Settlement.All.Where(x => x?.IsFortification == true && x.OwnerClan?.Kingdom == kingdom && x.Town?.GarrisonParty != null)
                .OrderBy(x => target == null ? 0f : x.GetPosition2D.DistanceSquared(target.GetPosition2D))
                .ThenBy(x => x.StringId).FirstOrDefault();
        }

        private void AddCommitmentHistory(ReignDocketCommitment commitment, int day, string detail)
        {
            EnsureRulerDocketState().History.Add(new ReignDocketHistoryRecord
            {
                RecordId = "docket_history_" + Guid.NewGuid().ToString("N"), ReignId = CurrentReignId(),
                Type = "commitment", Outcome = commitment.EndReason, PetitionId = commitment.PetitionId,
                PetitionKind = commitment.Kind, Severity = commitment.Severity, Day = day,
                SettlementId = commitment.TargetSettlementId, SettlementName = commitment.TargetSettlementName,
                Summary = "Petition commitment ended.", Detail = detail
            });
        }

        private static double HealthyVillageProduction(Village village)
        {
            if (village?.VillageType?.Productions == null || TaleWorlds.CampaignSystem.Campaign.Current?.Models?.VillageProductionCalculatorModel == null)
                return 0d;
            double total = 0d;
            foreach (ValueTuple<ItemObject, float> production in village.VillageType.Productions)
            {
                if (production.Item1 == null) continue;
                total += Math.Max(0f, ReignVillageProductionCalculatorModel.PotentialProduction(village, production.Item1));
            }
            return total;
        }

        private void AddPetitionHistory(ReignDocketPetition petition, string outcome, string detail)
        {
            EnsureRulerDocketState().History.Add(new ReignDocketHistoryRecord
            {
                RecordId = "docket_history_" + Guid.NewGuid().ToString("N"), ReignId = petition.ReignId,
                Type = "petition", Outcome = outcome, PetitionId = petition.PetitionId, PetitionKind = petition.Kind,
                Severity = petition.Severity, Day = petition.DecidedDay, PetitionerHeroId = petition.PetitionerHeroId,
                PetitionerName = petition.PetitionerName, SettlementId = petition.TargetSettlementId,
                SettlementName = petition.TargetSettlementName, Summary = petition.ProblemSummary, Detail = detail,
                TranscriptId = petition.TranscriptId, SceneAssetPath = petition.SceneAssetPath
            });
        }

        private static string PetitionProblemSummary(ReignPetitionKind kind, ReignPetitionSeverity severity, string settlement)
        {
            string place = string.IsNullOrWhiteSpace(settlement) ? "A settlement" : settlement;
            string problem = kind == ReignPetitionKind.Food ? "needs food relief"
                : kind == ReignPetitionKind.TownGold ? "needs investment"
                : kind == ReignPetitionKind.VillageGold ? "needs production support" : "needs temporary soldiers";
            return place + " " + problem + " (" + severity.ToString().ToLowerInvariant() + ").";
        }

        internal sealed class NativePetitionCandidate
        {
            public ReignPetitionCandidateScore Score;
            public Settlement ParentTown;
            public Settlement Target;
            public Hero Petitioner;
            public double NeedValue;
            public double ExpectedValue;
        }
    }
}
