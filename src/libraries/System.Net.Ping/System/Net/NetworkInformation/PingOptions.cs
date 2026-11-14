// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Net.NetworkInformation
{
    public class PingOptions
    {
        private int _ttl;
        private bool _dontFragment;

        public PingOptions()
        {
            _ttl = 128;
        }

        public PingOptions(int ttl, bool dontFragment)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ttl);

            _ttl = ttl;
            _dontFragment = dontFragment;
        }

        public int Ttl
        {
            get
            {
                return _ttl;
            }
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

                // Useful to discover routes.
                _ttl = value;
            }
        }

        public bool DontFragment
        {
            get
            {
                return _dontFragment;
            }
            set
            {
                // Useful for discovering MTU.
                _dontFragment = value;
            }
        }
    }
}
