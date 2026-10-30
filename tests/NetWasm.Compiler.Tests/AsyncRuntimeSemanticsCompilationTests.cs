using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AsyncRuntimeSemanticsCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CompilerPreservesSingleReactorAsyncSemantics(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;
            using System.Threading.Tasks.Sources;

            namespace AsyncRuntimeSemanticsFixture;

            public static class EntryPoint
            {
                private static readonly AsyncLocal<int> Local = new();
                private static CancellationTokenSource? _scheduledCancellation;
                private static Task? _scheduledContinuation;
                private static Task<bool>? _periodicTick;
                private static Task<int>? _forcedYield;
                private static Task<int>? _whenEachOrder;
                private static int _failure;
                private static int _capturedContextPosts;
                private static bool _lateValueTaskSourceContinuation;
                private static bool _delayedCancellationContinuation;

                public static int Run(int input)
                {
                    _failure = 0;
                    Check(Task.WhenAll(Task.CompletedTask, Task.FromResult(input)).IsCompletedSuccessfully, 1);
                    Check(Task.WhenAny(Task.CompletedTask, Task.FromResult(input)).IsCompletedSuccessfully, 2);
                    var singleWhenAll = Task.FromResult(input);
                    Check(ReferenceEquals(
                        Task.WhenAll(new Task[] { singleWhenAll }),
                        singleWhenAll), 107);

                    var firstAnySource = new TaskCompletionSource();
                    var secondAnySource = new TaskCompletionSource();
                    var anyInputs = new[]
                    {
                        firstAnySource.Task,
                        secondAnySource.Task,
                    };
                    var snapshotAny = Task.WhenAny(anyInputs);
                    anyInputs[1] = Task.CompletedTask;
                    secondAnySource.SetResult();
                    Check(snapshotAny.IsCompletedSuccessfully &&
                        ReferenceEquals(snapshotAny.Result, secondAnySource.Task), 57);

                    var invalidAllSource = new TaskCompletionSource();
                    try
                    {
                        _ = Task.WhenAll(new Task[]
                        {
                            invalidAllSource.Task,
                            null!,
                        });
                        Check(false, 58);
                    }
                    catch (ArgumentException)
                    {
                    }
                    invalidAllSource.SetResult();

                    var invalidGenericAllSource = new TaskCompletionSource<int>();
                    try
                    {
                        _ = Task.WhenAll(new Task<int>[]
                        {
                            invalidGenericAllSource.Task,
                            null!,
                        });
                        Check(false, 59);
                    }
                    catch (ArgumentException)
                    {
                    }
                    invalidGenericAllSource.SetResult(input);

                    try
                    {
                        _ = Task.WhenAny(new Task[]
                        {
                            Task.CompletedTask,
                            null!,
                        });
                        Check(false, 60);
                    }
                    catch (ArgumentException)
                    {
                    }

                    var synchronousContinuation = Task.CompletedTask.ContinueWith(
                        _ => Check(Task.CurrentId.HasValue, 3),
                        TaskContinuationOptions.ExecuteSynchronously);
                    Check(synchronousContinuation.IsCompletedSuccessfully, 4);

                    var synchronousTask = new Task<int>(() =>
                        Task.CurrentId.HasValue ? input + 1 : -1);
                    synchronousTask.RunSynchronously();
                    Check(synchronousTask.IsCompletedSuccessfully, 29);
                    Check(synchronousTask.Result == input + 1, 33);

                    try
                    {
                        _ = new Task(
                            static () => { },
                            TaskCreationOptions.AttachedToParent);
                        Check(false, 91);
                    }
                    catch (PlatformNotSupportedException)
                    {
                    }
                    try
                    {
                        _ = new Task<int>(
                            static () => 1,
                            (TaskCreationOptions)128);
                        Check(false, 92);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                    }
                    var hintedTask = new Task(
                        static () => { },
                        TaskCreationOptions.PreferFairness |
                        TaskCreationOptions.LongRunning |
                        TaskCreationOptions.DenyChildAttach |
                        TaskCreationOptions.HideScheduler);
                    Check(hintedTask.CreationOptions ==
                        (TaskCreationOptions.PreferFairness |
                            TaskCreationOptions.LongRunning |
                            TaskCreationOptions.DenyChildAttach |
                            TaskCreationOptions.HideScheduler), 93);

                    try
                    {
                        _ = Task.WhenEach(new Task[]
                        {
                            Task.CompletedTask,
                            null!,
                        });
                        Check(false, 94);
                    }
                    catch (ArgumentException)
                    {
                    }

                    var firstEachSource = new TaskCompletionSource<int>();
                    var secondEachSource = new TaskCompletionSource<int>();
                    _whenEachOrder = ObserveWhenEach(Task.WhenEach(
                        firstEachSource.Task,
                        secondEachSource.Task));
                    secondEachSource.SetResult(2);
                    firstEachSource.SetResult(1);

                    var builder = AsyncTaskMethodBuilder.Create();
                    var builderTask = builder.Task;
                    Check(ReferenceEquals(builderTask, builder.Task) &&
                        !builderTask.IsCompleted, 96);
                    builder.SetResult();
                    Check(builderTask.IsCompletedSuccessfully, 97);

                    var synchronousBuilder = AsyncTaskMethodBuilder.Create();
                    synchronousBuilder.SetResult();
                    Check(ReferenceEquals(
                        synchronousBuilder.Task,
                        Task.CompletedTask), 98);

                    var genericBuilder = AsyncTaskMethodBuilder<int>.Create();
                    var genericBuilderTask = genericBuilder.Task;
                    genericBuilder.SetResult(input);
                    Check(ReferenceEquals(genericBuilderTask, genericBuilder.Task) &&
                        genericBuilderTask.Result == input, 99);

                    var synchronousGenericBuilder =
                        AsyncTaskMethodBuilder<int>.Create();
                    synchronousGenericBuilder.SetResult(1);
                    Check(ReferenceEquals(
                        synchronousGenericBuilder.Task,
                        Task.FromResult(1)), 100);

                    Check(ReferenceEquals(Task.FromResult(true),
                        Task.FromResult(true)), 101);
                    Check(ReferenceEquals(Task.FromResult(-1),
                        Task.FromResult(-1)) &&
                        ReferenceEquals(Task.FromResult(8),
                            Task.FromResult(8)) &&
                        !ReferenceEquals(Task.FromResult(-2),
                            Task.FromResult(-2)) &&
                        !ReferenceEquals(Task.FromResult(9),
                            Task.FromResult(9)), 102);
                    Check(ReferenceEquals(Task.FromResult<string?>(null),
                        Task.FromResult<string?>(null)) &&
                        ReferenceEquals(Task.FromResult<int?>(null),
                            Task.FromResult<int?>(null)), 103);
                    Check(ReferenceEquals(Task.FromResult((byte)0),
                            Task.FromResult((byte)0)) &&
                        ReferenceEquals(Task.FromResult((ushort)0),
                            Task.FromResult((ushort)0)) &&
                        ReferenceEquals(Task.FromResult(0u),
                            Task.FromResult(0u)) &&
                        ReferenceEquals(Task.FromResult(0ul),
                            Task.FromResult(0ul)) &&
                        ReferenceEquals(Task.FromResult(Guid.Empty),
                            Task.FromResult(Guid.Empty)), 104);
                    Check(!ReferenceEquals(Task.FromResult(0d),
                            Task.FromResult(-0d)) &&
                        !ReferenceEquals(Task.FromResult(
                                default(ReferenceContainingResult)),
                            Task.FromResult(
                                default(ReferenceContainingResult))), 105);
                    Check(ReferenceEquals(ReturnOne(), Task.FromResult(1)), 106);
                    Check(ExerciseAsyncLocalMapBoundaries(), 108);
                    Check(ExerciseAsyncLocalNotificationOrder(), 109);

                    var genericAwaitable = new ManualAwaitable();
                    var genericReuseTask = AwaitTwice(
                        genericAwaitable,
                        returnValue: 78);
                    var firstGenericContinuation = genericAwaitable.Continuation;
                    genericAwaitable.Resume();
                    var secondGenericContinuation = genericAwaitable.Continuation;
                    genericAwaitable.Resume();
                    Check(firstGenericContinuation is not null &&
                        ReferenceEquals(
                            firstGenericContinuation,
                            secondGenericContinuation) &&
                        genericReuseTask.IsCompletedSuccessfully &&
                        genericReuseTask.Result == 78, 110);

                    var voidAwaitable = new ManualAwaitable();
                    var voidReuseTask = AwaitTwice(voidAwaitable);
                    var firstVoidContinuation = voidAwaitable.Continuation;
                    voidAwaitable.Resume();
                    var secondVoidContinuation = voidAwaitable.Continuation;
                    voidAwaitable.Resume();
                    Check(firstVoidContinuation is not null &&
                        ReferenceEquals(
                            firstVoidContinuation,
                            secondVoidContinuation) &&
                        voidReuseTask.IsCompletedSuccessfully, 111);

                    var unmatchedCancellation = new Task(
                        static () => throw new OperationCanceledException());
                    unmatchedCancellation.RunSynchronously();
                    Check(unmatchedCancellation.IsFaulted &&
                        !unmatchedCancellation.IsCanceled, 61);

                    var matchingCancellationSource = new CancellationTokenSource();
                    var matchingCancellation = new Task(
                        () =>
                        {
                            matchingCancellationSource.Cancel();
                            throw new OperationCanceledException(
                                matchingCancellationSource.Token);
                        },
                        matchingCancellationSource.Token);
                    matchingCancellation.RunSynchronously();
                    Check(matchingCancellation.IsCanceled &&
                        matchingCancellation.Exception is null, 62);

                    var mismatchedCancellationSource = new CancellationTokenSource();
                    var otherCancellationSource = new CancellationTokenSource();
                    var mismatchedCancellation = new Task<int>(
                        () =>
                        {
                            mismatchedCancellationSource.Cancel();
                            throw new OperationCanceledException(
                                otherCancellationSource.Token);
                        },
                        mismatchedCancellationSource.Token);
                    mismatchedCancellation.RunSynchronously();
                    Check(mismatchedCancellation.IsFaulted &&
                        !mismatchedCancellation.IsCanceled, 63);

                    var copiedCancellation = new TaskCompletionSource();
                    Check(copiedCancellation.TrySetFromTask(matchingCancellation), 64);
                    try
                    {
                        copiedCancellation.Task.GetAwaiter().GetResult();
                        Check(false, 65);
                    }
                    catch (TaskCanceledException exception)
                    {
                        Check(exception.CancellationToken ==
                            matchingCancellationSource.Token, 66);
                    }

                    var canceledGenericSource = new TaskCompletionSource<int>();
                    canceledGenericSource.SetCanceled(
                        matchingCancellationSource.Token);
                    var copiedGenericCancellation = new TaskCompletionSource<int>();
                    Check(copiedGenericCancellation.TrySetFromTask(
                        canceledGenericSource.Task), 73);
                    try
                    {
                        _ = copiedGenericCancellation.Task.GetAwaiter().GetResult();
                        Check(false, 74);
                    }
                    catch (TaskCanceledException exception)
                    {
                        Check(exception.CancellationToken ==
                            matchingCancellationSource.Token, 75);
                    }

                    var incompleteCopySource = new TaskCompletionSource();
                    try
                    {
                        _ = new TaskCompletionSource().TrySetFromTask(
                            incompleteCopySource.Task);
                        Check(false, 67);
                    }
                    catch (ArgumentException)
                    {
                    }

                    var incompleteGenericCopySource = new TaskCompletionSource<int>();
                    try
                    {
                        _ = new TaskCompletionSource<int>().TrySetFromTask(
                            incompleteGenericCopySource.Task);
                        Check(false, 68);
                    }
                    catch (ArgumentException)
                    {
                    }

                    try
                    {
                        _ = new TaskCompletionSource().TrySetException(
                            Array.Empty<Exception>());
                        Check(false, 69);
                    }
                    catch (ArgumentException)
                    {
                    }

                    try
                    {
                        _ = new TaskCompletionSource<int>().TrySetException(
                            new Exception[] { new InvalidOperationException(), null! });
                        Check(false, 70);
                    }
                    catch (ArgumentException)
                    {
                    }

                    var waitCancellationSource = new TaskCompletionSource();
                    var copiedWaitCancellation = waitCancellationSource.Task.WaitAsync(
                        Timeout.InfiniteTimeSpan);
                    waitCancellationSource.SetCanceled(
                        matchingCancellationSource.Token);
                    try
                    {
                        copiedWaitCancellation.GetAwaiter().GetResult();
                        Check(false, 71);
                    }
                    catch (TaskCanceledException exception)
                    {
                        Check(exception.CancellationToken ==
                            matchingCancellationSource.Token, 72);
                    }

                    var genericWaitCancellationSource =
                        new TaskCompletionSource<int>();
                    var copiedGenericWaitCancellation =
                        genericWaitCancellationSource.Task.WaitAsync(
                            Timeout.InfiniteTimeSpan);
                    genericWaitCancellationSource.SetCanceled(
                        matchingCancellationSource.Token);
                    try
                    {
                        _ = copiedGenericWaitCancellation.GetAwaiter().GetResult();
                        Check(false, 76);
                    }
                    catch (TaskCanceledException exception)
                    {
                        Check(exception.CancellationToken ==
                            matchingCancellationSource.Token, 77);
                    }

                    Task.FromException(new InvalidOperationException())
                        .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)
                        .GetAwaiter()
                        .GetResult();

                    var firstFailure = new InvalidOperationException();
                    var secondFailure = new ArgumentException();
                    var faultSource = new TaskCompletionSource();
                    faultSource.SetException(new Exception[]
                    {
                        firstFailure,
                        secondFailure,
                    });
                    Check(faultSource.Task.Exception is not null &&
                        faultSource.Task.Exception.InnerExceptions.Count == 2, 39);
                    try
                    {
                        faultSource.Task.GetAwaiter().GetResult();
                        Check(false, 40);
                    }
                    catch (InvalidOperationException exception)
                    {
                        Check(ReferenceEquals(exception, firstFailure), 41);
                    }

                    var userAggregate = new AggregateException(firstFailure, secondFailure);
                    var aggregateTask = Task.FromException(userAggregate);
                    Check(aggregateTask.Exception is not null &&
                        aggregateTask.Exception.InnerExceptions.Count == 1 &&
                        ReferenceEquals(
                            aggregateTask.Exception.InnerExceptions[0],
                            userAggregate), 42);

                    var allFaults = Task.WhenAll(
                        Task.FromException(firstFailure),
                        Task.FromException(secondFailure));
                    Check(allFaults.Exception is not null &&
                        allFaults.Exception.InnerExceptions.Count == 2, 43);
                    try
                    {
                        _ = Task.FromResult(input).ConfigureAwait(
                            ConfigureAwaitOptions.SuppressThrowing);
                        Check(false, 30);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                    }
                    _forcedYield = ForceYield(input);
                    Check(!_forcedYield.IsCompleted, 31);

                    _scheduledContinuation = Task.CompletedTask.ContinueWith(_ => { });
                    Check(!_scheduledContinuation.IsCompleted, 5);
                    Check(Task.Delay(TimeSpan.FromTicks(1)).IsCompletedSuccessfully, 6);
                    var timeoutSource = new TaskCompletionSource();
                    Check(timeoutSource.Task.WaitAsync(TimeSpan.Zero).IsFaulted, 22);

                    Local.Value = 11;
                    var flowSource = new TaskCompletionSource<int>();
                    var flowed = ObserveFlow(flowSource.Task);
                    Local.Value = 22;
                    flowSource.SetResult(input);
                    Check(flowed.IsCompletedSuccessfully && flowed.Result == 11, 7);
                    Check(Local.Value == 22, 8);

                    Local.Value = 61;
                    var outerContext = new RecordingSynchronizationContext();
                    var innerContext = new RecordingSynchronizationContext();
                    SynchronizationContext.SetSynchronizationContext(outerContext);
                    var firstStepSource = new TaskCompletionSource<int>();
                    var firstStep = MutateFirstStep(
                        firstStepSource.Task,
                        innerContext);
                    Check(Local.Value == 61, 44);
                    Check(ReferenceEquals(
                        SynchronizationContext.Current,
                        outerContext), 45);
                    SynchronizationContext.SetSynchronizationContext(null);
                    firstStepSource.SetResult(input);
                    Check(firstStep.IsCompletedSuccessfully &&
                        firstStep.Result == 62, 46);

                    var suppressionSource = new TaskCompletionSource<int>();
                    Task<int> suppressionStep;
                    using (ExecutionContext.SuppressFlow())
                    {
                        suppressionStep = RestoreFlowInFirstStep(
                            suppressionSource.Task);
                        Check(ExecutionContext.IsFlowSuppressed(), 47);
                        suppressionSource.SetResult(input);
                        Check(suppressionStep.IsCompletedSuccessfully, 48);
                    }

                    Local.Value = 51;
                    var suppressedSource = new TaskCompletionSource<int>();
                    Task<int> suppressedFlow;
                    using (ExecutionContext.SuppressFlow())
                    {
                        suppressedFlow = ObserveFlow(suppressedSource.Task);
                    }
                    Local.Value = 52;
                    suppressedSource.SetResult(input);
                    Check(suppressedFlow.IsCompletedSuccessfully &&
                        suppressedFlow.Result == 52, 25);

                    var firstSuppression = ExecutionContext.SuppressFlow();
                    var staleSuppression = firstSuppression;
                    firstSuppression.Dispose();
                    using (ExecutionContext.SuppressFlow())
                    {
                        try
                        {
                            staleSuppression.Undo();
                            Check(false, 26);
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    }

                    var synchronizationContext = new RecordingSynchronizationContext();
                    SynchronizationContext.SetSynchronizationContext(synchronizationContext);
                    var noCaptureSource = new TaskCompletionSource<int>();
                    var noCapture = ObserveSynchronizationContext(noCaptureSource.Task);
                    SynchronizationContext.SetSynchronizationContext(null);
                    noCaptureSource.SetResult(input);
                    Check(noCapture.IsCompletedSuccessfully && noCapture.Result == 0, 9);
                    Check(synchronizationContext.PostCount == 0, 10);

                    var capturedSource = new TaskCompletionSource();
                    SynchronizationContext.SetSynchronizationContext(synchronizationContext);
                    capturedSource.Task.GetAwaiter().OnCompleted(
                        () => _capturedContextPosts = synchronizationContext.PostCount);
                    SynchronizationContext.SetSynchronizationContext(null);
                    capturedSource.SetResult();
                    Check(synchronizationContext.PostCount == 1, 11);

                    var asyncVoidSource = new TaskCompletionSource();
                    SynchronizationContext.SetSynchronizationContext(synchronizationContext);
                    ThrowFromAsyncVoid(asyncVoidSource.Task);
                    SynchronizationContext.SetSynchronizationContext(null);
                    asyncVoidSource.SetResult();
                    Check(synchronizationContext.OperationStartedCount == 1 &&
                        synchronizationContext.OperationCompletedCount == 1 &&
                        synchronizationContext.ExceptionCount == 1, 24);

                    var valueSource = new FlowingValueTaskSource();
                    Local.Value = 31;
                    valueSource.Register();
                    Local.Value = 32;
                    valueSource.Complete(input);
                    Check(valueSource.ObservedValue == 31 && Local.Value == 32, 12);

                    ManualResetValueTaskSourceCore<int> lateValueSource = default;
                    lateValueSource.SetResult(input);
                    lateValueSource.OnCompleted(
                        _ => _lateValueTaskSourceContinuation = true,
                        null,
                        lateValueSource.Version,
                        ValueTaskSourceOnCompletedFlags.None);
                    Check(!_lateValueTaskSourceContinuation, 49);

                    ManualResetValueTaskSourceCore<int> duplicateValueSource = default;
                    duplicateValueSource.OnCompleted(
                        static _ => { },
                        null,
                        duplicateValueSource.Version,
                        ValueTaskSourceOnCompletedFlags.None);
                    try
                    {
                        duplicateValueSource.OnCompleted(
                            static _ => { },
                            null,
                            duplicateValueSource.Version,
                            ValueTaskSourceOnCompletedFlags.None);
                        Check(false, 50);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    duplicateValueSource.SetResult(input);

                    var cancellation = new CancellationTokenSource();
                    var safeValue = 0;
                    var unsafeValue = 0;
                    Local.Value = 41;
                    cancellation.Token.Register(() => safeValue = Local.Value);
                    cancellation.Token.UnsafeRegister(_ => unsafeValue = Local.Value, null);
                    Local.Value = 42;
                    cancellation.Cancel();
                    Check(safeValue == 41 && unsafeValue == 42, 13);

                    try
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        Check(false, 14);
                    }
                    catch (OperationCanceledException exception)
                    {
                        Check(exception.CancellationToken == cancellation.Token, 15);
                    }

                    var canceledTask = Task.FromCanceled(cancellation.Token);
                    Check(timeoutSource.Task.WaitAsync(cancellation.Token).IsCanceled, 23);
                    try
                    {
                        canceledTask.GetAwaiter().GetResult();
                        Check(false, 16);
                    }
                    catch (TaskCanceledException exception)
                    {
                        Check(exception.CancellationToken == cancellation.Token, 17);
                    }

                    _scheduledCancellation = new CancellationTokenSource();
                    _scheduledCancellation.CancelAfter(0);
                    Check(!_scheduledCancellation.IsCancellationRequested, 18);

                    using (var zeroDelay = new CancellationTokenSource(
                        TimeSpan.Zero))
                    {
                        Check(zeroDelay.IsCancellationRequested, 78);
                    }
                    using (var zeroMilliseconds = new CancellationTokenSource(0))
                    {
                        Check(zeroMilliseconds.IsCancellationRequested, 79);
                    }

                    var longDelayProvider = new ManualTimeProvider();
                    var longDelay = TimeSpan.FromMilliseconds(
                        (long)int.MaxValue + 10L);
                    using (var longDelaySource = new CancellationTokenSource(
                        longDelay,
                        longDelayProvider))
                    {
                        Check(longDelayProvider.CreateCount == 1 &&
                            longDelayProvider.LastDueTime == longDelay &&
                            longDelayProvider.WasFlowSuppressed, 80);
                        longDelaySource.CancelAfter(3);
                        longDelaySource.CancelAfter(4);
                        Check(longDelayProvider.CreateCount == 1 &&
                            longDelayProvider.ChangeCount == 2 &&
                            longDelayProvider.LastDueTime ==
                                TimeSpan.FromMilliseconds(4), 81);
                        Check(!longDelaySource.TryReset(), 82);
                    }

                    var resetCancellation = new CancellationTokenSource(
                        TimeSpan.FromMinutes(1));
                    var resetCallbackInvoked = false;
                    resetCancellation.Token.Register(
                        () => resetCallbackInvoked = true);
                    Check(resetCancellation.TryReset(), 83);
                    resetCancellation.Cancel();
                    Check(!resetCallbackInvoked, 84);
                    resetCancellation.Dispose();

                    var reentrantCancellation = new CancellationTokenSource();
                    var reentrantUnregistered = true;
                    CancellationTokenRegistration reentrantRegistration = default;
                    reentrantRegistration = reentrantCancellation.Token.Register(
                        () => reentrantUnregistered =
                            reentrantRegistration.Unregister());
                    reentrantCancellation.Cancel();
                    Check(!reentrantUnregistered, 85);

                    var delayedCancellation = new CancellationTokenSource();
                    var canceledDelay = Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        delayedCancellation.Token);
                    canceledDelay.GetAwaiter().OnCompleted(
                        () => _delayedCancellationContinuation = true);
                    delayedCancellation.Cancel();
                    Check(canceledDelay.IsCanceled &&
                        !_delayedCancellationContinuation, 86);
                    using (var stoppedTimer = new PeriodicTimer(
                        Timeout.InfiniteTimeSpan))
                    {
                        var stoppedTick = stoppedTimer.WaitForNextTickAsync();
                        Check(!stoppedTick.IsCompleted, 27);
                        stoppedTimer.Dispose();
                        Check(stoppedTick.IsCompletedSuccessfully &&
                            !stoppedTick.Result, 52);
                    }
                    _periodicTick = ObservePeriodicTick(
                        new PeriodicTimer(TimeSpan.FromMilliseconds(1)));

                    var timeProvider = new ManualTimeProvider();
                    var fixedTimeProvider = new FixedTimeProvider();
                    Check(fixedTimeProvider.GetLocalNow() ==
                        fixedTimeProvider.GetUtcNow() &&
                        ReferenceEquals(
                            fixedTimeProvider.LocalTimeZone,
                            TimeZoneInfo.Utc), 112);
                    try
                    {
                        _ = new NullLocalTimeProvider().GetLocalNow();
                        Check(false, 113);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    var customDelay = Task.Delay(
                        TimeSpan.FromMilliseconds(1),
                        timeProvider);
                    Check(timeProvider.WasFlowSuppressed, 88);
                    Check(!customDelay.IsCompleted, 34);
                    timeProvider.Fire();
                    Check(customDelay.IsCompletedSuccessfully, 35);

                    var customWaitSource = new TaskCompletionSource();
                    var customWait = customWaitSource.Task.WaitAsync(
                        TimeSpan.FromMilliseconds(1),
                        timeProvider);
                    Check(timeProvider.WasFlowSuppressed, 89);
                    timeProvider.Fire();
                    Check(customWait.IsFaulted, 36);

                    var customCancellation = new CancellationTokenSource(
                        TimeSpan.FromMilliseconds(1),
                        timeProvider);
                    Check(timeProvider.WasFlowSuppressed, 90);
                    timeProvider.Fire();
                    Check(customCancellation.IsCancellationRequested, 37);

                    using (var customPeriodic = new PeriodicTimer(
                        TimeSpan.FromMilliseconds(1),
                        timeProvider))
                    {
                        var tick = customPeriodic.WaitForNextTickAsync();
                        timeProvider.Fire();
                        Check(tick.IsCompletedSuccessfully && tick.Result, 38);
                    }

                    var periodicProvider = new ManualTimeProvider();
                    using (var recurringTimer = new PeriodicTimer(
                        TimeSpan.FromMilliseconds(1),
                        periodicProvider))
                    {
                        Check(periodicProvider.CreateCount == 1 &&
                            periodicProvider.WasFlowSuppressed, 53);
                        var firstTick = recurringTimer.WaitForNextTickAsync();
                        periodicProvider.Fire();
                        Check(firstTick.IsCompletedSuccessfully &&
                            firstTick.Result, 54);
                        var secondTick = recurringTimer.WaitForNextTickAsync();
                        periodicProvider.Fire();
                        Check(secondTick.IsCompletedSuccessfully &&
                            secondTick.Result &&
                            periodicProvider.CreateCount == 1, 55);
                        recurringTimer.Period = TimeSpan.FromMilliseconds(2);
                        Check(periodicProvider.ChangeCount == 1 &&
                            periodicProvider.LastDueTime ==
                                TimeSpan.FromMilliseconds(2) &&
                            periodicProvider.LastPeriod ==
                                TimeSpan.FromMilliseconds(2), 56);
                    }

                    return _failure;
                }

                public static int Observe(int input)
                {
                    Check(_scheduledContinuation is not null &&
                        _scheduledContinuation.IsCompletedSuccessfully, 19);
                    Check(_scheduledCancellation is not null &&
                        _scheduledCancellation.IsCancellationRequested, 20);
                    Check(_capturedContextPosts == 1, 21);
                    Check(_periodicTick is not null &&
                        _periodicTick.IsCompletedSuccessfully &&
                        _periodicTick.Result, 28);
                    Check(_forcedYield is not null &&
                        _forcedYield.IsCompletedSuccessfully &&
                        _forcedYield.Result == input + 1, 32);
                    Check(_lateValueTaskSourceContinuation, 51);
                    Check(_delayedCancellationContinuation, 87);
                    Check(_whenEachOrder is not null &&
                        _whenEachOrder.IsCompletedSuccessfully &&
                        _whenEachOrder.Result == 21, 95);
                    return _failure == 0 ? input + 1 : _failure;
                }

                private static async Task<int> ObserveFlow(Task<int> task)
                {
                    await task.ConfigureAwait(false);
                    return Local.Value;
                }

                private static async Task<int> MutateFirstStep(
                    Task<int> task,
                    SynchronizationContext synchronizationContext)
                {
                    Local.Value = 62;
                    SynchronizationContext.SetSynchronizationContext(
                        synchronizationContext);
                    await task.ConfigureAwait(false);
                    return Local.Value;
                }

                private static async Task<int> RestoreFlowInFirstStep(
                    Task<int> task)
                {
                    ExecutionContext.RestoreFlow();
                    return await task.ConfigureAwait(false);
                }

                private static async Task<int> ObserveSynchronizationContext(Task<int> task)
                {
                    await task.ConfigureAwait(false);
                    return SynchronizationContext.Current is null ? 0 : -1;
                }

                private static async void ThrowFromAsyncVoid(Task task)
                {
                    await task;
                    throw new InvalidOperationException();
                }

                private static async Task<bool> ObservePeriodicTick(PeriodicTimer timer)
                {
                    try
                    {
                        return await timer.WaitForNextTickAsync();
                    }
                    finally
                    {
                        timer.Dispose();
                    }
                }

                private static async Task<int> ForceYield(int value)
                {
                    await Task.CompletedTask.ConfigureAwait(
                        ConfigureAwaitOptions.ForceYielding);
                    return value + 1;
                }

                private static async Task<int> ObserveWhenEach(
                    IAsyncEnumerable<Task<int>> tasks)
                {
                    var order = 0;
                    await foreach (var task in tasks)
                    {
                        order = (order * 10) + await task;
                    }

                    await foreach (var task in tasks)
                    {
                        return -1;
                    }

                    return order;
                }

                private static async Task<int> ReturnOne() => 1;

                private static async Task<int> AwaitTwice(
                    ManualAwaitable awaitable,
                    int returnValue)
                {
                    Local.Value = 7;
                    await awaitable;
                    if (Local.Value != 7)
                    {
                        return -1;
                    }
                    Local.Value = 8;
                    await awaitable;
                    return Local.Value == 8 ? returnValue : -1;
                }

                private static async Task AwaitTwice(ManualAwaitable awaitable)
                {
                    await awaitable;
                    await awaitable;
                }

                private static bool ExerciseAsyncLocalMapBoundaries()
                {
                    var locals = new AsyncLocal<object?>[18];
                    var values = new object[18];
                    for (var index = 0; index < locals.Length; index++)
                    {
                        locals[index] = new AsyncLocal<object?>();
                        values[index] = new object();
                        locals[index].Value = values[index];
                    }

                    var captured = ExecutionContext.Capture()!;
                    locals[0].Value = null;
                    locals[4].Value = null;
                    locals[16].Value = null;

                    var valid = locals[0].Value is null &&
                        locals[4].Value is null &&
                        locals[16].Value is null;
                    ExecutionContext.Run(
                        captured,
                        _ =>
                        {
                            for (var index = 0; index < locals.Length; index++)
                            {
                                valid &= ReferenceEquals(
                                    locals[index].Value,
                                    values[index]);
                            }
                        },
                        null);

                    valid &= locals[0].Value is null &&
                        locals[4].Value is null &&
                        locals[16].Value is null;
                    for (var index = 0; index < locals.Length; index++)
                    {
                        locals[index].Value = null;
                    }

                    return valid;
                }

                private static bool ExerciseAsyncLocalNotificationOrder()
                {
                    var immediateNotifications = 0;
                    var contextOrder = 0;
                    var first = new AsyncLocal<object?>(args =>
                    {
                        if (args.ThreadContextChanged)
                        {
                            contextOrder = (contextOrder * 10) + 1;
                        }
                        else
                        {
                            immediateNotifications++;
                        }
                    });
                    var second = new AsyncLocal<object?>(args =>
                    {
                        if (args.ThreadContextChanged)
                        {
                            contextOrder = (contextOrder * 10) + 2;
                        }
                        else
                        {
                            immediateNotifications++;
                        }
                    });

                    first.Value = null;
                    if (immediateNotifications != 0)
                    {
                        return false;
                    }

                    var firstValue = new object();
                    var secondValue = new object();
                    first.Value = firstValue;
                    first.Value = firstValue;
                    second.Value = secondValue;
                    if (immediateNotifications != 2)
                    {
                        return false;
                    }

                    var captured = ExecutionContext.Capture()!;
                    first.Value = new object();
                    second.Value = new object();
                    contextOrder = 0;
                    ExecutionContext.Run(captured, static _ => { }, null);
                    first.Value = null;
                    second.Value = null;
                    return contextOrder == 1212;
                }

                private static void Check(bool condition, int failure)
                {
                    if (!condition && _failure == 0)
                    {
                        _failure = failure;
                    }
                }

                private sealed class RecordingSynchronizationContext : SynchronizationContext
                {
                    public int PostCount { get; private set; }
                    public int ExceptionCount { get; private set; }
                    public int OperationStartedCount { get; private set; }
                    public int OperationCompletedCount { get; private set; }

                    public override void Post(SendOrPostCallback callback, object? state)
                    {
                        PostCount++;
                        try
                        {
                            callback(state);
                        }
                        catch (Exception)
                        {
                            ExceptionCount++;
                        }
                    }

                    public override void OperationStarted() => OperationStartedCount++;

                    public override void OperationCompleted() => OperationCompletedCount++;
                }

                private struct ReferenceContainingResult
                {
                    public object? Value;
                }

                private sealed class ManualAwaitable : ICriticalNotifyCompletion
                {
                    public Action? Continuation { get; private set; }
                    public bool IsCompleted => false;

                    public ManualAwaitable GetAwaiter() => this;

                    public void OnCompleted(Action continuation) =>
                        Continuation = continuation;

                    public void UnsafeOnCompleted(Action continuation) =>
                        Continuation = continuation;

                    public void GetResult()
                    {
                    }

                    public void Resume()
                    {
                        var continuation = Continuation;
                        Continuation = null;
                        continuation!();
                    }
                }

                private sealed class FlowingValueTaskSource
                {
                    private ManualResetValueTaskSourceCore<int> _core;

                    public int ObservedValue { get; private set; }

                    public void Register() => _core.OnCompleted(
                        _ => ObservedValue = Local.Value,
                        null,
                        _core.Version,
                        ValueTaskSourceOnCompletedFlags.FlowExecutionContext);

                    public void Complete(int value) => _core.SetResult(value);
                }

                private sealed class ManualTimeProvider : TimeProvider
                {
                    private ManualTimer? _timer;

                    public int CreateCount { get; private set; }
                    public int ChangeCount { get; private set; }
                    public TimeSpan LastDueTime { get; private set; }
                    public TimeSpan LastPeriod { get; private set; }
                    public bool WasFlowSuppressed { get; private set; }

                    public override ITimer CreateTimer(
                        TimerCallback callback,
                        object? state,
                        TimeSpan dueTime,
                        TimeSpan period)
                    {
                        CreateCount++;
                        LastDueTime = dueTime;
                        LastPeriod = period;
                        WasFlowSuppressed = ExecutionContext.IsFlowSuppressed();
                        _timer = new ManualTimer(this, callback, state);
                        return _timer;
                    }

                    public void Fire() => _timer!.Fire();

                    private sealed class ManualTimer : ITimer
                    {
                        private readonly ManualTimeProvider _owner;
                        private readonly TimerCallback _callback;
                        private readonly object? _state;
                        private bool _disposed;

                        public ManualTimer(
                            ManualTimeProvider owner,
                            TimerCallback callback,
                            object? state)
                        {
                            _owner = owner;
                            _callback = callback;
                            _state = state;
                        }

                        public bool Change(TimeSpan dueTime, TimeSpan period)
                        {
                            if (_disposed)
                            {
                                return false;
                            }

                            _owner.ChangeCount++;
                            _owner.LastDueTime = dueTime;
                            _owner.LastPeriod = period;
                            return true;
                        }

                        public void Dispose()
                        {
                            if (_disposed)
                            {
                                return;
                            }

                            _disposed = true;
                        }

                        public ValueTask DisposeAsync()
                        {
                            Dispose();
                            return default;
                        }

                        public void Fire()
                        {
                            if (!_disposed)
                            {
                                _callback(_state);
                            }
                        }
                    }
                }

                private sealed class FixedTimeProvider : TimeProvider
                {
                    private static readonly DateTimeOffset Value =
                        new(638000000000000000L, TimeSpan.Zero);

                    public override DateTimeOffset GetUtcNow() => Value;

                    public override TimeZoneInfo LocalTimeZone =>
                        TimeZoneInfo.Utc;
                }

                private sealed class NullLocalTimeProvider : TimeProvider
                {
                    public override TimeZoneInfo LocalTimeZone => null!;
                }
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "AsyncRuntimeSemanticsFixture",
            source,
            "AsyncRuntimeSemanticsFixture.EntryPoint",
            optimize,
            target,
            41,
            [])
        {
            Exports =
            [
                new RequestedExport(
                    "run",
                    "AsyncRuntimeSemanticsFixture.EntryPoint",
                    "Run"),
                new RequestedExport(
                    "observe",
                    "AsyncRuntimeSemanticsFixture.EntryPoint",
                    "Observe"),
            ],
            WitPath = Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/async-platform@1.0.0",
            DrainReactor = true,
            ObserveExportName = "observe",
        });

        Assert.Equal(42, observed);
    }
}
