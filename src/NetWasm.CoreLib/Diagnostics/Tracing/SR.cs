// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce resources;
// only the strings this namespace references are included. The NetWasm strings below them
// describe the disabled EventSource profile's unsupported boundaries.

namespace System.Diagnostics.Tracing
{
    internal static class SR
    {
        internal const string EventSource_ListenerCreatedInsideCallback =
            "Creating an EventListener inside a EventListener callback.";

        internal const string EventSource_TypeDiscoveryNotSupported =
            "EventSource name and GUID discovery reads the type's attributes through reflection, which NetWasm does not support.";
        internal const string EventSource_EventMetadataNotSupported =
            "Event metadata comes from an enabled EventSource, and NetWasm's EventSource is permanently disabled.";
        internal const string EventSource_OSThreadIdNotSupported =
            "NetWasm does not expose operating system thread identifiers.";
    }
}
