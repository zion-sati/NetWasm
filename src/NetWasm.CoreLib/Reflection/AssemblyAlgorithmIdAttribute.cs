// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Configuration.Assemblies;

namespace System.Reflection
{
    [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
    public sealed class AssemblyAlgorithmIdAttribute : Attribute
    {
        public AssemblyAlgorithmIdAttribute(AssemblyHashAlgorithm algorithmId)
        {
            AlgorithmId = (uint)algorithmId;
        }

        [CLSCompliant(false)]
        public AssemblyAlgorithmIdAttribute(uint algorithmId)
        {
            AlgorithmId = algorithmId;
        }

        [CLSCompliant(false)]
        public uint AlgorithmId { get; }
    }
}
