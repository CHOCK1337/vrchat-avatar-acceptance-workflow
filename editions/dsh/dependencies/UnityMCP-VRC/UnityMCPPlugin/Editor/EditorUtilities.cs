using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace UnityMCP.Editor
{
    public static class EditorUtilities
    {
        // How long a main-thread request waits before giving up. Unity throttles (and on
        // some platforms effectively suspends) its editor loop while the window is unfocused,
        // so this is intentionally generous: a queued request completes whenever Unity next
        // ticks (e.g. when you refocus the Editor) rather than erroring out early. Raise this
        // AND the matching per-tool timeout in the *.ts tools if you want to wait even longer.
        public const int MainThreadTimeoutMs = 55000;

        // Work that must run on Unity's main thread, drained on every editor tick.
        private static readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();
        private static bool loggedThrottleHint = false;

        // Wall-clock (UTC ticks) of the last editor-loop tick. Unity throttles - and on some
        // platforms effectively suspends - EditorApplication.update while the window is unfocused,
        // so a stale value means queued work isn't going to run soon. We use it to fail fast with a
        // clear message instead of waiting out the full timeout. Updated every tick, including while
        // compiling (the loop keeps ticking during a compile). Accessed off the main thread, so
        // read/written via Interlocked for an atomic 64-bit value.
        private static long lastTickTicks = DateTime.UtcNow.Ticks;

        // True only while we're synchronously running queued actions. A long-running command keeps
        // the main thread busy - so no ticks fire - without the loop being throttled; this flag
        // keeps that from being misread as "unfocused".
        private static volatile bool isDraining;

        // If the loop hasn't ticked for this long while idle (not compiling, not draining), treat
        // the Editor as unfocused/suspended and fail queued requests fast rather than hanging.
        private const int StallThresholdMs = 6000;

        /// <summary>Number of queued main-thread actions waiting to run (for diagnostics).</summary>
        public static int PendingMainThreadActions => mainThreadQueue.Count;

        [InitializeOnLoadMethod]
        private static void RegisterMainThreadPump()
        {
            // Guard against double-registration across domain reloads.
            EditorApplication.update -= DrainMainThreadQueue;
            EditorApplication.update += DrainMainThreadQueue;
        }

        private static void DrainMainThreadQueue()
        {
            // Record that the loop is alive on every tick (even while compiling) so RunOnMainThread
            // can tell a throttled/suspended editor from a merely busy one.
            Interlocked.Exchange(ref lastTickTicks, DateTime.UtcNow.Ticks);

            // Defer while Unity is compiling so queued work runs against a stable, post-compile
            // state and returns a real result instead of racing the recompile. (Script recompiles
            // end in a domain reload that resets this queue; those in-flight requests are lost and
            // must be retried by the caller.)
            if (EditorApplication.isCompiling)
                return;

            isDraining = true;
            try
            {
                while (mainThreadQueue.TryDequeue(out var action))
                {
                    try { action(); }
                    catch (Exception e) { Debug.LogError($"[UnityMCP] Main-thread action failed: {e.Message}"); }
                }
            }
            finally
            {
                isDraining = false;
            }
        }

        /// <summary>
        /// Runs <paramref name="func"/> on the Unity main thread and returns its result.
        /// The work is queued and drained from EditorApplication.update, so it executes as soon
        /// as Unity next ticks (and is held back while Unity is compiling, running once that
        /// finishes). While the Editor is unfocused Unity throttles that loop, so a
        /// call may wait until you refocus the window (or set Preferences > General >
        /// Interaction Mode to "No Throttling"). If the editor loop has clearly stalled it throws
        /// a TimeoutException early (a few seconds) rather than hanging; otherwise it waits up to
        /// <paramref name="timeoutMs"/> before throwing. Both messages are actionable.
        /// The wait runs off the main thread (ConfigureAwait(false)) so it is not itself blocked
        /// by the throttled loop.
        /// </summary>
        public static async Task<T> RunOnMainThread<T>(Func<T> func, int timeoutMs = MainThreadTimeoutMs)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            mainThreadQueue.Enqueue(() =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });

            // Poll for completion. Besides the hard timeout, fail fast once the editor loop has
            // clearly stalled (unfocused/suspended): that turns a near-minute hang into a few-second
            // error the caller can act on and retry.
            var startUtc = DateTime.UtcNow;
            while (true)
            {
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(250)).ConfigureAwait(false);
                if (completed == tcs.Task)
                    return await tcs.Task.ConfigureAwait(false); // value, or rethrows the inner exception

                var now = DateTime.UtcNow;
                var lastTick = new DateTime(Interlocked.Read(ref lastTickTicks), DateTimeKind.Utc);
                var stalledMs = (now - lastTick).TotalMilliseconds;

                // The loop hasn't ticked for a while, and it isn't compiling (we wait those out) or
                // busy running an earlier queued command - so the Editor is unfocused/suspended.
                if (stalledMs > StallThresholdMs && !EditorApplication.isCompiling && !isDraining)
                {
                    LogThrottleHintOnce();
                    throw new TimeoutException(
                        $"Unity Editor appears unfocused or busy - its editor loop has not ticked for " +
                        $"{stalledMs / 1000f:0.#}s, so the request could not run. Focus the Unity Editor " +
                        "window (or set Preferences > General > Interaction Mode to 'No Throttling') and retry.");
                }

                if ((now - startUtc).TotalMilliseconds >= timeoutMs)
                {
                    LogThrottleHintOnce();
                    throw new TimeoutException(
                        $"Unity main thread did not run within {timeoutMs / 1000f:0.#}s. " +
                        "The Editor is likely unfocused or compiling - focus the Unity Editor window " +
                        "(or set Preferences > General > Interaction Mode to 'No Throttling') and retry.");
                }
            }
        }

        private static void LogThrottleHintOnce()
        {
            if (loggedThrottleHint) return;
            loggedThrottleHint = true;
            Debug.LogWarning("[UnityMCP] A request could not reach the main thread (Editor unfocused or " +
                "busy). To let MCP tools work while the Editor is unfocused, set " +
                "Preferences > General > Interaction Mode > No Throttling.");
        }
    }
}
