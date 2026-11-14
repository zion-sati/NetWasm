// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System;

namespace System.Security.Cryptography
{
    internal interface ILiteSymmetricCipher : IDisposable
    {
        int TransformFinal(ReadOnlySpan<byte> input, Span<byte> output);
        int Transform(ReadOnlySpan<byte> input, Span<byte> output);
        void Reset(ReadOnlySpan<byte> iv);

        int BlockSizeInBytes { get; }
        int PaddingSizeInBytes { get; }
    }
}
