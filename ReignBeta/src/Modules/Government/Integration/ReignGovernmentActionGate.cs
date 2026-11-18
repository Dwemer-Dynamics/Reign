using ReignBeta.World;
using ReignBeta.Government.Campaign;
using ReignBeta.WorldSimulation.World;

namespace ReignBeta.Government.Integration
{
    public static class ReignGovernmentActionGate
    {
        public static bool TryAuthorize(ReignWorldActionRecord action, out ReignActionResult blockedResult)
        {
            blockedResult = null;
            ReignGovernmentCampaignBehavior government = ReignGovernmentCampaignBehavior.Instance;
            if (government == null || action == null) return true;
            return government.TryAuthorizeAction(action, out blockedResult);
        }
    }
}
