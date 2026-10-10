using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;

namespace ReignBeta.Government
{
    internal static class ReignGovernmentPatches
    {
        internal const string HarmonyId = "com.bannerlordreign.reignbeta.government";
        private static Harmony _harmony;
        private static bool _ready;
        private static string _lastError = string.Empty;
        private static readonly List<Requirement> Requirements = new List<Requirement>();
        internal static bool IsReady => _ready;

        // Deliberately explicit: assembly-wide PatchAll would also install unrelated subsystem patches.
        internal static readonly Type[] PatchClasses =
        {
            typeof(ReignGovernmentNativeNoticePatch), typeof(ReignGovernmentNativeProposalPatch),
            typeof(ReignGovernmentNativeOutcomePatch), typeof(ReignGovernmentNativeElectionPatch),
            typeof(ReignGovernmentNativeControlEnabledPatch), typeof(ReignGovernmentNativeControlReasonPatch),
            typeof(ReignGovernmentNativeControlExecutePatch), typeof(ReignGovernmentNativeDiplomacyExplanationPatch),
            typeof(ReignGovernmentNativeDecisionScreenPatch), typeof(ReignGovernmentNativePeaceOfferPatch),
            typeof(ReignGovernmentNativePeaceCallbackPatch), typeof(ReignGovernmentNativePeaceResolvedPatch),
            typeof(ReignGovernmentNativeRelationPrivacyPatch), typeof(ReignGovernmentNativeCallOfferPatch),
            typeof(ReignGovernmentNativeTreatyOfferPatch), typeof(ReignGovernmentRuralVolunteerPatch),
            typeof(ReignGovernmentRuralIssueContextPatch)
        };

        internal static void Apply()
        {
            if (_harmony != null && VerifyReady()) return;
            _ready = false;
            try
            {
                Requirements.Clear();
                foreach (Type patch in PatchClasses)
                {
                    MethodInfo hook = AccessTools.DeclaredMethod(patch, "Prefix") ?? AccessTools.DeclaredMethod(patch, "Postfix");
                    if (hook == null) throw new MissingMethodException(patch.FullName, "Prefix/Postfix");
                    MethodInfo resolver = AccessTools.DeclaredMethod(patch, "TargetMethods");
                    IEnumerable<MethodBase> targets = resolver != null
                        ? (IEnumerable<MethodBase>)resolver.Invoke(null, null)
                        : new[] { SingleTarget(patch) };
                    int count = 0;
                    foreach (MethodBase target in targets)
                    {
                        if (target == null) throw new MissingMethodException("Government target unavailable: " + patch.Name);
                        Requirements.Add(new Requirement { Target = target, Hook = hook });
                        count++;
                    }
                    if (count == 0) throw new MissingMethodException("Government patch has no targets: " + patch.Name);
                }
                _harmony = new Harmony(HarmonyId);
                _harmony.UnpatchAll(HarmonyId);
                foreach (Type patch in PatchClasses) _harmony.CreateClassProcessor(patch).Patch();
                if (!HooksInstalled()) throw new InvalidOperationException("Government patch inventory is incomplete or duplicated.");
                _lastError = string.Empty;
                _ready = true;
                ReignLog.Info("Government native interception ready: " + Requirements.Count + " verified hooks.");
            }
            catch (Exception ex)
            {
                _lastError = ex.ToString();
                Unapply();
                ReignLog.Warn("Government native interception unavailable; native decisions remain with Bannerlord and saved hearings are held: " + ex);
            }
        }

        internal static void Unapply()
        {
            _ready = false;
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
        }

        internal static bool VerifyReady()
        {
            if (!_ready) return false;
            if (HooksInstalled()) return true;
            _lastError = "A required Government hook was removed or duplicated after startup.";
            Unapply();
            ReignLog.Warn(_lastError + " Native takeover is disabled; pending hearings require review.");
            return false;
        }

        private static bool HooksInstalled() => Requirements.Count > 0 && Requirements.All(Installed);

        private static bool Installed(Requirement requirement)
        {
            var info = Harmony.GetPatchInfo(requirement.Target);
            var hooks = requirement.Hook.Name == "Prefix" ? info?.Prefixes : info?.Postfixes;
            return hooks != null && hooks.Count(p => p.PatchMethod == requirement.Hook) == 1
                && hooks.Count(p => p.owner == HarmonyId && p.PatchMethod == requirement.Hook) == 1;
        }

        internal static JObject BuildDiagnostics()
        {
            bool ready = VerifyReady();
            return new JObject
            {
                ["schema"] = "reign-government-interception-v1", ["ready"] = ready,
                ["ownerId"] = HarmonyId, ["requiredHookCount"] = Requirements.Count, ["lastError"] = _lastError,
                ["hooks"] = new JArray(Requirements.Select(r => new JObject
                {
                    ["target"] = r.Target.DeclaringType.FullName + "." + r.Target.Name,
                    ["patch"] = r.Hook.DeclaringType.Name, ["installedExactlyOnce"] = Installed(r)
                }))
            };
        }

        private static MethodBase SingleTarget(Type patch)
        {
            if (patch == typeof(ReignGovernmentNativeProposalPatch)) return AccessTools.Method(typeof(Kingdom), "AddDecision");
            if (patch == typeof(ReignGovernmentNativeElectionPatch)) return AccessTools.Method(typeof(KingdomElection), "ApplyChosenOutcome");
            if (patch == typeof(ReignGovernmentNativeDiplomacyExplanationPatch)) return AccessTools.Method(typeof(KingdomDiplomacyProposalActionItemVM), "RefreshValues");
            if (patch == typeof(ReignGovernmentNativePeaceOfferPatch)) return AccessTools.Method(typeof(PeaceOfferCampaignBehavior), "OnPeaceOffered");
            if (patch == typeof(ReignGovernmentNativePeaceResolvedPatch)) return AccessTools.Method(typeof(PeaceOfferCampaignBehavior), "OnPeaceOfferResolved");
            if (patch == typeof(ReignGovernmentNativeRelationPrivacyPatch)) return AccessTools.Method(typeof(ChangeRelationAction), "ApplyInternal");
            throw new InvalidOperationException("Unregistered Government patch target: " + patch.FullName);
        }

        private sealed class Requirement
        {
            internal MethodBase Target;
            internal MethodInfo Hook;
        }
    }
}
