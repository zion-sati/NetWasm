using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.Tests;

public sealed class JavaScriptMarshalAsRulesTests
{
    private static readonly AssemblyIdentity Assembly = new("NetWasm.CoreLib");
    private static readonly CliTypeIdentity Void = Primitive("void", CliValueKind.Void);
    private static readonly CliTypeIdentity Boolean = Primitive("bool", CliValueKind.I4);
    private static readonly CliTypeIdentity Int32 = Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Int64 = Primitive("i8", CliValueKind.I8);
    private static readonly CliTypeIdentity UInt64 = Primitive("u8", CliValueKind.I8);
    private static readonly CliTypeIdentity Text = Named("System", "String", isValueType: false);
    private static readonly CliTypeIdentity Object = Named("System", "Object", isValueType: false);
    private static readonly CliTypeIdentity Exception = Named("System", "Exception", isValueType: false);
    private static readonly CliTypeIdentity JSObject =
        Named("System.Runtime.InteropServices.JavaScript", "JSObject", isValueType: false);
    private static readonly CliTypeIdentity Date = Named("System", "DateTime", isValueType: true);
    private static readonly CliTypeIdentity Opaque = Named("Sample", "Opaque", isValueType: true);
    private static readonly CliTypeIdentity Task = Named("System.Threading.Tasks", "Task", isValueType: false);
    private static readonly CliTypeIdentity Callback = Named("Sample", "Callback", isValueType: false);
    private static readonly CliTypeIdentity Notify = Named("Sample", "Notify", isValueType: false);

    [Fact]
    public void ComparesAnnotationsByTheirMarkers()
    {
        var annotation = new JavaScriptMarshalAs(["Number", null], "BigInt");

        Assert.Equal(annotation, new JavaScriptMarshalAs(["Number", null], "BigInt"));
        Assert.Equal(annotation.GetHashCode(), new JavaScriptMarshalAs(["Number", null], "BigInt").GetHashCode());
        Assert.NotEqual(annotation, new JavaScriptMarshalAs(["Number", null], "Number"));
        Assert.NotEqual(annotation, new JavaScriptMarshalAs(["String", null], "BigInt"));
        Assert.False(annotation.Equals(null));
        Assert.Equal("Number", annotation.Parameter(0));
        Assert.Null(annotation.Parameter(2));
    }

    [Theory]
    [InlineData("Boolean")]
    [InlineData("Discard")]
    public void AcceptsTheMarkersAScalarOrVoidTakes(string marker)
    {
        var type = marker == "Discard" ? Void : Boolean;

        Assert.Null(Mismatch(type, marker));
    }

    [Fact]
    public void AcceptsTheMarkersOfReferencesAndDates()
    {
        Assert.Null(Mismatch(UInt64, "BigInt"));
        Assert.Null(Mismatch(JSObject, "Object"));
        Assert.Null(Mismatch(Exception, "Error"));
        Assert.Null(Mismatch(Date, "Date"));
        Assert.Null(Mismatch(Nullable(Int32), "Number"));
        Assert.Null(Mismatch(Primitive("char", CliValueKind.I4), "String"));
        Assert.Null(Mismatch(Int64, "Number"));
        Assert.Null(Mismatch(Int64, "BigInt"));
        Assert.Null(Mismatch(Generic(Named("System", "Span`1", isValueType: true), Int32), "MemoryView"));
    }

    [Fact]
    public void NamesTheMarkerATypeTakesWhenAnotherIsGiven()
    {
        Assert.Equal("Boolean", Mismatch(Boolean, "Number"));
        Assert.Equal("Number", Mismatch(Nullable(Int32), "String"));
        Assert.Equal("Date", Mismatch(Nullable(Date), "Number"));
        Assert.Equal("no JSType", Mismatch(Nullable(Opaque), "Number"));
        Assert.Equal("no JSType", Mismatch(Opaque, "Any"));
        Assert.Equal("Number", Mismatch(Int32, "Number<Number>"));
        Assert.Equal("Number", Mismatch(Int32, "Number>"));
    }

    [Fact]
    public void MatchesADelegateWithAFunctionOfItsValues()
    {
        Assert.Null(Mismatch(Callback, "Function"));
        Assert.Null(Mismatch(Callback, "Function<String,Number>"));
        Assert.Null(Mismatch(Notify, "Function"));
        Assert.Equal("Function<String,Number>", Mismatch(Callback, "Number"));
        Assert.Equal("Function<String,Number>", Mismatch(Callback, "Function<String>"));
        Assert.Equal("Function<String,Number>", Mismatch(Callback, "Function<Number,Number>"));
        Assert.Equal("Function", Mismatch(Notify, "Function<Number>"));
    }

    [Fact]
    public void MatchesATaskWithAPromiseOfItsResult()
    {
        var numbers = Generic(Task, Int32);

        Assert.Null(Mismatch(Task, "Promise<Void>"));
        Assert.Null(Mismatch(numbers, "Promise<Number>"));
        Assert.Equal("Promise<Void>", Mismatch(Task, "Promise<Void<Number>>"));
        Assert.Equal("Promise<Void>", Mismatch(Task, "Promise<Number>"));
        Assert.Equal("Promise<Void>", Mismatch(Task, "Promise"));
        Assert.Equal("Promise<Number>", Mismatch(numbers, "Promise<String>"));
        Assert.Equal("Promise<Number>", Mismatch(numbers, "Promise<Number,Number>"));
        Assert.Equal("Promise<Number>", Mismatch(numbers, "Number"));
    }

    [Fact]
    public void MatchesAnArrayWithAnArrayOfItsElements()
    {
        Assert.Null(Mismatch(CliTypeIdentity.SzArray(Int32), "Array<Number>"));
        Assert.Null(Mismatch(CliTypeIdentity.SzArray(Text), "Array<String>"));
        Assert.Equal("Array<Number>", Mismatch(CliTypeIdentity.SzArray(Int32), "Array"));
        Assert.Equal("Array<String>", Mismatch(CliTypeIdentity.SzArray(Text), "Number"));
        Assert.Equal("Array<Any>", Mismatch(CliTypeIdentity.SzArray(Object), "Array<String>"));
        Assert.Equal("Any", Mismatch(Object, "Array<Any>"));
    }

    [Fact]
    public void DescribesAndSplitsMarkersAsCSharpWritesThem()
    {
        Assert.Equal("no JSType", JavaScriptMarshalAsRules.Describe("no JSType"));
        Assert.Equal(
            "JSType.Function<JSType.Number, JSType.Promise<JSType.String>>",
            JavaScriptMarshalAsRules.Describe("Function<Number,Promise<String>>"));
        Assert.Equal(
            ["Number", "Promise<String>"],
            JavaScriptMarshalAsRules.Arguments("Function<Number,Promise<String>>").ToArray());
        Assert.Empty(JavaScriptMarshalAsRules.Arguments(null));
    }

    [Fact]
    public void SelectsInt52OnlyForANumberMarkedLong()
    {
        Assert.True(JavaScriptMarshalAsRules.SelectsInt52(Int64, "Number"));
        Assert.True(JavaScriptMarshalAsRules.SelectsInt52(Nullable(Int64), "Number"));
        Assert.False(JavaScriptMarshalAsRules.SelectsInt52(Int64, "BigInt"));
        Assert.False(JavaScriptMarshalAsRules.SelectsInt52(Int32, "Number"));
    }

    private static string? Mismatch(CliTypeIdentity type, string marker) =>
        JavaScriptMarshalAsRules.Mismatch(type, marker, Invoke);

    // Callback is Func<string, int> and Notify is Action.
    private static MethodSignatureModel? Invoke(CliTypeIdentity type) =>
        type.Equals(Callback) ? new MethodSignatureModel(Int32, [Text])
        : type.Equals(Notify) ? new MethodSignatureModel(Void, [])
        : null;

    private static CliTypeIdentity Nullable(CliTypeIdentity underlying) =>
        Generic(Named("System", "Nullable`1", isValueType: true), underlying);

    private static CliTypeIdentity Generic(CliTypeIdentity definition, CliTypeIdentity argument) =>
        CliTypeIdentity.GenericInstantiation(definition, [argument]);

    private static CliTypeIdentity Primitive(string name, CliValueKind kind) => CliTypeIdentity.Primitive(name, kind);

    private static CliTypeIdentity Named(string @namespace, string name, bool isValueType) =>
        CliTypeIdentity.Named(Assembly, @namespace, name, isValueType);
}
