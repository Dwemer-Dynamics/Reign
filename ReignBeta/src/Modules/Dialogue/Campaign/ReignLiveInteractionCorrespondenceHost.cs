using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.UI;
using ReignBeta.UI.ViewModels;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static Hero _correspondenceHero;
        private static string _correspondenceThreadId = string.Empty;

        private static async Task<string> OpenCorrespondenceAsync(
            JObject command,
            IList<Hero> resolvedHeroes)
        {
            List<string> requestedTargets = ReadSearches(command);
            Hero recipient = resolvedHeroes?.FirstOrDefault();
            if (recipient == null && requestedTargets.Count > 0)
                return "No known living correspondence contact matched the requested target.";
            if (recipient == null)
                recipient = await ReignMainThread.InvokeAsync(() =>
                    ReignServerClient.GetKnownCorrespondenceContacts()
                        .FirstOrDefault()).ConfigureAwait(false);
            if (recipient == null)
                return "No known living correspondence contact matched the requested target.";

            bool legitimate = await ReignMainThread.InvokeAsync(() =>
                ReignServerClient.GetKnownCorrespondenceContacts()
                    .Any(hero => string.Equals(
                        hero.StringId,
                        recipient.StringId,
                        StringComparison.OrdinalIgnoreCase))).ConfigureAwait(false);
            if (!legitimate)
                return "The requested hero is not currently an eligible correspondence contact.";

            _correspondenceHero = recipient;
            _correspondenceThreadId = string.Empty;
            if (_presentation == "visible")
            {
                await ReignMainThread.InvokeAsync(() =>
                    ReignCorrespondenceScreenManager.Open(recipient)).ConfigureAwait(false);
            }
            return string.Empty;
        }

        private static async Task<LiveCommandResult> SendCorrespondenceAsync(
            string text,
            string correlation)
        {
            ReignLetterSendResult send =
                await ReignCorrespondenceScreenVM.SendProductionLetterAsync(
                    _correspondenceHero,
                    text,
                    correlation).ConfigureAwait(false);
            if (send.Ok)
                _correspondenceThreadId = send.ThreadId ?? string.Empty;

            JObject data = new JObject
            {
                ["ok"] = send.Ok,
                ["error"] = send.Error ?? string.Empty,
                ["recipientHeroId"] = _correspondenceHero?.StringId ?? string.Empty,
                ["letterId"] = send.LetterId ?? string.Empty,
                ["threadId"] = send.ThreadId ?? string.Empty,
                ["dispatchDay"] = send.DispatchDay,
                ["deliveryDay"] = send.DeliveryDay,
                ["correlationId"] = send.CorrelationId ?? correlation,
                ["queuedActionCount"] = send.QueuedActions?.Count ?? 0
            };
            LiveCommandResult result = send.Ok
                ? LiveCommandResult.Completed(
                    "Production correspondence was dispatched.",
                    data)
                : LiveCommandResult.Failed(send.Error, data);
            result.CorrelationIds.Add(send.CorrelationId ?? correlation);
            return result;
        }

        private static void ResetCorrespondenceSession()
        {
            _correspondenceHero = null;
            _correspondenceThreadId = string.Empty;
        }
    }
}
