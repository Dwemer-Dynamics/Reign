using System;
using System.Globalization;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        internal static Task<JObject> GetPatronagePublicHistoryAsync(string campaign, string timeline, string kingdom, double day)
            => GetJsonAsync("/court/patronage/public-history?campaignId=" + Uri.EscapeDataString(campaign)
                + "&timelineId=" + Uri.EscapeDataString(timeline) + "&kingdomId=" + Uri.EscapeDataString(kingdom)
                + "&worldDay=" + day.ToString("R", CultureInfo.InvariantCulture));
    }
}

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private JArray _patronagePublicSubjects = new JArray();
        private string _patronagePublicHistoryScope = "";
        private double _patronagePublicHistoryDay = double.MinValue;
        private bool _patronagePublicHistoryPending;

        public async Task<bool> PreparePatronagePublicHistoryAsync()
        {
            if (_patronagePublicHistoryPending) return false;
            string campaign = ReignServerClient.GetCampaignId();
            string timeline = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            string kingdom = Clan.PlayerClan?.Kingdom?.StringId ?? "";
            double day = CurrentCourtLifeDay();
            string scope = campaign + "|" + timeline + "|" + kingdom;
            ReignRulerDocketState requestedState = EnsureRulerDocketState();
            ReignCourtCampaignBehavior requestedBehavior = Instance;
            if (string.IsNullOrWhiteSpace(kingdom)) return false;
            if (_patronagePublicHistoryScope == scope && _patronagePublicHistoryDay <= day
                && Math.Floor(_patronagePublicHistoryDay) == Math.Floor(day)) return true;
            _patronagePublicHistoryPending = true;
            try
            {
                JObject result = await ReignServerClient.GetPatronagePublicHistoryAsync(campaign, timeline, kingdom, day).ConfigureAwait(false);
                return await ReignMainThread.InvokeAsync(() => {
                    string currentScope = ReignServerClient.GetCampaignId() + "|" + (ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")
                        + "|" + (Clan.PlayerClan?.Kingdom?.StringId ?? "");
                    if (currentScope != scope || !ReferenceEquals(Instance, requestedBehavior)
                        || !ReferenceEquals(EnsureRulerDocketState(), requestedState) || result?.Value<bool?>("ok") != true) return false;
                    _patronagePublicSubjects = result["subjects"] as JArray ?? new JArray();
                    _patronagePublicHistoryScope = scope; _patronagePublicHistoryDay = day; return true;
                }).ConfigureAwait(false);
            }
            catch (Exception) { return false; }
            finally { await ReignMainThread.InvokeAsync(() => _patronagePublicHistoryPending = false).ConfigureAwait(false); }
        }
    }
}
