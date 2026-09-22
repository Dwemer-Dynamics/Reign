using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Dialogue;
using ReignBeta.Campaign;
using ReignBeta.Runtime;

namespace ReignBeta.Integration
{
    internal sealed class ReignXpInteraction
    {
        internal ReignXpCampaignBehavior Owner;
        internal ReignXpOptions Options;
        internal void Award(string receipt, ReignXpSkill skill, double baseXp)
            => Owner?.Award(receipt, skill, baseXp, Options);
    }

    public static partial class ReignServerClient
    {
        private static readonly System.Net.Http.HttpClient XpOptionsClient = CreateLocalServerHttpClient(2, 5);
        internal static async Task<ReignXpInteraction> BeginXpInteractionAsync()
        {
            var owner = await ReignMainThread.InvokeAsync(() => ReignXpCampaignBehavior.Instance).ConfigureAwait(false);
            var options = owner?.LastOptions ?? new ReignXpOptions();
            if (owner != null)
            {
                try
                {
                    JObject response = await GetJsonAsync("/api/gameplay-options", XpOptionsClient).ConfigureAwait(false);
                    double multiplier = response.Value<double?>("reignXpMultiplier") ?? double.NaN;
                    if (response.Value<bool?>("ok") == true && response["reignXpEnabled"]?.Type == JTokenType.Boolean
                        && ReignXpRules.IsValidMultiplier(multiplier))
                    {
                        options = new ReignXpOptions { Enabled = response.Value<bool>("reignXpEnabled"),
                            Multiplier = multiplier, Confirmed = true };
                        await ReignMainThread.InvokeAsync(() =>
                        {
                            if (owner == ReignXpCampaignBehavior.Instance) owner.LastOptions = options;
                        }).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) { ReignLog.Warn("Reign XP options refresh failed: " + ex.Message); }
            }
            return new ReignXpInteraction { Owner = owner, Options = options };
        }
    }
}
