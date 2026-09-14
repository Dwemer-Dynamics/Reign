#nullable disable
using System;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Integration
{
    internal static class ReignCampaignPreparationStartPolicy
    {
        internal static bool CanStart(
            bool sessionLaunched,
            bool campaignAvailable,
            bool playerAvailable,
            bool saveSyncReady,
            bool pipelineStarted)
        {
            return sessionLaunched
                && campaignAvailable
                && playerAvailable
                && saveSyncReady
                && !pipelineStarted;
        }
    }

    internal struct ReignInitializationReleaseToken
    {
        internal ReignInitializationReleaseToken(string generationId, string authorizationNonce)
        {
            GenerationId = generationId ?? string.Empty;
            AuthorizationNonce = authorizationNonce ?? string.Empty;
        }

        internal string GenerationId { get; }
        internal string AuthorizationNonce { get; }
        internal bool IsIssued =>
            !string.IsNullOrWhiteSpace(GenerationId)
            && !string.IsNullOrWhiteSpace(AuthorizationNonce);
    }

    internal sealed class ReignInitializationAuthorization
    {
        private readonly string _generationId;
        private readonly string _authorizationNonce;

        private ReignInitializationAuthorization(string generationId)
        {
            _generationId = generationId;
            _authorizationNonce = Guid.NewGuid().ToString("N");
        }

        internal static ReignInitializationAuthorization Begin(string generationId)
        {
            if (string.IsNullOrWhiteSpace(generationId))
            {
                throw new ArgumentException("Generation id is required.", nameof(generationId));
            }

            return new ReignInitializationAuthorization(generationId);
        }

        internal ReignInitializationReleaseToken IssueReleaseToken(string generationId)
        {
            return string.Equals(_generationId, generationId, StringComparison.Ordinal)
                ? new ReignInitializationReleaseToken(_generationId, _authorizationNonce)
                : default(ReignInitializationReleaseToken);
        }

        internal bool CanRelease(
            string generationId,
            ReignInitializationReleaseToken token)
        {
            return token.IsIssued
                && string.Equals(_generationId, generationId, StringComparison.Ordinal)
                && string.Equals(token.GenerationId, generationId, StringComparison.Ordinal)
                && string.Equals(
                    token.AuthorizationNonce,
                    _authorizationNonce,
                    StringComparison.Ordinal);
        }
    }

    internal enum ReignInitializationStage
    {
        CampaignStart = 0,
        Personalities = 1,
        NativeFoundations = 2,
        Timeline = 3,
        Identity = 4,
        AuthoritativeSnapshots = 5,
        Relationships = 6,
        CriticalDrain = 7,
        InitialSocialWorld = 8,
        ServerAcknowledgement = 9,
        Ready = 10,
        Failed = 11
    }

    internal enum ReignInitializationMode
    {
        Fresh = 0,
        Resume = 1,
        CompatibilityAudit = 2,
        Sealed = 3
    }

    [Flags]
    internal enum ReignInitializationStageMask
    {
        None = 0,
        Personalities = 1 << 0,
        NativeFoundations = 1 << 1,
        Timeline = 1 << 2,
        Identity = 1 << 3,
        AuthoritativeSnapshots = 1 << 4,
        Relationships = 1 << 5,
        CriticalDrain = 1 << 6,
        InitialSocialWorld = 1 << 7,
        ServerAcknowledgement = 1 << 8
    }

    internal sealed class ReignInitializationJournal
    {
        internal int SealVersion { get; set; }
        internal string CampaignId { get; set; } = string.Empty;
        internal string TimelineId { get; set; } = string.Empty;
        internal ReignInitializationStageMask CompletedStageMask { get; set; }
        internal long SealedHistorySequence { get; set; }
        internal bool IsSealed =>
            SealVersion >= ReignCampaignReadinessState.CurrentSealVersion
            && !string.IsNullOrWhiteSpace(CampaignId)
            && !string.IsNullOrWhiteSpace(TimelineId);

        internal static ReignInitializationMode SelectLoadMode(
            int sealVersion,
            bool personalitiesComplete,
            bool establishedCampaign)
        {
            if (sealVersion >= ReignCampaignReadinessState.CurrentSealVersion)
            {
                return ReignInitializationMode.Sealed;
            }

            if (establishedCampaign)
            {
                return ReignInitializationMode.CompatibilityAudit;
            }

            return personalitiesComplete
                ? ReignInitializationMode.Resume
                : ReignInitializationMode.Fresh;
        }
    }

    internal sealed class ReignInitializationQueueObservation : IEquatable<ReignInitializationQueueObservation>
    {
        internal int WorldHistoryCount { get; set; }
        internal int RelationshipInputCount { get; set; }
        internal int NativeTargetCount { get; set; }
        internal int NativeReceiptCount { get; set; }
        internal int InFlightCount { get; set; }
        internal int NativeProjectionFailureCount { get; set; }
        internal long HistorySequence { get; set; }

        internal bool IsQuiescent =>
            WorldHistoryCount == 0
            && RelationshipInputCount == 0
            && NativeTargetCount == 0
            && NativeReceiptCount == 0
            && InFlightCount == 0
            && NativeProjectionFailureCount == 0;

        internal static ReignInitializationQueueObservation Idle(long historySequence)
        {
            return new ReignInitializationQueueObservation
            {
                HistorySequence = historySequence
            };
        }

        public bool Equals(ReignInitializationQueueObservation other)
        {
            if (ReferenceEquals(other, null)) return false;
            return WorldHistoryCount == other.WorldHistoryCount
                && RelationshipInputCount == other.RelationshipInputCount
                && NativeTargetCount == other.NativeTargetCount
                && NativeReceiptCount == other.NativeReceiptCount
                && InFlightCount == other.InFlightCount
                && NativeProjectionFailureCount == other.NativeProjectionFailureCount
                && HistorySequence == other.HistorySequence;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ReignInitializationQueueObservation);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + WorldHistoryCount;
                hash = (hash * 31) + RelationshipInputCount;
                hash = (hash * 31) + NativeTargetCount;
                hash = (hash * 31) + NativeReceiptCount;
                hash = (hash * 31) + InFlightCount;
                hash = (hash * 31) + NativeProjectionFailureCount;
                hash = (hash * 31) + HistorySequence.GetHashCode();
                return hash;
            }
        }
    }

    internal sealed class ReignCampaignReadinessState
    {
        internal const int CurrentSealVersion = 3;

        private readonly string _campaignId;
        private readonly string _generationId;
        private ReignInitializationStage _requiredStage;
        private ReignInitializationQueueObservation _lastObservation;
        private int _stableObservationCount;
        private string _timelineId = string.Empty;
        private string _error = string.Empty;
        private long _sealedHistorySequence;
        private bool _serverAcknowledged;
        private bool _timelineHandshakeComplete;
        private ReignInitializationStage _retryStage;
        private ReignInitializationStageMask _completedStageMask;

        private ReignCampaignReadinessState(
            string campaignId,
            string generationId,
            ReignInitializationStage requiredStage,
            ReignInitializationStageMask completedStageMask)
        {
            _campaignId = campaignId ?? string.Empty;
            _generationId = generationId ?? string.Empty;
            _requiredStage = requiredStage;
            _retryStage = requiredStage;
            _completedStageMask = completedStageMask;
        }

        internal static ReignCampaignReadinessState Start(string campaignId, string generationId)
        {
            if (string.IsNullOrWhiteSpace(campaignId))
            {
                throw new ArgumentException("Campaign id is required.", nameof(campaignId));
            }

            if (string.IsNullOrWhiteSpace(generationId))
            {
                throw new ArgumentException("Generation id is required.", nameof(generationId));
            }

            return new ReignCampaignReadinessState(
                campaignId,
                generationId,
                ReignInitializationStage.Personalities,
                ReignInitializationStageMask.None);
        }

        internal static ReignCampaignReadinessState Resume(
            ReignInitializationJournal journal,
            string campaignId,
            string generationId)
        {
            if (journal == null) throw new ArgumentNullException(nameof(journal));
            var required = FirstIncompleteStage(journal.CompletedStageMask);
            var state = new ReignCampaignReadinessState(
                campaignId,
                generationId,
                required,
                journal.CompletedStageMask);
            state._timelineId = journal.TimelineId ?? string.Empty;
            state._sealedHistorySequence = journal.SealedHistorySequence;
            if (journal.IsSealed)
            {
                state._requiredStage = ReignInitializationStage.Ready;
                state._serverAcknowledged = true;
            }
            return state;
        }

        internal bool CanRelease =>
            _requiredStage == ReignInitializationStage.Ready
            && _serverAcknowledged
            && !RequiresTimelineHandshake
            && string.IsNullOrWhiteSpace(_error);

        internal ReignInitializationStage RequiredStage => _requiredStage;
        internal string CampaignId => _campaignId;
        internal string GenerationId => _generationId;
        internal string TimelineId => _timelineId;
        internal string Error => _error;
        internal long SealedHistorySequence => _sealedHistorySequence;
        internal ReignInitializationStageMask CompletedStageMask => _completedStageMask;
        internal bool RequiresTimelineHandshake =>
            !_timelineHandshakeComplete
            && !string.IsNullOrWhiteSpace(_timelineId)
            && (_completedStageMask & ReignInitializationStageMask.Timeline) != 0;

        internal void SetTimeline(string timelineId)
        {
            _timelineId = timelineId ?? string.Empty;
            _timelineHandshakeComplete = !string.IsNullOrWhiteSpace(_timelineId);
            ResetQuiescence();
        }

        internal bool AcceptTimelineHandshake(
            string generationId,
            string timelineId)
        {
            if (!MatchesGeneration(generationId)
                || string.IsNullOrWhiteSpace(timelineId))
            {
                return false;
            }

            string previousTimelineId = _timelineId;
            bool changed = !string.IsNullOrWhiteSpace(previousTimelineId)
                && !string.Equals(
                    previousTimelineId,
                    timelineId,
                    StringComparison.Ordinal);
            _timelineId = timelineId;
            _timelineHandshakeComplete = true;
            if (changed)
            {
                _completedStageMask &=
                    ReignInitializationStageMask.Personalities
                    | ReignInitializationStageMask.NativeFoundations
                    | ReignInitializationStageMask.Timeline;
                _sealedHistorySequence = 0L;
                _serverAcknowledged = false;
                _error = string.Empty;
                _requiredStage = ReignInitializationStage.Identity;
                _retryStage = _requiredStage;
            }
            ResetQuiescence();
            return true;
        }

        internal void Advance(ReignInitializationStage completed)
        {
            if (_requiredStage == ReignInitializationStage.Failed
                || _requiredStage == ReignInitializationStage.Ready)
            {
                return;
            }

            if (completed != _requiredStage)
            {
                throw new InvalidOperationException(
                    "Cannot complete " + completed + " while awaiting " + _requiredStage + ".");
            }

            _completedStageMask |= MaskFor(completed);
            _requiredStage = NextStage(completed);
            _retryStage = _requiredStage;
            ResetQuiescence();
        }

        internal bool TryAdvance(string generationId, ReignInitializationStage completed)
        {
            if (!MatchesGeneration(generationId)) return false;
            Advance(completed);
            return true;
        }

        internal bool AcceptStageResult(
            string generationId,
            ReignInitializationStage completed,
            bool success)
        {
            if (!MatchesGeneration(generationId)) return false;
            if (!success)
            {
                Fail(completed + " preparation failed.");
                return false;
            }

            Advance(completed);
            return true;
        }

        internal bool ObserveQuiescence(ReignInitializationQueueObservation observation)
        {
            if (_requiredStage != ReignInitializationStage.ServerAcknowledgement
                || observation == null
                || !observation.IsQuiescent)
            {
                ResetQuiescence();
                return false;
            }

            if (!(_lastObservation?.Equals(observation) ?? false))
            {
                _lastObservation = observation;
                _stableObservationCount = 1;
                return false;
            }

            _stableObservationCount++;
            _sealedHistorySequence = observation.HistorySequence;
            return _stableObservationCount >= 2;
        }

        internal void AcceptServerAcknowledgement(
            string campaignId,
            string timelineId,
            long historySequence,
            int sealVersion)
        {
            if (_requiredStage != ReignInitializationStage.ServerAcknowledgement
                || _stableObservationCount < 2
                || sealVersion != CurrentSealVersion
                || !string.Equals(_campaignId, campaignId, StringComparison.Ordinal)
                || !string.Equals(_timelineId, timelineId, StringComparison.Ordinal)
                || historySequence != _sealedHistorySequence)
            {
                return;
            }

            _serverAcknowledged = true;
            _completedStageMask |= ReignInitializationStageMask.ServerAcknowledgement;
            _requiredStage = ReignInitializationStage.Ready;
        }

        internal void Fail(string error)
        {
            _retryStage = _requiredStage;
            _error = string.IsNullOrWhiteSpace(error) ? "Campaign preparation failed." : error;
            _requiredStage = ReignInitializationStage.Failed;
            ResetQuiescence();
        }

        internal void Retry()
        {
            if (_requiredStage != ReignInitializationStage.Failed) return;
            _error = string.Empty;
            _requiredStage = _retryStage;
        }

        internal JObject Snapshot()
        {
            return new JObject
            {
                ["campaignId"] = _campaignId,
                ["generationId"] = _generationId,
                ["timelineId"] = _timelineId,
                ["requiredStage"] = _requiredStage.ToString(),
                ["completedStageMask"] = (int)_completedStageMask,
                ["stableObservationCount"] = _stableObservationCount,
                ["sealedHistorySequence"] = _sealedHistorySequence,
                ["serverAcknowledged"] = _serverAcknowledged,
                ["requiresTimelineHandshake"] = RequiresTimelineHandshake,
                ["canRelease"] = CanRelease,
                ["error"] = _error
            };
        }

        private static ReignInitializationStage NextStage(ReignInitializationStage completed)
        {
            switch (completed)
            {
                case ReignInitializationStage.Personalities:
                    return ReignInitializationStage.NativeFoundations;
                case ReignInitializationStage.NativeFoundations:
                    return ReignInitializationStage.Timeline;
                case ReignInitializationStage.Timeline:
                    return ReignInitializationStage.Identity;
                case ReignInitializationStage.Identity:
                    return ReignInitializationStage.AuthoritativeSnapshots;
                case ReignInitializationStage.AuthoritativeSnapshots:
                    return ReignInitializationStage.Relationships;
                case ReignInitializationStage.Relationships:
                    return ReignInitializationStage.CriticalDrain;
                case ReignInitializationStage.CriticalDrain:
                    return ReignInitializationStage.InitialSocialWorld;
                case ReignInitializationStage.InitialSocialWorld:
                    return ReignInitializationStage.ServerAcknowledgement;
                case ReignInitializationStage.ServerAcknowledgement:
                    return ReignInitializationStage.Ready;
                default:
                    throw new InvalidOperationException("Unsupported readiness stage " + completed + ".");
            }
        }

        private static ReignInitializationStage FirstIncompleteStage(
            ReignInitializationStageMask completed)
        {
            if ((completed & ReignInitializationStageMask.Personalities) == 0)
                return ReignInitializationStage.Personalities;
            if ((completed & ReignInitializationStageMask.NativeFoundations) == 0)
                return ReignInitializationStage.NativeFoundations;
            if ((completed & ReignInitializationStageMask.Timeline) == 0)
                return ReignInitializationStage.Timeline;
            if ((completed & ReignInitializationStageMask.Identity) == 0)
                return ReignInitializationStage.Identity;
            if ((completed & ReignInitializationStageMask.AuthoritativeSnapshots) == 0)
                return ReignInitializationStage.AuthoritativeSnapshots;
            if ((completed & ReignInitializationStageMask.Relationships) == 0)
                return ReignInitializationStage.Relationships;
            if ((completed & ReignInitializationStageMask.CriticalDrain) == 0)
                return ReignInitializationStage.CriticalDrain;
            if ((completed & ReignInitializationStageMask.InitialSocialWorld) == 0)
                return ReignInitializationStage.InitialSocialWorld;
            return ReignInitializationStage.ServerAcknowledgement;
        }

        private static ReignInitializationStageMask MaskFor(ReignInitializationStage stage)
        {
            switch (stage)
            {
                case ReignInitializationStage.Personalities:
                    return ReignInitializationStageMask.Personalities;
                case ReignInitializationStage.NativeFoundations:
                    return ReignInitializationStageMask.NativeFoundations;
                case ReignInitializationStage.Timeline:
                    return ReignInitializationStageMask.Timeline;
                case ReignInitializationStage.Identity:
                    return ReignInitializationStageMask.Identity;
                case ReignInitializationStage.AuthoritativeSnapshots:
                    return ReignInitializationStageMask.AuthoritativeSnapshots;
                case ReignInitializationStage.Relationships:
                    return ReignInitializationStageMask.Relationships;
                case ReignInitializationStage.CriticalDrain:
                    return ReignInitializationStageMask.CriticalDrain;
                case ReignInitializationStage.InitialSocialWorld:
                    return ReignInitializationStageMask.InitialSocialWorld;
                case ReignInitializationStage.ServerAcknowledgement:
                    return ReignInitializationStageMask.ServerAcknowledgement;
                default:
                    return ReignInitializationStageMask.None;
            }
        }

        private bool MatchesGeneration(string generationId)
        {
            return string.Equals(_generationId, generationId, StringComparison.Ordinal);
        }

        private void ResetQuiescence()
        {
            _lastObservation = null;
            _stableObservationCount = 0;
        }
    }
}
