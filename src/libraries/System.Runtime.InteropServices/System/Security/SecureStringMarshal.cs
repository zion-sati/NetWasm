// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Runtime.InteropServices;

namespace System.Security
{
    public static class SecureStringMarshal
    {
        public static IntPtr SecureStringToCoTaskMemAnsi(SecureString s) => Marshal.SecureStringToCoTaskMemAnsi(s);
        public static IntPtr SecureStringToGlobalAllocAnsi(SecureString s) => Marshal.SecureStringToGlobalAllocAnsi(s);
        public static IntPtr SecureStringToCoTaskMemUnicode(SecureString s) => Marshal.SecureStringToCoTaskMemUnicode(s);
        public static IntPtr SecureStringToGlobalAllocUnicode(SecureString s) => Marshal.SecureStringToGlobalAllocUnicode(s);
    }
}
