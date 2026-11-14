using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using TUnit.Assertions;

namespace NetWasm.CoreLib.Tests;

// The converted compiler-harness corpus fixtures: each expected line is "input:result", desktop .NET 11's result for
// that input. A fixture that throws reads "throws", observed without its type's name.
internal static class Corpus
{
    internal static async Task Matches(Func<int, int> run, params string[] expected)
    {
        var observed = new List<string>(expected.Length);
        foreach (string line in expected)
        {
            int input = int.Parse(line.AsSpan(0, line.IndexOf(':')), CultureInfo.InvariantCulture);
            string result;
            try
            {
                result = run(input).ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                result = "throws";
            }

            observed.Add(input.ToString(CultureInfo.InvariantCulture) + ":" + result);
        }

        await Assert.That(string.Join("\n", observed)).IsEqualTo(string.Join("\n", expected));
    }
}
