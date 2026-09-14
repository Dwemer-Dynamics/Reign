using System;
using System.Collections.Generic;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Campaign
{
    /// <summary>Applies Reign's authored ages after native heroes exist in each new campaign.</summary>
    internal sealed class ReignNativeStartingAgeCampaignBehavior : CampaignBehaviorBase
    {
        private static readonly IReadOnlyDictionary<string, float> AuthoredStartingAges =
            new Dictionary<string, float>(StringComparer.Ordinal)
            {
                ["lord_1_14"] = 36f, // Rhagaea
                ["lord_1_37"] = 19f  // Ira
            };

        public override void RegisterEvents()
        {
            CampaignEvents.OnNewGameCreatedPartialFollowUpEndEvent.AddNonSerializedListener(this, ApplyAuthoredStartingAges);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Age is serialized by Bannerlord with the hero. Never alter loaded campaigns.
        }

        private static void ApplyAuthoredStartingAges(CampaignGameStarter starter)
        {
            foreach (KeyValuePair<string, float> definition in AuthoredStartingAges)
            {
                Hero hero = Hero.Find(definition.Key);
                if (hero == null)
                    throw new InvalidOperationException("Required native hero is unavailable: " + definition.Key);
                hero.SetBirthDay(CampaignTime.YearsFromNow(-definition.Value));
                if (Math.Abs(hero.Age - definition.Value) > 0.02f)
                    throw new InvalidOperationException("Could not apply Reign's authored starting age for " + definition.Key);
            }

            ReignLog.Info("Applied Reign authored starting ages: Rhagaea=36, Ira=19.");
        }
    }
}
