using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface IMetadataTypeSignatureResolver
{
    CliTypeIdentity Resolve(
        MetadataAssemblySnapshot source,
        int metadataToken,
        CliGenericContext? genericContext = null);
}

