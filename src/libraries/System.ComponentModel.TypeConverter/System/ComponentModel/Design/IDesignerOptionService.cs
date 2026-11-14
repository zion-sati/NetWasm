// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Diagnostics.CodeAnalysis;

namespace System.ComponentModel.Design
{
    /// <summary>
    /// Provides access to get and set option values for a designer.
    /// </summary>
    public interface IDesignerOptionService
    {
        /// <summary>
        /// Gets the value of an option defined in this package.
        /// </summary>
        [RequiresUnreferencedCode("The option value's Type cannot be statically discovered.")]
        object? GetOptionValue(string pageName, string valueName);

        /// <summary>
        /// Sets the value of an option defined in this package.
        /// </summary>
        [RequiresUnreferencedCode("The option value's Type cannot be statically discovered.")]
        void SetOptionValue(string pageName, string valueName, object value);
    }
}
