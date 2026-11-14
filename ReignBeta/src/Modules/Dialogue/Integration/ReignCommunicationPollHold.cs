#nullable enable
using System;

namespace ReignBeta.Integration
{
    // A process-local scheduling lease, never a replacement for a quiescence check.
    // The game thread owns callers. In-flight work and explicit/durable work are not held.
    internal sealed class ReignCommunicationPollHold
    {
        private object? _campaign;
        private string _scope = "", _token = "";
        internal bool HasLease => _token.Length > 0;
        internal DateTime UntilUtc { get; private set; }

        internal bool Active(object? campaign, string scope, DateTime now)
        {
            if (_token.Length == 0) return false;
            if (!ReferenceEquals(campaign, _campaign) || scope != _scope || now >= UntilUtc)
            {
                _campaign = null; _scope = _token = ""; UntilUtc = DateTime.MinValue;
                return false;
            }
            return true;
        }

        internal void Acquire(object? campaign, string scope, string token, int seconds, DateTime now)
        {
            if (campaign == null || string.IsNullOrEmpty(scope) || scope.Length > 1024
                || string.IsNullOrEmpty(token) || token.Length > 100
                || !System.Text.RegularExpressions.Regex.IsMatch(token, @"\A[A-Za-z0-9_-]+\z")
                || seconds < 1 || seconds > 60)
                throw new ArgumentException("A checkpoint poll hold requires an exact scope, safe token and 1-60 second deadline.");
            if (Active(campaign, scope, now) && _token != token)
                throw new InvalidOperationException("Another checkpoint owns the periodic communication hold.");
            _campaign = campaign; _scope = scope; _token = token; UntilUtc = now.AddSeconds(seconds);
        }

        internal bool Release(object? campaign, string scope, string token, DateTime now)
        {
            // A delayed release from an old generation must not invalidate its successor.
            if (!ReferenceEquals(campaign, _campaign) || _scope != scope || _token != token
                || !Active(campaign, scope, now)) return false;
            _campaign = null; _scope = _token = ""; UntilUtc = DateTime.MinValue;
            return true;
        }

        internal bool Suppresses(object? campaign, string scope, DateTime now, bool explicitRequest, bool pendingReceipt)
            => Active(campaign, scope, now) && !explicitRequest && !pendingReceipt;
    }
}
