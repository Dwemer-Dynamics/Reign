#nullable disable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ReignBeta.UI
{
    // Scene text and art consume the same established session, but neither waits
    // for the other's provider. Reopening shares any pending art request.
    internal static class ReignScenePreparationCoordinator
    {
        private static readonly Dictionary<object, Lazy<Task>> ArtJobs =
            new Dictionary<object, Lazy<Task>>();

        internal static async Task RunAsync(object session, Func<Task> openDialogue, Func<Task> prepareArt)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            Task dialogue = InvokeAsync(openDialogue);
            Lazy<Task> job;
            lock (ArtJobs)
            {
                if (!ArtJobs.TryGetValue(session, out job))
                {
                    job = new Lazy<Task>(() => InvokeAsync(prepareArt));
                    ArtJobs.Add(session, job);
                }
            }
            try { await Task.WhenAll(dialogue, job.Value).ConfigureAwait(false); }
            finally
            {
                lock (ArtJobs)
                {
                    if (ArtJobs.TryGetValue(session, out Lazy<Task> current)
                        && ReferenceEquals(current, job)) ArtJobs.Remove(session);
                }
            }
        }

        private static async Task InvokeAsync(Func<Task> operation)
        {
            await operation().ConfigureAwait(false);
        }
    }
}
