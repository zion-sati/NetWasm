// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace System.Diagnostics
{
    // NetWasm adaptation: the diagnostics profile has one logical reactor, so
    // publication preserves ordering without scheduling or cross-thread claims.
    internal interface IDiagnosticsPublicationGate
    {
        void Publish(Action transition);
    }

    internal sealed class SingleReactorPublicationGate : IDiagnosticsPublicationGate
    {
        public void Publish(Action transition)
        {
            if (transition is null)
            {
                throw new ArgumentNullException(nameof(transition));
            }

            transition();
        }
    }
}
