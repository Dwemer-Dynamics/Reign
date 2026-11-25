using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;
using ReignBeta.Relationships.Campaign;
using ReignBeta.Relationships.Integration;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private const string NativeRelationshipPullFixtureId = "fixture.relationship_native_pull";
        private static NativeRelationshipResponseHold _nativeRelationshipResponseHold;

        private static bool IsNativeRelationshipPullFixture(JObject command) =>
            command.Value<string>("operation") == "structured_action_test"
            && (command["testCase"] as JObject)?.Value<string>("actionId") == NativeRelationshipPullFixtureId;

        private static bool OwnsAcceptedNativeRelationshipCommand(JObject acknowledgment, JObject issued,
            ReignMainThread.GameScope scope, string campaignId, string gameInstanceId)
        {
            JObject accepted = acknowledgment?["command"] as JObject;
            JObject run = acknowledgment?["run"] as JObject;
            return scope != null && ReferenceEquals(scope, ReignMainThread.CurrentGameScope)
                && acknowledgment?.Value<bool?>("ok") == true && acknowledgment.Value<bool?>("idempotent") != true
                && run?.Value<string>("status") == "running" && run.Value<string>("mode") == "structured_actions"
                && run.Value<string>("campaignId") == campaignId && run.Value<string>("gameInstanceId") == gameInstanceId
                && run.Value<string>("runId") == issued.Value<string>("runId")
                && accepted?.Value<string>("status") == "accepted"
                && accepted.Value<string>("runId") == issued.Value<string>("runId")
                && accepted.Value<string>("commandId") == issued.Value<string>("commandId")
                && !string.IsNullOrEmpty(issued.Value<string>("leaseId"))
                && accepted.Value<string>("leaseId") == issued.Value<string>("leaseId")
                && JToken.DeepEquals(accepted["testCase"], issued["testCase"])
                && JToken.DeepEquals(accepted["enrollment"], issued["enrollment"]);
        }

        private static LiveCommandResult NativeRelationshipPullFixture(JObject test, JObject enrollment)
        {
            string id = test.Value<string>("operationId") ?? string.Empty;
            string control = (test["arguments"] as JObject)?.Value<string>("control");
            JObject arguments = test["arguments"] as JObject;
            if (test.Count != 6 || test["caseId"]?.Type != JTokenType.String
                || test["operationId"]?.Type != JTokenType.String
                || test.Value<string>("phase") != "fixture" || test.Value<string>("kind") != "fixture"
                || id.Length == 0 || id.Length > 80 || id.Any(ch => !(ch >= 'a' && ch <= 'z')
                    && !(ch >= 'A' && ch <= 'Z') && !(ch >= '0' && ch <= '9') && ch != '_' && ch != '-')
                || arguments?["control"]?.Type != JTokenType.String)
                return LiveCommandResult.Failed("The relationship pull fixture requires one exact case and operation ID.");
            bool arm = control == "arm" || control == "arm_pair";
            string pair = arm ? arguments.Value<string>("pairKey") ?? string.Empty : null;
            int revision = 0;
            if (arm)
            {
                if (arguments["pairKey"]?.Type != JTokenType.String
                    || pair.Length < 3 || pair.Length > 257 || pair != pair.Trim() || pair.Any(char.IsControl)
                    || pair.Count(ch => ch == '|') != 1 || pair[0] == '|' || pair[pair.Length - 1] == '|'
                    || (control == "arm_pair" ? arguments.Count != 2
                        : arguments.Count != 3 || !TryReadNativeRelationshipRevision(arguments["revision"], out revision)))
                    return LiveCommandResult.Failed("Arm requires an exact pair and positive revision. Arm pair accepts only an exact pair.");
            }
            else if (arguments.Count != 1 || (control != "observe" && control != "release"))
                return LiveCommandResult.Failed("Observe and release accept only their exact control.");

            var owner = TaleWorlds.CampaignSystem.Campaign.Current;
            var relationship = ReignServices.Relationship;
            var load = ReignSaveSyncCoordinator.CurrentVerifiedLoad;
            string campaignId = enrollment.Value<string>("campaignId");
            string timelineId = enrollment.Value<string>("timelineId");
            string save = enrollment.Value<string>("disposableSaveName");
            string baseline = enrollment.Value<string>("protectedBaselineSaveName");
            string runId = enrollment.Value<string>("campaignTestRunId");
            string point = enrollment.Value<string>("checkpointSavePointId") ?? string.Empty;
            string hash = enrollment.Value<string>("checkpointSha256") ?? string.Empty;
            string sharedGeneration = ReignServices.WorldHistory?.SharedActivityGeneration;
            if (enrollment.Value<bool?>("recoveryCheckpointVerified") != true
                || owner == null || relationship == null || load == null
                || !ReferenceEquals(owner, _observedCampaign)
                || string.Equals(save, baseline, StringComparison.OrdinalIgnoreCase)
                || ReignSaveSyncCoordinator.IsAlignmentPending || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress
                || point.Length == 0 || load.CampaignId != campaignId || load.SavePointId != point
                || load.SavePointId != ReignServices.SaveSync?.CurrentSavePointId
                || string.IsNullOrEmpty(sharedGeneration) || sharedGeneration != load.RestoreGeneration
                || !NativeSaveHasPoint(save, campaignId, point) || hash.Length != 64 || NativeCheckpointHash(save) != hash)
                return LiveCommandResult.Failed("The fixture requires the exact verified loaded Current checkpoint, point and bytes.");

            var scope = ReignMainThread.CurrentGameScope;
            if (scope == null)
                return LiveCommandResult.Failed("The fixture requires the active native game scope.");
            var current = Volatile.Read(ref _nativeRelationshipResponseHold);
            if (arm)
            {
                if (current != null)
                    return LiveCommandResult.Failed("The relationship response hold can be armed once per process.");
                current = new NativeRelationshipResponseHold(id, runId, save, baseline, hash, campaignId,
                    timelineId, _gameInstanceId, owner, relationship, scope, load, pair, revision);
                Volatile.Write(ref _nativeRelationshipResponseHold, current);
            }
            else
            {
                if (current == null || current.Id != id || current.RunId != runId
                    || current.Save != save || current.Baseline != baseline || current.Hash != hash
                    || current.Load.SavePointId != point || current.CampaignId != campaignId
                    || current.TimelineId != timelineId || current.Load.Origin != load.Origin)
                    return LiveCommandResult.Failed("This operation does not own the exact held response.");
                if (control == "release")
                {
                    var originalCampaign = Volatile.Read(ref current.Campaign);
                    var originalRelationship = Volatile.Read(ref current.Relationship);
                    if (current.Expiry.IsCompleted || current.TargetCount == 0
                        || originalCampaign == null || originalRelationship == null
                        || ReferenceEquals(originalCampaign, owner) || ReferenceEquals(originalRelationship, relationship)
                        || current.GameInstance == _gameInstanceId || current.Load.LoadSessionId == load.LoadSessionId
                        || current.Load.CoordinatorGeneration == load.CoordinatorGeneration
                        || !current.Resume.TrySetResult(true))
                        return LiveCommandResult.Failed("Release requires the held response and an exact same-process reload into a new owner.");
                    current.ReleaseGameInstance = _gameInstanceId;
                    current.ReleaseLoad = load;
                    current.ReleaseScope = scope;
                }
            }
            return LiveCommandResult.Completed("Native relationship response fixture.", current.Evidence());
        }

        private static bool TryReadNativeRelationshipRevision(JToken token, out int revision)
        {
            revision = 0;
            return token?.Type == JTokenType.Integer
                && int.TryParse(token.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out revision)
                && revision > 0;
        }

        // Only the fixture inspects the raw revision. Keep the ordinary parser and its compatibility defaults.
        internal static Task<Action<string>> HoldNativeRelationshipResponse(ReignRelationshipCampaignBehavior relationship,
            Func<bool> owns, IReadOnlyList<ReignNativeRelationSyncTarget> targets, JArray rawTargets)
        {
            var hold = Volatile.Read(ref _nativeRelationshipResponseHold);
            if (hold == null || Volatile.Read(ref hold.TargetCount) != 0 || hold.Expiry.IsCompleted
                || !ReferenceEquals(hold.Relationship, relationship))
                return null;

            var rows = rawTargets.OfType<JObject>().Where(row => row.Value<string>("pairKey") == hold.Pair).Take(2).ToArray();
            if (rows.Length != 1 || !TryReadNativeRelationshipRevision(rows[0]["revision"], out int revision)
                || (hold.RequestedRevision > 0 && revision != hold.RequestedRevision))
                return null;
            var target = targets.FirstOrDefault(row => row.PairKey == hold.Pair && row.Revision == revision);
            if (target == null || rows[0]["pairKey"]?.Type != JTokenType.String
                || rows[0]["heroAId"]?.Type != JTokenType.String || rows[0]["heroBId"]?.Type != JTokenType.String
                || string.IsNullOrWhiteSpace(target.HeroAId) || string.IsNullOrWhiteSpace(target.HeroBId)
                || target.HeroAId == target.HeroBId || rows[0]["targetRelation"]?.Type != JTokenType.Integer
                || target.TargetRelation < -100 || target.TargetRelation > 100)
                return null;
            return DelayNativeRelationshipResponseAsync(hold, owns, targets.Count, target);
        }

        private static async Task<Action<string>> DelayNativeRelationshipResponseAsync(
            NativeRelationshipResponseHold hold, Func<bool> owns, int targetCount, ReignNativeRelationSyncTarget target)
        {
            bool admitted = await ReignMainThread.InvokeAsync(() =>
            {
                var owner = TaleWorlds.CampaignSystem.Campaign.Current;
                if (!_armed || _gameInstanceId != hold.GameInstance || hold.TargetCount != 0 || hold.Expiry.IsCompleted
                    || !ReferenceEquals(hold.OriginScope, ReignMainThread.CurrentGameScope)
                    || owner == null || !ReferenceEquals(hold.Campaign, owner)
                    || !ReferenceEquals(hold.Load, ReignSaveSyncCoordinator.CurrentVerifiedLoad)
                    || owner.TimeControlMode != CampaignTimeControlMode.Stop
                    || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress || ActiveSaveName() != hold.Save || !owns())
                    return false;
                hold.BindHeldTarget(target);
                Volatile.Write(ref hold.TargetCount, targetCount);
                return true;
            }).ConfigureAwait(false);
            if (!admitted)
                return null;
            var completed = await Task.WhenAny(hold.Resume.Task, hold.Expiry).ConfigureAwait(false);
            // An expired hold resumes the ordinary continuation and cannot prove a test result.
            return completed == hold.Resume.Task ? hold.RecordOutcome : null;
        }

        // On the game thread after ordinary native application. Neither this callback nor its evidence owns an action.
        internal static void ObserveNativeRelationshipTarget(ReignRelationshipCampaignBehavior relationship,
            ReignNativeRelationSyncTarget target, bool nativeSetterInvoked, int? before, int? after, string disposition)
        {
            var hold = Volatile.Read(ref _nativeRelationshipResponseHold);
            if (hold == null || Volatile.Read(ref hold.TargetCount) == 0 || hold.Expiry.IsCompleted
                || target.PairKey != hold.Pair)
                return;
            if (target.Revision != hold.Revision)
            {
                hold.WitnessIncomplete = true;
                return;
            }
            try
            {
                var owner = TaleWorlds.CampaignSystem.Campaign.Current;
                var scope = ReignMainThread.CurrentGameScope;
                var load = ReignSaveSyncCoordinator.CurrentVerifiedLoad;
                if (target.HeroAId != hold.HeroA || target.HeroBId != hold.HeroB || target.TargetRelation != hold.TargetRelation
                    || owner == null || scope == null || !ReferenceEquals(relationship, ReignServices.Relationship)
                    || load == null || load.CampaignId != hold.CampaignId || load.SavePointId != hold.Load.SavePointId
                    || load.Origin != hold.Load.Origin || ActiveSaveName() != hold.Save
                    || ReignServices.WorldHistory?.TimelineId != hold.TimelineId
                    || ReignServices.SaveSync?.CurrentSavePointId != load.SavePointId
                    || ReignServices.WorldHistory?.SharedActivityGeneration != load.RestoreGeneration)
                {
                    hold.WitnessIncomplete = true;
                    return;
                }
                if (ReferenceEquals(load, hold.Load))
                {
                    if (!ReferenceEquals(scope, hold.OriginScope) || !ReferenceEquals(owner, hold.Campaign)
                        || !ReferenceEquals(relationship, hold.Relationship))
                        hold.WitnessIncomplete = true;
                    else
                        hold.OriginAttempts.Record(nativeSetterInvoked, before, after, disposition);
                    return;
                }
                if (ReferenceEquals(owner, hold.Campaign) || ReferenceEquals(relationship, hold.Relationship)
                    || load.LoadSessionId == hold.Load.LoadSessionId
                    || load.CoordinatorGeneration == hold.Load.CoordinatorGeneration
                    || !hold.TryBindSuccessor(scope, load))
                {
                    hold.WitnessIncomplete = true;
                    return;
                }
                hold.SuccessorAttempts.Record(nativeSetterInvoked, before, after, disposition);
            }
            catch (Exception)
            {
                hold.WitnessIncomplete = true;
            }
        }

        private sealed class NativeRelationshipAttempts
        {
            private int _observations, _nativeCalls, _appliedCalls, _aligned, _failed, _obsolete;
            private int? _firstCallBefore, _firstCallAfter;

            internal void Record(bool nativeCall, int? before, int? after, string disposition)
            {
                _observations++;
                if (disposition == "already_aligned") _aligned++;
                else if (disposition == "failed") _failed++;
                else if (disposition == "obsolete") _obsolete++;
                if (!nativeCall) return;
                if (_nativeCalls++ == 0)
                {
                    _firstCallBefore = before;
                    _firstCallAfter = after;
                }
                if (disposition == "applied") _appliedCalls++;
            }

            internal JObject Evidence() => new JObject
            {
                ["observedNormalAttempts"] = _observations, ["nativeSetterCalls"] = _nativeCalls,
                ["nativeSetterApplied"] = _appliedCalls, ["alreadyAlignedObservations"] = _aligned,
                ["failedObservations"] = _failed, ["obsoleteObservations"] = _obsolete,
                ["firstNativeSetterBefore"] = _firstCallBefore, ["firstNativeSetterAfter"] = _firstCallAfter
            };
        }

        private sealed class NativeRelationshipResponseHold
        {
            internal readonly string Id, RunId, Save, Baseline, Hash, CampaignId, TimelineId, GameInstance, Pair;
            internal readonly int RequestedRevision;
            internal int Revision, TargetRelation;
            internal string HeroA, HeroB;
            internal TaleWorlds.CampaignSystem.Campaign Campaign;
            internal ReignRelationshipCampaignBehavior Relationship;
            internal readonly ReignSaveSyncCoordinator.VerifiedLoad Load;
            internal readonly ReignMainThread.GameScope OriginScope;
            internal readonly Task Expiry = Task.Delay(TimeSpan.FromMinutes(3));
            internal readonly TaskCompletionSource<bool> Resume =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal string ReleaseGameInstance;
            internal ReignSaveSyncCoordinator.VerifiedLoad ReleaseLoad;
            internal ReignMainThread.GameScope ReleaseScope;
            internal readonly NativeRelationshipAttempts OriginAttempts = new NativeRelationshipAttempts();
            internal readonly NativeRelationshipAttempts SuccessorAttempts = new NativeRelationshipAttempts();
            internal bool WitnessIncomplete;
            private ReignMainThread.GameScope _successorScope;
            private ReignSaveSyncCoordinator.VerifiedLoad _successorLoad;
            internal int TargetCount;
            private string _outcome;

            internal NativeRelationshipResponseHold(string id, string runId, string save, string baseline,
                string hash, string campaignId, string timeline, string game, TaleWorlds.CampaignSystem.Campaign campaign,
                ReignRelationshipCampaignBehavior relationship, ReignMainThread.GameScope scope,
                ReignSaveSyncCoordinator.VerifiedLoad load, string pair,
                int revision)
            {
                Id = id; RunId = runId; Save = save; Baseline = baseline; Hash = hash;
                CampaignId = campaignId; TimelineId = timeline; GameInstance = game; Campaign = campaign;
                Relationship = relationship; OriginScope = scope; Load = load; Pair = pair; RequestedRevision = revision;
                _ = Expiry.ContinueWith(_ => ClearOriginalOwner(), TaskScheduler.Default);
            }

            internal void BindHeldTarget(ReignNativeRelationSyncTarget target)
            {
                Revision = target.Revision;
                HeroA = target.HeroAId;
                HeroB = target.HeroBId;
                TargetRelation = target.TargetRelation;
            }

            internal bool TryBindSuccessor(ReignMainThread.GameScope scope, ReignSaveSyncCoordinator.VerifiedLoad load)
            {
                if (_successorScope == null)
                {
                    _successorScope = scope;
                    _successorLoad = load;
                }
                return ReferenceEquals(_successorScope, scope) && ReferenceEquals(_successorLoad, load)
                    && (ReleaseScope == null || ReferenceEquals(ReleaseScope, scope) && ReferenceEquals(ReleaseLoad, load));
            }

            internal void RecordOutcome(string outcome)
            {
                if (Interlocked.CompareExchange(ref _outcome, outcome, null) == null)
                    ClearOriginalOwner();
            }

            private void ClearOriginalOwner()
            {
                Interlocked.Exchange(ref Campaign, null);
                Interlocked.Exchange(ref Relationship, null);
            }

            internal JObject Evidence() => new JObject
            {
                ["schema"] = "reign-native-relationship-response-v1", ["operationId"] = Id,
                ["clientProcessInstanceId"] = PartyAgencyClientProcessInstanceId, ["saveName"] = Save,
                ["savePointId"] = Load.SavePointId, ["saveSha256"] = Hash, ["pairKey"] = Pair, ["revision"] = Revision,
                ["responseTargetCount"] = TargetCount, ["originGameInstanceId"] = GameInstance,
                ["heldTarget"] = TargetCount == 0 ? null : new JObject
                {
                    ["source"] = "normal_claimed_response", ["heroAId"] = HeroA, ["heroBId"] = HeroB,
                    ["targetRelation"] = TargetRelation, ["revision"] = Revision
                },
                ["nativeWriteWitness"] = new JObject
                {
                    ["schema"] = "reign-native-relationship-writes-v1",
                    ["incomplete"] = WitnessIncomplete || (_successorScope != null && ReleaseScope != null
                        && (!ReferenceEquals(_successorScope, ReleaseScope) || !ReferenceEquals(_successorLoad, ReleaseLoad))),
                    ["windowClosed"] = Expiry.IsCompleted,
                    ["alignedToRelease"] = _successorScope != null && ReferenceEquals(_successorScope, ReleaseScope)
                        && ReferenceEquals(_successorLoad, ReleaseLoad),
                    ["origin"] = OriginAttempts.Evidence(), ["successor"] = SuccessorAttempts.Evidence()
                },
                ["originCoordinatorGeneration"] = Load.CoordinatorGeneration, ["originLoadSessionId"] = Load.LoadSessionId,
                ["originSharedActivityGeneration"] = Load.RestoreGeneration,
                ["releaseGameInstanceId"] = ReleaseGameInstance, ["releaseLoadSessionId"] = ReleaseLoad?.LoadSessionId,
                ["releaseCoordinatorGeneration"] = ReleaseLoad?.CoordinatorGeneration,
                ["releaseSharedActivityGeneration"] = ReleaseLoad?.RestoreGeneration,
                ["nativeCampaignChanged"] = ReleaseLoad != null, ["relationshipOwnerChanged"] = ReleaseLoad != null,
                ["outcome"] = Volatile.Read(ref _outcome) ?? (Resume.Task.IsCompleted ? "released"
                    : Expiry.IsCompleted ? "expired" : TargetCount == 0 ? "armed" : "held")
            };
        }
    }
}
