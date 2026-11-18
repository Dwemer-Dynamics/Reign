using System;
using System.Threading.Tasks;

namespace ReignBeta.Integration
{
    // Thrown when a request's local owner (for example an accepted Individual Chat turn)
    // closed before the next guarded stage.
    internal sealed class ReignRequestOwnerAbandonedException : OperationCanceledException
    {
        internal ReignRequestOwnerAbandonedException(string message) : base(message) { }
    }

    // Local request-owner helpers. A null owner is the legacy unowned request.
    // Owners are Bannerlord-thread predicates; Ensure must be called from the same
    // game-thread callback that performs the effect.
    internal static class ReignRequestOwnership
    {
        internal static async Task EnsureAsync(Func<bool> requestOwner, string stage)
        {
            if (requestOwner != null && !await ReignMainThread.InvokeAsync(requestOwner).ConfigureAwait(false))
                throw new ReignRequestOwnerAbandonedException("The local request owner closed before " + stage + ".");
        }

        internal static void Ensure(Func<bool> requestOwner, string stage)
        {
            if (requestOwner != null && !requestOwner())
                throw new ReignRequestOwnerAbandonedException("The local request owner closed before " + stage + ".");
        }

        internal static bool IsCurrent(Func<bool> requestOwner) => requestOwner == null || requestOwner();
    }
}
