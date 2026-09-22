using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        // Native dates already exceed 90,000 days. A float loses several game minutes
        // there and can put a newly created audience ahead of its own current time.
        private static double CurrentCourtLifeDay() => CampaignTime.Now.ToDays;

        private float _courtLifeBackgroundElapsed;
        private int _courtLifeConversationsInFlight;
        public bool HasPendingCourtLifeDeliveryWork => _courtLifeConversationsInFlight > 0 || _docketRelationDeliveryInFlight || _docketMemoryDeliveryInFlight
            || (_rulerDocketState?.PendingRelationAdjustments?.Any(x => x != null && !x.Applied) ?? false)
            || (_rulerDocketState?.PendingMemoryJobs?.Any(x => x != null && !x.Applied) ?? false)
            || (_rulerDocketState?.PendingWorldHistoryJobs?.Any(x => x != null && !x.Applied) ?? false);
        internal void BeginCourtLifeConversation() { _courtLifeConversationsInFlight++; }
        internal void EndCourtLifeConversation() { _courtLifeConversationsInFlight = Math.Max(0, _courtLifeConversationsInFlight - 1); }
        internal bool OwnsCourtLifeMatter(ReignCourtLifeMatter matter) => ReferenceEquals(Instance, this)
            && (_rulerDocketState?.CourtLifeMatters?.Contains(matter) ?? false);
        public void ProcessCourtLifeBackgroundWork(float dt)
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null) return;
            _courtLifeBackgroundElapsed += dt;
            if (_courtLifeBackgroundElapsed < 5f) return;
            _courtLifeBackgroundElapsed = 0f;
            double now = CampaignTime.Now.ToDays;
            ProcessFamilyVisits(now);
            ProcessInternationalCourtLife(now);
            TryProcessPendingDocketRelationshipAdjustments();
            TryProcessPendingDocketMemoryJobs();
            TryProcessPendingDocketWorldHistoryJobs();
            ProcessPatronageCommissions(now);
        }

        public IReadOnlyList<ReignCourtLifeMatter> DocketCourtLifeMatters => EnsureRulerDocketState().CourtLifeMatters
            .Where(x => x != null && x.IsPending && x.State != ReignCourtLifeMatterState.Arriving && x.State != ReignCourtLifeMatterState.Deferred && x.AvailableDay <= CurrentCourtLifeDay())
            .OrderBy(x => x.ExpiresDay < 0 ? double.MaxValue : x.ExpiresDay)
            .ThenBy(x => x.ReceivedDay).ThenBy(x => x.MatterId, StringComparer.Ordinal).ToList();

        public bool TryActivateCourtLifeMatter(ReignCourtLifeMatter matter, out string error)
        {
            error = string.Empty;
            if (!HasRoyalCommandAccess || matter == null || !matter.IsPending
                || !EnsureRulerDocketState().CourtLifeMatters.Contains(matter))
            { error = "This audience is no longer available at your court."; return false; }
            if (matter.AvailableDay > CurrentCourtLifeDay())
            { error = "Your visitors have not arrived yet."; return false; }
            if (matter.State == ReignCourtLifeMatterState.Deferred)
            { error = "A reply is still awaited."; return false; }
            if (matter.ExpiresDay >= 0 && CurrentCourtLifeDay() >= matter.ExpiresDay)
            { error = "The time for this visit has passed."; return false; }
            if (matter.Source == ReignDocketSource.Visitor && !AreNobleVisitorsAvailable(matter, out error)) return false;
            foreach (ReignCourtLifeParticipant participant in matter.Participants)
            {
                if (string.IsNullOrWhiteSpace(participant.HeroId)) continue;
                Hero hero = FindHero(participant.HeroId);
                if (hero == null || !hero.IsAlive || hero.IsPrisoner)
                { error = participant.Name + " is unavailable."; matter.TechnicalFailure = true; return false; }
            }
            matter.State = ReignCourtLifeMatterState.Active;
            matter.TranscriptId = string.IsNullOrWhiteSpace(matter.TranscriptId) ? "court_life_" + matter.MatterId : matter.TranscriptId;
            StateChanged?.Invoke();
            return true;
        }

        public bool ApplyCourtLifeDecision(ReignCourtLifeMatter matter, JObject decision, out string summary)
        {
            summary = string.Empty;
            if (matter == null) { summary = "The audience is missing."; return false; }
            if (matter.EffectsCommitted) { summary = matter.DecisionSummary; return true; }
            if (!TryActivateCourtLifeMatter(matter, out summary)) return false;
            string optionId = decision?.Value<string>("optionId") ?? string.Empty;
            ReignCourtLifeOption option = matter.Options.FirstOrDefault(x => x.OptionId == optionId);
            if (option == null) { summary = "Choose one of the current audience's available decisions."; return false; }
            bool applied;
            if (matter.Source == ReignDocketSource.International)
                applied = ApplyInternationalDecision(matter, decision, out summary);
            else if (matter.Source == ReignDocketSource.Patronage)
                applied = ApplyPatronageDecision(matter, decision, out summary);
            else if (matter.Source == ReignDocketSource.Visitor)
                applied = ApplyNobleVisitorDecision(matter, decision, out summary);
            else
            {
                // A social audience concludes here; later activities remain in the
                // normal family/visitor conversations and never execute by narration.
                applied = true;
                summary = option.Description;
            }
            if (!applied) return false;
            if (decision.Value<bool?>("deferred") == true)
            { matter.DecisionSummary = summary; StateChanged?.Invoke(); return true; }
            matter.SelectedOptionId = optionId;
            matter.DecisionSummary = summary;
            if (string.IsNullOrWhiteSpace(matter.ResolutionReceiptId)) matter.ResolutionReceiptId = "court_life_decision_" + matter.MatterId;
            matter.EffectsCommitted = true;
            matter.State = ReignCourtLifeMatterState.Resolved;
            EnsureRulerDocketState().History.Add(new ReignDocketHistoryRecord {
                RecordId = matter.ResolutionReceiptId, ReignId = matter.ReignId,
                Type = "court_life_" + matter.Source.ToString().ToLowerInvariant(),
                Outcome = optionId, Day = CurrentDay(), Summary = matter.Title + ": " + summary,
                Detail = "Audience " + matter.MatterId + "; template " + matter.TemplateId,
                PetitionId = matter.MatterId, TranscriptId = matter.TranscriptId, SceneAssetPath = matter.SceneAssetPath });
            StateChanged?.Invoke();
            return true;
        }

        public void NotifyCourtLifeChanged() { StateChanged?.Invoke(); }

        private void OnCanNobleVisitorLeadParty(Hero hero, ref bool result)
        { if (IsNobleVisitorReserved(hero)) result = false; }

        internal JObject BuildCourtLifeApiPayload(ReignCourtLifeMatter matter)
        {
            RefreshLegacyInternationalPaymentTerms(matter);
            EnsureInternationalCaptiveContext(matter);
            return new JObject {
                ["campaignId"] = matter.CampaignId, ["timelineId"] = matter.TimelineId,
                ["matterId"] = matter.MatterId, ["templateId"] = matter.TemplateId,
                ["source"] = matter.Source.ToString(), ["worldDay"] = CurrentCourtLifeDay(),
                ["playerHeroStringId"] = Hero.MainHero?.StringId ?? "",
                ["playerClanStringId"] = Clan.PlayerClan?.StringId ?? "",
                ["playerKingdomStringId"] = Clan.PlayerClan?.Kingdom?.StringId ?? "",
                ["playerIsKingdomRuler"] = Clan.PlayerClan?.Kingdom?.Leader == Hero.MainHero,
                ["sessionId"] = Session?.SessionId ?? "", ["courtScope"] = "capital",
                ["hostSettlementStringId"] = Session?.HostSettlementStringId ?? "",
                ["capitalSettlementStringId"] = CurrentCapital?.StringId ?? "",
                ["transcriptId"] = matter.TranscriptId,
                ["paymentParties"] = InternationalPaymentParties(matter),
                ["availableSettlements"] = JArray.FromObject(Settlement.All.Where(x => x?.Town != null && x.OwnerClan?.Kingdom == Clan.PlayerClan?.Kingdom)
                    .Select(x => new { settlementId = x.StringId, name = x.Name?.ToString() ?? x.StringId })),
                ["options"] = JArray.FromObject(matter.Options.Select(x => new {
                    optionId = x.OptionId, label = x.Label, description = x.Description,
                    terms = ParseCourtLifeJson(x.TermsJson) }))
            };
        }

        internal static JObject ParseCourtLifeJson(string text)
        { try { return JObject.Parse(text ?? "{}"); } catch { return new JObject(); } }
    }
}
