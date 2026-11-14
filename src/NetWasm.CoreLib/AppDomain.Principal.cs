// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Upstream default-principal storage. As upstream, the unauthenticated default comes from
// System.Security.Claims' GenericPrincipal through an UnsafeAccessor; Windows principals are unavailable.
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Threading;
namespace System;

public sealed partial class AppDomain
{
    private IPrincipal? _defaultPrincipal;
    private PrincipalPolicy _principalPolicy = PrincipalPolicy.NoPrincipal;

    public void SetPrincipalPolicy(PrincipalPolicy policy) => _principalPolicy = policy;

    public void SetThreadPrincipal(IPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (Interlocked.CompareExchange(ref _defaultPrincipal, principal, null) is not null)
            throw new SystemException("Default principal object cannot be set twice.");
    }

    internal IPrincipal? GetThreadPrincipal()
    {
        var principal = _defaultPrincipal;
        if (principal is not null) return principal;
        switch (_principalPolicy)
        {
            case PrincipalPolicy.UnauthenticatedPrincipal:
                return (IPrincipal)GetDefaultPrincipal(null);
            case PrincipalPolicy.WindowsPrincipal:
                throw new PlatformNotSupportedException("Windows principal creation is not supported on NetWasm.");
            default:
                return null;
        }

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetDefaultInstance")]
        [return: UnsafeAccessorType("System.Security.Principal.GenericPrincipal, System.Security.Claims")]
        static extern object GetDefaultPrincipal(
            [UnsafeAccessorType("System.Security.Principal.GenericPrincipal, System.Security.Claims")] object? _);
    }
}
