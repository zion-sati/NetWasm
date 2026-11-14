// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.IO.Pipelines
{
    /// <summary>Abstraction for running <see cref="System.IO.Pipelines.PipeReader" /> and <see cref="System.IO.Pipelines.PipeWriter" /> callbacks and continuations.</summary>
    public abstract class PipeScheduler
    {
        private static readonly InlineScheduler s_inlineScheduler = new InlineScheduler();

        /// <summary>The thread-pool scheduler is unavailable in the single-reactor NetWasm profile.</summary>
        /// <exception cref="PlatformNotSupportedException">Thread-pool scheduling is not supported.</exception>
        public static PipeScheduler ThreadPool => throw new PlatformNotSupportedException("Thread-pool scheduling is not supported; use PipeScheduler.Inline or a custom scheduler.");

        /// <summary>The <see cref="System.IO.Pipelines.PipeScheduler" /> implementation that runs callbacks inline.</summary>
        /// <value>A <see cref="System.IO.Pipelines.PipeScheduler" /> instance that runs callbacks inline.</value>
        public static PipeScheduler Inline => s_inlineScheduler;

        /// <summary>Requests <paramref name="action" /> to be run on scheduler with <paramref name="state" /> being passed in.</summary>
        /// <param name="action">The single-parameter action delegate to schedule.</param>
        /// <param name="state">The parameter to pass to the <paramref name="action" /> delegate.</param>
        public abstract void Schedule(Action<object?> action, object? state);

        internal virtual void UnsafeSchedule(Action<object?> action, object? state)
            => Schedule(action, state);
    }
}
