// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: the named-color table comes from the KnownColor enum instead of property
// reflection, which NetWasm does not support. Each name is the known color's public Name, since
// KnownColorNames is internal to System.Drawing.Primitives.

using System.Collections.Generic;

namespace System.Drawing
{
    internal static class ColorTable
    {
        private static readonly Lazy<Dictionary<string, Color>> s_colorConstants = new Lazy<Dictionary<string, Color>>(GetColors);

        private static Dictionary<string, Color> GetColors()
        {
            // Upstream reflects over the static Color properties of Color and SystemColors; there is
            // exactly one per KnownColor value, named after it, so the enum gives the same table.
            var colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            for (KnownColor color = KnownColor.ActiveBorder; color <= KnownColor.RebeccaPurple; color++)
            {
                Color known = Color.FromKnownColor(color);
                colors[known.Name] = known;
            }
            return colors;
        }

        internal static Dictionary<string, Color> Colors => s_colorConstants.Value;

        internal static bool TryGetNamedColor(string name, out Color result) => Colors.TryGetValue(name, out result);

        internal static bool IsKnownNamedColor(string name) => Colors.TryGetValue(name, out _);
    }
}
