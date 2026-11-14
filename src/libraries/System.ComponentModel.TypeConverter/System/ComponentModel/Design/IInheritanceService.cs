// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.ComponentModel.Design
{
    /// <summary>
    /// Provides a set of utilities for analyzing and identifying inherited components.
    /// </summary>
    public interface IInheritanceService
    {
        /// <summary>
        /// Adds inherited components from the specified component to the specified container.
        /// </summary>
        void AddInheritedComponents(IComponent component, IContainer container);

        /// <summary>
        /// Gets the inheritance attribute of the specified
        /// component. If the component is not being inherited, this method will return the
        /// value <see cref='System.ComponentModel.InheritanceAttribute.NotInherited'/>.
        /// Otherwise it will return the inheritance attribute for this component.
        /// </summary>
        InheritanceAttribute GetInheritanceAttribute(IComponent component);
    }
}
