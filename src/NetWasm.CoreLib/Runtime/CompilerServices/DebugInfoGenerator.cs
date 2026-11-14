// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: the internal IL hooks (the ILGenerator MarkSequencePoint overload and
// SetLocalName) serve upstream's IL compiler, which NetWasm does not have, so they are not ported.

using System.Linq.Expressions;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Generates debug information for lambdas in an expression tree.
    /// </summary>
    public abstract class DebugInfoGenerator
    {
        /// <summary>
        /// Creates PDB symbol generator.
        /// </summary>
        /// <returns>PDB symbol generator.</returns>
        [Obsolete(Obsoletions.CreatePdbGeneratorMessage, DiagnosticId = Obsoletions.CreatePdbGeneratorDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public static DebugInfoGenerator CreatePdbGenerator()
        {
            // Creating PDBs is not supported in .NET Core
            throw new PlatformNotSupportedException();
        }

        /// <summary>
        /// Marks a sequence point.
        /// </summary>
        /// <param name="method">The lambda being generated.</param>
        /// <param name="ilOffset">IL offset where to mark the sequence point.</param>
        /// <param name="sequencePoint">Debug information corresponding to the sequence point.</param>
        public abstract void MarkSequencePoint(LambdaExpression method, int ilOffset, DebugInfoExpression sequencePoint);
    }
}
