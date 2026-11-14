// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Runtime.Serialization
{
    [Obsolete(Obsoletions.LegacyFormatterMessage, DiagnosticId = Obsoletions.LegacyFormatterDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
    public interface ISurrogateSelector
    {
        void ChainSelector(ISurrogateSelector selector);
        ISerializationSurrogate? GetSurrogate(Type type, StreamingContext context, out ISurrogateSelector selector);
        ISurrogateSelector? GetNextSelector();
    }
}
