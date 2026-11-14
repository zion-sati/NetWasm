// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: the constructor looks the event up through reflection, which NetWasm does
// not support, so it throws PlatformNotSupportedException; the members are upstream's.

using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Versioning;

// This type is obsolete, and is expected to be used in very specific ways or it may
// throw null reference exceptions.
#pragma warning disable CS8610

namespace System.Runtime.InteropServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    [RequiresUnreferencedCode("Built-in COM support is not trim compatible", Url = "https://aka.ms/dotnet-illink/com")]
    public class ComAwareEventInfo : EventInfo
    {
        private readonly EventInfo _innerEventInfo;

        public ComAwareEventInfo([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicEvents)] Type type, string eventName)
        {
            _innerEventInfo = null!;
            throw new PlatformNotSupportedException(SR.PlatformNotSupported_EventReflection);
        }

        [SupportedOSPlatform("windows")]
#pragma warning disable CS8765 // Nullability of parameters 'target' and 'handler' don't match overridden member
        public override void AddEventHandler(object target, Delegate handler)
#pragma warning restore CS8765
        {
            if (Marshal.IsComObject(target))
            {
                // retrieve sourceIid and dispid
                GetDataForComInvocation(_innerEventInfo, out Guid sourceIid, out int dispid);
                ComEventsHelper.Combine(target, sourceIid, dispid, handler);
            }
            else
            {
                // we are dealing with a managed object - just add the delegate through reflection
                _innerEventInfo.AddEventHandler(target, handler);
            }
        }

        [SupportedOSPlatform("windows")]
#pragma warning disable CS8765 // Nullability of parameters 'target' and 'handler' don't match overridden member
        public override void RemoveEventHandler(object target, Delegate handler)
#pragma warning restore CS8765
        {
            if (Marshal.IsComObject(target))
            {
                // retrieve sourceIid and dispid
                GetDataForComInvocation(_innerEventInfo, out Guid sourceIid, out int dispid);

                ComEventsHelper.Remove(target, sourceIid, dispid, handler);
            }
            else
            {
                // we are dealing with a managed object - just add the delegate through reflection
                _innerEventInfo.RemoveEventHandler(target, handler);
            }
        }

        public override EventAttributes Attributes => _innerEventInfo.Attributes;

        public override MethodInfo? GetAddMethod(bool nonPublic) => _innerEventInfo.GetAddMethod(nonPublic);

        public override MethodInfo[] GetOtherMethods(bool nonPublic) => _innerEventInfo.GetOtherMethods(nonPublic);

        public override MethodInfo? GetRaiseMethod(bool nonPublic) => _innerEventInfo.GetRaiseMethod(nonPublic);

        public override MethodInfo? GetRemoveMethod(bool nonPublic) => _innerEventInfo.GetRemoveMethod(nonPublic);

        public override Type? DeclaringType => _innerEventInfo.DeclaringType;

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            return _innerEventInfo.GetCustomAttributes(attributeType, inherit);
        }

        public override object[] GetCustomAttributes(bool inherit)
        {
            return _innerEventInfo.GetCustomAttributes(inherit);
        }

        public override IList<CustomAttributeData> GetCustomAttributesData() => _innerEventInfo.GetCustomAttributesData();

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            return _innerEventInfo.IsDefined(attributeType, inherit);
        }

        public override int MetadataToken => _innerEventInfo.MetadataToken;

        public override Module Module => _innerEventInfo.Module;

        public override string Name => _innerEventInfo.Name;

        public override Type? ReflectedType => _innerEventInfo.ReflectedType;

        private static void GetDataForComInvocation(EventInfo eventInfo, out Guid sourceIid, out int dispid)
        {
            object[] comEventInterfaces = eventInfo.DeclaringType!.GetCustomAttributes(typeof(ComEventInterfaceAttribute), inherit: false);

            if (comEventInterfaces == null || comEventInterfaces.Length == 0)
            {
                throw new InvalidOperationException(SR.InvalidOperation_NoComEventInterfaceAttribute);
            }

            ComEventInterfaceAttribute interfaceAttribute = (ComEventInterfaceAttribute)comEventInterfaces[0];

            if (comEventInterfaces.Length > 1)
            {
                throw new AmbiguousMatchException(SR.Format(SR.AmbiguousMatch_MultipleEventInterfaceAttributes, interfaceAttribute));
            }

            Type sourceInterface = interfaceAttribute.SourceInterface;
            Guid guid = sourceInterface.GUID;
            MethodInfo methodInfo = sourceInterface.GetMethod(eventInfo.Name)!;
            Attribute? dispIdAttribute = Attribute.GetCustomAttribute(methodInfo, typeof(DispIdAttribute));
            if (dispIdAttribute == null)
            {
                throw new InvalidOperationException(SR.InvalidOperation_NoDispIdAttribute);
            }

            sourceIid = guid;
            dispid = ((DispIdAttribute)dispIdAttribute).Value;
        }
    }
}
