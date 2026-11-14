// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.ComponentModel
{
    /// <summary>
    /// Represents the method that will handle the <see langword='PropertyChanged'/>
    /// event raised when a property is changed on a component.
    /// </summary>
    public delegate void PropertyChangedEventHandler(object? sender, PropertyChangedEventArgs e);
}
