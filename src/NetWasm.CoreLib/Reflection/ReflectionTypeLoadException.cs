// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

// Adapted from the pinned runtime: legacy formatter serialization is outside
// this ordinary exception contract slice. Messages use invariant resources.
namespace System.Reflection
{
    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public sealed partial class ReflectionTypeLoadException : SystemException
    {
        public ReflectionTypeLoadException(Type?[]? classes, Exception?[]? exceptions) :
            this(classes, exceptions, null)
        {
        }

        public ReflectionTypeLoadException(Type?[]? classes, Exception?[]? exceptions, string? message)
            : base(message)
        {
            Types = classes ?? [];
            LoaderExceptions = exceptions ?? [];
            HResult = HResults.COR_E_REFLECTIONTYPELOAD;
        }



        public Type?[] Types { get; }

        public Exception?[] LoaderExceptions { get; }

        public override string Message => CreateString(isMessage: true);

        public override string ToString() => CreateString(isMessage: false);

        private string CreateString(bool isMessage)
        {
            string baseValue = isMessage ? base.Message : base.ToString();

            Exception?[] exceptions = LoaderExceptions;
            if (exceptions.Length == 0)
            {
                return baseValue;
            }

            var text = new StringBuilder(baseValue);
            foreach (Exception? e in exceptions)
            {
                if (e != null)
                {
                    text.AppendLine().Append(isMessage ? e.Message : e.ToString());
                }
            }

            return text.ToString();
        }
    }
}
