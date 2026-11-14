// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: disabled sources never receive Enable or Disable commands, so the command
// handler has no work and the polling thread, sleep event and timer are omitted.

using System.Collections.Generic;
using System.Threading;

namespace System.Diagnostics.Tracing
{
    internal sealed class CounterGroup
    {
        private readonly EventSource _eventSource;
        private readonly List<DiagnosticCounter> _counters;
        private static readonly object s_counterGroupLock = new object();

        internal CounterGroup(EventSource eventSource)
        {
            _eventSource = eventSource;
            _counters = new List<DiagnosticCounter>();
            RegisterCommandCallback();
        }

        internal void Add(DiagnosticCounter eventCounter)
        {
            lock (s_counterGroupLock) // Lock the CounterGroup
                _counters.Add(eventCounter);
        }

        internal void Remove(DiagnosticCounter eventCounter)
        {
            lock (s_counterGroupLock) // Lock the CounterGroup
                _counters.Remove(eventCounter);
        }

        #region EventSource Command Processing

        private void RegisterCommandCallback()
        {
            _eventSource.EventCommandExecuted += OnEventSourceCommand;
        }

        private void OnEventSourceCommand(object? sender, EventCommandEventArgs e)
        {
            // Disabled sources deliver no commands, so the polling timer is never enabled.
        }

        #endregion // EventSource Command Processing

        #region Global CounterGroup Array management

        // We need eventCounters to 'attach' themselves to a particular EventSource.
        // this table provides the mapping from EventSource -> CounterGroup
        // which represents this 'attached' information.
        private static WeakReference<CounterGroup>[]? s_counterGroups;

        private static void EnsureEventSourceIndexAvailable(int eventSourceIndex)
        {
            Debug.Assert(Monitor.IsEntered(s_counterGroupLock));
            if (s_counterGroups == null)
            {
                s_counterGroups = new WeakReference<CounterGroup>[eventSourceIndex + 1];
            }
            else if (eventSourceIndex >= s_counterGroups.Length)
            {
                WeakReference<CounterGroup>[] newCounterGroups = new WeakReference<CounterGroup>[eventSourceIndex + 1];
                Array.Copy(s_counterGroups, newCounterGroups, s_counterGroups.Length);
                s_counterGroups = newCounterGroups;
            }
        }

        internal static CounterGroup GetCounterGroup(EventSource eventSource)
        {
            lock (s_counterGroupLock)
            {
                int eventSourceIndex = EventListener.EventSourceIndex(eventSource);
                EnsureEventSourceIndexAvailable(eventSourceIndex);
                Debug.Assert(s_counterGroups != null);
                WeakReference<CounterGroup> weakRef = s_counterGroups[eventSourceIndex];
                if (weakRef == null || !weakRef.TryGetTarget(out CounterGroup? ret))
                {
                    ret = new CounterGroup(eventSource);
                    s_counterGroups[eventSourceIndex] = new WeakReference<CounterGroup>(ret);
                }
                return ret;
            }
        }

        #endregion // Global CounterGroup Array management
    }
}
