using KitsuneCommand.Core;

namespace KitsuneCommand.GameIntegration
{
    /// <summary>
    /// Runs game work on the main thread and waits for its answer. Chat handlers are
    /// already there and run it inline; web controllers and delayed deliveries are not.
    /// </summary>
    internal static class GameThread
    {
        private const int Pending = 0, Started = 1, Abandoned = 2;

        /// <summary>
        /// What <paramref name="work"/> returned, or <paramref name="failed"/> when it threw or
        /// the main thread didn't get to it within 5 seconds. Work that timed out never runs
        /// later, so "not delivered" stays true and a caller that didn't charge for it is right.
        /// </summary>
        public static T Run<T>(string what, Func<T> work, T failed)
        {
            var main = ModEntry.MainThreadContext;
            if (main == null || SynchronizationContext.Current == main)
                return work();

            var state = Pending;
            var result = failed;
            // Not disposed: after a timeout the posted callback still runs and sets it.
            var done = new ManualResetEventSlim(false);
            main.Post(_ =>
            {
                if (Interlocked.CompareExchange(ref state, Started, Pending) != Pending) return;
                try { result = work(); }
                catch (Exception ex) { Log.Warning($"[KitsuneCommand] {what} failed: {ex.Message}"); }
                finally { done.Set(); }
            }, null);

            if (done.Wait(TimeSpan.FromSeconds(5))) return result;

            if (Interlocked.CompareExchange(ref state, Abandoned, Pending) == Pending)
            {
                Log.Warning($"[KitsuneCommand] {what} timed out waiting for the main thread.");
                return failed;
            }

            // It started just as we gave up; it's running now, so report what it did.
            done.Wait();
            return result;
        }
    }
}
