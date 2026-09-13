using System.Text;
using System.Text.Json;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

public sealed class NetWasmExecutionRequestWriterTests
{
    [Fact]
    public void WritesDeterministicTransportSafeUtf8AndRoundTrips()
    {
        var request = ExecutionRequestFixture.Create();
        var subject = Assert.IsAssignableFrom<INetWasmExecutionRequestWriter>(
            new NetWasmExecutionRequestWriter(new NetWasmExecutionRequestValidator()));
        var bytes = subject.Write(request);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Equal(bytes, subject.Write(request));
        Assert.StartsWith("{\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
        Assert.Equal((byte)'{', bytes[0]);
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal("denyAll", json.RootElement.GetProperty("grants").GetProperty("network").GetString());
        Assert.Equal("readOnly", json.RootElement.GetProperty("grants").GetProperty("preopens")[0].GetProperty("access").GetString());
        Assert.Equal("wall", json.RootElement.GetProperty("grants").GetProperty("clocks")[0].GetString());
        var restored = new NetWasmExecutionRequestReader(new NetWasmExecutionRequestValidator()).Read(bytes);
        Assert.Equal(request.BuildFingerprint, restored.BuildFingerprint);
        Assert.Equal<string>(request.Arguments, restored.Arguments);
        Assert.Equal<NetWasmEnvironmentVariable>(request.Environment.OrderBy(item => item.Name), restored.Environment);
        Assert.Equal<string>(request.Grants.Environment.Order(), restored.Grants.Environment);
        Assert.Equal<NetWasmApplicationImport>(request.ApplicationImports.OrderBy(item => item.Module), restored.ApplicationImports);
    }

    [Fact]
    public void CanonicalizesUnorderedCollectionsButPreservesArgumentsWithoutMutatingInput()
    {
        var request = ExecutionRequestFixture.Create();
        var environment = request.Environment.Reverse().ToArray();
        var grantEnvironment = request.Grants.Environment.Reverse().ToArray();
        var preopens = request.Grants.Preopens.Reverse().ToArray();
        var clocks = request.Grants.Clocks.Reverse().ToArray();
        var imports = request.ApplicationImports.Reverse().ToArray();
        var arguments = new[] { "z-last", "a-first" };
        request = request with
        {
            Arguments = [.. arguments],
            Environment = [.. environment],
            Grants = request.Grants with
            {
                Environment = [.. grantEnvironment],
                Preopens = [.. preopens],
                Clocks = [.. clocks],
            },
            ApplicationImports = [.. imports],
        };
        var canonical = request with
        {
            Environment = [.. environment.Reverse()],
            Grants = request.Grants with
            {
                Environment = [.. grantEnvironment.Reverse()],
                Preopens = [.. preopens.Reverse()],
                Clocks = [.. clocks.Reverse()],
            },
            ApplicationImports = [.. imports.Reverse()],
        };
        var subject = new NetWasmExecutionRequestWriter(new NetWasmExecutionRequestValidator());
        Assert.Equal(subject.Write(canonical), subject.Write(request));
        Assert.Equal(arguments, request.Arguments);
        Assert.Same(environment[0], request.Environment[0]);
        Assert.Equal(grantEnvironment[0], request.Grants.Environment[0]);
        Assert.Same(preopens[0], request.Grants.Preopens[0]);
        Assert.Equal(clocks[0], request.Grants.Clocks[0]);
        Assert.Same(imports[0], request.ApplicationImports[0]);
    }

    [Fact]
    public void PropagatesValidationFailureBeforeSerializing()
    {
        var request = ExecutionRequestFixture.Create();
        var failure = new ArgumentException("Rejected request.");
        var subject = new NetWasmExecutionRequestWriter(new ExecutionRequestValidationStub(value =>
        {
            Assert.Same(request, value);
            throw failure;
        }));
        Assert.Same(failure, Assert.Throws<ArgumentException>(() => subject.Write(request)));
    }

    [Fact]
    public void RejectsNullInputBeforeDelegation()
    {
        var subject = new NetWasmExecutionRequestWriter(
            new ExecutionRequestValidationStub(_ => Assert.Fail("Must not delegate.")));
        Assert.Throws<ArgumentNullException>(() => subject.Write(null!));
    }

    [Fact]
    public void RejectsMissingValidator() =>
        Assert.Throws<ArgumentNullException>(() => new NetWasmExecutionRequestWriter(null!));
}
