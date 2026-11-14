// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: dotnet/runtime 811225a4's XmlObjectSerializer, with its public surface, overload dispatch, null checks
// and exception wrapping unchanged. Upstream routes the dictionary overloads through the internal
// XmlWriterDelegator/XmlReaderDelegator of the DataContract engine, which NetWasm does not port; here they call
// the hooks directly with the same checks (the delegators' ArgumentNullException) and the same XmlException and
// FormatException wrapping. The engine's root-element and contract helpers are not ported.

using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;

namespace System.Runtime.Serialization
{
    public abstract class XmlObjectSerializer
    {
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public abstract void WriteStartObject(XmlDictionaryWriter writer, object? graph);
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public abstract void WriteObjectContent(XmlDictionaryWriter writer, object? graph);
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public abstract void WriteEndObject(XmlDictionaryWriter writer);

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public virtual void WriteObject(Stream stream, object? graph)
        {
            ArgumentNullException.ThrowIfNull(stream);

            XmlDictionaryWriter writer = XmlDictionaryWriter.CreateTextWriter(stream, Encoding.UTF8, false /*ownsStream*/);
            WriteObject(writer, graph);
            writer.Flush();
        }
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public virtual void WriteObject(XmlWriter writer, object? graph)
        {
            ArgumentNullException.ThrowIfNull(writer);

            WriteObject(XmlDictionaryWriter.CreateDictionaryWriter(writer), graph);
        }
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public virtual void WriteStartObject(XmlWriter writer, object? graph)
        {
            ArgumentNullException.ThrowIfNull(writer);

            WriteStartObject(XmlDictionaryWriter.CreateDictionaryWriter(writer), graph);
        }
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public virtual void WriteObjectContent(XmlWriter writer, object? graph)
        {
            ArgumentNullException.ThrowIfNull(writer);

            WriteObjectContent(XmlDictionaryWriter.CreateDictionaryWriter(writer), graph);
        }
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public virtual void WriteEndObject(XmlWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);

            WriteEndObject(XmlDictionaryWriter.CreateDictionaryWriter(writer));
        }
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public virtual void WriteObject(XmlDictionaryWriter writer, object? graph)
        {
            ArgumentNullException.ThrowIfNull(writer);

            try
            {
                WriteStartObject(writer, graph);
                WriteObjectContent(writer, graph);
                WriteEndObject(writer);
            }
            catch (XmlException ex)
            {
                throw CreateSerializationException(GetTypeInfoError(SR.ErrorSerializing, GetSerializeType(graph), ex), ex);
            }
            catch (FormatException ex)
            {
                throw CreateSerializationException(GetTypeInfoError(SR.ErrorSerializing, GetSerializeType(graph), ex), ex);
            }
        }
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public virtual object? ReadObject(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            return ReadObject(XmlDictionaryReader.CreateTextReader(stream, XmlDictionaryReaderQuotas.Max));
        }
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public virtual object? ReadObject(XmlReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);

            return ReadObject(XmlDictionaryReader.CreateDictionaryReader(reader));
        }
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public virtual object? ReadObject(XmlDictionaryReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);

            try
            {
                return ReadObject(reader, true /*verifyObjectName*/);
            }
            catch (XmlException ex)
            {
                throw CreateSerializationException(GetTypeInfoError(SR.ErrorDeserializing, GetDeserializeType(), ex), ex);
            }
            catch (FormatException ex)
            {
                throw CreateSerializationException(GetTypeInfoError(SR.ErrorDeserializing, GetDeserializeType(), ex), ex);
            }
        }
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public virtual object? ReadObject(XmlReader reader, bool verifyObjectName)
        {
            ArgumentNullException.ThrowIfNull(reader);

            return ReadObject(XmlDictionaryReader.CreateDictionaryReader(reader), verifyObjectName);
        }
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public abstract object? ReadObject(XmlDictionaryReader reader, bool verifyObjectName);

        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public virtual bool IsStartObject(XmlReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);

            return IsStartObject(XmlDictionaryReader.CreateDictionaryReader(reader));
        }
        [RequiresDynamicCode(Globals.SerializerAOTWarning)]
        [RequiresUnreferencedCode(Globals.SerializerTrimmerWarning)]
        public abstract bool IsStartObject(XmlDictionaryReader reader);

        internal static SerializationException CreateSerializationException(string errorMessage)
        {
            return XmlObjectSerializer.CreateSerializationException(errorMessage, null);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static SerializationException CreateSerializationException(string errorMessage, Exception? innerException)
        {
            return new SerializationException(errorMessage, innerException);
        }

        // NetWasm: upstream names the type with DataContract.GetClrTypeFullName, which differs from FullName only for
        // open generic types; the types here are always those of run-time instances, which are closed.
        internal static string GetTypeInfoError(string errorMessage, Type? type, Exception innerException)
        {
            string typeInfo = (type == null) ? string.Empty : SR.Format(SR.ErrorTypeInfo, type.FullName);
            string innerExceptionMessage = (innerException == null) ? string.Empty : innerException.Message;
            return SR.Format(errorMessage, typeInfo, innerExceptionMessage);
        }

        internal virtual Type? GetSerializeType(object? graph)
        {
            return graph?.GetType();
        }

        internal virtual Type? GetDeserializeType()
        {
            return null;
        }
    }
}
