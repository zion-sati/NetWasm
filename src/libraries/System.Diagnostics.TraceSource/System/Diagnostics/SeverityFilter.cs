// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System;
using System.Diagnostics.CodeAnalysis;

namespace System.Diagnostics
{
    public class EventTypeFilter : TraceFilter
    {
        private SourceLevels _level;

        public EventTypeFilter(SourceLevels level)
        {
            _level = level;
        }

        public override bool ShouldTrace(TraceEventCache? cache, string source, TraceEventType eventType, int id, [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string? formatOrMessage,
                                         object?[]? args, object? data1, object?[]? data)
        {
            return ((int)eventType & (int)_level) != 0;
        }

        public SourceLevels EventType
        {
            get
            {
                return _level;
            }
            set
            {
                _level = value;
            }
        }
    }
}
