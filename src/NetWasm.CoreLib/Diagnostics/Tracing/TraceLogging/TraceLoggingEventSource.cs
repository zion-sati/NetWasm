// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: only the named constructors and Write entry points of the disabled profile
// are kept. Each Write checks IsEnabled before touching payload metadata, and no source can be
// enabled, so payload analysis and dispatch are omitted; payload getters are never invoked.

using System.Diagnostics.CodeAnalysis;

namespace System.Diagnostics.Tracing
{
    public partial class EventSource
    {
        private const string EventSourceRequiresUnreferenceMessage = "EventSource will serialize the whole object graph. Trimmer will not safely handle this case because properties may be trimmed. This can be suppressed if the object is a primitive type";
        private const string EventSourceSuppressMessage = "Parameters to this method are primitive and are trimmer safe";

        /// <summary>
        /// Construct an EventSource with a given name for non-contract based events (e.g. those using the Write() API).
        /// </summary>
        /// <param name="eventSourceName">
        /// The name of the event source. Must not be null.
        /// </param>
        public EventSource(
            string eventSourceName)
            : this(eventSourceName, EventSourceSettings.EtwSelfDescribingEventFormat)
        { }

        /// <summary>
        /// Construct an EventSource with a given name for non-contract based events (e.g. those using the Write() API).
        /// </summary>
        /// <param name="eventSourceName">
        /// The name of the event source. Must not be null.
        /// </param>
        /// <param name="config">
        /// Configuration options for the EventSource as a whole.
        /// </param>
        public EventSource(
            string eventSourceName,
            EventSourceSettings config)
            : this(eventSourceName, config, null) { }

        /// <summary>
        /// Construct an EventSource with a given name for non-contract based events (e.g. those using the Write() API).
        ///
        /// Also specify a list of key-value pairs called traits (you must pass an even number of strings).
        /// The first string is the key and the second is the value.   These are not interpreted by EventSource
        /// itself but may be interpreted the listeners.  Can be fetched with GetTrait(string).
        /// </summary>
        /// <param name="eventSourceName">
        /// The name of the event source. Must not be null.
        /// </param>
        /// <param name="config">
        /// Configuration options for the EventSource as a whole.
        /// </param>
        /// <param name="traits">A collection of key-value strings (must be an even number).</param>
        public EventSource(
            string eventSourceName,
            EventSourceSettings config,
            params string[]? traits)
            : this(
                eventSourceName,
                GenerateGuidFromName((eventSourceName ?? throw new ArgumentNullException(nameof(eventSourceName))).ToUpperInvariant()),
                config, traits)
        {
        }

        /// <summary>
        /// Writes an event with no fields and default options.
        /// (Native API: EventWriteTransfer)
        /// </summary>
        /// <param name="eventName">The name of the event.</param>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        public unsafe void Write(string? eventName)
        {
            // Payload metadata and dispatch run only for an enabled source, and no source can be enabled.
        }

        /// <summary>
        /// Writes an event with no fields.
        /// (Native API: EventWriteTransfer)
        /// </summary>
        /// <param name="eventName">The name of the event.</param>
        /// <param name="options">
        /// Options for the event, such as the level, keywords, and opcode. Unset
        /// options will be set to default values.
        /// </param>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                   Justification = EventSourceSuppressMessage)]
        public unsafe void Write(string? eventName, EventSourceOptions options)
        {
            // Payload metadata and dispatch run only for an enabled source, and no source can be enabled.
        }

        /// <summary>
        /// Writes an event.
        /// (Native API: EventWriteTransfer)
        /// </summary>
        /// <typeparam name="T">
        /// The type that defines the event and its payload. This must be an
        /// anonymous type or a type with an [EventData] attribute.
        /// </typeparam>
        /// <param name="eventName">
        /// The name for the event. If null, the event name is automatically
        /// determined based on T, either from the Name property of T's EventData
        /// attribute or from typeof(T).Name.
        /// </param>
        /// <param name="data">
        /// The object containing the event payload data. The type T must be
        /// an anonymous type or a type with an [EventData] attribute. The
        /// public instance properties of data will be written recursively to
        /// create the fields of the event.
        /// </param>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2112:ReflectionToRequiresUnreferencedCode",
                    Justification = "EnsureDescriptorsInitialized's use of GetType preserves this method which " +
                                    "requires unreferenced code, but EnsureDescriptorsInitialized does not access this member and is safe to call.")]
        [RequiresUnreferencedCode(EventSourceRequiresUnreferenceMessage)]
        public unsafe void Write<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(
            string? eventName,
            T data)
        {
            // Payload metadata and dispatch run only for an enabled source, and no source can be enabled.
        }

        /// <summary>
        /// Writes an event.
        /// (Native API: EventWriteTransfer)
        /// </summary>
        /// <typeparam name="T">
        /// The type that defines the event and its payload. This must be an
        /// anonymous type or a type with an [EventData] attribute.
        /// </typeparam>
        /// <param name="eventName">
        /// The name for the event. If null, the event name is automatically
        /// determined based on T, either from the Name property of T's EventData
        /// attribute or from typeof(T).Name.
        /// </param>
        /// <param name="options">
        /// Options for the event, such as the level, keywords, and opcode. Unset
        /// options will be set to default values.
        /// </param>
        /// <param name="data">
        /// The object containing the event payload data. The type T must be
        /// an anonymous type or a type with an [EventData] attribute. The
        /// public instance properties of data will be written recursively to
        /// create the fields of the event.
        /// </param>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2112:ReflectionToRequiresUnreferencedCode",
                    Justification = "EnsureDescriptorsInitialized's use of GetType preserves this method which " +
                                    "requires unreferenced code, but EnsureDescriptorsInitialized does not access this member and is safe to call.")]
        [RequiresUnreferencedCode(EventSourceRequiresUnreferenceMessage)]
        public unsafe void Write<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(
            string? eventName,
            EventSourceOptions options,
            T data)
        {
            // Payload metadata and dispatch run only for an enabled source, and no source can be enabled.
        }

        /// <summary>
        /// Writes an event.
        /// This overload is for use with extension methods that wish to efficiently
        /// forward the options or data parameter without performing an extra copy.
        /// (Native API: EventWriteTransfer)
        /// </summary>
        /// <typeparam name="T">
        /// The type that defines the event and its payload. This must be an
        /// anonymous type or a type with an [EventData] attribute.
        /// </typeparam>
        /// <param name="eventName">
        /// The name for the event. If null, the event name is automatically
        /// determined based on T, either from the Name property of T's EventData
        /// attribute or from typeof(T).Name.
        /// </param>
        /// <param name="options">
        /// Options for the event, such as the level, keywords, and opcode. Unset
        /// options will be set to default values.
        /// </param>
        /// <param name="data">
        /// The object containing the event payload data. The type T must be
        /// an anonymous type or a type with an [EventData] attribute. The
        /// public instance properties of data will be written recursively to
        /// create the fields of the event.
        /// </param>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2112:ReflectionToRequiresUnreferencedCode",
                    Justification = "EnsureDescriptorsInitialized's use of GetType preserves this method which " +
                                    "requires unreferenced code, but EnsureDescriptorsInitialized does not access this member and is safe to call.")]
        [RequiresUnreferencedCode(EventSourceRequiresUnreferenceMessage)]
        public unsafe void Write<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(
            string? eventName,
            ref EventSourceOptions options,
            ref T data)
        {
            // Payload metadata and dispatch run only for an enabled source, and no source can be enabled.
        }

        /// <summary>
        /// Writes an event.
        /// This overload is meant for clients that need to manipuate the activityId
        /// and related ActivityId for the event.
        /// </summary>
        /// <typeparam name="T">
        /// The type that defines the event and its payload. This must be an
        /// anonymous type or a type with an [EventData] attribute.
        /// </typeparam>
        /// <param name="eventName">
        /// The name for the event. If null, the event name is automatically
        /// determined based on T, either from the Name property of T's EventData
        /// attribute or from typeof(T).Name.
        /// </param>
        /// <param name="options">
        /// Options for the event, such as the level, keywords, and opcode. Unset
        /// options will be set to default values.
        /// </param>
        /// <param name="activityId">
        /// The GUID of the activity associated with this event.
        /// </param>
        /// <param name="relatedActivityId">
        /// The GUID of another activity that is related to this activity, or Guid.Empty
        /// if there is no related activity. Most commonly, the Start operation of a
        /// new activity specifies a parent activity as its related activity.
        /// </param>
        /// <param name="data">
        /// The object containing the event payload data. The type T must be
        /// an anonymous type or a type with an [EventData] attribute. The
        /// public instance properties of data will be written recursively to
        /// create the fields of the event.
        /// </param>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2112:ReflectionToRequiresUnreferencedCode",
                    Justification = "EnsureDescriptorsInitialized's use of GetType preserves this method which " +
                                    "requires unreferenced code, but EnsureDescriptorsInitialized does not access this member and is safe to call.")]
        [RequiresUnreferencedCode(EventSourceRequiresUnreferenceMessage)]
        public unsafe void Write<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(
            string? eventName,
            ref EventSourceOptions options,
            ref Guid activityId,
            ref Guid relatedActivityId,
            ref T data)
        {
            // Payload metadata and dispatch run only for an enabled source, and no source can be enabled.
        }

    }
}
