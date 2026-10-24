using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using Reign.Core.Contracts.World;
using ReignBeta.Campaign;
using ReignBeta.Runtime;
using ReignBeta.Save;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private GovernmentNewsLedger _governmentNews = new GovernmentNewsLedger();
        private List<string> _governmentNewsChunks = new List<string>();

        partial void RecordParticipationNewsCore(string kingdomId, string kind, string text)
        {
            var state = GetGovernment(kingdomId); if (state == null) return;
            float day = state.LastMeetingDay >= 0 && CurrentDay() - state.LastMeetingDay < 1 ? state.LastMeetingDay : NextGovernmentSessionDay(CurrentDay());
            RecordGovernmentNewsItem(day, state, new GovernmentNewsItem { ReceiptId = "participation:" + kingdomId + ":" + kind + ":" + GovernmentInterval(CurrentDay()) + ":" + ReignGovernmentRules.StableHash(text), Stage = kind, Text = text });
        }

        public IReadOnlyList<GovernmentNewsEdition> GetGovernmentNews()
            => _governmentNews.Archive.OrderByDescending(e => e.PublishedDay).ToList();

        public void OpenGovernmentNews(string editionId)
        {
            var edition = _governmentNews.Archive.FirstOrDefault(e => e.Id == editionId);
            if (edition != null) ReignNewsCampaignBehavior.Instance?.OpenGovernmentArchive(edition);
        }

        partial void SyncGovernmentSessionNews(IDataStore store)
        {
            if (store.IsSaving) _governmentNewsChunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_governmentNews));
            store.SyncData("_reignGovernment_news_v1", ref _governmentNewsChunks);
            if (!store.IsLoading) return;
            _governmentNews = _governmentNewsChunks == null || _governmentNewsChunks.Count == 0 ? new GovernmentNewsLedger()
                : JsonConvert.DeserializeObject<GovernmentNewsLedger>(ReignSavePayloadCodec.Decode(_governmentNewsChunks)) ?? new GovernmentNewsLedger();
            _governmentNews.Normalize();
        }

        partial void RecordGovernmentSessionNews(ReignGovernmentStateRecord state, float sessionDay)
        {
            var kingdom = FindKingdom(state.KingdomStringId);
            if (kingdom == null) return;
            var introduced = _business.Where(b => Same(b.KingdomStringId, state.KingdomStringId)
                && Math.Abs(b.PresentationDay - sessionDay) < .01f && b.DecisionDay > b.PresentationDay).ToList();
            foreach (var business in introduced)
                RecordGovernmentPresentationNews(business);
            RecordGovernmentNewsItem(sessionDay, state, new GovernmentNewsItem {
                ReceiptId = state.KingdomStringId + ":session:" + sessionDay.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                Stage = "session", Text = introduced.Count == 0 ? "The regular session met. No new matters were introduced."
                    : "The regular session introduced " + introduced.Count + (introduced.Count == 1 ? " new matter." : " new matters.") });
        }

        partial void RecordGovernmentPresentationNews(ReignGovernmentBusinessRecord business)
        {
            var state = GetGovernment(business.KingdomStringId);
            if (state == null) return;
            string petitioner = FindHero(business.PetitionerHeroStringId)?.Name?.ToString();
            string sponsor = FindHero(business.SponsorHeroStringId)?.Name?.ToString();
            string attribution = !string.IsNullOrWhiteSpace(petitioner) ? " Petitioned by " + petitioner + "." : "";
            if (!string.IsNullOrWhiteSpace(sponsor)) attribution += " Presented by " + sponsor + ".";
            string schedule = business.DecisionDay > business.PresentationDay
                ? " Decision scheduled for " + GovernmentNewsDate(business.DecisionDay) + "."
                : " An extraordinary hearing is in progress; its decision will be reported separately.";
            RecordGovernmentNewsItem(business.PresentationDay, state, new GovernmentNewsItem {
                ReceiptId = business.BusinessId + ":presented", BusinessId = business.BusinessId, Stage = "presented",
                Text = "PRESENTED — " + GovernmentBusinessRules.PublicText(business.Title) + ". "
                    + GovernmentBusinessRules.PublicText(GovernmentBusinessRules.PublicMatterSummary(business.Kind, business.Summary)) + attribution + schedule });
        }

        partial void RecordGovernmentDecisionNews(ReignGovernmentBusinessRecord business)
            => RecordGovernmentOutcomeNews(business, true);

        partial void RecordGovernmentVoteNews(ReignGovernmentBusinessRecord business)
        {
            if (!business.ExecutionApplied && !string.IsNullOrEmpty(business.WinningOptionId))
                RecordGovernmentOutcomeNews(business, false);
        }

        private void RecordGovernmentOutcomeNews(ReignGovernmentBusinessRecord business, bool applied)
        {
            var state = GetGovernment(business.KingdomStringId);
            if (state == null) return;
            string outcomeId = applied ? business.ExecutedOptionId : business.WinningOptionId;
            var option = BusinessOptions(business).FirstOrDefault(x => Same(x.Value<string>("id"), outcomeId));
            string chosen = option?.Value<string>("label") ?? business.Outcome;
            string execution = applied ? GovernmentBusinessRules.PublicText(business.Outcome)
                : GovernmentBusinessRules.IsClosed(business.Status)
                    ? "The recorded decision was not implemented. " + GovernmentBusinessRules.PublicText(business.Outcome)
                    : business.Status == "execution_failed"
                        ? GovernmentBusinessRules.PublicText(business.Outcome)
                        : "The vote is recorded. Implementation remains pending; any required counterpart approval or delivery has not yet been confirmed.";
            string description = "DECIDED — " + GovernmentBusinessRules.PublicText(business.Title) + ": "
                + GovernmentBusinessRules.PublicText(chosen) + ". " + execution;
            var votes = JArray.Parse(business.VotesJson ?? "[]").OfType<JObject>().Where(x => !string.IsNullOrEmpty(x.Value<string>("optionId")));
            var totals = votes.GroupBy(x => x.Value<string>("optionId"), StringComparer.Ordinal)
                .Select(g => (BusinessOptions(business).FirstOrDefault(o => o.Value<string>("id") == g.Key)?.Value<string>("label") ?? g.Key)
                    + " " + g.Sum(x => x.Value<decimal?>("weight") ?? 0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%");
            if (totals.Any()) description += " Recorded vote: " + string.Join("; ", totals) + ".";
            if (business.CrownVoteSource == "chancellor")
                description += " " + (FindHero(business.CrownActorHeroId)?.Name?.ToString() ?? "The Chancellor") + " represented the absent ruler.";
            // Approval and completion are separate facts; do not report delivery from the vote alone.
            if (business.Kind == "seasonal" || business.Kind == "resolution") description += " Any required deliveries or other obligations are tracked separately.";
            bool laterImplementation = applied && _governmentNews.IsPublished(business.BusinessId + ":decision");
            if (laterImplementation) description = description.Replace("DECIDED — ", "IMPLEMENTATION UPDATE — ");
            RecordGovernmentNewsItem(CurrentDay(), state, new GovernmentNewsItem {
                ReceiptId = business.BusinessId + (laterImplementation ? ":implementation" : ":decision"),
                BusinessId = business.BusinessId, Stage = laterImplementation ? "implementation" : "decided", Text = description });
        }

        private void RecordGovernmentNewsItem(float day, ReignGovernmentStateRecord state, GovernmentNewsItem item)
        {
            if (!ReignCalendarService.TryGetAbsoluteCalendarDay(GovernmentCampaignTime(day), out double calendarDay)) return;
            _governmentNews.Record(calendarDay, GovernmentNewsDate(day), state.KingdomStringId,
                FindKingdom(state.KingdomStringId)?.Name?.ToString() ?? state.KingdomStringId, state.InstitutionName, item);
        }

        partial void TickGovernmentSessionNews()
        {
            if (ReignNewsCampaignBehavior.Instance == null
                || !ReignCalendarService.TryGetAbsoluteCalendarDay(CampaignTime.Now, out double calendarDay)) return;
            foreach (var edition in _governmentNews.PublishDue(calendarDay))
                ReignNewsCampaignBehavior.Instance.PublishGovernmentEdition(edition);
        }

        private static string GovernmentNewsDate(float day)
        {
            if (day >= 0 && ReignCalendarService.TryGetCalendarPosition(GovernmentCampaignTime(day), out int year,
                out _, out int season, out int seasonDay, out _))
                return new[] { "Spring", "Summer", "Autumn", "Winter" }[season] + " " + (seasonDay + 1) + ", " + year;
            return "the next session";
        }
    }
}
