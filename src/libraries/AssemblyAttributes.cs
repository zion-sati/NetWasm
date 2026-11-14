// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// As dotnet/runtime's build marks its libraries: CLS-compliant unless the project sets CLSCompliant to false.
#if NETWASM_CLS_NONCOMPLIANT
[assembly: System.CLSCompliant(false)]
#else
[assembly: System.CLSCompliant(true)]
#endif
