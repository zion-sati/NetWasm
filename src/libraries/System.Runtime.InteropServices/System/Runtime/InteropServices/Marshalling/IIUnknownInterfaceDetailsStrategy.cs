// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

// This type is for the COM source generator and implements part of the COM-specific interactions.
// This API need to be exposed to implement the COM source generator in one form or another.

namespace System.Runtime.InteropServices.Marshalling
{
    /// <summary>
    /// Strategy for acquiring interface details.
    /// </summary>
    [CLSCompliant(false)]
    public interface IIUnknownInterfaceDetailsStrategy
    {
        /// <summary>
        /// Given a <see cref="RuntimeTypeHandle"/> get the IUnknown details.
        /// </summary>
        /// <param name="type">RuntimeTypeHandle instance</param>
        /// <returns>Details if type is known.</returns>
        IIUnknownDerivedDetails? GetIUnknownDerivedDetails(RuntimeTypeHandle type);

        /// <summary>
        /// Given a <see cref="RuntimeTypeHandle"/> get the details about the type that are exposed to COM.
        /// </summary>
        /// <param name="type">RuntimeTypeHandle instance</param>
        /// <returns>Details if type is known.</returns>
        IComExposedDetails? GetComExposedTypeDetails(RuntimeTypeHandle type);
    }
}
