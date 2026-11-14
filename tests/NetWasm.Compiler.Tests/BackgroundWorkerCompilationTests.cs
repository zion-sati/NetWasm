using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class BackgroundWorkerCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void WorkersPreserveReactorProgressCancellationAndCompletion(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "CategoryFixture", Source, "CategoryFixture.EntryPoint", false, target, 41, [])
        {
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/platform@1.0.0",
            ObserveMethodName = "Observe",
        }));
    }

    private const string Source = """
        using System;
        using System.ComponentModel;
        using System.Reflection;
        namespace CategoryFixture;
        public static class EntryPoint
        {
            private static int _completed, _progress, _work, _failures, _validation;
            private static object _state;
            private static Exception _error;
            public static int Run(int input)
            {
                _completed = _progress = _work = _failures = _validation = 0;
                _state = new object(); _error = new Exception("worker failure");
                var normal = new BackgroundWorker();
                if (normal.IsBusy || normal.CancellationPending || normal.WorkerReportsProgress || normal.WorkerSupportsCancellation) _failures++;
                try { normal.CancelAsync(); } catch (InvalidOperationException) { _validation++; }
                try { normal.ReportProgress(1); } catch (InvalidOperationException) { _validation++; }
                normal.WorkerReportsProgress = true;
                normal.ProgressChanged += (sender, args) => { if (sender == normal && args.ProgressPercentage == 35 && args.UserState == _state) _progress++; else _failures++; };
                normal.DoWork += (sender, args) => { _work++; if (args.Argument != _state || !normal.IsBusy) _failures++; normal.ReportProgress(35, _state); args.Result = 42; };
                normal.RunWorkerCompleted += (sender, args) => { _completed++; if (normal.IsBusy || normal.CancellationPending || args.Cancelled || args.Error != null || (int)args.Result != 42 || _progress != 1) _failures++; };
                normal.RunWorkerAsync(_state);
                if (!normal.IsBusy || _work != 0) _failures++;
                try { normal.RunWorkerAsync(); } catch (InvalidOperationException) { _validation++; }
                var cancelled = new BackgroundWorker { WorkerSupportsCancellation = true };
                cancelled.DoWork += (sender, args) => { _work++; if (!cancelled.CancellationPending) _failures++; args.Cancel = true; };
                cancelled.RunWorkerCompleted += (sender, args) => {
                    _completed++; if (!args.Cancelled || args.Error != null || cancelled.IsBusy || cancelled.CancellationPending) _failures++;
                    try { var ignored = args.Result; _failures++; } catch (InvalidOperationException) { }
                };
                cancelled.RunWorkerAsync(); cancelled.CancelAsync();
                var failed = new BackgroundWorker();
                failed.DoWork += (sender, args) => { _work++; throw _error; };
                failed.RunWorkerCompleted += (sender, args) => {
                    _completed++; if (args.Error != _error || args.Cancelled || failed.IsBusy) _failures++;
                    try { var ignored = args.Result; _failures++; } catch (TargetInvocationException e) { if (e.InnerException != _error) _failures++; }
                };
                failed.RunWorkerAsync();
                return 0;
            }
            public static int Observe() => _completed == 3 && _work == 3 && _progress == 1 && _validation == 3 && _failures == 0 ? 42 : 100 + _failures;
        }
        """;
}
