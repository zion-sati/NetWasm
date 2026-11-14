// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime Array.Enumerators.cs at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Collections;
using System.Collections.Generic;

namespace System
{
    internal abstract class GenericEmptyEnumeratorBase : IDisposable, IEnumerator
    {
        public bool MoveNext() => false;

        public object Current
        {
            get
            {
                ThrowHelper.ThrowInvalidOperationException_EnumCurrent(-1);
                return default;
            }
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }

    internal sealed class GenericEmptyEnumerator<T> : GenericEmptyEnumeratorBase, IEnumerator<T>
    {
        public static readonly GenericEmptyEnumerator<T> Instance = new();

        private GenericEmptyEnumerator()
        {
        }

        public new T Current
        {
            get
            {
                ThrowHelper.ThrowInvalidOperationException_EnumCurrent(-1);
                return default;
            }
        }
    }
}
