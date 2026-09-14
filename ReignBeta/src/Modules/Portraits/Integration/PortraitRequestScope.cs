using System;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Integration
{
    // ExecutionContext carries the original campaign across awaits, including cache registration.
    internal sealed class PortraitRequestScope : IDisposable
    {
        private static readonly AsyncLocal<PortraitRequestScope> Slot = new AsyncLocal<PortraitRequestScope>();
        private readonly PortraitRequestScope _previous;
        public static PortraitRequestScope Current => Slot.Value;
        public string CampaignId { get; }
        public string CampaignFolder { get; }
        public JObject Snapshot { get; }
        public string Error { get; set; }

        public PortraitRequestScope(string campaignId, string campaignFolder, JObject snapshot)
        {
            if (string.IsNullOrWhiteSpace(campaignId) || string.IsNullOrWhiteSpace(campaignFolder)
                || campaignFolder != ReignCampaignIdentity.SafePathSegment(campaignFolder, "invalid"))
                throw new ArgumentException("A valid portrait campaign is required.");
            CampaignId = campaignId;
            CampaignFolder = campaignFolder;
            Snapshot = (JObject)snapshot.DeepClone();
            _previous = Slot.Value;
            Slot.Value = this;
        }
        public void Dispose() { Slot.Value = _previous; }
    }
}
