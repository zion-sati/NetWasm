// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Reflection.Emit
{
    /// <summary>
    /// Describes the types of the IL instructions.
    /// </summary>
    public enum OpCodeType
    {
        [Obsolete("OpCodeType.Annotation has been deprecated and is not supported.")]
        Annotation = 0,
        Macro = 1,
        Nternal = 2,
        Objmodel = 3,
        Prefix = 4,
        Primitive = 5,
    }
}
