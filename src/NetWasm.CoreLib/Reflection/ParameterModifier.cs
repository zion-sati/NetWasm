// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection
{
    public readonly struct ParameterModifier
    {
        private readonly bool[] _byRef;

        public ParameterModifier(int parameterCount)
        {
            if (parameterCount <= 0)
                throw new ArgumentException("Must specify one or more parameters.");

            _byRef = new bool[parameterCount];
        }

        public bool this[int index]
        {
            get => _byRef[index];
            set => _byRef[index] = value;
        }


    }
}
