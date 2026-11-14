// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.ComponentModel.Design
{
    /// <summary>
    /// Represents the method that will handle the System.ComponentModel.Design.IDesignerEventService.DesignerEvent
    /// event raised when a document is created or disposed.
    /// </summary>
    public delegate void DesignerEventHandler(object? sender, DesignerEventArgs e);
}
