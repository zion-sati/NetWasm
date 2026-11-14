using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TUnit.Core;

namespace NetWasm.System.Text.Json.Tests;

public sealed class GeneratedDeserializationStageReducerTests
{
    [Test]
    public void GeneratedDeserializationPublishesEveryManagedBoundary()
    {
        var scalar = CaptureScalar();
        var nullableEnum = CaptureDocument(StageScenario.NullableEnum, "{\"Choice\":1}");
        var escapedString = CaptureDocument(StageScenario.EscapedString, "{\"Text\":\"line\\nvalue\"}");
        var extensionData = CaptureDocument(StageScenario.ExtensionData, "{\"Extra\":true}");

        Console.WriteLine(Marker(scalar));
        Console.WriteLine(Marker(nullableEnum));
        Console.WriteLine(Marker(escapedString));
        Console.WriteLine(Marker(extensionData));

        if (!scalar.Completed(StageBoundary.FinalResultReturned)
            || !nullableEnum.Completed(StageBoundary.FinalResultReturned)
            || !escapedString.Completed(StageBoundary.FinalResultReturned)
            || !extensionData.Completed(StageBoundary.FinalResultReturned))
        {
            throw new StageProbeException(string.Concat(
                Marker(scalar), ";",
                Marker(nullableEnum), ";",
                Marker(escapedString), ";",
                Marker(extensionData)));
        }
    }

    private static StageReceipt CaptureScalar()
    {
        StageRecorder.Reset();

        try
        {
            const string json = "17";
            ObserveInitialToken(json);
            var typeInfo = GeneratedDeserializationStageContext.Default.StageScalar;
            StageRecorder.Mark(StageBoundary.MetadataAvailable);
            _ = JsonSerializer.Deserialize(json, typeInfo);
            StageRecorder.Mark(StageBoundary.FinalResultReturned);
            return StageRecorder.Receipt(StageScenario.Scalar, StageOutcome.Success);
        }
        catch (Exception exception)
        {
            return StageRecorder.Receipt(StageScenario.Scalar, Classify(exception));
        }
    }

    private static StageReceipt CaptureDocument(StageScenario scenario, string json)
    {
        StageRecorder.Reset();

        try
        {
            ObserveInitialToken(json);
            var typeInfo = GeneratedDeserializationStageContext.Default.StageDocument;
            StageRecorder.Mark(StageBoundary.MetadataAvailable);
            var result = JsonSerializer.Deserialize(json, typeInfo);

            if (result is null)
            {
                return StageRecorder.Receipt(scenario, StageOutcome.NullResult);
            }

            StageRecorder.Mark(StageBoundary.FinalResultReturned);
            return StageRecorder.Receipt(scenario, StageOutcome.Success);
        }
        catch (Exception exception)
        {
            return StageRecorder.Receipt(scenario, Classify(exception));
        }
    }

    private static void ObserveInitialToken(string json)
    {
        var utf8 = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(utf8);
        if (!reader.Read())
        {
            throw new JsonException();
        }

        StageRecorder.Mark(StageBoundary.ReaderAndTokenObserved);
    }

    private static StageOutcome Classify(Exception exception) => exception switch
    {
        NullReferenceException => StageOutcome.NullReference,
        IndexOutOfRangeException => StageOutcome.IndexOutOfRange,
        JsonException => StageOutcome.Json,
        _ => StageOutcome.OtherException,
    };

    private static string Marker(StageReceipt receipt) => string.Concat(
        "STAGE_RECEIPT:",
        ((byte)receipt.Scenario).ToString(), ":",
        ((byte)receipt.Boundaries).ToString(), ":",
        ((byte)receipt.Outcome).ToString());
}

[Flags]
internal enum StageBoundary : byte
{
    None = 0,
    ReaderAndTokenObserved = 1,
    MetadataAvailable = 2,
    InstanceCreated = 4,
    PropertyResolved = 8,
    ConverterReadAndCastCompleted = 16,
    PropertyAssigned = 32,
    ExtensionDataPublished = 64,
    FinalResultReturned = 128,
}

internal enum StageScenario : byte
{
    Scalar = 1,
    NullableEnum = 2,
    EscapedString = 3,
    ExtensionData = 4,
}

internal enum StageOutcome : byte
{
    Success = 0,
    NullResult = 1,
    NullReference = 2,
    IndexOutOfRange = 3,
    Json = 4,
    OtherException = 5,
}

internal sealed class StageReceipt
{
    public StageReceipt(StageScenario scenario, StageBoundary boundaries, StageOutcome outcome)
    {
        Scenario = scenario;
        Boundaries = boundaries;
        Outcome = outcome;
    }

    public StageScenario Scenario { get; }

    public StageBoundary Boundaries { get; }

    public StageOutcome Outcome { get; }

    public bool Completed(StageBoundary boundary) => (Boundaries & boundary) != 0;
}

internal static class StageRecorder
{
    private static StageBoundary s_boundaries;

    public static void Reset() => s_boundaries = StageBoundary.None;

    public static void Mark(StageBoundary boundary) => s_boundaries |= boundary;

    public static StageReceipt Receipt(StageScenario scenario, StageOutcome outcome) =>
        new(scenario, s_boundaries, outcome);
}

internal sealed class StageProbeException : Exception
{
    public StageProbeException(string marker)
        : base(marker)
    {
    }
}

[JsonConverter(typeof(StageScalarConverter))]
internal readonly struct StageScalar
{
    public StageScalar(int value) => Value = value;

    public int Value { get; }
}

internal sealed class StageScalarConverter : JsonConverter<StageScalar>
{
    public StageScalarConverter()
    {
    }

    public override StageScalar Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        StageRecorder.Mark(StageBoundary.PropertyResolved);

        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new JsonException();
        }

        StageRecorder.Mark(StageBoundary.ReaderAndTokenObserved);
        var result = new StageScalar(reader.GetInt32());
        StageRecorder.Mark(StageBoundary.ConverterReadAndCastCompleted);
        return result;
    }

    public override void Write(Utf8JsonWriter writer, StageScalar value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
}

internal enum StageChoice
{
    None,
    One,
}

internal sealed class StageDocument
{
    private StageChoice? _choice;
    private string? _text;
    private Dictionary<string, JsonElement>? _extensionData;

    public StageDocument() => StageRecorder.Mark(StageBoundary.InstanceCreated);

    public StageChoice? Choice
    {
        get => _choice;
        set
        {
            StageRecorder.Mark(StageBoundary.PropertyResolved | StageBoundary.PropertyAssigned);
            _choice = value;
        }
    }

    public string? Text
    {
        get => _text;
        set
        {
            StageRecorder.Mark(StageBoundary.PropertyResolved | StageBoundary.PropertyAssigned);
            _text = value;
        }
    }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData
    {
        get => _extensionData;
        set
        {
            StageRecorder.Mark(StageBoundary.ExtensionDataPublished);
            _extensionData = value;
        }
    }
}

[JsonSerializable(typeof(StageScalar))]
[JsonSerializable(typeof(StageDocument))]
internal sealed partial class GeneratedDeserializationStageContext : JsonSerializerContext
{
}
