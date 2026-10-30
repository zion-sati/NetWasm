// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Copied from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de src/libraries/System.Private.CoreLib/src/System/Numerics/Quaternion.Extensions.cs

using System.Runtime.CompilerServices;

namespace System.Numerics
{
    public static partial class Vector
    {
        /// <summary>Reinterprets a <see cref="Quaternion" /> as a new <see cref="Vector4" />.</summary>
        /// <param name="value">The quaternion to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see cref="Quaternion" />.</returns>
        [Intrinsic]
        public static Vector4 AsVector4(this Quaternion value) => Unsafe.BitCast<Quaternion, Vector4>(value);
    }
}
