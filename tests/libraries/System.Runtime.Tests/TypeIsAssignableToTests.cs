using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Type.IsAssignableTo is IsAssignableFrom from the other side, as upstream defines it (.NET 5).
public sealed class TypeIsAssignableToTests
{
    [Test]
    public async Task IsAssignableToMirrorsIsAssignableFrom()
    {
        Type text = typeof(string), anything = typeof(object), list = typeof(List<int>), sequence = typeof(IEnumerable<int>);

        await Assert.That(text.IsAssignableTo(anything)).IsTrue();
        await Assert.That(anything.IsAssignableTo(text)).IsFalse();
        await Assert.That(list.IsAssignableTo(sequence)).IsTrue();
        await Assert.That(sequence.IsAssignableTo(list)).IsFalse();
        await Assert.That(text.IsAssignableTo(text)).IsTrue();
        await Assert.That(text.IsAssignableTo(null)).IsFalse();
    }
}
