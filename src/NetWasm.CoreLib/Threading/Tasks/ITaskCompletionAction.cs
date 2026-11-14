// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce (Task.cs).

namespace System.Threading.Tasks
{
    /// <summary>
    /// Interface which all non-Task-based continuations must implement.
    /// </summary>
    internal interface ITaskCompletionAction
    {
        /// <summary>Invoked the completion action.</summary>
        void Invoke(Task completingTask);

        /// <summary>
        /// Some completion actions are considered internal implementation details of tasks,
        /// using the continuation mechanism only for performance reasons.  Such actions perform
        /// known quantities and types of work, and can be invoked safely as a continuation even
        /// if the system wants to prevent arbitrary continuations from running synchronously.
        /// This should only return false for a limited set of implementations where a small amount
        /// of work is guaranteed to be performed, e.g. setting a ManualResetEventSlim.
        /// </summary>
        bool InvokeMayRunArbitraryCode { get; }
    }
}
