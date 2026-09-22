using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Court;
using ReignBeta.Integration;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static CourtMatter _courtMatter;
        private static Hero _courtSpeaker;
        private static bool _courtMatterIsDisposable;
        private static bool _courtSessionWasOpenedForTest;

        private static async Task<string> OpenCourtAsync(
            JObject command,
            IList<Hero> resolvedHeroes)
        {
            ReignCourtCampaignBehavior court =
                ReignCourtCampaignBehavior.Instance;
            if (court == null)
                return "The production court controller is unavailable.";
            bool createTestEvent =
                command.Value<bool?>("createTestEvent") == true;
            if (!court.IsRuleModeActive && createTestEvent)
            {
                string openError = string.Empty;
                bool opened = await ReignMainThread.InvokeAsync(() =>
                    court.TryOpenSession(
                        Settlement.CurrentSettlement,
                        out openError)).ConfigureAwait(false);
                if (!opened)
                    return "A disposable production court session could not be opened: "
                        + openError;
                _courtSessionWasOpenedForTest = true;
                DateTime sessionDeadline = DateTime.UtcNow.AddSeconds(20);
                while (!court.ServerSessionOpened
                    && DateTime.UtcNow < sessionDeadline)
                    await Task.Delay(250).ConfigureAwait(false);
            }
            if (!court.IsRuleModeActive)
                return "The production court session is not active.";
            if (!court.ServerAvailable)
                return "The production court server is unavailable.";
            if (!court.ServerSessionOpened)
                return "The production court session did not align with the server in time.";

            string requestedMatterId =
                command.Value<string>("matterId") ?? string.Empty;
            CourtMatter matter = court.Matters
                .Where(candidate => candidate != null && !candidate.IsTerminal)
                .Where(candidate =>
                    string.IsNullOrWhiteSpace(requestedMatterId)
                    || string.Equals(
                        candidate.MatterId,
                        requestedMatterId,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate =>
                    string.Equals(
                        court.Session?.ActiveMatterId,
                        candidate.MatterId,
                        StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.CreatedDay)
                .FirstOrDefault();
            if (matter == null && createTestEvent)
            {
                Hero requestedSpeaker = (resolvedHeroes ?? new List<Hero>())
                    .FirstOrDefault(hero => hero != null && IsLivingNpc(hero));
                matter = court.CreateLiveTestMatter(
                    requestedSpeaker,
                    command.Value<string>("runId")
                    ?? command.Value<string>("commandId")
                    ?? Guid.NewGuid().ToString("N"));
                _courtMatterIsDisposable = matter != null;
            }
            if (matter == null)
                return "No active production CourtMatter matched the request.";

            HashSet<string> participantIds = new HashSet<string>(
                (matter.ParticipantHeroIdsCsv ?? string.Empty)
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim()),
                StringComparer.OrdinalIgnoreCase);
            Hero speaker = (resolvedHeroes ?? new List<Hero>())
                .FirstOrDefault(hero =>
                    hero != null && participantIds.Contains(hero.StringId));
            if (speaker == null)
            {
                speaker = Hero.AllAliveHeroes.FirstOrDefault(hero =>
                    IsLivingNpc(hero)
                    && participantIds.Contains(hero.StringId));
            }
            if (speaker == null)
                return "The selected court matter has no eligible living participant.";

            bool alreadyActive =
                matter.State == ReignCourtMatterState.Active
                && string.Equals(
                    court.Session?.ActiveMatterId,
                    matter.MatterId,
                    StringComparison.OrdinalIgnoreCase);
            if (!alreadyActive)
            {
                string error =
                    await court.BeginAudienceAsync(matter).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    if (_courtMatterIsDisposable)
                    {
                        court.RemoveLiveTestMatter(matter);
                        _courtMatterIsDisposable = false;
                    }
                    return error;
                }
            }

            _courtMatter = matter;
            _courtSpeaker = speaker;
            if (_presentation == "visible")
            {
                await ReignMainThread.InvokeAsync(() =>
                    ReignCourtScreenManager.Open(court)).ConfigureAwait(false);
            }
            return string.Empty;
        }

        private static async Task<LiveCommandResult> SendCourtAsync(
            string text,
            string correlation)
        {
            ReignCourtCampaignBehavior court =
                ReignCourtCampaignBehavior.Instance;
            if (court == null)
                return LiveCommandResult.Failed(
                    "The production court controller is unavailable.");
            ReignCourtDialogueReply reply =
                await court.RespondToAudienceAsync(
                    _courtMatter,
                    _courtSpeaker,
                    text,
                    correlation).ConfigureAwait(false);
            JObject data = new JObject
            {
                ["ok"] = reply.Ok,
                ["error"] = reply.Error ?? string.Empty,
                ["text"] = reply.Text ?? string.Empty,
                ["emotion"] = reply.Emotion ?? string.Empty,
                ["matterId"] = _courtMatter?.MatterId ?? string.Empty,
                ["speakerHeroId"] = _courtSpeaker?.StringId ?? string.Empty,
                ["sessionId"] = court.Session?.SessionId ?? string.Empty,
                ["revision"] = reply.Revision,
                ["correlationId"] = correlation
            };
            LiveCommandResult result = reply.Ok
                ? LiveCommandResult.Completed(
                    "Production court-audience turn completed.",
                    data)
                : LiveCommandResult.Failed(reply.Error, data);
            result.CorrelationIds.Add(correlation);
            return result;
        }

        private static void EndCourtAudience()
        {
            ReignCourtCampaignBehavior court =
                ReignCourtCampaignBehavior.Instance;
            court?.EndAudience(_courtMatter);
            if (_courtMatterIsDisposable)
                court?.RemoveLiveTestMatter(_courtMatter);
            if (_courtSessionWasOpenedForTest)
                court?.CloseSession("live_test_completed");
        }

        private static void ResetCourtSession()
        {
            ReignCourtCampaignBehavior court =
                ReignCourtCampaignBehavior.Instance;
            if (_courtMatterIsDisposable && _courtMatter != null)
                court?.RemoveLiveTestMatter(_courtMatter);
            if (_courtSessionWasOpenedForTest)
                court?.CloseSession("live_test_reset");
            _courtMatter = null;
            _courtSpeaker = null;
            _courtMatterIsDisposable = false;
            _courtSessionWasOpenedForTest = false;
        }
    }
}
