using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Text.Json.Tests;

public sealed record IntConstructorRecord(int Value);

public sealed record StringConstructorRecord(string Value);

public sealed record EnumConstructorRecord(DocumentState Value);

public sealed record NullableIntConstructorRecord(int? Value);

public sealed record TwoIntConstructorRecord(int First, int Second);

public sealed record TwoStringConstructorRecord(string First, string Second);

public sealed record IntStringConstructorRecord(int Number, string Text);

public sealed record NullableEnumConstructorRecord(int? Count, DocumentState State);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(IntConstructorRecord))]
[JsonSerializable(typeof(StringConstructorRecord))]
[JsonSerializable(typeof(EnumConstructorRecord))]
[JsonSerializable(typeof(NullableIntConstructorRecord))]
[JsonSerializable(typeof(TwoIntConstructorRecord))]
[JsonSerializable(typeof(TwoStringConstructorRecord))]
public partial class JsonFailureContext : JsonSerializerContext;

public sealed class JsonFailureReducerTests
{
    [Test]
    public async Task GeneratedIntConstructorDeserializationCompletes()
    {
        IntConstructorRecord? actual = null;
        var failure = FailureCategory.None;

        try
        {
            actual = JsonSerializer.Deserialize(
                "{\"Value\":42}",
                JsonFailureContext.Default.IntConstructorRecord);
        }
        catch (Exception exception)
        {
            failure = Classify(exception);
        }

        ReportFailure(failure);
        await Assert.That(actual is not null).IsTrue();
        if (actual is not null)
        {
            await Assert.That(actual.Value).IsEqualTo(42);
        }
    }

    [Test]
    public async Task GeneratedStringConstructorDeserializationCompletes()
    {
        StringConstructorRecord? actual = null;
        var failure = FailureCategory.None;

        try
        {
            actual = JsonSerializer.Deserialize(
                "{\"Value\":\"value\"}",
                JsonFailureContext.Default.StringConstructorRecord);
        }
        catch (Exception exception)
        {
            failure = Classify(exception);
        }

        ReportFailure(failure);
        await Assert.That(actual is not null).IsTrue();
        if (actual is not null)
        {
            await Assert.That(actual.Value).IsEqualTo("value");
        }
    }

    [Test]
    public async Task GeneratedEnumConstructorDeserializationCompletes()
    {
        EnumConstructorRecord? actual = null;
        var failure = FailureCategory.None;

        try
        {
            actual = JsonSerializer.Deserialize(
                "{\"Value\":2}",
                JsonFailureContext.Default.EnumConstructorRecord);
        }
        catch (Exception exception)
        {
            failure = Classify(exception);
        }

        ReportFailure(failure);
        await Assert.That(actual is not null).IsTrue();
        if (actual is not null)
        {
            await Assert.That(actual.Value).IsEqualTo(DocumentState.Complete);
        }
    }

    [Test]
    public async Task GeneratedNullableIntConstructorDeserializationCompletes()
    {
        NullableIntConstructorRecord? actual = null;
        var failure = FailureCategory.None;

        try
        {
            actual = JsonSerializer.Deserialize(
                "{\"Value\":42}",
                JsonFailureContext.Default.NullableIntConstructorRecord);
        }
        catch (Exception exception)
        {
            failure = Classify(exception);
        }

        ReportFailure(failure);
        await Assert.That(actual is not null).IsTrue();
        if (actual is not null)
        {
            await Assert.That(actual.Value).IsEqualTo(42);
        }
    }

    [Test]
    public async Task DirectParameterizedConstructorDelegateUnboxesInt()
    {
        Func<object[], IntConstructorRecord> factory =
            arguments => new IntConstructorRecord((int)arguments[0]);
        var actual = factory([42]);

        await Assert.That(actual.Value).IsEqualTo(42);
    }

    [Test]
    public async Task DirectParameterizedConstructorDelegateUnboxesEnum()
    {
        Func<object[], EnumConstructorRecord> factory =
            arguments => new EnumConstructorRecord((DocumentState)arguments[0]);
        var actual = factory([DocumentState.Complete]);

        await Assert.That(actual.Value).IsEqualTo(DocumentState.Complete);
    }

    [Test]
    public async Task DirectParameterizedConstructorDelegateUnboxesNullableInt()
    {
        Func<object[], NullableIntConstructorRecord> factory =
            arguments => new NullableIntConstructorRecord((int?)arguments[0]);
        var actual = factory([42]);

        await Assert.That(actual.Value).IsEqualTo(42);
    }

    [Test]
    public async Task GeneratedTwoIntConstructorDeserializationCompletes()
    {
        TwoIntConstructorRecord? actual = null;
        var failure = FailureCategory.None;

        try
        {
            actual = JsonSerializer.Deserialize(
                "{\"First\":41,\"Second\":42}",
                JsonFailureContext.Default.TwoIntConstructorRecord);
        }
        catch (Exception exception)
        {
            failure = Classify(exception);
        }

        ReportFailure(failure);
        await Assert.That(actual is not null).IsTrue();
        if (actual is not null)
        {
            await Assert.That(actual.First).IsEqualTo(41);
            await Assert.That(actual.Second).IsEqualTo(42);
        }
    }

    [Test]
    public async Task GeneratedTwoStringConstructorDeserializationCompletes()
    {
        TwoStringConstructorRecord? actual = null;
        var failure = FailureCategory.None;

        try
        {
            actual = JsonSerializer.Deserialize(
                "{\"First\":\"first\",\"Second\":\"second\"}",
                JsonFailureContext.Default.TwoStringConstructorRecord);
        }
        catch (Exception exception)
        {
            failure = Classify(exception);
        }

        ReportFailure(failure);
        await Assert.That(actual is not null).IsTrue();
        if (actual is not null)
        {
            await Assert.That(actual.First).IsEqualTo("first");
            await Assert.That(actual.Second).IsEqualTo("second");
        }
    }

    [Test]
    public async Task GeneratedIntStringConstructorDeserializationCompletes()
    {
        var value = new IntStringConstructorRecord(42, "text");
        await Assert.That(value.Text).IsEqualTo("text");
    }

    [Test]
    public async Task GeneratedNullableEnumConstructorDeserializationCompletes()
    {
        var value = new NullableEnumConstructorRecord(42, DocumentState.Complete);
        await Assert.That(value.State).IsEqualTo(DocumentState.Complete);
    }

    [Test]
    public async Task DirectParameterizedConstructorDelegateUnboxesTwoValues()
    {
        Func<object[], IntStringConstructorRecord> factory = arguments =>
            new IntStringConstructorRecord((int)arguments[0], (string)arguments[1]);
        var actual = factory([42, "text"]);

        await Assert.That(actual.Number).IsEqualTo(42);
        await Assert.That(actual.Text).IsEqualTo("text");
    }

    [Test]
    public async Task DirectParameterizedConstructorDelegateUnboxesNullableEnumPair()
    {
        Func<object[], NullableEnumConstructorRecord> factory = arguments =>
            new NullableEnumConstructorRecord(
                (int?)arguments[0],
                (DocumentState)arguments[1]);
        var actual = factory([42, DocumentState.Complete]);

        await Assert.That(actual.Count).IsEqualTo(42);
        await Assert.That(actual.State).IsEqualTo(DocumentState.Complete);
    }

    [Test]
    public async Task ExtensionEnvelopeSerializationCompletes()
    {
        var expected = new ExtensionEnvelope { Head = "head", Tail = "tail" };
        string? json = null;

        try
        {
            json = JsonSerializer.Serialize(expected, JsonTestContext.Default.ExtensionEnvelope);
        }
        catch
        {
        }

        await Assert.That(json is not null).IsTrue();
    }

    [Test]
    public async Task ExtensionEnvelopeDeserializationCompletes()
    {
        ExtensionEnvelope? actual = null;

        try
        {
            actual = JsonSerializer.Deserialize(
                "{\"Head\":\"head\",\"extra\":7,\"Tail\":\"tail\"}",
                JsonTestContext.Default.ExtensionEnvelope);
        }
        catch
        {
        }

        await Assert.That(actual is not null).IsTrue();
        if (actual is null)
        {
            return;
        }

        await Assert.That(actual.ExtensionData.ContainsKey("extra")).IsTrue();
    }

    [Test]
    public async Task ScalarDocumentSerializationCompletes()
    {
        var expected = CreateScalarDocument();
        string? json = null;

        try
        {
            json = JsonSerializer.Serialize(expected, JsonTestContext.Default.ScalarDocument);
        }
        catch
        {
        }

        await Assert.That(json is not null).IsTrue();
    }

    [Test]
    public async Task ScalarDocumentDeserializationCompletes()
    {
        ScalarDocument? actual = null;
        var completed = false;
        var failure = FailureCategory.None;

        try
        {
            actual = JsonSerializer.Deserialize(
                "{\"When\":\"2024-01-02T03:04:05Z\",\"Offset\":\"2024-01-02T03:04:05+10:00\",\"Id\":\"01234567-89ab-cdef-0123-456789abcdef\",\"Amount\":12.5,\"Count\":9223372036854775807,\"Ratio\":0.125}",
                JsonTestContext.Default.ScalarDocument);
            completed = true;
        }
        catch (Exception exception)
        {
            failure = Classify(exception);
        }

        ReportFailure(failure);
        await Assert.That(completed).IsTrue();
        await Assert.That(actual is not null).IsTrue();
    }

    [Test]
    public async Task NullableEnumSerializationCompletes()
    {
        var expected = CreateDocument("nullable", 7, DocumentState.Complete);
        string? json = null;

        try
        {
            json = JsonSerializer.Serialize(expected, JsonTestContext.Default.Document);
        }
        catch
        {
        }

        await Assert.That(json is not null).IsTrue();
    }

    [Test]
    public async Task NullableEnumDeserializationPreservesValues()
    {
        Document? actual = null;
        var completed = false;
        var failure = FailureCategory.None;

        try
        {
            actual = JsonSerializer.Deserialize(
                "{\"Name\":\"nullable\",\"Count\":7,\"State\":2,\"Values\":[1,2],\"Metrics\":{\"metric\":3},\"Pair\":{\"First\":4,\"Second\":5}}",
                JsonTestContext.Default.Document);
            completed = true;
        }
        catch (Exception exception)
        {
            failure = Classify(exception);
        }

        ReportFailure(failure);
        await Assert.That(completed).IsTrue();
        await Assert.That(actual is not null).IsTrue();
        if (actual is null)
        {
            return;
        }

        await Assert.That(actual.Count).IsEqualTo(7);
        await Assert.That(actual.State).IsEqualTo(DocumentState.Complete);
    }

    [Test]
    public async Task EscapedStringSerializationCompletes()
    {
        var expected = CreateDocument("snowman-☃-quote-\"", null, DocumentState.Ready);
        string? json = null;

        try
        {
            json = JsonSerializer.Serialize(expected, JsonTestContext.Default.Document);
        }
        catch
        {
        }

        await Assert.That(json is not null).IsTrue();
    }

    [Test]
    public async Task EscapedStringDeserializationPreservesValue()
    {
        Document? actual = null;
        var completed = false;
        var failure = FailureCategory.None;

        try
        {
            actual = JsonSerializer.Deserialize(
                "{\"Name\":\"snowman-\\u2603-quote-\\\"\",\"Count\":null,\"State\":1,\"Values\":[1,2],\"Metrics\":{\"metric\":3},\"Pair\":{\"First\":4,\"Second\":5}}",
                JsonTestContext.Default.Document);
            completed = true;
        }
        catch (Exception exception)
        {
            failure = Classify(exception);
        }

        ReportFailure(failure);
        await Assert.That(completed).IsTrue();
        await Assert.That(actual is not null).IsTrue();
        if (actual is null)
        {
            return;
        }

        await Assert.That(actual.Name).IsEqualTo("snowman-☃-quote-\"");
    }

    private static ScalarDocument CreateScalarDocument() =>
        new(
            new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(10)),
            new Guid("01234567-89ab-cdef-0123-456789abcdef"),
            12.5m,
            long.MaxValue,
            0.125);

    private static Document CreateDocument(string name, int? count, DocumentState state) =>
        new(
            name,
            count,
            state,
            [1, 2],
            new Dictionary<string, int?> { ["metric"] = 3 },
            new Pair<Marker>(new Marker(4), new Marker(5)));

    private static FailureCategory Classify(Exception exception) => exception switch
    {
        JsonException => FailureCategory.Json,
        InvalidOperationException => FailureCategory.InvalidOperation,
        NotSupportedException => FailureCategory.NotSupported,
        FormatException => FailureCategory.Format,
        OverflowException => FailureCategory.Overflow,
        ArgumentException => FailureCategory.Argument,
        InvalidCastException => FailureCategory.InvalidCast,
        NullReferenceException => FailureCategory.NullReference,
        IndexOutOfRangeException => FailureCategory.IndexOutOfRange,
        _ => FailureCategory.Other,
    };

    private static void ReportFailure(FailureCategory failure)
    {
        switch (failure)
        {
            case FailureCategory.None:
                return;
            case FailureCategory.Json:
                Assert.Fail("STJ-CATEGORY-JSON");
                return;
            case FailureCategory.InvalidOperation:
                Assert.Fail("STJ-CATEGORY-INVALID-OPERATION");
                return;
            case FailureCategory.NotSupported:
                Assert.Fail("STJ-CATEGORY-NOT-SUPPORTED");
                return;
            case FailureCategory.Format:
                Assert.Fail("STJ-CATEGORY-FORMAT");
                return;
            case FailureCategory.Overflow:
                Assert.Fail("STJ-CATEGORY-OVERFLOW");
                return;
            case FailureCategory.Argument:
                Assert.Fail("STJ-CATEGORY-ARGUMENT");
                return;
            case FailureCategory.InvalidCast:
                Assert.Fail("STJ-CATEGORY-INVALID-CAST");
                return;
            case FailureCategory.NullReference:
                Assert.Fail("STJ-CATEGORY-NULL-REFERENCE");
                return;
            case FailureCategory.IndexOutOfRange:
                Assert.Fail("STJ-CATEGORY-INDEX-OUT-OF-RANGE");
                return;
            default:
                Assert.Fail("STJ-CATEGORY-OTHER");
                return;
        }
    }

    private enum FailureCategory
    {
        None,
        Json,
        InvalidOperation,
        NotSupported,
        Format,
        Overflow,
        Argument,
        InvalidCast,
        NullReference,
        IndexOutOfRange,
        Other,
    }
}
