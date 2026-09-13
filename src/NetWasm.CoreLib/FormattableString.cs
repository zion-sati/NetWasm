// Portions derived from dotnet/runtime System.Private.CoreLib FormattableString.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace System
{
    /// <summary>Represents a composite format string and its arguments.</summary>
    public abstract class FormattableString : IFormattable
    {
        [StringSyntax(StringSyntaxAttribute.CompositeFormat)]
        public abstract string Format { get; }

        public abstract object?[] GetArguments();
        public abstract int ArgumentCount { get; }
        public abstract object? GetArgument(int index);
        public abstract string ToString(IFormatProvider? formatProvider);

        string IFormattable.ToString(string? ignored, IFormatProvider? formatProvider) =>
            ToString(formatProvider);

        public static string Invariant(FormattableString formattable)
        {
            if (formattable is null)
            {
                throw new ArgumentNullException(nameof(formattable));
            }
            return formattable.ToString(null);
        }

        public static string CurrentCulture(FormattableString formattable)
        {
            if (formattable is null)
            {
                throw new ArgumentNullException(nameof(formattable));
            }
            return formattable.ToString(null);
        }

        public override string ToString() => ToString(null);
    }
}
