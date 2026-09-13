using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawModuleImportSignatureReaderTests
{
    private static readonly RawModuleInspectionRequest Request = new(
        "/node", "/inspect.mjs", "/application.wasm", "/binaryen/index.js");

    [Fact]
    public void ReadPassesOneProcessResultToTheProtocolReader()
    {
        var process = new RecordingProcess();
        var protocol = new RecordingProtocol();
        var reader = Assert.IsAssignableFrom<IRawModuleImportSignatureReader>(
            new RawModuleImportSignatureReader(process, protocol));

        var result = reader.Read(Request);

        Assert.Same(Request, process.Request);
        Assert.Same(process.Result, protocol.Result);
        Assert.True(protocol.Imports.SequenceEqual(result));
        Assert.Equal(1, process.Calls);
        Assert.Equal(1, protocol.Calls);
    }

    [Fact]
    public void ConstructorAndReadRejectMissingInputsBeforeCollaboration()
    {
        var process = new RecordingProcess();
        var protocol = new RecordingProtocol();
        var reader = new RawModuleImportSignatureReader(process, protocol);

        Assert.Throws<ArgumentNullException>(() =>
            new RawModuleImportSignatureReader(null!, protocol));
        Assert.Throws<ArgumentNullException>(() =>
            new RawModuleImportSignatureReader(process, null!));
        Assert.Throws<ArgumentNullException>(() => reader.Read(null!));
        Assert.Equal(0, process.Calls);
        Assert.Equal(0, protocol.Calls);
    }

    [Fact]
    public void ProcessFailurePreventsProtocolParsing()
    {
        var cause = new InvalidOperationException("process failed");
        var process = new RecordingProcess { Failure = cause };
        var protocol = new RecordingProtocol();
        var reader = new RawModuleImportSignatureReader(process, protocol);

        Assert.Throws<InvalidOperationException>(() => reader.Read(Request));

        Assert.Equal(1, process.Calls);
        Assert.Equal(0, protocol.Calls);
    }

    [Fact]
    public void NullProcessProductPreventsProtocolParsing()
    {
        var process = new RecordingProcess { ReturnNull = true };
        var protocol = new RecordingProtocol();
        var reader = new RawModuleImportSignatureReader(process, protocol);

        Assert.Throws<ArgumentNullException>(() => reader.Read(Request));

        Assert.Equal(1, process.Calls);
        Assert.Equal(0, protocol.Calls);
    }

    [Fact]
    public void ProtocolFailureReturnsNoImportProduct()
    {
        var cause = new InvalidOperationException("protocol failed");
        var process = new RecordingProcess();
        var protocol = new RecordingProtocol { Failure = cause };
        var reader = new RawModuleImportSignatureReader(process, protocol);

        Assert.Throws<InvalidOperationException>(() => reader.Read(Request));

        Assert.Equal(1, process.Calls);
        Assert.Equal(1, protocol.Calls);
    }

    private sealed class RecordingProcess : IRawModuleInspectionProcess
    {
        public ToolResult Result { get; } = new(0, "output", "error");
        public RawModuleInspectionRequest? Request { get; private set; }
        public Exception? Failure { get; init; }
        public bool ReturnNull { get; init; }
        public int Calls { get; private set; }

        public ToolResult Run(RawModuleInspectionRequest request)
        {
            Calls++;
            Request = request;
            if (Failure is not null)
            {
                throw Failure;
            }
            return ReturnNull ? null! : Result;
        }
    }

    private sealed class RecordingProtocol : IRawModuleInspectionProtocolReader
    {
        public ImmutableArray<RawCoreFunctionImportSignature> Imports { get; } =
            [new(new("host", "call"), [RawCoreValueType.I32], [])];
        public ToolResult? Result { get; private set; }
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }

        public ImmutableArray<RawCoreFunctionImportSignature> Read(ToolResult result)
        {
            Calls++;
            Result = result;
            if (Failure is not null)
            {
                throw Failure;
            }
            return Imports;
        }
    }
}
