#nullable disable
using System;
using System.Threading.Tasks;

namespace ReignBeta.Runtime
{
    internal static class ReignWildernessEventCompletion
    {
        // Campaign ownership must be sampled inside the game-thread dispatch,
        // after the provider finishes. Completion persists a pending event; its
        // presentation separately waits for the actual open campaign map.
        internal static async Task<bool> RunAsync<T>(
            Task<T> generation,
            Func<Func<bool>, Task<bool>> dispatch,
            Func<bool> canComplete,
            Func<T, bool> complete)
        {
            T result = await generation.ConfigureAwait(false);
            return await dispatch(() => canComplete() && complete(result)).ConfigureAwait(false);
        }
    }
}
