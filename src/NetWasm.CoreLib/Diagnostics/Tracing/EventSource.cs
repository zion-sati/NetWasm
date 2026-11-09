// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.
// NetWasm adaptation: this is upstream's permanently disabled profile (IsSupported is false and no
// switch can enable it, since NetWasm has no tracing backend). Members keep their upstream disabled
// results. Code that runs only once a source is initialized or enabled (provider registration,
// manifest and descriptor generation, ETW/EventPipe/listener dispatch, activity tracking) is
// omitted because no source can be. GetGuid(Type) and GetName(Type) discover attributes through
// reflection and throw PlatformNotSupportedException after their argument check.
// EventWrittenEventArgs is never produced; its descriptor-backed getters and OSThreadId throw
// PlatformNotSupportedException. Out-of-band error messages have no debugger or listener to reach.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace System.Diagnostics.Tracing
{
    /// <summary>
    /// This class is meant to be inherited by a user-defined event source in order to define a managed
    /// ETW provider.   Please See DESIGN NOTES above for the internal architecture.
    /// The minimal definition of an EventSource simply specifies a number of ETW event methods that
    /// call one of the EventSource.WriteEvent overloads, <see cref="WriteEventCore"/>,
    /// or <see cref="WriteEventWithRelatedActivityIdCore"/> to log them. This functionality
    /// is sufficient for many users.
    /// <para>
    /// To achieve more control over the ETW provider manifest exposed by the event source type, the
    /// [<see cref="EventAttribute"/>] attributes can be specified for the ETW event methods.
    /// </para><para>
    /// For very advanced EventSources, it is possible to intercept the commands being given to the
    /// eventSource and change what filtering is done (see EventListener.EnableEvents and
    /// <see cref="EventListener.DisableEvents"/>) or cause actions to be performed by the eventSource,
    /// e.g. dumping a data structure (see EventSource.SendCommand and
    /// <see cref="OnEventCommand"/>).
    /// </para><para>
    /// The eventSources can be turned on with Windows ETW controllers (e.g. logman), immediately.
    /// It is also possible to control and intercept the data dispatcher programmatically.  See
    /// <see cref="EventListener"/> for more.
    /// </para>
    /// </summary>
    /// <remarks>
    /// This is a minimal definition for a custom event source:
    /// <code>
    /// [EventSource(Name="Samples.Demos.Minimal")]
    /// sealed class MinimalEventSource : EventSource
    /// {
    ///     public static readonly MinimalEventSource Log = new MinimalEventSource();
    ///     public void Load(long ImageBase, string Name) { WriteEvent(1, ImageBase, Name); }
    ///     public void Unload(long ImageBase) { WriteEvent(2, ImageBase); }
    ///     private MinimalEventSource() {}
    /// }
    /// </code>
    /// </remarks>
    public partial class EventSource : IDisposable
    {
        // private instance state
        // Initialization never runs in the disabled profile, so these fields keep their default values.
#pragma warning disable CS0649
        private string m_name = null!;                  // My friendly name (privided in ctor)
        internal int m_id;                              // A small integer that is unique to this instance.
        private Guid m_guid;                            // GUID representing the ETW eventSource to the OS.

        private EventHandler<EventCommandEventArgs>? m_eventCommandExecuted;

        private readonly EventSourceSettings m_config;      // configuration information

        private bool m_eventSourceDisposed;              // has Dispose been called.

        // Enabling bits
        private bool m_eventSourceEnabled;              // am I enabled (any of my events are enabled for any dispatcher)

        // Dispatching state
        internal volatile EventDispatcher? m_Dispatchers;    // Linked list of code:EventDispatchers we write the data to (we also do ETW specially)
        private bool m_completelyInited;                // The EventSource constructor has returned without exception.
        private Exception? m_constructionException;      // If there was an exception construction, this is it
        private EventCommandEventArgs? m_deferredCommands; // If we get commands before we are fully we store them here and run the when we are fully inited.

        private string[]? m_traits;                      // Used to implement GetTraits
#pragma warning restore CS0649

        // Upstream reads the System.Diagnostics.Tracing.EventSource.IsSupported switch; with no tracing
        // backend NetWasm keeps the disabled profile whatever the switch says.
        internal static bool IsSupported => false;

        /// <summary>
        /// The human-friendly name of the eventSource.  It defaults to the simple name of the class
        /// </summary>
        public string Name => m_name;
        /// <summary>
        /// Every eventSource is assigned a GUID to uniquely identify it to the system.
        /// </summary>
        public Guid Guid => m_guid;

        /// <summary>
        /// Returns true if the eventSource has been enabled at all. This is the preferred test
        /// to be performed before a relatively expensive EventSource operation.
        /// </summary>
        public bool IsEnabled()
        {
            return m_eventSourceEnabled;
        }

        /// <summary>
        /// Returns true if events with greater than or equal 'level' and have one of 'keywords' set are enabled.
        ///
        /// Note that the result of this function is only an approximation on whether a particular
        /// event is active or not. It is only meant to be used as way of avoiding expensive
        /// computation for logging when logging is not on, therefore it sometimes returns false
        /// positives (but is always accurate when returning false).  EventSources are free to
        /// have additional filtering.
        /// </summary>
        public bool IsEnabled(EventLevel level, EventKeywords keywords)
        {
            return IsEnabled(level, keywords, EventChannel.None);
        }

        /// <summary>
        /// Returns true if events with greater than or equal 'level' and have one of 'keywords' set are enabled, or
        /// if 'keywords' specifies a channel bit for a channel that is enabled.
        ///
        /// Note that the result of this function only an approximation on whether a particular
        /// event is active or not. It is only meant to be used as way of avoiding expensive
        /// computation for logging when logging is not on, therefore it sometimes returns false
        /// positives (but is always accurate when returning false).  EventSources are free to
        /// have additional filtering.
        /// </summary>
        public bool IsEnabled(EventLevel level, EventKeywords keywords, EventChannel channel)
        {
            // Level, keyword and channel filtering applies only to an enabled source.
            return IsEnabled();
        }

        /// <summary>
        /// Returns the settings for the event source instance
        /// </summary>
        public EventSourceSettings Settings => m_config;

        /// <summary>
        /// Returns the GUID that uniquely identifies the eventSource defined by 'eventSourceType'.
        /// This API allows you to compute this without actually creating an instance of the EventSource.
        /// It only needs to reflect over the type.
        /// </summary>
        public static Guid GetGuid(Type eventSourceType)
        {
            ArgumentNullException.ThrowIfNull(eventSourceType);

            throw new PlatformNotSupportedException(SR.EventSource_TypeDiscoveryNotSupported);
        }

        /// <summary>
        /// Returns the official ETW Provider name for the eventSource defined by 'eventSourceType'.
        /// This API allows you to compute this without actually creating an instance of the EventSource.
        /// It only needs to reflect over the type.
        /// </summary>
        public static string GetName(Type eventSourceType)
        {
            ArgumentNullException.ThrowIfNull(eventSourceType);

            throw new PlatformNotSupportedException(SR.EventSource_TypeDiscoveryNotSupported);
        }

        private const DynamicallyAccessedMemberTypes ManifestMemberTypes =
            DynamicallyAccessedMemberTypes.PublicMethods
            | DynamicallyAccessedMemberTypes.NonPublicMethods
            | DynamicallyAccessedMemberTypes.PublicNestedTypes;

        /// <summary>
        /// Returns a string of the XML manifest associated with the eventSourceType. The scheme for this XML is
        /// documented at in EventManifest Schema https://learn.microsoft.com/windows/desktop/WES/eventmanifestschema-schema.
        /// This is the preferred way of generating a manifest to be embedded in the ETW stream as it is fast and
        /// the fact that it only includes localized entries for the current UI culture is an acceptable tradeoff.
        /// </summary>
        /// <param name="eventSourceType">The type of the event source class for which the manifest is generated</param>
        /// <param name="assemblyPathToIncludeInManifest">The manifest XML fragment contains the string name of the DLL name in
        /// which it is embedded.  This parameter specifies what name will be used</param>
        /// <returns>The XML data string</returns>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2114:ReflectionToDynamicallyAccessedMembers",
            Justification = "EnsureDescriptorsInitialized's use of GetType preserves this method which " +
                            "has dynamically accessed members requirements, but EnsureDescriptorsInitialized does not " +
                            "access this member and is safe to call.")]
        public static string? GenerateManifest(
            [DynamicallyAccessedMembers(ManifestMemberTypes)]
            Type eventSourceType,
            string? assemblyPathToIncludeInManifest)
        {
            return GenerateManifest(eventSourceType, assemblyPathToIncludeInManifest, EventManifestOptions.None);
        }

        /// <summary>
        /// Returns a string of the XML manifest associated with the eventSourceType. The scheme for this XML is
        /// documented at in EventManifest Schema https://learn.microsoft.com/windows/desktop/WES/eventmanifestschema-schema.
        /// Pass EventManifestOptions.AllCultures when generating a manifest to be registered on the machine. This
        /// ensures that the entries in the event log will be "optimally" localized.
        /// </summary>
        /// <param name="eventSourceType">The type of the event source class for which the manifest is generated</param>
        /// <param name="assemblyPathToIncludeInManifest">The manifest XML fragment contains the string name of the DLL name in
        /// which it is embedded.  This parameter specifies what name will be used</param>
        /// <param name="flags">The flags to customize manifest generation. If flags has bit OnlyIfNeededForRegistration specified
        /// this returns null when the eventSourceType does not require explicit registration</param>
        /// <returns>The XML data string or null</returns>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2114:ReflectionToDynamicallyAccessedMembers",
            Justification = "EnsureDescriptorsInitialized's use of GetType preserves this method which " +
                            "has dynamically accessed members requirements, but EnsureDescriptorsInitialized does not " +
                            "access this member and is safe to call.")]
        public static string? GenerateManifest(
            [DynamicallyAccessedMembers(ManifestMemberTypes)]
            Type eventSourceType,
            string? assemblyPathToIncludeInManifest,
            EventManifestOptions flags)
        {
            // Disabled profile: upstream returns null before validating the type.
            return null;
        }

        // EventListener support
        /// <summary>
        /// returns a list (IEnumerable) of all sources in the appdomain).  EventListeners typically need this.
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<EventSource> GetSources()
        {
            return [];
        }

        /// <summary>
        /// Send a command to a particular EventSource identified by 'eventSource'.
        /// Calling this routine simply forwards the command to the EventSource.OnEventCommand
        /// callback.  What the EventSource does with the command and its arguments are from
        /// that point EventSource-specific.
        /// </summary>
        /// <param name="eventSource">The instance of EventSource to send the command to</param>
        /// <param name="command">A positive user-defined EventCommand, or EventCommand.SendManifest</param>
        /// <param name="commandArguments">A set of (name-argument, value-argument) pairs associated with the command</param>
        public static void SendCommand(EventSource eventSource, EventCommand command, IDictionary<string, string?>? commandArguments)
        {
            // Disabled profile: upstream returns before validating the source or command.
        }

        // Error APIs.  (We don't throw by default, but you can probe for status)
        /// <summary>
        /// Because
        ///
        ///     1) Logging is often optional and thus should not generate fatal errors (exceptions)
        ///     2) EventSources are often initialized in class constructors (which propagate exceptions poorly)
        ///
        /// The event source constructor does not throw exceptions.  Instead we remember any exception that
        /// was generated (it is also logged to Trace.WriteLine).
        /// </summary>
        public Exception? ConstructionException => m_constructionException;

        /// <summary>
        /// EventSources can have arbitrary string key-value pairs associated with them called Traits.
        /// These traits are not interpreted by the EventSource but may be interpreted by EventListeners
        /// (e.g. like the built in ETW listener).   These traits are specified at EventSource
        /// construction time and can be retrieved by using this GetTrait API.
        /// </summary>
        /// <param name="key">The key to look up in the set of key-value pairs passed to the EventSource constructor</param>
        /// <returns>The value string associated with key.  Will return null if there is no such key.</returns>
        public string? GetTrait(string key)
        {
            if (m_traits != null)
            {
                for (int i = 0; i < m_traits.Length - 1; i += 2)
                {
                    if (m_traits[i] == key)
                        return m_traits[i + 1];
                }
            }

            return null;
        }

        /// <summary>
        /// Displays the name and GUID for the eventSource for debugging purposes.
        /// </summary>
        public override string ToString()
        {
            return base.ToString()!;
        }

        /// <summary>
        /// Fires when a Command (e.g. Enable) comes from a an EventListener.
        /// </summary>
        public event EventHandler<EventCommandEventArgs>? EventCommandExecuted
        {
            add
            {
                if (value == null)
                    return;

                m_eventCommandExecuted += value;

                if (m_completelyInited)
                {
                    // If we have an EventHandler<EventCommandEventArgs> attached to the EventSource before the first command arrives
                    // It should get a chance to handle the deferred commands.
                    EventCommandEventArgs? deferredCommands = m_deferredCommands;
                    while (deferredCommands != null)
                    {
                        value(this, deferredCommands);
                        deferredCommands = deferredCommands.nextCommand;
                    }
                }
            }
            remove
            {
                m_eventCommandExecuted -= value;
            }
        }

        #region ActivityID

        /// <summary>
        /// When a thread starts work that is on behalf of 'something else' (typically another
        /// thread or network request) it should mark the thread as working on that other work.
        /// This API marks the current thread as working on activity 'activityID'. This API
        /// should be used when the caller knows the thread's current activity (the one being
        /// overwritten) has completed. Otherwise, callers should prefer the overload that
        /// return the oldActivityThatWillContinue (below).
        ///
        /// All events created with the EventSource on this thread are also tagged with the
        /// activity ID of the thread.
        ///
        /// It is common, and good practice after setting the thread to an activity to log an event
        /// with a 'start' opcode to indicate that precise time/thread where the new activity
        /// started.
        /// </summary>
        /// <param name="activityId">A Guid that represents the new activity with which to mark
        /// the current thread</param>
        public static void SetCurrentThreadActivityId(Guid activityId)
        {
            // Disabled profile: there is no activity ID store to update.
        }

        /// <summary>
        /// Retrieves the ETW activity ID associated with the current thread.
        /// </summary>
        public static Guid CurrentThreadActivityId
        {
            get
            {
                return default;
            }
        }

        /// <summary>
        /// When a thread starts work that is on behalf of 'something else' (typically another
        /// thread or network request) it should mark the thread as working on that other work.
        /// This API marks the current thread as working on activity 'activityID'. It returns
        /// whatever activity the thread was previously marked with. There is a convention that
        /// callers can assume that callees restore this activity mark before the callee returns.
        /// To encourage this, this API returns the old activity, so that it can be restored later.
        ///
        /// All events created with the EventSource on this thread are also tagged with the
        /// activity ID of the thread.
        ///
        /// It is common, and good practice after setting the thread to an activity to log an event
        /// with a 'start' opcode to indicate that precise time/thread where the new activity
        /// started.
        /// </summary>
        /// <param name="activityId">A Guid that represents the new activity with which to mark
        /// the current thread</param>
        /// <param name="oldActivityThatWillContinue">The Guid that represents the current activity
        /// which will continue at some point in the future, on the current thread</param>
        public static void SetCurrentThreadActivityId(Guid activityId, out Guid oldActivityThatWillContinue)
        {
            oldActivityThatWillContinue = default;
        }
        #endregion

        #region protected
        /// <summary>
        /// This is the constructor that most users will use to create their eventSource.   It takes
        /// no parameters.  The ETW provider name and GUID of the EventSource are determined by the EventSource
        /// custom attribute (so you can determine these things declaratively).   If the GUID for the eventSource
        /// is not specified in the EventSourceAttribute (recommended), it is Generated by hashing the name.
        /// If the ETW provider name of the EventSource is not given, the name of the EventSource class is used as
        /// the ETW provider name.
        /// </summary>
        protected EventSource()
            : this(EventSourceSettings.EtwManifestEventFormat)
        {
        }

        protected EventSource(bool throwOnEventWriteErrors)
            : this(EventSourceSettings.EtwManifestEventFormat | (throwOnEventWriteErrors ? EventSourceSettings.ThrowOnEventWriteErrors : 0))
        { }

        /// <summary>
        /// Construct an EventSource with additional non-default settings (see EventSourceSettings for more)
        /// </summary>
        protected EventSource(EventSourceSettings settings) : this(settings, null) { }

        /// <summary>
        /// Construct an EventSource with additional non-default settings.
        ///
        /// Also specify a list of key-value pairs called traits (you must pass an even number of strings).
        /// The first string is the key and the second is the value.   These are not interpreted by EventSource
        /// itself but may be interpreted the listeners.  Can be fetched with GetTrait(string).
        /// </summary>
        /// <param name="settings">See EventSourceSettings for more.</param>
        /// <param name="traits">A collection of key-value strings (must be an even number).</param>
        protected EventSource(EventSourceSettings settings, params string[]? traits)
        {
            // Disabled profile: upstream skips settings validation, type discovery and provider registration.
        }

        /// <summary>
        /// This method is called when the eventSource is updated by the controller.
        /// </summary>
        protected virtual void OnEventCommand(EventCommandEventArgs command) { }

#pragma warning disable 1591
        // optimized for common signatures (no args)
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected unsafe void WriteEvent(int eventId)
        {
            WriteEventCore(eventId, 0, null);
        }

        // optimized for common signatures (ints)
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected unsafe void WriteEvent(int eventId, int arg1)
        {
            if (IsEnabled())
            {
                EventData* descrs = stackalloc EventData[1];
                descrs[0].DataPointer = (IntPtr)(&arg1);
                descrs[0].Size = 4;
                descrs[0].Reserved = 0;
                WriteEventCore(eventId, 1, descrs);
            }
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected unsafe void WriteEvent(int eventId, int arg1, int arg2)
        {
            if (IsEnabled())
            {
                EventData* descrs = stackalloc EventData[2];
                descrs[0].DataPointer = (IntPtr)(&arg1);
                descrs[0].Size = 4;
                descrs[0].Reserved = 0;
                descrs[1].DataPointer = (IntPtr)(&arg2);
                descrs[1].Size = 4;
                descrs[1].Reserved = 0;
                WriteEventCore(eventId, 2, descrs);
            }
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected unsafe void WriteEvent(int eventId, int arg1, int arg2, int arg3)
        {
            if (IsEnabled())
            {
                EventData* descrs = stackalloc EventData[3];
                descrs[0].DataPointer = (IntPtr)(&arg1);
                descrs[0].Size = 4;
                descrs[0].Reserved = 0;
                descrs[1].DataPointer = (IntPtr)(&arg2);
                descrs[1].Size = 4;
                descrs[1].Reserved = 0;
                descrs[2].DataPointer = (IntPtr)(&arg3);
                descrs[2].Size = 4;
                descrs[2].Reserved = 0;
                WriteEventCore(eventId, 3, descrs);
            }
        }

        // optimized for common signatures (longs)
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected unsafe void WriteEvent(int eventId, long arg1)
        {
            if (IsEnabled())
            {
                EventData* descrs = stackalloc EventData[1];
                descrs[0].DataPointer = (IntPtr)(&arg1);
                descrs[0].Size = 8;
                descrs[0].Reserved = 0;
                WriteEventCore(eventId, 1, descrs);
            }
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected unsafe void WriteEvent(int eventId, long arg1, long arg2)
        {
            if (IsEnabled())
            {
                EventData* descrs = stackalloc EventData[2];
                descrs[0].DataPointer = (IntPtr)(&arg1);
                descrs[0].Size = 8;
                descrs[0].Reserved = 0;
                descrs[1].DataPointer = (IntPtr)(&arg2);
                descrs[1].Size = 8;
                descrs[1].Reserved = 0;
                WriteEventCore(eventId, 2, descrs);
            }
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected unsafe void WriteEvent(int eventId, long arg1, long arg2, long arg3)
        {
            if (IsEnabled())
            {
                EventData* descrs = stackalloc EventData[3];
                descrs[0].DataPointer = (IntPtr)(&arg1);
                descrs[0].Size = 8;
                descrs[0].Reserved = 0;
                descrs[1].DataPointer = (IntPtr)(&arg2);
                descrs[1].Size = 8;
                descrs[1].Reserved = 0;
                descrs[2].DataPointer = (IntPtr)(&arg3);
                descrs[2].Size = 8;
                descrs[2].Reserved = 0;
                WriteEventCore(eventId, 3, descrs);
            }
        }

        // optimized for common signatures (strings)
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected unsafe void WriteEvent(int eventId, string? arg1)
        {
            if (IsEnabled())
            {
                arg1 ??= "";
                fixed (char* string1Bytes = arg1)
                {
                    EventData* descrs = stackalloc EventData[1];
                    descrs[0].DataPointer = (IntPtr)string1Bytes;
                    descrs[0].Size = ((arg1.Length + 1) * 2);
                    descrs[0].Reserved = 0;
                    WriteEventCore(eventId, 1, descrs);
                }
            }
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected unsafe void WriteEvent(int eventId, string? arg1, string? arg2)
        {
            if (IsEnabled())
            {
                arg1 ??= "";
                arg2 ??= "";
                fixed (char* string1Bytes = arg1)
                fixed (char* string2Bytes = arg2)
                {
                    EventData* descrs = stackalloc EventData[2];
                    descrs[0].DataPointer = (IntPtr)string1Bytes;
                    descrs[0].Size = ((arg1.Length + 1) * 2);
                    descrs[0].Reserved = 0;
                    descrs[1].DataPointer = (IntPtr)string2Bytes;
                    descrs[1].Size = ((arg2.Length + 1) * 2);
                    descrs[1].Reserved = 0;
                    WriteEventCore(eventId, 2, descrs);
                }
            }
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected unsafe void WriteEvent(int eventId, string? arg1, string? arg2, string? arg3)
        {
            if (IsEnabled())
            {
                arg1 ??= "";
                arg2 ??= "";
                arg3 ??= "";
                fixed (char* string1Bytes = arg1)
                fixed (char* string2Bytes = arg2)
                fixed (char* string3Bytes = arg3)
                {
                    EventData* descrs = stackalloc EventData[3];
                    descrs[0].DataPointer = (IntPtr)string1Bytes;
                    descrs[0].Size = ((arg1.Length + 1) * 2);
                    descrs[0].Reserved = 0;
                    descrs[1].DataPointer = (IntPtr)string2Bytes;
                    descrs[1].Size = ((arg2.Length + 1) * 2);
                    descrs[1].Reserved = 0;
                    descrs[2].DataPointer = (IntPtr)string3Bytes;
                    descrs[2].Size = ((arg3.Length + 1) * 2);
                    descrs[2].Reserved = 0;
                    WriteEventCore(eventId, 3, descrs);
                }
            }
        }

        // optimized for common signatures (string and ints)
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected unsafe void WriteEvent(int eventId, string? arg1, int arg2)
        {
            if (IsEnabled())
            {
                arg1 ??= "";
                fixed (char* string1Bytes = arg1)
                {
                    EventData* descrs = stackalloc EventData[2];
                    descrs[0].DataPointer = (IntPtr)string1Bytes;
                    descrs[0].Size = ((arg1.Length + 1) * 2);
                    descrs[0].Reserved = 0;
                    descrs[1].DataPointer = (IntPtr)(&arg2);
                    descrs[1].Size = 4;
                    descrs[1].Reserved = 0;
                    WriteEventCore(eventId, 2, descrs);
                }
            }
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected unsafe void WriteEvent(int eventId, string? arg1, int arg2, int arg3)
        {
            if (IsEnabled())
            {
                arg1 ??= "";
                fixed (char* string1Bytes = arg1)
                {
                    EventData* descrs = stackalloc EventData[3];
                    descrs[0].DataPointer = (IntPtr)string1Bytes;
                    descrs[0].Size = ((arg1.Length + 1) * 2);
                    descrs[0].Reserved = 0;
                    descrs[1].DataPointer = (IntPtr)(&arg2);
                    descrs[1].Size = 4;
                    descrs[1].Reserved = 0;
                    descrs[2].DataPointer = (IntPtr)(&arg3);
                    descrs[2].Size = 4;
                    descrs[2].Reserved = 0;
                    WriteEventCore(eventId, 3, descrs);
                }
            }
        }

        // optimized for common signatures (string and longs)
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected unsafe void WriteEvent(int eventId, string? arg1, long arg2)
        {
            if (IsEnabled())
            {
                arg1 ??= "";
                fixed (char* string1Bytes = arg1)
                {
                    EventData* descrs = stackalloc EventData[2];
                    descrs[0].DataPointer = (IntPtr)string1Bytes;
                    descrs[0].Size = ((arg1.Length + 1) * 2);
                    descrs[0].Reserved = 0;
                    descrs[1].DataPointer = (IntPtr)(&arg2);
                    descrs[1].Size = 8;
                    descrs[1].Reserved = 0;
                    WriteEventCore(eventId, 2, descrs);
                }
            }
        }

        // optimized for common signatures (long and string)
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected unsafe void WriteEvent(int eventId, long arg1, string? arg2)
        {
            if (IsEnabled())
            {
                arg2 ??= "";
                fixed (char* string2Bytes = arg2)
                {
                    EventData* descrs = stackalloc EventData[2];
                    descrs[0].DataPointer = (IntPtr)(&arg1);
                    descrs[0].Size = 8;
                    descrs[0].Reserved = 0;
                    descrs[1].DataPointer = (IntPtr)string2Bytes;
                    descrs[1].Size = ((arg2.Length + 1) * 2);
                    descrs[1].Reserved = 0;
                    WriteEventCore(eventId, 2, descrs);
                }
            }
        }

        // optimized for common signatures (int and string)
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected unsafe void WriteEvent(int eventId, int arg1, string? arg2)
        {
            if (IsEnabled())
            {
                arg2 ??= "";
                fixed (char* string2Bytes = arg2)
                {
                    EventData* descrs = stackalloc EventData[2];
                    descrs[0].DataPointer = (IntPtr)(&arg1);
                    descrs[0].Size = 4;
                    descrs[0].Reserved = 0;
                    descrs[1].DataPointer = (IntPtr)string2Bytes;
                    descrs[1].Size = ((arg2.Length + 1) * 2);
                    descrs[1].Reserved = 0;
                    WriteEventCore(eventId, 2, descrs);
                }
            }
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected unsafe void WriteEvent(int eventId, byte[]? arg1)
        {
            if (IsEnabled())
            {
                EventData* descrs = stackalloc EventData[2];
                if (arg1 == null || arg1.Length == 0)
                {
                    int blobSize = 0;
                    descrs[0].DataPointer = (IntPtr)(&blobSize);
                    descrs[0].Size = 4;
                    descrs[0].Reserved = 0;
                    descrs[1].DataPointer = (IntPtr)(&blobSize); // valid address instead of empty content
                    descrs[1].Size = 0;
                    descrs[1].Reserved = 0;
                    WriteEventCore(eventId, 2, descrs);
                }
                else
                {
                    int blobSize = arg1.Length;
                    fixed (byte* blob = &arg1[0])
                    {
                        descrs[0].DataPointer = (IntPtr)(&blobSize);
                        descrs[0].Size = 4;
                        descrs[0].Reserved = 0;
                        descrs[1].DataPointer = (IntPtr)blob;
                        descrs[1].Size = blobSize;
                        descrs[1].Reserved = 0;
                        WriteEventCore(eventId, 2, descrs);
                    }
                }
            }
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected unsafe void WriteEvent(int eventId, long arg1, byte[]? arg2)
        {
            if (IsEnabled())
            {
                EventData* descrs = stackalloc EventData[3];
                descrs[0].DataPointer = (IntPtr)(&arg1);
                descrs[0].Size = 8;
                descrs[0].Reserved = 0;
                if (arg2 == null || arg2.Length == 0)
                {
                    int blobSize = 0;
                    descrs[1].DataPointer = (IntPtr)(&blobSize);
                    descrs[1].Size = 4;
                    descrs[1].Reserved = 0;
                    descrs[2].DataPointer = (IntPtr)(&blobSize); // valid address instead of empty contents
                    descrs[2].Size = 0;
                    descrs[2].Reserved = 0;
                    WriteEventCore(eventId, 3, descrs);
                }
                else
                {
                    int blobSize = arg2.Length;
                    fixed (byte* blob = &arg2[0])
                    {
                        descrs[1].DataPointer = (IntPtr)(&blobSize);
                        descrs[1].Size = 4;
                        descrs[1].Reserved = 0;
                        descrs[2].DataPointer = (IntPtr)blob;
                        descrs[2].Size = blobSize;
                        descrs[2].Reserved = 0;
                        WriteEventCore(eventId, 3, descrs);
                    }
                }
            }
        }

#pragma warning restore 1591

        /// <summary>
        /// A wrapper type for separating primitive types (int, long, string, etc) from other types
        /// in the EventSource API. This type shouldn't be used directly, but just as implicit conversions
        /// when using the WriteEvent API.
        /// </summary>
        public readonly struct EventSourcePrimitive
        {
            internal readonly object? Value;

            private EventSourcePrimitive(object? value)
            {
                Value = value;
            }
            public static implicit operator EventSourcePrimitive(bool value) => new(value);
            public static implicit operator EventSourcePrimitive(byte value) => new(value);
            public static implicit operator EventSourcePrimitive(short value) => new(value);
            public static implicit operator EventSourcePrimitive(int value) => new(value);
            public static implicit operator EventSourcePrimitive(long value) => new(value);

            [CLSCompliant(false)]
            public static implicit operator EventSourcePrimitive(sbyte value) => new(value);
            [CLSCompliant(false)]
            public static implicit operator EventSourcePrimitive(ushort value) => new(value);
            [CLSCompliant(false)]
            public static implicit operator EventSourcePrimitive(uint value) => new(value);
            [CLSCompliant(false)]
            public static implicit operator EventSourcePrimitive(ulong value) => new(value);
            [CLSCompliant(false)]
            // Added to prevent going through the nuint -> ulong conversion
            public static implicit operator EventSourcePrimitive(nuint value) => new(value);

            public static implicit operator EventSourcePrimitive(float value) => new(value);
            public static implicit operator EventSourcePrimitive(double value) => new(value);
            public static implicit operator EventSourcePrimitive(decimal value) => new(value);

            public static implicit operator EventSourcePrimitive(string? value) => new(value);
            public static implicit operator EventSourcePrimitive(byte[]? value) => new(value);

            public static implicit operator EventSourcePrimitive(Guid value) => new(value);
            public static implicit operator EventSourcePrimitive(DateTime value) => new(value);
            public static implicit operator EventSourcePrimitive(nint value) => new(value);
            public static implicit operator EventSourcePrimitive(char value) => new(value);

            public static implicit operator EventSourcePrimitive(Enum value) => new(value);
        }

        /// <summary>
        /// Used to construct the data structure to be passed to the native ETW APIs - EventWrite and EventWriteTransfer.
        /// </summary>
        protected internal struct EventData
        {
            /// <summary>
            /// Address where the one argument lives (if this points to managed memory you must ensure the
            /// managed object is pinned.
            /// </summary>
            public unsafe IntPtr DataPointer
            {
                get => (IntPtr)(void*)m_Ptr;
                set => m_Ptr = unchecked((ulong)(void*)value);
            }

            /// <summary>
            /// Size of the argument referenced by DataPointer
            /// </summary>
            public int Size
            {
                get => m_Size;
                set => m_Size = value;
            }

            /// <summary>
            /// Reserved by ETW.  This property is present to ensure that we can zero it
            /// since System.Private.CoreLib uses are not zero'd.
            /// </summary>
            internal int Reserved
            {
                get => m_Reserved;
                set => m_Reserved = value;
            }

            #region private
            /// <summary>
            /// Initializes the members of this EventData object to point at a previously-pinned
            /// tracelogging-compatible metadata blob.
            /// </summary>
            /// <param name="pointer">Pinned tracelogging-compatible metadata blob.</param>
            /// <param name="size">The size of the metadata blob.</param>
            /// <param name="reserved">Value for reserved: 2 for per-provider metadata, 1 for per-event metadata</param>
            internal unsafe void SetMetadata(byte* pointer, int size, int reserved)
            {
                this.m_Ptr = (ulong)pointer;
                this.m_Size = size;
                this.m_Reserved = reserved; // Mark this descriptor as containing tracelogging-compatible metadata.
            }

            // Important, we pass this structure directly to the Win32 EventWrite API, so this structure must
            // be laid out exactly the way EventWrite wants it.
            internal ulong m_Ptr;
            internal int m_Size;
#pragma warning disable 0649
            internal int m_Reserved;       // Used to pad the size to match the Win32 API
#pragma warning restore 0649
            #endregion
        }

        /// <summary>
        /// This routine allows you to create efficient WriteEvent helpers, however the code that you use to
        /// do this, while straightforward, is unsafe.
        /// </summary>
        /// <remarks>
        /// <code>
        ///    protected unsafe void WriteEvent(int eventId, string arg1, long arg2)
        ///    {
        ///        if (IsEnabled())
        ///        {
        ///            arg2 ??= "";
        ///            fixed (char* string2Bytes = arg2)
        ///            {
        ///                EventSource.EventData* descrs = stackalloc EventSource.EventData[2];
        ///                descrs[0].DataPointer = (IntPtr)(&amp;arg1);
        ///                descrs[0].Size = 8;
        ///                descrs[0].Reserved = 0;
        ///                descrs[1].DataPointer = (IntPtr)string2Bytes;
        ///                descrs[1].Size = ((arg2.Length + 1) * 2);
        ///                descrs[1].Reserved = 0;
        ///                WriteEventCore(eventId, 2, descrs);
        ///            }
        ///        }
        ///    }
        /// </code>
        /// </remarks>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2112:ReflectionToRequiresUnreferencedCode",
                    Justification = "EnsureDescriptorsInitialized's use of GetType preserves this method which " +
                                    "requires unreferenced code, but EnsureDescriptorsInitialized does not access this member and is safe to call.")]
        [RequiresUnreferencedCode(EventSourceRequiresUnreferenceMessage)]
        [CLSCompliant(false)]
        protected unsafe void WriteEventCore(int eventId, int eventDataCount, EventData* data)
        {
            WriteEventWithRelatedActivityIdCore(eventId, null, eventDataCount, data);
        }

        /// <summary>
        /// This routine allows you to create efficient WriteEventWithRelatedActivityId helpers, however the code
        /// that you use to do this, while straightforward, is unsafe. The only difference from
        /// <see cref="WriteEventCore"/> is that you pass the relatedActivityId from caller through to this API
        /// </summary>
        /// <remarks>
        /// <code>
        ///    protected unsafe void WriteEventWithRelatedActivityId(int eventId, Guid relatedActivityId, string arg1, long arg2)
        ///    {
        ///        if (IsEnabled())
        ///        {
        ///            arg2 ??= "";
        ///            fixed (char* string2Bytes = arg2)
        ///            {
        ///                EventSource.EventData* descrs = stackalloc EventSource.EventData[2];
        ///                descrs[0].DataPointer = (IntPtr)(&amp;arg1);
        ///                descrs[0].Size = 8;
        ///                descrs[1].DataPointer = (IntPtr)string2Bytes;
        ///                descrs[1].Size = ((arg2.Length + 1) * 2);
        ///                WriteEventWithRelatedActivityIdCore(eventId, relatedActivityId, 2, descrs);
        ///            }
        ///        }
        ///    }
        /// </code>
        /// </remarks>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2112:ReflectionToRequiresUnreferencedCode",
                    Justification = "EnsureDescriptorsInitialized's use of GetType preserves this method which " +
                                    "requires unreferenced code, but EnsureDescriptorsInitialized does not access this member and is safe to call.")]
        [RequiresUnreferencedCode(EventSourceRequiresUnreferenceMessage)]
        [CLSCompliant(false)]
        protected unsafe void WriteEventWithRelatedActivityIdCore(int eventId, Guid* relatedActivityId, int eventDataCount, EventData* data)
        {
            // Dispatch runs only for an enabled source, and no source can be enabled.
        }

        /// <summary>
        /// This is a varargs helper for writing an event. It does create an array and box all the arguments so it is
        /// relatively inefficient and should only be used for relatively rare events (e.g. less than 100 / sec). If your
        /// rates are faster than that you should use <see cref="WriteEventCore"/> to create fast helpers for your particular
        /// method signature. Even if you use this for rare events, this call should be guarded by an <see cref="IsEnabled()"/>
        /// check so that the varargs call is not made when the EventSource is not active.
        /// </summary>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        protected void WriteEvent(int eventId, params EventSourcePrimitive[] args)
        {
            var argValues = new object?[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                argValues[i] = args[i].Value;
            }
            unsafe
            {
                WriteEventVarargs(eventId, null, argValues);
            }
        }

        /// <summary>
        /// This is the varargs helper for writing an event. It does create an array and box all the arguments so it is
        /// relatively inefficient and should only be used for relatively rare events (e.g. less than 100 / sec). If your
        /// rates are faster than that you should use <see cref="WriteEventCore"/> to create fast helpers for your particular
        /// method signature. Even if you use this for rare events, this call should be guarded by an <see cref="IsEnabled()"/>
        /// check so that the varargs call is not made when the EventSource is not active.
        /// </summary>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2112:ReflectionToRequiresUnreferencedCode",
                    Justification = "EnsureDescriptorsInitialized's use of GetType preserves this method which " +
                                    "requires unreferenced code, but EnsureDescriptorsInitialized does not access this member and is safe to call.")]
        [RequiresUnreferencedCode(EventSourceRequiresUnreferenceMessage)]
        protected unsafe void WriteEvent(int eventId, params object?[] args)
        {
            WriteEventVarargs(eventId, null, args);
        }

        /// <summary>
        /// This is the varargs helper for writing an event which also specifies a related activity. It is completely analogous
        /// to corresponding WriteEvent (they share implementation). It does create an array and box all the arguments so it is
        /// relatively inefficient and should only be used for relatively rare events (e.g. less than 100 / sec).  If your
        /// rates are faster than that you should use <see cref="WriteEventWithRelatedActivityIdCore"/> to create fast helpers for your
        /// particular method signature. Even if you use this for rare events, this call should be guarded by an <see cref="IsEnabled()"/>
        /// check so that the varargs call is not made when the EventSource is not active.
        /// </summary>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2112:ReflectionToRequiresUnreferencedCode",
                    Justification = "EnsureDescriptorsInitialized's use of GetType preserves this method which " +
                                    "requires unreferenced code, but EnsureDescriptorsInitialized does not access this member and is safe to call.")]
        [RequiresUnreferencedCode(EventSourceRequiresUnreferenceMessage)]
        protected unsafe void WriteEventWithRelatedActivityId(int eventId, Guid relatedActivityId, params object?[] args)
        {
            WriteEventVarargs(eventId, &relatedActivityId, args);
        }

        #endregion

        #region IDisposable Members
        /// <summary>
        /// Disposes of an EventSource.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }
        /// <summary>
        /// Disposes of an EventSource.
        /// </summary>
        /// <remarks>
        /// Called from Dispose() with disposing=true, and from the finalizer (~EventSource) with disposing=false.
        /// Guidelines:
        /// 1. We may be called more than once: do nothing after the first call.
        /// 2. Avoid throwing exceptions if disposing is false, i.e. if we're being finalized.
        /// </remarks>
        /// <param name="disposing">True if called from Dispose(), false if called from the finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            // Disabled profile: upstream returns before recording disposal.
        }
        /// <summary>
        /// Finalizer for EventSource
        /// </summary>
        ~EventSource()
        {
            this.Dispose(false);
        }
        #endregion

        #region private

        /// <summary>
        /// Construct an EventSource with a given name for non-contract based events (e.g. those using the Write() API)
        /// and a given Guid to identify the source.
        /// </summary>
        /// <param name="eventSourceName">
        /// The name of the event source. Must not be null.
        /// </param>
        /// <param name="eventSourceGuid">
        /// The unique identifier for the event source. Must not be Guid.Empty.
        /// </param>
        public EventSource(string eventSourceName, Guid eventSourceGuid)
            : this(eventSourceName, eventSourceGuid, EventSourceSettings.EtwManifestEventFormat)
        { }

        /// <summary>
        /// Construct an EventSource with a given name for non-contract based events (e.g. those using the Write() API)
        /// and a given Guid to identify the source.
        ///
        /// Also specify a list of key-value pairs called traits (you must pass an even number of strings).
        /// The first string is the key and the second is the value.   These are not interpreted by EventSource
        /// itself but may be interpreted the listeners.  Can be fetched with GetTrait(string).
        /// </summary>
        /// <param name="eventSourceName">
        /// The name of the event source. Must not be null.
        /// </param>
        /// <param name="eventSourceGuid">
        /// The unique identifier for the event source. Must not be Guid.Empty.
        /// </param>
        /// <param name="settings">
        /// Configuration options for the EventSource as a whole.
        /// </param>
        /// <param name="traits">A collection of key-value strings (must be an even number).</param>
        public EventSource(string eventSourceName, Guid eventSourceGuid, EventSourceSettings settings, string[]? traits = null)
        {
            // Disabled profile: upstream skips settings validation and initialization.
        }

        private static unsafe Guid GenerateGuidFromName(string name)
        {
            ReadOnlySpan<byte> namespaceBytes =
            [
                0x48, 0x2C, 0x2D, 0xB2, 0xC3, 0x90, 0x47, 0xC8,
                0x87, 0xF8, 0x1A, 0x15, 0xBF, 0xC1, 0x30, 0xFB,
            ];

            int nameByteCount = Encoding.BigEndianUnicode.GetByteCount(name);
            int totalLength = namespaceBytes.Length + nameByteCount;
            Span<byte> source = totalLength <= 256 ? stackalloc byte[totalLength] : new byte[totalLength];

            namespaceBytes.CopyTo(source);
            Encoding.BigEndianUnicode.GetBytes(name, source.Slice(namespaceBytes.Length));

            Span<byte> bytes = stackalloc byte[20];
            Sha1ForNonSecretPurposes.HashData(source, bytes);

            bytes[7] = unchecked((byte)((bytes[7] & 0x0F) | 0x50));    // Set high 4 bits of octet 7 to 5, as per RFC 4122
            return new Guid(bytes.Slice(0, 16));
        }

        internal bool IsDisposed => m_eventSourceDisposed;

        // Disabled profile: the overload EventListener uses returns before building a command.
        internal void SendCommand(EventListener? listener, EventProviderType eventProviderType, int perEventSourceSessionId,
                                  EventCommand command, bool enable,
                                  EventLevel level, EventKeywords matchAnyKeyword,
                                  IDictionary<string, string?>? commandArguments)
        {
        }

        // Disabled profile: no dispatcher or descriptor exists to enable.
        internal bool EnableEventForDispatcher(EventDispatcher? dispatcher, EventProviderType eventProviderType, int eventId, bool value)
        {
            return false;
        }

        // Helper used by code:EventListener.AddEventSource and code:EventListener.EventListener
        // when a listener gets attached to a eventSource
        internal void AddListener(EventListener listener)
        {
            lock (EventListener.EventListenersLock)
            {
                // A disabled source has no event descriptors, so the per-event enable table stays null.
                m_Dispatchers = new EventDispatcher(m_Dispatchers, null, listener);
                listener.OnEventSourceCreated(this);
            }
        }

        // Varargs writes dispatch only for an enabled source, and no source can be enabled.
        private unsafe void WriteEventVarargs(int eventId, Guid* childActivityID, object?[] args)
        {
        }

        // Upstream sends the message to the debugger and to listeners; a disabled source has neither.
        internal void ReportOutOfBandMessage(string msg)
        {
        }
        #endregion
    }

    /// <summary>
    /// Enables specifying event source configuration options to be used in the EventSource constructor.
    /// </summary>
    [Flags]
    public enum EventSourceSettings
    {
        /// <summary>
        /// This specifies none of the special configuration options should be enabled.
        /// </summary>
        Default = 0,
        /// <summary>
        /// Normally an EventSource NEVER throws; setting this option will tell it to throw when it encounters errors.
        /// </summary>
        ThrowOnEventWriteErrors = 1,
        /// <summary>
        /// Setting this option is a directive to the ETW listener should use manifest-based format when
        /// firing events. This is the default option when defining a type derived from EventSource
        /// (using the protected EventSource constructors).
        /// Only one of EtwManifestEventFormat or EtwSelfDescribingEventFormat should be specified
        /// </summary>
        EtwManifestEventFormat = 4,
        /// <summary>
        /// Setting this option is a directive to the ETW listener should use self-describing event format
        /// when firing events. This is the default option when creating a new instance of the EventSource
        /// type (using the public EventSource constructors).
        /// Only one of EtwManifestEventFormat or EtwSelfDescribingEventFormat should be specified
        /// </summary>
        EtwSelfDescribingEventFormat = 8,
    }

    /// <summary>
    /// Passed to the code:EventSource.OnEventCommand callback
    /// </summary>
    public class EventCommandEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the command for the callback.
        /// </summary>
        public EventCommand Command { get; internal set; }

        /// <summary>
        /// Gets the arguments for the callback.
        /// </summary>
        public IDictionary<string, string?>? Arguments { get; internal set; }

        /// <summary>
        /// Enables the event that has the specified identifier.
        /// </summary>
        /// <param name="eventId">Event ID of event to be enabled</param>
        /// <returns>true if eventId is in range</returns>
        public bool EnableEvent(int eventId)
        {
            if (Command != EventCommand.Enable && Command != EventCommand.Disable)
                throw new InvalidOperationException();
            return eventSource.EnableEventForDispatcher(dispatcher, eventProviderType, eventId, true);
        }

        /// <summary>
        /// Disables the event that have the specified identifier.
        /// </summary>
        /// <param name="eventId">Event ID of event to be disabled</param>
        /// <returns>true if eventId is in range</returns>
        public bool DisableEvent(int eventId)
        {
            if (Command != EventCommand.Enable && Command != EventCommand.Disable)
                throw new InvalidOperationException();
            return eventSource.EnableEventForDispatcher(dispatcher, eventProviderType, eventId, false);
        }

        #region private

        internal EventCommandEventArgs(EventCommand command, IDictionary<string, string?>? arguments, EventSource eventSource,
            EventListener? listener, EventProviderType eventProviderType, int perEventSourceSessionId, bool enable, EventLevel level, EventKeywords matchAnyKeyword)
        {
            this.Command = command;
            this.Arguments = arguments;
            this.eventSource = eventSource;
            this.listener = listener;
            this.eventProviderType = eventProviderType;
            this.perEventSourceSessionId = perEventSourceSessionId;
            this.enable = enable;
            this.level = level;
            this.matchAnyKeyword = matchAnyKeyword;
        }

        internal EventSource eventSource;
        internal EventDispatcher? dispatcher;
        internal EventProviderType eventProviderType;

        // These are the arguments of sendCommand and are only used for deferring commands until after we are fully initialized.
        internal EventListener? listener;
        internal int perEventSourceSessionId;
        internal bool enable;
        internal EventLevel level;
        internal EventKeywords matchAnyKeyword;
        internal EventCommandEventArgs? nextCommand;     // We form a linked list of these deferred commands.

        #endregion
    }

    /// <summary>
    /// EventSourceCreatedEventArgs is passed to <see cref="EventListener.EventSourceCreated"/>
    /// </summary>
    public class EventSourceCreatedEventArgs : EventArgs
    {
        /// <summary>
        /// The EventSource that is attaching to the listener.
        /// </summary>
        public EventSource? EventSource
        {
            get;
            internal set;
        }
    }

    /// <summary>
    /// EventWrittenEventArgs is passed to the user-provided override for
    /// <see cref="EventListener.OnEventWritten"/> when an event is fired.
    /// </summary>
    public class EventWrittenEventArgs : EventArgs
    {
        /// <summary>
        /// The name of the event.
        /// </summary>
        public string? EventName
        {
            get => _moreInfo?.EventName ?? (EventId <= 0 ? null : throw NoEventMetadata());
            internal set => MoreInfo.EventName = value;
        }

        /// <summary>
        /// Gets the event ID for the event that was written.
        /// </summary>
        public int EventId { get; }

        private Guid _activityId;

        /// <summary>
        /// Gets the activity ID for the thread on which the event was written.
        /// </summary>
        public Guid ActivityId
        {
            get
            {
                if (_activityId == Guid.Empty)
                {
                    _activityId = EventSource.CurrentThreadActivityId;
                }

                return _activityId;
            }
        }

        /// <summary>
        /// Gets the related activity ID if one was specified when the event was written.
        /// </summary>
        public Guid RelatedActivityId => _moreInfo?.RelatedActivityId ?? default;

        /// <summary>
        /// Gets the payload for the event.
        /// </summary>
        public ReadOnlyCollection<object?>? Payload { get; internal set; }

        /// <summary>
        /// Gets the payload argument names.
        /// </summary>
        public ReadOnlyCollection<string>? PayloadNames
        {
            get => _moreInfo?.PayloadNames ?? (EventId <= 0 ? null : throw NoEventMetadata());
            internal set => MoreInfo.PayloadNames = value;
        }

        /// <summary>
        /// Gets the event source object.
        /// </summary>
        public EventSource EventSource { get; }

        /// <summary>
        /// Gets the keywords for the event.
        /// </summary>
        public EventKeywords Keywords
        {
            get => EventId <= 0 ? (_moreInfo?.Keywords ?? default) : throw NoEventMetadata();
            internal set => MoreInfo.Keywords = value;
        }

        /// <summary>
        /// Gets the operation code for the event.
        /// </summary>
        public EventOpcode Opcode
        {
            get => EventId <= 0 ? (_moreInfo?.Opcode ?? default) : throw NoEventMetadata();
            internal set => MoreInfo.Opcode = value;
        }

        /// <summary>
        /// Gets the task for the event.
        /// </summary>
        public EventTask Task => EventId <= 0 ? EventTask.None : throw NoEventMetadata();

        /// <summary>
        /// Any provider/user defined options associated with the event.
        /// </summary>
        public EventTags Tags
        {
            get => EventId <= 0 ? (_moreInfo?.Tags ?? default) : throw NoEventMetadata();
            internal set => MoreInfo.Tags = value;
        }

        /// <summary>
        /// Gets the message for the event.  If the message has {N} parameters they are NOT substituted.
        /// </summary>
        public string? Message
        {
            get => _moreInfo?.Message ?? (EventId <= 0 ? null : throw NoEventMetadata());
            internal set => MoreInfo.Message = value;
        }

        /// <summary>
        /// Gets the channel for the event.
        /// </summary>
        public EventChannel Channel => EventId <= 0 ? EventChannel.None : throw NoEventMetadata();

        /// <summary>
        /// Gets the version of the event.
        /// </summary>
        public byte Version => EventId <= 0 ? (byte)0 : throw NoEventMetadata();

        /// <summary>
        /// Gets the level for the event.
        /// </summary>
        public EventLevel Level
        {
            get => EventId <= 0 ? (_moreInfo?.Level ?? default) : throw NoEventMetadata();
            internal set => MoreInfo.Level = value;
        }

        /// <summary>
        /// Gets the identifier for the OS thread that wrote the event.
        /// </summary>
        public long OSThreadId
        {
            get => MoreInfo.OsThreadId ?? throw new PlatformNotSupportedException(SR.EventSource_OSThreadIdNotSupported);
            internal set => MoreInfo.OsThreadId = value;
        }

        /// <summary>
        /// Gets a UTC DateTime that specifies when the event was written.
        /// </summary>
        public DateTime TimeStamp { get; internal set; }

        internal EventWrittenEventArgs(EventSource eventSource, int eventId)
        {
            EventSource = eventSource;
            EventId = eventId;
            TimeStamp = DateTime.UtcNow;
        }

        // Provider descriptors back positive event IDs, and the disabled profile has none.
        private static PlatformNotSupportedException NoEventMetadata() =>
            new PlatformNotSupportedException(SR.EventSource_EventMetadataNotSupported);

        private MoreEventInfo? _moreInfo;
        private MoreEventInfo MoreInfo => _moreInfo ??= new MoreEventInfo();

        private sealed class MoreEventInfo
        {
            public string? Message;
            public string? EventName;
            public ReadOnlyCollection<string>? PayloadNames;
#pragma warning disable CS0649 // Only the dispatch-time constructor, absent from the disabled profile, sets it.
            public Guid RelatedActivityId;
#pragma warning restore CS0649
            public long? OsThreadId;
            public EventTags Tags;
            public EventOpcode Opcode;
            public EventLevel Level;
            public EventKeywords Keywords;
        }
    }

    /// <summary>
    /// Allows customizing defaults and specifying localization support for the event source class to which it is applied.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class EventSourceAttribute : Attribute
    {
        /// <summary>
        /// Overrides the ETW name of the event source (which defaults to the class name)
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Overrides the default (calculated) Guid of an EventSource type. Explicitly defining a GUID is discouraged,
        /// except when upgrading existing ETW providers to using event sources.
        /// </summary>
        public string? Guid { get; set; }

        /// <summary>
        /// <para>
        /// EventSources support localization of events. The names used for events, opcodes, tasks, keywords and maps
        /// can be localized to several languages if desired. This works by creating a ResX style string table
        /// (by simply adding a 'Resource File' to your project). This resource file is given a name e.g.
        /// 'DefaultNameSpace.ResourceFileName' which can be passed to the ResourceManager constructor to read the
        /// resources. This name is the value of the LocalizationResources property.
        /// </para><para>
        /// If LocalizationResources property is non-null, then EventSource will look up the localized strings for events by
        /// using the following resource naming scheme
        /// </para>
        ///     <para>* event_EVENTNAME</para>
        ///     <para>* task_TASKNAME</para>
        ///     <para>* keyword_KEYWORDNAME</para>
        ///     <para>* map_MAPNAME</para>
        /// <para>
        /// where the capitalized name is the name of the event, task, keyword, or map value that should be localized.
        /// Note that the localized string for an event corresponds to the Message string, and can have {0} values
        /// which represent the payload values.
        /// </para>
        /// </summary>
        public string? LocalizationResources { get; set; }
    }

    /// <summary>
    /// Any instance methods in a class that subclasses <see cref="EventSource"/> and that return void are
    /// assumed by default to be methods that generate an ETW event. Enough information can be deduced from the
    /// name of the method and its signature to generate basic schema information for the event. The
    /// <see cref="EventAttribute"/> class allows you to specify additional event schema information for an event if
    /// desired.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class EventAttribute : Attribute
    {
        /// <summary>Construct an EventAttribute with specified eventId</summary>
        /// <param name="eventId">ID of the ETW event (an integer between 1 and 65535)</param>
        public EventAttribute(int eventId)
        {
            EventId = eventId;
            Level = EventLevel.Informational;
        }

        /// <summary>Event's ID</summary>
        public int EventId { get; }
        /// <summary>Event's severity level: indicates the severity or verbosity of the event</summary>
        public EventLevel Level { get; set; }
        /// <summary>Event's keywords: allows classification of events by "categories"</summary>
        public EventKeywords Keywords { get; set; }
        /// <summary>Event's operation code: allows defining operations, generally used with Tasks</summary>
        public EventOpcode Opcode
        {
            get => m_opcode;
            set
            {
                m_opcode = value;
                m_opcodeSet = true;
            }
        }

        internal bool IsOpcodeSet => m_opcodeSet;

        /// <summary>Event's task: allows logical grouping of events</summary>
        public EventTask Task { get; set; }

        /// <summary>Event's channel: defines an event log as an additional destination for the event</summary>
        public EventChannel Channel { get; set; }

        /// <summary>Event's version</summary>
        public byte Version { get; set; }

        /// <summary>
        /// This can be specified to enable formatting and localization of the event's payload. You can
        /// use standard .NET substitution operators (eg {1}) in the string and they will be replaced
        /// with the 'ToString()' of the corresponding part of the  event payload.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// User defined options associated with the event.  These do not have meaning to the EventSource but
        /// are passed through to listeners which given them semantics.
        /// </summary>
        public EventTags Tags { get; set; }

        /// <summary>
        /// Allows fine control over the Activity IDs generated by start and stop events
        /// </summary>
        public EventActivityOptions ActivityOptions { get; set; }

        #region private
        private EventOpcode m_opcode;
        private bool m_opcodeSet;
        #endregion
    }

    /// <summary>
    /// By default all instance methods in a class that subclasses code:EventSource that and return
    /// void are assumed to be methods that generate an event. This default can be overridden by specifying
    /// the code:NonEventAttribute
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class NonEventAttribute : Attribute
    {
        /// <summary>
        /// Constructs a default NonEventAttribute
        /// </summary>
        public NonEventAttribute() { }
    }

    /// <summary>
    /// Describes the pre-defined command (EventCommandEventArgs.Command property) that is passed to the OnEventCommand callback.
    /// </summary>
    public enum EventCommand
    {
        /// <summary>
        /// Update EventSource state
        /// </summary>
        Update = 0,
        /// <summary>
        /// Request EventSource to generate and send its manifest
        /// </summary>
        SendManifest = -1,
        /// <summary>
        /// Enable event
        /// </summary>
        Enable = -2,
        /// <summary>
        /// Disable event
        /// </summary>
        Disable = -3
    }

    /// <summary>
    /// code:EventDispatchers are a simple 'helper' structure that holds the filtering state
    /// (m_EventEnabled) for a particular EventSource X EventListener tuple
    ///
    /// Thus a single EventListener may have many EventDispatchers (one for every EventSource
    /// that EventListener has activate) and a Single EventSource may also have many
    /// event Dispatchers (one for every EventListener that has activated it).
    ///
    /// Logically a particular EventDispatcher belongs to exactly one EventSource and exactly
    /// one EventListener (although EventDispatcher does not 'remember' the EventSource it is
    /// associated with.
    /// </summary>
    internal sealed class EventDispatcher
    {
        internal EventDispatcher(EventDispatcher? next, Dictionary<int, bool>? eventEnabled, EventListener listener)
        {
            m_Next = next;
            m_EventEnabled = eventEnabled;
            m_Listener = listener;
        }

        // Instance fields
        internal readonly EventListener m_Listener;   // The dispatcher this entry is for
        internal Dictionary<int, bool>? m_EventEnabled;              // For every event in a the eventSource, is it enabled?

        // Only guaranteed to exist after a EnsureInit()
        internal EventDispatcher? m_Next;              // These form a linked list in code:EventSource.m_Dispatchers
        // Of all listeners for that eventSource.
    }

    /// <summary>
    /// Flags that can be used with EventSource.GenerateManifest to control how the ETW manifest for the EventSource is
    /// generated.
    /// </summary>
    [Flags]
    public enum EventManifestOptions
    {
        /// <summary>
        /// Only the resources associated with current UI culture are included in the  manifest
        /// </summary>
        None = 0x0,
        /// <summary>
        /// Throw exceptions for any inconsistency encountered
        /// </summary>
        Strict = 0x1,
        /// <summary>
        /// Generate a "resources" node under "localization" for every satellite assembly provided
        /// </summary>
        AllCultures = 0x2,
        /// <summary>
        /// Generate the manifest only if the event source needs to be registered on the machine,
        /// otherwise return null (but still perform validation if Strict is specified)
        /// </summary>
        OnlyIfNeededForRegistration = 0x4,
        /// <summary>
        /// When generating the manifest do *not* enforce the rule that the current EventSource class
        /// must be the base class for the user-defined type passed in. This allows validation of .net
        /// event sources using the new validation code
        /// </summary>
        AllowEventSourceOverride = 0x8,
    }

}
