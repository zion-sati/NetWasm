// Portions derived from dotnet/runtime System.Private.CoreLib FormattableStringFactory.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace System.Runtime.CompilerServices
{
    /// <summary>Creates FormattableString instances for compiler-generated interpolations.</summary>
    public static class FormattableStringFactory
    {
        public static FormattableString Create(
            [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format,
            params object?[] arguments)
        {
            if (format is null)
            {
                throw new ArgumentNullException(nameof(format));
            }
            if (arguments is null)
            {
                throw new ArgumentNullException(nameof(arguments));
            }
            return new ConcreteFormattableString(format, arguments);
        }

        private sealed class ConcreteFormattableString : FormattableString
        {
            private readonly string _format;
            private readonly object?[] _arguments;

            internal ConcreteFormattableString(string format, object?[] arguments)
            {
                _format = format;
                _arguments = arguments;
            }

            public override string Format => _format;
            public override object?[] GetArguments() => _arguments;
            public override int ArgumentCount => _arguments.Length;
            public override object? GetArgument(int index) => _arguments[index];

            public override string ToString(IFormatProvider? formatProvider)
            {
                var builder = new Text.StringBuilder();
                builder.AppendFormat(formatProvider, _format, _arguments);
                return builder.ToString();
            }
        }
    }
}
