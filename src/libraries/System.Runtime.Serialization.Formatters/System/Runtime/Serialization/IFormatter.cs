// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace System.Runtime.Serialization
{
    [Obsolete(Obsoletions.BinaryFormatterMessage, DiagnosticId = Obsoletions.BinaryFormatterDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
    public interface IFormatter
    {
        internal const string RequiresDynamicCodeMessage = "BinaryFormatter serialization uses dynamic code generation, the type of objects being processed cannot be statically discovered.";
        internal const string RequiresUnreferencedCodeMessage = "BinaryFormatter serialization is not trim compatible because the type of objects being processed cannot be statically discovered.";

        [RequiresDynamicCode(RequiresDynamicCodeMessage)]
        [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
        object Deserialize(Stream serializationStream);
        [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
        void Serialize(Stream serializationStream, object graph);
        ISurrogateSelector? SurrogateSelector { get; set; }
        SerializationBinder? Binder { get; set; }
        StreamingContext Context { get; set; }
    }
}
