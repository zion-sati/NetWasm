// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Diagnostics
{
    public class Stopwatch
    {
        private long _elapsed;
        private long _startTimestamp;
        private bool _isRunning;

        public static readonly long Frequency = 1_000_000_000;
        public static readonly bool IsHighResolution = true;

        private static readonly double s_tickFrequency =
            (double)TimeSpan.TicksPerSecond / Frequency;

        public Stopwatch()
        {
        }

        public bool IsRunning { get { return _isRunning; } }

        public TimeSpan Elapsed { get { return new(ElapsedTimeSpanTicks); } }

        public long ElapsedMilliseconds
        {
            get { return ElapsedTimeSpanTicks / TimeSpan.TicksPerMillisecond; }
        }

        public long ElapsedTicks
        {
            get
            {
                var elapsed = _elapsed;
                if (_isRunning)
                {
                    elapsed += GetTimestamp() - _startTimestamp;
                }
                return elapsed;
            }
        }

        public static long GetTimestamp() => unchecked((long)
            Runtime.InteropServices.PlatformServices.MonotonicClock
                .GetMonotonicTime());

        public static TimeSpan GetElapsedTime(long startingTimestamp) =>
            GetElapsedTime(startingTimestamp, GetTimestamp());

        public static TimeSpan GetElapsedTime(
            long startingTimestamp,
            long endingTimestamp) =>
            new((long)((endingTimestamp - startingTimestamp) * s_tickFrequency));

        public static Stopwatch StartNew()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            return stopwatch;
        }

        public void Start()
        {
            if (_isRunning)
            {
                return;
            }
            _startTimestamp = GetTimestamp();
            _isRunning = true;
        }

        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }
            _elapsed += GetTimestamp() - _startTimestamp;
            _isRunning = false;
        }

        public void Reset()
        {
            _elapsed = 0;
            _startTimestamp = 0;
            _isRunning = false;
        }

        public void Restart()
        {
            _elapsed = 0;
            _startTimestamp = GetTimestamp();
            _isRunning = true;
        }

        public override string ToString() => Elapsed.ToString();

        private long ElapsedTimeSpanTicks =>
            (long)(ElapsedTicks * s_tickFrequency);
    }
}
