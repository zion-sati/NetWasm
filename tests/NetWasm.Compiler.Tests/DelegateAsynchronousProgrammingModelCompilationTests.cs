// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// BeginInvoke and EndInvoke are runtime-provided delegate members. .NET Core throws
// PlatformNotSupportedException from both, and NetWasm must give the same catchable
// failure instead of rejecting the program at compile time.
public sealed class DelegateAsynchronousProgrammingModelCompilationTests
{
    public static TheoryData<bool, WasmTarget> Cases()
    {
        var cases = new TheoryData<bool, WasmTarget>();
        foreach (var optimize in new[] { false, true })
        {
            foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
            {
                cases.Add(optimize, target);
            }
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BeginInvokeAndEndInvokeThrowCatchablePlatformNotSupported(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            $"DelegateApm{(optimize ? "Release" : "Debug")}{target}",
            Source,
            "DelegateApm.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(42, observed);
    }

    internal const string Source = """
        using System;
        namespace DelegateApm;

        public delegate int Combine(int left, ref int right);
        public delegate T Echo<T>(T value);

        public static class EntryPoint
        {
            static bool callbackRan;

            static int Check(Action call, int code)
            {
                try
                {
                    call();
                    return code;
                }
                catch (PlatformNotSupportedException e) when (e.Message == "Operation is not supported on this platform.")
                {
                    return 0;
                }
            }

            public static int Run(int input)
            {
                Action action = () => { };
                Func<int, int> twice = value => value * 2;
                var offset = input;
                Combine combine = (int left, ref int right) => left + right + offset;
                Echo<string> echoString = value => value;
                Echo<long> echoLong = value => value + 1;
                Action multicast = action + (() => { });
                AsyncCallback callback = _ => callbackRan = true;
                var right = 1;

                var result = Check(() => action.BeginInvoke(callback, null), 1);
                if (result == 0) result = Check(() => action.EndInvoke(null!), 2);
                if (result == 0) result = Check(() => twice.BeginInvoke(3, callback, "state"), 3);
                if (result == 0) result = Check(() => twice.EndInvoke(null!), 4);
                if (result == 0) result = Check(() => combine.BeginInvoke(1, ref right, callback, null), 5);
                if (result == 0) result = Check(() => combine.EndInvoke(ref right, null!), 6);
                if (result == 0) result = Check(() => echoString.BeginInvoke("x", callback, null), 7);
                if (result == 0) result = Check(() => echoString.EndInvoke(null!), 8);
                if (result == 0) result = Check(() => echoLong.BeginInvoke(1L, callback, null), 9);
                if (result == 0) result = Check(() => echoLong.EndInvoke(null!), 10);
                if (result == 0) result = Check(() => multicast.BeginInvoke(callback, null), 11);
                if (result != 0) return result;
                if (callbackRan || right != 1) return 12;

                // The delegates still invoke synchronously.
                if (twice(4) != 8 || combine(1, ref right) != 2 + input || echoString("y") != "y" || echoLong(2) != 3) return 13;

                Action missing = null!;
                try
                {
                    missing.BeginInvoke(null, null);
                    return 14;
                }
                catch (NullReferenceException)
                {
                }

                return input + 1;
            }
        }
        """;
}
