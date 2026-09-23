using System;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private const string FiefSaveFixturePrefix = "government-cert-fief-save-";

        private Kingdom FiefFixtureKingdom(bool player) => Kingdom.All.Where(IsEligibleKingdom)
            .Where(k => (k == Clan.PlayerClan?.Kingdom) == player && k.Leader != null
                && (player || k.Leader != Hero.MainHero) && k.RulingClan != null)
            .OrderBy(k => k.StringId, StringComparer.Ordinal)
            .FirstOrDefault(k => EnsureGovernment(k) != null && GetSeats(k.StringId).Count > 0
                && FiefFixtureSettlement(k) != null);

        private Settlement FiefFixtureSettlement(Kingdom kingdom) => Settlement.All
            .Where(s => s.IsFortification && s.MapFaction == kingdom && s.OwnerClan != null
                && s.Town != null && !s.IsUnderSiege
                && !_business.Any(b => b.TargetId == s.StringId && !GovernmentBusinessRules.IsClosed(b.Status)))
            .OrderBy(s => s.StringId, StringComparer.Ordinal).FirstOrDefault();

        private ReignGovernmentBusinessRecord CreateFiefFixture(Kingdom kingdom, Settlement settlement,
            out SettlementClaimantDecision decision)
        {
            settlement.Town.IsOwnerUnassigned = true; // The bounded post-capture prerequisite; no siege is fabricated.
            decision = new SettlementClaimantDecision(kingdom.RulingClan, settlement, kingdom.Leader, null);
            kingdom.AddDecision(decision, true); // Exercise the real NPC immediate-election entrypoint.
            var captured = decision;
            return _business.FirstOrDefault(b => ReferenceEquals(NativeBusinessDecision(b), captured));
        }

        private void GovernmentFiefNativeCase(JArray assertions, string caseId)
        {
            bool ready = ReignGovernmentPatches.VerifyReady();
            AddGovernmentAssertion(assertions, "government_native_interception_ready", ready,
                "Real Harmony metadata, including proposal, election-start and outcome hooks, is complete.", ReignGovernmentPatches.BuildDiagnostics());
            if (!ready) return;
            Kingdom kingdom = FiefFixtureKingdom(caseId == "GOV-NATIVE-045");
            Settlement settlement = kingdom == null ? null : FiefFixtureSettlement(kingdom);
            AddGovernmentAssertion(assertions, "government_fief_fixture_eligible", settlement != null,
                "A real eligible kingdom has representation and a fortification without another pending hearing.", null);
            if (settlement == null) return;
            var state = GetGovernment(kingdom);
            int previousLevel = state.Level;
            try
            {
                if (caseId == "GOV-NATIVE-046")
                {
                    GovernmentFiefPatchFailureCase(assertions, kingdom, settlement);
                    return;
                }
                if (caseId == "GOV-NATIVE-048")
                {
                    GovernmentFiefPreliminaryCase(assertions, kingdom, settlement);
                    return;
                }
                state.Level = 5;
                string originalOwner = settlement.OwnerClan.StringId;
                int countBefore = _business.Count;
                int targetCountBefore = _business.Count(b => b.TargetId == settlement.StringId && b.Kind == "fief_allocation");
                var record = CreateFiefFixture(kingdom, settlement, out var decision);
                bool captured = record != null && _business.Count == countBefore + 1
                    && settlement.OwnerClan.StringId == originalOwner && settlement.Town.IsOwnerUnassigned
                    && !kingdom.UnresolvedDecisions.Contains(decision) && record.NativeCaptureSource == "proposal"
                    && record.NativeEffectInvocationCount == 0 && record.NativeOwnerChangeEventCount == 0;
                AddGovernmentAssertion(assertions, "government_fief_captured_before_election", captured,
                    "The real AddDecision call creates one hearing and no native award before authorization.", record == null ? null : JObject.FromObject(record));
                if (!captured) return;
                if (caseId == "GOV-NATIVE-047")
                {
                    record.NativeCaptureVersion = 0; record.NativeCaptureSource = string.Empty;
                    MigrateNativeGovernmentBusiness();
                    TickGovernmentBusiness();
                    bool refused = !VoteBusiness(record.BusinessId, kingdom.Leader, out _);
                    AddGovernmentAssertion(assertions, "government_legacy_fief_quarantined", refused
                        && record.Status == "execution_failed" && record.NativeQuarantineReason.Length > 0
                        && !record.NativeEffectStarted && !record.ExecutionApplied && !record.ConsequencesApplied
                        && settlement.OwnerClan.StringId == originalOwner && record.NativeOwnerChangeEventCount == 0,
                        "A legacy pending hearing cannot automatically or manually replay an uncertain native award.", JObject.FromObject(record));
                    return;
                }
                var alias = new SettlementClaimantDecision(kingdom.RulingClan, settlement, kingdom.Leader, null);
                kingdom.AddDecision(alias, true);
                new KingdomElection(decision).StartElection();
                bool duplicatesHeld = _business.Count == countBefore + 1 && OwnsNativeDecision(alias)
                    && settlement.OwnerClan.StringId == originalOwner && record.NativeOwnerChangeEventCount == 0;
                if (caseId == "GOV-NATIVE-044") TickGovernmentBusiness();
                else
                {
                    // This is a native adapter regression, not evidence of player UI interaction.
                    RecommendBusiness(record.BusinessId, BusinessOptions(record).First().Value<string>("id"), kingdom.Leader, out _);
                    VoteBusiness(record.BusinessId, kingdom.Leader, out _);
                }
                string winner = "clan:" + settlement.OwnerClan.StringId;
                var replay = decision.DetermineInitialCandidates().First();
                decision.ApplyChosenOutcome(replay);
                alias.ApplyChosenOutcome(alias.DetermineInitialCandidates().First());
                kingdom.AddDecision(decision, true);
                TickGovernmentBusiness(); TickGovernmentBusiness();
                bool appliedOnce = duplicatesHeld && record.Status == "decided" && record.ExecutionApplied
                    && record.ConsequencesApplied && record.NativeEffectInvocationCount == 1 && record.NativeOwnerChangeEventCount == 1
                    && record.ExecutedOptionId == winner && "clan:" + settlement.OwnerClan.StringId == winner
                    && _business.Count(b => b.TargetId == settlement.StringId && b.Kind == "fief_allocation") == targetCountBefore + 1
                    && !settlement.Town.IsOwnerUnassigned;
                AddGovernmentAssertion(assertions, "government_fief_awarded_exactly_once", appliedOnce,
                    "The production hearing applies one award. A direct election, equivalent alias, repeated AddDecision, outcome calls and daily processing cannot apply it again.",
                    new JObject { ["business"] = JObject.FromObject(record), ["settlementId"] = settlement.StringId,
                        ["ownerBefore"] = originalOwner, ["ownerAfter"] = settlement.OwnerClan.StringId,
                        ["playerUiAcceptance"] = false, ["nativeTimeAdvanced"] = false });
            }
            finally { state.Level = previousLevel; }
        }

        private void GovernmentFiefPatchFailureCase(JArray assertions, Kingdom kingdom, Settlement settlement)
        {
            int before = _business.Count;
            var decision = new SettlementClaimantDecision(kingdom.RulingClan, settlement, kingdom.Leader, null);
            bool failedSafely = false;
            try
            {
                new Harmony(ReignGovernmentPatches.HarmonyId).Unpatch(AccessTools.Method(typeof(Kingdom), "AddDecision"),
                    HarmonyPatchType.Prefix, ReignGovernmentPatches.HarmonyId);
                bool disabled = !ReignGovernmentPatches.VerifyReady();
                OnNativeKingdomDecisionAdded(decision, false);
                failedSafely = disabled && !TryCaptureNativeDecision(decision) && _business.Count == before
                    && !ReignGovernmentNativeControlAuthority.Active;
            }
            finally { ReignGovernmentPatches.Apply(); }
            AddGovernmentAssertion(assertions, "government_patch_failure_has_no_duplicate_hearing", failedSafely && ReignGovernmentPatches.VerifyReady(),
                "Removing a real required hook disables takeover and the late event creates no executable hearing; all hooks are restored in finally. No native election or Influence spending is performed by this fault probe.",
                ReignGovernmentPatches.BuildDiagnostics());
        }

        private void GovernmentFiefPreliminaryCase(JArray assertions, Kingdom kingdom, Settlement settlement)
        {
            string originalOwner = settlement.OwnerClan.StringId;
            GetGovernment(kingdom).Level = 1;
            var preliminary = new SettlementClaimantPreliminaryDecision(kingdom.RulingClan, settlement);
            kingdom.AddDecision(preliminary, true);
            var record = _business.FirstOrDefault(b => ReferenceEquals(NativeBusinessDecision(b), preliminary));
            bool approved = record != null;
            if (record?.Status == "awaiting_sponsorship") approved &= AdoptBusinessPetition(record.BusinessId, kingdom.Leader, out _);
            approved = approved && RecommendBusiness(record.BusinessId, "accept", kingdom.Leader, out _)
                && VoteBusiness(record.BusinessId, kingdom.Leader, out _)
                && ResolveBusiness(record.BusinessId, "accept", kingdom.Leader, true, out _);
            var allocation = _business.FirstOrDefault(b => b.Kind == "fief_allocation" && b.TargetId == settlement.StringId
                && !GovernmentBusinessRules.IsClosed(b.Status));
            bool staged = approved && record.NativeEffectInvocationCount == 1 && allocation != null
                && allocation.NativeEffectInvocationCount == 0 && settlement.OwnerClan.StringId == originalOwner;
            TickGovernmentBusiness();
            AddGovernmentAssertion(assertions, "government_preliminary_then_allocation_once", staged && allocation.ExecutionApplied
                && allocation.NativeEffectInvocationCount == 1 && allocation.NativeOwnerChangeEventCount == 1,
                "Approved reassignment creates a distinct pending allocation; that follow-on hearing legitimately awards the fief once.",
                new JObject { ["preliminary"] = record == null ? null : JObject.FromObject(record),
                    ["allocation"] = allocation == null ? null : JObject.FromObject(allocation) });
        }

        private void GovernmentFiefSavePrepare(JArray assertions)
        {
            bool absent = !_business.Any(b => b.BusinessId.StartsWith(FiefSaveFixturePrefix, StringComparison.Ordinal));
            var kingdom = absent && ReignGovernmentPatches.VerifyReady() ? FiefFixtureKingdom(false) : null;
            var settlement = kingdom == null ? null : FiefFixtureSettlement(kingdom);
            ReignGovernmentBusinessRecord record = null;
            if (settlement != null)
            {
                record = CreateFiefFixture(kingdom, settlement, out _);
                if (record != null) record.BusinessId = FiefSaveFixturePrefix + Guid.NewGuid().ToString("N");
            }
            AddGovernmentAssertion(assertions, "government_fief_open_save_prepared", record != null
                && !record.NativeEffectStarted && record.NativeCaptureSource == "proposal"
                && NativeSettlementOwnerClanId(NativeBusinessDecision(record)) == record.NativeCapturedOwnerClanId,
                "One fresh NPC fief hearing is captured for an external checkpoint/restart. No save or time advancement occurs here.",
                record == null ? null : JObject.FromObject(record));
        }

        private void GovernmentFiefSaveAssert(JArray assertions, bool awarded)
        {
            var records = _business.Where(b => b.BusinessId.StartsWith(FiefSaveFixturePrefix, StringComparison.Ordinal)).ToList();
            var record = records.Count == 1 ? records[0] : null;
            var decision = record == null ? null : NativeBusinessDecision(record) as SettlementClaimantDecision;
            bool valid = record != null && decision != null && ReignGovernmentPatches.VerifyReady()
                && ReignGovernmentNativeSafety.IsTrustedCapture(record) && record.NativeQuarantineReason.Length == 0;
            if (valid && awarded)
                valid = record.Status == "decided" && record.ExecutionApplied && record.NativeEffectStarted
                    && record.NativeEffectInvocationCount == 1 && record.NativeOwnerChangeEventCount == 1
                    && record.ExecutedOptionId == "clan:" + decision.Settlement.OwnerClan.StringId
                    && CurrentDay() >= record.CreatedDay + 1f;
            else if (valid)
                valid = record.Status == "hearing" && !record.ExecutionApplied && !record.NativeEffectStarted
                    && record.NativeEffectInvocationCount == 0 && record.NativeOwnerChangeEventCount == 0
                    && record.NativeCapturedOwnerClanId == decision.Settlement.OwnerClan.StringId;
            AddGovernmentAssertion(assertions, awarded ? "government_fief_award_persisted_once" : "government_fief_open_persisted",
                valid, awarded
                    ? "The fief has exactly one native invocation and ownership event, its owner is still the authorized winner, and at least a full campaign day elapsed after capture."
                    : "The exact open fief hearing and native decision survived reload without any award or quarantine.",
                record == null ? null : new JObject { ["business"] = JObject.FromObject(record), ["currentDay"] = CurrentDay(),
                    ["currentOwner"] = decision?.Settlement.OwnerClan?.StringId });
        }
    }
}
