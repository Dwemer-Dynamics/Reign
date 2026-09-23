using System;
using System.Collections.Generic;

namespace ReignBeta.PartyAgency
{
    // Main-thread owned, deliberately not persisted. Saved agreement/ledger state remains authoritative.
    // A close is never a departure decision; only a native end action or the explicit inquiry can depart.
    public enum ReignGuestReviewResolution { Pending, Retry, Resolved, Stale }

    public sealed class ReignGuestReviewSession
    {
        public const int PendingCloseTimeoutSeconds = 30;
        public string SessionId { get; } = Guid.NewGuid().ToString("N");
        public string HeroId { get; }
        public string AgreementId { get; }
        public bool ProviderPending { get; private set; }
        public bool ProviderFailed { get; private set; } = true;
        public DateTime? ClosedUtc { get; private set; }
        public HashSet<string> ActionIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public ReignGuestReviewSession(string heroId, string agreementId)
        { HeroId = heroId; AgreementId = agreementId; }

        public void BeginTurn() { ProviderPending = true; ProviderFailed = true; }
        public void CompleteTurn(bool ok, IEnumerable<string> actionIds)
        {
            foreach (string id in actionIds ?? new string[0])
                if (!string.IsNullOrWhiteSpace(id)) ActionIds.Add(id);
            ProviderFailed = !ok;
            ProviderPending = false;
        }
        public void Close(DateTime now) { if (!ClosedUtc.HasValue) ClosedUtc = now; }

        public ReignGuestReviewResolution Resolve(string heroId, string agreementId, string phase,
            bool eligible, bool pendingNativeAction, DateTime now)
        {
            if (!eligible || !string.Equals(HeroId, heroId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(AgreementId, agreementId, StringComparison.OrdinalIgnoreCase))
                return ReignGuestReviewResolution.Stale;
            if (phase != "ReviewDue") return ReignGuestReviewResolution.Resolved;
            if (!ClosedUtc.HasValue || ((ProviderPending || pendingNativeAction)
                && now - ClosedUtc.Value < TimeSpan.FromSeconds(PendingCloseTimeoutSeconds)))
                return ReignGuestReviewResolution.Pending;
            return ReignGuestReviewResolution.Retry;
        }

        public static bool MatchesRenewal(string agreementId, double reviewDueDay, string phase,
            string expectedAgreementId, double expectedReviewDueDay, int revision = 0, int expectedRevision = 0)
            => (phase == "Active" || phase == "ReviewDue")
                && !string.IsNullOrWhiteSpace(expectedAgreementId)
                && string.Equals(agreementId, expectedAgreementId, StringComparison.OrdinalIgnoreCase)
                && !double.IsNaN(expectedReviewDueDay) && !double.IsInfinity(expectedReviewDueDay)
                && expectedReviewDueDay > 0d && reviewDueDay == expectedReviewDueDay
                && revision >= 0 && revision < int.MaxValue && revision == expectedRevision;
    }
}
