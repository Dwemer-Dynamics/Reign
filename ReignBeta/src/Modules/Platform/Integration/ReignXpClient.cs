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
        internal void Award(string receipt, ReignXpSkill skill, double baseXp, ReignXpReason reason)
            => Owner?.Award(receipt, skill, baseXp, Options, reason);
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
                    double conversation = ReadXpCategoryMultiplier(response, "reignXpConversationMultiplier");
                    double socialCharm = ReadXpCategoryMultiplier(response, "reignXpSocialCharmMultiplier");
                    double socialBonus = ReadXpCategoryMultiplier(response, "reignXpSocialBonusMultiplier");
                    double petition = ReadXpCategoryMultiplier(response, "reignXpPetitionMultiplier");
                    if (response.Value<bool?>("ok") == true && response["reignXpEnabled"]?.Type == JTokenType.Boolean
                        && ReignXpRules.IsValidMultiplier(multiplier)
                        && ReignXpRules.IsValidCategoryMultiplier(conversation)
                        && ReignXpRules.IsValidCategoryMultiplier(socialCharm)
                        && ReignXpRules.IsValidCategoryMultiplier(socialBonus)
                        && ReignXpRules.IsValidCategoryMultiplier(petition))
                    {
                        options = new ReignXpOptions { Enabled = response.Value<bool>("reignXpEnabled"),
                            Multiplier = multiplier, ConversationMultiplier = conversation,
                            SocialCharmMultiplier = socialCharm, SocialBonusMultiplier = socialBonus,
                            PetitionMultiplier = petition, Confirmed = true };
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

        private static double ReadXpCategoryMultiplier(JObject response, string key)
        {
            JToken token = response[key];
            if (token == null) return 1; // Servers predating category controls retain their reward amounts.
            return token.Type == JTokenType.Integer || token.Type == JTokenType.Float
                ? token.Value<double>() : double.NaN;
        }
    }
}
