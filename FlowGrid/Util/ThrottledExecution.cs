using System;
using System.Windows.Forms;

namespace FlowGrid.Util
{
    /// <summary>
    /// Trailing debounce for UI-thread work (e.g. persisting position after a
    /// move): the action runs once, <c>delay</c> after the most recent Run call.
    /// Replaces an async-void implementation that could invoke the action
    /// multiple times and leaked exceptions past the caller.
    /// Not thread-safe by design - create and use on the UI thread only.
    /// </summary>
    public class ThrottledExecution : IDisposable
    {
        private readonly Timer timer;
        private Action pending;

        public ThrottledExecution(TimeSpan delay)
        {
            timer = new Timer { Interval = Math.Max(1, (int)delay.TotalMilliseconds) };
            timer.Tick += (s, e) => Flush();
        }

        /// <summary>Schedules the action, replacing any previously scheduled one.</summary>
        public void Run(Action action)
        {
            pending = action;
            timer.Stop();
            timer.Start();
        }

        /// <summary>Runs a pending action immediately (e.g. before the window closes).</summary>
        public void Flush()
        {
            timer.Stop();
            var action = pending;
            pending = null;
            action?.Invoke();
        }

        /// <summary>Drops any pending action without running it.</summary>
        public void Cancel()
        {
            timer.Stop();
            pending = null;
        }

        public void Dispose()
        {
            Cancel();
            timer.Dispose();
        }
    }
}
