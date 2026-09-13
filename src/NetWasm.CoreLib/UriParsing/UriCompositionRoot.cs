// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.
// Copyright (c) .NET Foundation and contributors; see the repository license.

namespace System.UriParsing;

// Composition root: construct each URI capability and wire only the collaborators
// required by the parser and resolver. This type owns no URI behavior or state.
internal static class UriCompositionRoot
{
    internal static IUriInputValidator CreateInputValidator() =>
        new UriInputValidator();

    internal static IUriTextParser CreateTextParser() =>
        new UriTextParser(
            CreateSchemeEndReader(),
            CreateAuthorityReader(),
            CreatePathReader());

    internal static IUriRelativeResolver CreateRelativeResolver() =>
        new UriRelativeResolver(CreateSchemeEndReader());

    internal static IUriAuthorityFormatter CreateAuthorityFormatter() =>
        new UriAuthorityFormatter();

    internal static IUriHostClassifier CreateHostClassifier() =>
        new UriHostClassifier();

    internal static IUriSegmentReader CreateSegmentReader() =>
        new UriSegmentReader();

    internal static IUriEqualityComparer CreateEqualityComparer() =>
        new UriEqualityComparer();

    internal static IUriSchemeEndReader CreateSchemeEndReader() =>
        new UriSchemeEndReader();

    internal static IDefaultUriPortResolver CreateDefaultUriPortResolver() =>
        new DefaultUriPortResolver();

    internal static IUriEscaper CreateEscaper() => new UriEscaper();

    internal static IUriUnescaper CreateUnescaper() => new UriUnescaper();

    internal static IUriIdnNormalizer CreateIdnNormalizer() => new UriIdnNormalizer();

    private static IUriAuthorityReader CreateAuthorityReader() =>
        new UriAuthorityReader(new UriPortReader());

    private static IUriPathReader CreatePathReader() =>
        new UriPathReader();
}
