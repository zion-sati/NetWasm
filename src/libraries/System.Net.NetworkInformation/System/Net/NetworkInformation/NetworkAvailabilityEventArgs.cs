// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Net.NetworkInformation
{
    public class NetworkAvailabilityEventArgs : EventArgs
    {
        private readonly bool _isAvailable;

        internal NetworkAvailabilityEventArgs(bool isAvailable)
        {
            _isAvailable = isAvailable;
        }

        public bool IsAvailable
        {
            get
            {
                return _isAvailable;
            }
        }
    }
}
