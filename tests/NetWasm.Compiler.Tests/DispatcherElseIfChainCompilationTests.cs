// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Uri's authority parser reads a port in a loop inside a dispatcher; the loop's else-if chain is flat, and its arms
// that break leave for the dispatcher's next block from inside the chain's labeled block. Each URI's port and host
// match desktop's.
public sealed class DispatcherElseIfChainCompilationTests
{
    private const string Source = """
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var sum = 0;
                foreach (var text in new[]
                {
                    "http://example.com:8080/a", "https://h:443?q", "http://a.b:1#f", "ftp://x:21/", "http://h/p",
                    "http://[::1]:9000/", "http://127.0.0.1:65535/", "custom://host:12/p",
                })
                {
                    var uri = new System.Uri(text);
                    sum = sum * 31 + uri.Port + uri.Host.Length * 7 + input;
                }
                return sum;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void UriPortsParseAsOnDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"DispatcherElseIfChain_{target}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target));

        Assert.Equal(Expected(3), ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 3, target, result.StaticDataEnd));
    }

    private static int Expected(int input)
    {
        var sum = 0;
        foreach (var text in new[]
        {
            "http://example.com:8080/a", "https://h:443?q", "http://a.b:1#f", "ftp://x:21/", "http://h/p",
            "http://[::1]:9000/", "http://127.0.0.1:65535/", "custom://host:12/p",
        })
        {
            var uri = new Uri(text);
            sum = unchecked(sum * 31 + uri.Port + uri.Host.Length * 7 + input);
        }
        return sum;
    }
}
