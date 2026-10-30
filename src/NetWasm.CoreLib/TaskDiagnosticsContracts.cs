// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// 474ed963699fc29809015392738a15231aaae2de.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Threading.Tasks
{
    [Flags]
    public enum ConfigureAwaitOptions
    {
        None = 0,
        ContinueOnCapturedContext = 1,
        SuppressThrowing = 2,
        ForceYielding = 4,
    }

    public class UnobservedTaskExceptionEventArgs : EventArgs
    {
        private readonly AggregateException _exception;
        private bool _observed;

        public UnobservedTaskExceptionEventArgs(AggregateException exception) =>
            _exception = exception ?? throw new ArgumentNullException(nameof(exception));

        public bool Observed => _observed;

        public AggregateException Exception => _exception;

        public void SetObserved() => _observed = true;
    }
}
