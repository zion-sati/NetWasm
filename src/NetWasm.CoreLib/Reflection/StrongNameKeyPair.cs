// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.IO;
using System.Runtime.Serialization;

namespace System.Reflection
{
    [Obsolete("Strong name signing is not supported and throws PlatformNotSupportedException.", DiagnosticId = "SYSLIB0017", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
    public class StrongNameKeyPair : IDeserializationCallback, ISerializable
    {
        public StrongNameKeyPair(FileStream keyPairFile) =>
            throw new PlatformNotSupportedException("Strong-name signing is not supported on this platform.");

        public StrongNameKeyPair(byte[] keyPairArray) =>
            throw new PlatformNotSupportedException("Strong-name signing is not supported on this platform.");

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected StrongNameKeyPair(SerializationInfo info, StreamingContext context) =>
            throw new PlatformNotSupportedException("Strong-name key serialization is not supported.");

        public StrongNameKeyPair(string keyPairContainer) =>
            throw new PlatformNotSupportedException("Strong-name signing is not supported on this platform.");

        public byte[] PublicKey =>
            throw new PlatformNotSupportedException("Strong-name signing is not supported on this platform.");

        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context) =>
            throw new PlatformNotSupportedException("Strong-name key serialization is not supported.");

        void IDeserializationCallback.OnDeserialization(object? sender) =>
            throw new PlatformNotSupportedException("Strong-name key serialization is not supported.");
    }
}
