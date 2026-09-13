// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public sealed class AsyncMethodBuilderAttribute : Attribute
    {
        public AsyncMethodBuilderAttribute(Type builderType)
        {
            BuilderType = builderType;
        }

        public Type BuilderType { get; }
    }

    public readonly struct YieldAwaitable
    {
        public YieldAwaiter GetAwaiter() => new();

        public readonly struct YieldAwaiter : ICriticalNotifyCompletion, INotifyCompletion
        {
            public bool IsCompleted => false;

            public void OnCompleted(Action continuation) =>
                Runtime.InteropServices.PlatformServices.Scheduler.Schedule(
                    continuation ?? throw new ArgumentNullException(nameof(continuation)),
                    0);

            public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);

            public void GetResult()
            {
            }
        }
    }

    public interface INotifyCompletion
    {
        void OnCompleted(Action continuation);
    }

    public interface ICriticalNotifyCompletion : INotifyCompletion
    {
        void UnsafeOnCompleted(Action continuation);
    }

    public interface IAsyncStateMachine
    {
        void MoveNext();
        void SetStateMachine(IAsyncStateMachine stateMachine);
    }

    internal sealed class AsyncStateMachineContinuation
    {
        private readonly IAsyncStateMachine _stateMachine;
        private readonly Threading.ExecutionContext? _executionContext;

        internal AsyncStateMachineContinuation(IAsyncStateMachine stateMachine)
        {
            _stateMachine = stateMachine;
            _executionContext = Threading.ExecutionContext.Capture();
        }

        internal void MoveNext()
        {
            if (_executionContext is null)
            {
                _stateMachine.MoveNext();
                return;
            }

            Threading.ExecutionContext.Run(
                _executionContext,
                static state => ((IAsyncStateMachine)state!).MoveNext(),
                _stateMachine);
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AsyncStateMachineAttribute : StateMachineAttribute
    {
        public AsyncStateMachineAttribute(Type stateMachineType)
            : base(stateMachineType)
        {
        }
    }

    public struct AsyncTaskMethodBuilder
    {
        private Threading.Tasks.Task _task;

        public static AsyncTaskMethodBuilder Create()
        {
            var builder = new AsyncTaskMethodBuilder();
            builder._task = new Threading.Tasks.Task();
            return builder;
        }

        public Threading.Tasks.Task Task => _task;

        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
        }

        public void SetResult() => _task.SetResult();

        public void SetException(Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                _task.SetCanceled(exception);
            }
            else
            {
                _task.SetException(exception);
            }
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            var continuation = new AsyncStateMachineContinuation(stateMachine);
            awaiter.OnCompleted(continuation.MoveNext);
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            var continuation = new AsyncStateMachineContinuation(stateMachine);
            awaiter.UnsafeOnCompleted(continuation.MoveNext);
        }
    }

    public struct AsyncVoidMethodBuilder
    {
        private Threading.SynchronizationContext? _synchronizationContext;

        public static AsyncVoidMethodBuilder Create()
        {
            var synchronizationContext = Threading.SynchronizationContext.Current;
            synchronizationContext?.OperationStarted();
            return new AsyncVoidMethodBuilder
            {
                _synchronizationContext = synchronizationContext,
            };
        }

        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
        }

        public void SetResult()
        {
            _synchronizationContext?.OperationCompleted();
        }

        public void SetException(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            var synchronizationContext = _synchronizationContext;
            if (synchronizationContext is not null)
            {
                try
                {
                    synchronizationContext.Post(
                        static state => throw (Exception)state!,
                        exception);
                }
                finally
                {
                    synchronizationContext.OperationCompleted();
                }
                return;
            }

            Runtime.InteropServices.PlatformServices.Scheduler.Schedule(
                () => throw exception,
                0);
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            var continuation = new AsyncStateMachineContinuation(stateMachine);
            awaiter.OnCompleted(continuation.MoveNext);
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            var continuation = new AsyncStateMachineContinuation(stateMachine);
            awaiter.UnsafeOnCompleted(continuation.MoveNext);
        }
    }

    public struct AsyncTaskMethodBuilder<T>
    {
        private Threading.Tasks.Task<T> _task;

        public static AsyncTaskMethodBuilder<T> Create()
        {
            var builder = new AsyncTaskMethodBuilder<T>();
            builder._task = new Threading.Tasks.Task<T>();
            return builder;
        }

        public Threading.Tasks.Task<T> Task => _task;

        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
        }

        public void SetResult(T result) => _task.SetResult(result);

        public void SetException(Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                _task.SetCanceled(exception);
            }
            else
            {
                _task.SetException(exception);
            }
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            var continuation = new AsyncStateMachineContinuation(stateMachine);
            awaiter.OnCompleted(continuation.MoveNext);
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            var continuation = new AsyncStateMachineContinuation(stateMachine);
            awaiter.UnsafeOnCompleted(continuation.MoveNext);
        }
    }

    public struct AsyncValueTaskMethodBuilder
    {
        private Threading.Tasks.Task? _task;
        private bool _completed;

        public static AsyncValueTaskMethodBuilder Create() => new();

        public Threading.Tasks.ValueTask Task => _task == null
            ? default
            : new(_task);

        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
        }

        public void SetResult()
        {
            if (_task == null)
            {
                _completed = true;
                return;
            }
            _task.SetResult();
        }

        public void SetException(Exception exception)
        {
            EnsureTask();
            if (exception is OperationCanceledException)
            {
                _task!.SetCanceled(exception);
            }
            else
            {
                _task!.SetException(exception);
            }
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            EnsureTask();
            var continuation = new AsyncStateMachineContinuation(stateMachine);
            awaiter.OnCompleted(continuation.MoveNext);
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            EnsureTask();
            var continuation = new AsyncStateMachineContinuation(stateMachine);
            awaiter.UnsafeOnCompleted(continuation.MoveNext);
        }

        private void EnsureTask()
        {
            if (_task != null)
            {
                return;
            }
            _task = new Threading.Tasks.Task();
            if (_completed)
            {
                _task.SetResult();
            }
        }
    }

    public struct AsyncValueTaskMethodBuilder<T>
    {
        private Threading.Tasks.Task<T>? _task;
        private T _result;
        private bool _completed;

        public static AsyncValueTaskMethodBuilder<T> Create() => new();

        public Threading.Tasks.ValueTask<T> Task => _task == null
            ? new(_result)
            : new(_task);

        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
        }

        public void SetResult(T result)
        {
            if (_task == null)
            {
                _result = result;
                _completed = true;
                return;
            }
            _task.SetResult(result);
        }

        public void SetException(Exception exception)
        {
            EnsureTask();
            if (exception is OperationCanceledException)
            {
                _task!.SetCanceled(exception);
            }
            else
            {
                _task!.SetException(exception);
            }
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            EnsureTask();
            var continuation = new AsyncStateMachineContinuation(stateMachine);
            awaiter.OnCompleted(continuation.MoveNext);
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            EnsureTask();
            var continuation = new AsyncStateMachineContinuation(stateMachine);
            awaiter.UnsafeOnCompleted(continuation.MoveNext);
        }

        private void EnsureTask()
        {
            if (_task != null)
            {
                return;
            }
            _task = new Threading.Tasks.Task<T>();
            if (_completed)
            {
                _task.SetResult(_result);
            }
        }
    }

    // The pooling builders retain the public compiler contract on a reactor
    // target. There are no worker threads to pool; they delegate to the same
    // single-task completion path as the ordinary ValueTask builders.
    public struct PoolingAsyncValueTaskMethodBuilder
    {
        private AsyncValueTaskMethodBuilder _builder;

        public static PoolingAsyncValueTaskMethodBuilder Create() => new();
        public Threading.Tasks.ValueTask Task => _builder.Task;
        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine => _builder.Start(ref stateMachine);
        public void SetStateMachine(IAsyncStateMachine stateMachine) => _builder.SetStateMachine(stateMachine);
        public void SetResult() => _builder.SetResult();
        public void SetException(Exception exception) => _builder.SetException(exception);
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine =>
            _builder.AwaitOnCompleted(ref awaiter, ref stateMachine);
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine =>
            _builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }

    public struct PoolingAsyncValueTaskMethodBuilder<T>
    {
        private AsyncValueTaskMethodBuilder<T> _builder;

        public static PoolingAsyncValueTaskMethodBuilder<T> Create() => new();
        public Threading.Tasks.ValueTask<T> Task => _builder.Task;
        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine => _builder.Start(ref stateMachine);
        public void SetStateMachine(IAsyncStateMachine stateMachine) => _builder.SetStateMachine(stateMachine);
        public void SetResult(T result) => _builder.SetResult(result);
        public void SetException(Exception exception) => _builder.SetException(exception);
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine =>
            _builder.AwaitOnCompleted(ref awaiter, ref stateMachine);
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine =>
            _builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }

    public struct AsyncIteratorMethodBuilder
    {
        public static AsyncIteratorMethodBuilder Create() => new();

        public void MoveNext<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            var continuation = new AsyncStateMachineContinuation(stateMachine);
            awaiter.OnCompleted(continuation.MoveNext);
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            var continuation = new AsyncStateMachineContinuation(stateMachine);
            awaiter.UnsafeOnCompleted(continuation.MoveNext);
        }

        public void Complete()
        {
        }
    }

    public readonly struct ConfiguredTaskAwaitable
    {
        private readonly global::System.Runtime.CompilerServices.TaskAwaiter _awaiter;
        private readonly global::System.Threading.Tasks.ConfigureAwaitOptions _options;

        internal ConfiguredTaskAwaitable(
            global::System.Runtime.CompilerServices.TaskAwaiter awaiter,
            bool continueOnCapturedContext)
        {
            _awaiter = awaiter;
            _options = continueOnCapturedContext
                ? global::System.Threading.Tasks.ConfigureAwaitOptions.ContinueOnCapturedContext
                : global::System.Threading.Tasks.ConfigureAwaitOptions.None;
        }

        internal ConfiguredTaskAwaitable(
            global::System.Runtime.CompilerServices.TaskAwaiter awaiter,
            global::System.Threading.Tasks.ConfigureAwaitOptions options)
        {
            _awaiter = awaiter;
            _options = options;
        }

        public ConfiguredTaskAwaiter GetAwaiter() =>
            new(_awaiter, _options);

        public readonly struct ConfiguredTaskAwaiter : ICriticalNotifyCompletion, INotifyCompletion
        {
            private readonly global::System.Runtime.CompilerServices.TaskAwaiter _awaiter;
            private readonly global::System.Threading.Tasks.ConfigureAwaitOptions _options;

            internal ConfiguredTaskAwaiter(
                global::System.Runtime.CompilerServices.TaskAwaiter awaiter,
                global::System.Threading.Tasks.ConfigureAwaitOptions options)
            {
                _awaiter = awaiter;
                _options = options;
            }

            public bool IsCompleted =>
                (_options & global::System.Threading.Tasks.ConfigureAwaitOptions.ForceYielding) == 0 &&
                _awaiter.IsCompleted;
            public void OnCompleted(Action continuation) =>
                _awaiter.OnCompleted(
                    continuation,
                    ContinueOnCapturedContext);
            public void UnsafeOnCompleted(Action continuation) =>
                _awaiter.UnsafeOnCompleted(
                    continuation,
                    ContinueOnCapturedContext);
            public void GetResult() => _awaiter.GetResult(
                suppressThrowing:
                    (_options & global::System.Threading.Tasks.ConfigureAwaitOptions.SuppressThrowing) != 0);

            private bool ContinueOnCapturedContext =>
                (_options & global::System.Threading.Tasks.ConfigureAwaitOptions.ContinueOnCapturedContext) != 0;
        }
    }

    public readonly struct ConfiguredTaskAwaitable<T>
    {
        private readonly global::System.Runtime.CompilerServices.TaskAwaiter<T> _awaiter;
        private readonly global::System.Threading.Tasks.ConfigureAwaitOptions _options;

        internal ConfiguredTaskAwaitable(
            global::System.Runtime.CompilerServices.TaskAwaiter<T> awaiter,
            bool continueOnCapturedContext)
        {
            _awaiter = awaiter;
            _options = continueOnCapturedContext
                ? global::System.Threading.Tasks.ConfigureAwaitOptions.ContinueOnCapturedContext
                : global::System.Threading.Tasks.ConfigureAwaitOptions.None;
        }

        internal ConfiguredTaskAwaitable(
            global::System.Runtime.CompilerServices.TaskAwaiter<T> awaiter,
            global::System.Threading.Tasks.ConfigureAwaitOptions options)
        {
            _awaiter = awaiter;
            _options = options;
        }

        public ConfiguredTaskAwaiter GetAwaiter() =>
            new(_awaiter, _options);

        public readonly struct ConfiguredTaskAwaiter : ICriticalNotifyCompletion, INotifyCompletion
        {
            private readonly global::System.Runtime.CompilerServices.TaskAwaiter<T> _awaiter;
            private readonly global::System.Threading.Tasks.ConfigureAwaitOptions _options;

            internal ConfiguredTaskAwaiter(
                global::System.Runtime.CompilerServices.TaskAwaiter<T> awaiter,
                global::System.Threading.Tasks.ConfigureAwaitOptions options)
            {
                _awaiter = awaiter;
                _options = options;
            }

            public bool IsCompleted =>
                (_options & global::System.Threading.Tasks.ConfigureAwaitOptions.ForceYielding) == 0 &&
                _awaiter.IsCompleted;
            public void OnCompleted(Action continuation) =>
                _awaiter.OnCompleted(
                    continuation,
                    ContinueOnCapturedContext);
            public void UnsafeOnCompleted(Action continuation) =>
                _awaiter.UnsafeOnCompleted(
                    continuation,
                    ContinueOnCapturedContext);
            public T GetResult() => _awaiter.GetResult();

            private bool ContinueOnCapturedContext =>
                (_options & global::System.Threading.Tasks.ConfigureAwaitOptions.ContinueOnCapturedContext) != 0;
        }
    }

    public readonly struct TaskAwaiter : ICriticalNotifyCompletion, INotifyCompletion
    {
        private readonly global::System.Threading.Tasks.Task _task;

        internal TaskAwaiter(global::System.Threading.Tasks.Task task) =>
            _task = task ?? throw new ArgumentNullException(nameof(task));

        public bool IsCompleted => _task.IsCompleted;
        public void OnCompleted(Action continuation) =>
            _task.OnCompleted(continuation, flowExecutionContext: true);
        public void UnsafeOnCompleted(Action continuation) =>
            _task.OnCompleted(continuation, flowExecutionContext: false);
        internal void OnCompleted(Action continuation, bool continueOnCapturedContext) =>
            _task.OnCompleted(
                continuation,
                continueOnCapturedContext,
                flowExecutionContext: true);
        internal void UnsafeOnCompleted(Action continuation, bool continueOnCapturedContext) =>
            _task.OnCompleted(
                continuation,
                continueOnCapturedContext,
                flowExecutionContext: false);
        public void GetResult() => _task.GetVoidResult();
        internal void GetResult(bool suppressThrowing) =>
            _task.GetVoidResult(suppressThrowing);
    }

    public readonly struct TaskAwaiter<T> : ICriticalNotifyCompletion, INotifyCompletion
    {
        private readonly global::System.Threading.Tasks.Task<T> _task;

        internal TaskAwaiter(global::System.Threading.Tasks.Task<T> task) =>
            _task = task ?? throw new ArgumentNullException(nameof(task));

        public bool IsCompleted => _task.IsCompleted;
        public void OnCompleted(Action continuation) =>
            _task.OnCompleted(continuation, flowExecutionContext: true);
        public void UnsafeOnCompleted(Action continuation) =>
            _task.OnCompleted(continuation, flowExecutionContext: false);
        internal void OnCompleted(Action continuation, bool continueOnCapturedContext) =>
            _task.OnCompleted(
                continuation,
                continueOnCapturedContext,
                flowExecutionContext: true);
        internal void UnsafeOnCompleted(Action continuation, bool continueOnCapturedContext) =>
            _task.OnCompleted(
                continuation,
                continueOnCapturedContext,
                flowExecutionContext: false);
        public T GetResult() => _task.GetResult();
    }

    public readonly struct ValueTaskAwaiter : ICriticalNotifyCompletion, INotifyCompletion
    {
        private readonly global::System.Threading.Tasks.ValueTask _value;

        internal ValueTaskAwaiter(global::System.Threading.Tasks.ValueTask value) =>
            _value = value;

        public bool IsCompleted => _value.IsCompleted;
        public void OnCompleted(Action continuation) =>
            _value.OnCompleted(continuation, flowExecutionContext: true);
        public void UnsafeOnCompleted(Action continuation) =>
            _value.OnCompleted(continuation, flowExecutionContext: false);
        internal void OnCompleted(Action continuation, bool continueOnCapturedContext) =>
            _value.OnCompleted(
                continuation,
                continueOnCapturedContext,
                flowExecutionContext: true);
        internal void UnsafeOnCompleted(Action continuation, bool continueOnCapturedContext) =>
            _value.OnCompleted(
                continuation,
                continueOnCapturedContext,
                flowExecutionContext: false);
        public void GetResult() => _value.GetResult();
    }

    public readonly struct ValueTaskAwaiter<T> : ICriticalNotifyCompletion, INotifyCompletion
    {
        private readonly global::System.Threading.Tasks.ValueTask<T> _value;

        internal ValueTaskAwaiter(global::System.Threading.Tasks.ValueTask<T> value) =>
            _value = value;

        public bool IsCompleted => _value.IsCompleted;
        public void OnCompleted(Action continuation) =>
            _value.OnCompleted(continuation, flowExecutionContext: true);
        public void UnsafeOnCompleted(Action continuation) =>
            _value.OnCompleted(continuation, flowExecutionContext: false);
        internal void OnCompleted(Action continuation, bool continueOnCapturedContext) =>
            _value.OnCompleted(
                continuation,
                continueOnCapturedContext,
                flowExecutionContext: true);
        internal void UnsafeOnCompleted(Action continuation, bool continueOnCapturedContext) =>
            _value.OnCompleted(
                continuation,
                continueOnCapturedContext,
                flowExecutionContext: false);
        public T GetResult() => _value.GetResult();
    }

}

namespace System.Threading.Tasks
{
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks.Sources;

    public enum TaskStatus
    {
        Created,
        WaitingForActivation,
        WaitingToRun,
        Running,
        WaitingForChildrenToComplete,
        RanToCompletion,
        Canceled,
        Faulted,
    }

    [Flags]
    public enum TaskCreationOptions
    {
        None = 0,
        PreferFairness = 1,
        LongRunning = 2,
        AttachedToParent = 4,
        DenyChildAttach = 8,
        HideScheduler = 16,
        RunContinuationsAsynchronously = 64,
    }

    [Flags]
    public enum TaskContinuationOptions
    {
        None = 0,
        PreferFairness = 1,
        LongRunning = 2,
        AttachedToParent = 4,
        DenyChildAttach = 8,
        HideScheduler = 16,
        LazyCancellation = 32,
        RunContinuationsAsynchronously = 64,
        NotOnRanToCompletion = 65_536,
        NotOnFaulted = 131_072,
        NotOnCanceled = 262_144,
        OnlyOnRanToCompletion = 393_216,
        OnlyOnFaulted = 327_680,
        OnlyOnCanceled = 196_608,
        ExecuteSynchronously = 524_288,
    }

    public partial class Task : IAsyncResult, IDisposable
    {
        private static int s_nextId;
        private static readonly Task s_completedTask = CreateCompletedTask();
        private static Task? s_currentTask;

        private int _status;
        private readonly int _id;
        private readonly object? _asyncState;
        private readonly TaskCreationOptions _creationOptions;
        private Threading.CancellationToken _cancellationToken;
        protected readonly Delegate? _action;
        protected bool _started;
        private CancellationTokenRegistration _cancellationRegistration;
        internal Exception? _exception;
        private bool _exceptionIsAggregateContainer;
        private List<ContinuationRegistration>? _continuations;
        private TaskExceptionTracker? _exceptionTracker;
        private bool _disposed;

        internal Task()
            : this((object?)null, TaskCreationOptions.None)
        {
        }

        internal Task(object? state, TaskCreationOptions creationOptions)
        {
            _id = ++s_nextId;
            _asyncState = state;
            _creationOptions = creationOptions;
            _cancellationToken = default;
            _action = null;
        }

        public Task(Action action)
            : this(action, null, default, TaskCreationOptions.None)
        {
        }

        public Task(Action action, Threading.CancellationToken cancellationToken)
            : this(action, null, cancellationToken, TaskCreationOptions.None)
        {
        }

        public Task(
            Action action,
            Threading.CancellationToken cancellationToken,
            TaskCreationOptions creationOptions)
            : this(action, null, cancellationToken, creationOptions)
        {
        }

        public Task(Action action, TaskCreationOptions creationOptions)
            : this(action, null, default, creationOptions)
        {
        }

        public Task(Action<object?> action, object? state)
            : this(action, state, default, TaskCreationOptions.None)
        {
        }

        public Task(
            Action<object?> action,
            object? state,
            Threading.CancellationToken cancellationToken)
            : this(action, state, cancellationToken, TaskCreationOptions.None)
        {
        }

        public Task(
            Action<object?> action,
            object? state,
            TaskCreationOptions creationOptions)
            : this(action, state, default, creationOptions)
        {
        }

        public Task(
            Action<object?> action,
            object? state,
            Threading.CancellationToken cancellationToken,
            TaskCreationOptions creationOptions)
            : this((Delegate)action, state, cancellationToken, creationOptions)
        {
        }

        protected Task(
            Delegate? action,
            object? state,
            Threading.CancellationToken cancellationToken,
            TaskCreationOptions creationOptions)
            : this(state, creationOptions)
        {
            _action = action ?? throw new ArgumentNullException(nameof(action));
            _cancellationToken = cancellationToken;
            if (cancellationToken.IsCancellationRequested)
            {
                SetCanceled(new TaskCanceledException(
                    message: null,
                    innerException: null,
                    cancellationToken));
            }
            else if (cancellationToken.CanBeCanceled)
            {
                _cancellationRegistration = cancellationToken.Register(
                    static state => ((Task)state!).CancelFromToken(),
                    this);
            }
        }

        public bool IsCompleted => _status is 1 or 2 or 3;
        public bool IsCompletedSuccessfully => _status == 1;
        public bool IsFaulted => _status == 2;
        public bool IsCanceled => _status == 3;
        public TaskStatus Status => _status switch
        {
            1 => TaskStatus.RanToCompletion,
            2 => TaskStatus.Faulted,
            3 => TaskStatus.Canceled,
            _ => _action is not null
                ? (_started ? TaskStatus.WaitingToRun : TaskStatus.Created)
                : TaskStatus.WaitingForActivation,
        };
        public TaskCreationOptions CreationOptions => _creationOptions;
        public object? AsyncState => _asyncState;
        public int Id => _id;
        public static int? CurrentId => s_currentTask?._id;
        public AggregateException? Exception => _exception == null
            ? null
            : ObserveException(_exception, _exceptionIsAggregateContainer);
        public bool CompletedSynchronously => false;
        internal Threading.CancellationToken CancellationToken => _cancellationToken;
        internal bool ExceptionIsAggregateContainer => _exceptionIsAggregateContainer;

        private static Task CreateCompletedTask()
        {
            var task = new Task();
            task.SetResult();
            return task;
        }

        public static Task CompletedTask => s_completedTask;

        public global::System.Runtime.CompilerServices.TaskAwaiter GetAwaiter() => new(this);

        public ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) =>
            new(new global::System.Runtime.CompilerServices.TaskAwaiter(this), continueOnCapturedContext);

        public ConfiguredTaskAwaitable ConfigureAwait(ConfigureAwaitOptions options)
        {
            const ConfigureAwaitOptions validOptions =
                ConfigureAwaitOptions.ContinueOnCapturedContext |
                ConfigureAwaitOptions.SuppressThrowing |
                ConfigureAwaitOptions.ForceYielding;
            if ((options & ~validOptions) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }

            return new ConfiguredTaskAwaitable(
                new global::System.Runtime.CompilerServices.TaskAwaiter(this),
                options);
        }

        public static Task Delay(int millisecondsDelay)
            => Delay(millisecondsDelay, default);

        public static Task Delay(
            int millisecondsDelay,
            Threading.CancellationToken cancellationToken)
        {
            if (millisecondsDelay < -1)
            {
                throw new ArgumentOutOfRangeException();
            }
            return DelayCore(millisecondsDelay, cancellationToken);
        }

        private static Task DelayCore(
            long millisecondsDelay,
            Threading.CancellationToken cancellationToken)
        {
            var task = new Task();
            if (cancellationToken.IsCancellationRequested)
            {
                task.SetCanceled(new TaskCanceledException(
                    message: null,
                    innerException: null,
                    cancellationToken));
                return task;
            }
            if (millisecondsDelay == 0)
            {
                return CompletedTask;
            }
            var scheduled = default(IPlatformSchedule?);
            var registration = default(Threading.CancellationTokenRegistration);
            registration = cancellationToken.Register(() =>
            {
                scheduled?.Dispose();
                scheduled = null;
                task.TrySetCanceled(new TaskCanceledException(
                    message: null,
                    innerException: null,
                    cancellationToken));
            });
            if (!task.IsCompleted && millisecondsDelay >= 0)
            {
                scheduled = PlatformServices.Scheduler.Schedule(
                    () =>
                    {
                        scheduled?.Dispose();
                        scheduled = null;
                        registration.Dispose();
                        task.TrySetResult();
                    },
                    checked((ulong)millisecondsDelay * 1_000_000));
            }
            return task;
        }

        public static Task Delay(TimeSpan delay) =>
            Delay(delay, default(Threading.CancellationToken));

        public static Task Delay(TimeSpan delay, TimeProvider timeProvider) =>
            Delay(delay, timeProvider, default);

        public static Task Delay(
            TimeSpan delay,
            TimeProvider timeProvider,
            Threading.CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            var milliseconds = DelayMilliseconds(delay);
            if (ReferenceEquals(timeProvider, TimeProvider.System))
            {
                return DelayCore(milliseconds, cancellationToken);
            }

            var task = new Task();
            if (cancellationToken.IsCancellationRequested)
            {
                task.SetCanceled(new TaskCanceledException(
                    message: null,
                    innerException: null,
                    cancellationToken));
                return task;
            }
            if (milliseconds == 0)
            {
                return CompletedTask;
            }

            Threading.ITimer? timer = null;
            var registration = default(Threading.CancellationTokenRegistration);
            registration = cancellationToken.Register(() =>
            {
                timer?.Dispose();
                timer = null;
                task.TrySetCanceled(new TaskCanceledException(
                    message: null,
                    innerException: null,
                    cancellationToken));
            });
            if (milliseconds >= 0 && !task.IsCompleted)
            {
                timer = timeProvider.CreateTimer(
                    _ =>
                    {
                        timer?.Dispose();
                        timer = null;
                        registration.Dispose();
                        task.TrySetResult();
                    },
                    null,
                    TimeSpan.FromMilliseconds(milliseconds),
                    TimeSpan.FromMilliseconds(-1));
            }
            return task;
        }

        public static Task Delay(TimeSpan delay, Threading.CancellationToken cancellationToken)
        {
            var milliseconds = DelayMilliseconds(delay);
            return DelayCore(milliseconds, cancellationToken);
        }

        internal static long DelayMilliseconds(TimeSpan delay)
        {
            if (delay.Ticks < -TimeSpan.TicksPerMillisecond)
            {
                throw new ArgumentOutOfRangeException(nameof(delay));
            }
            if (delay.Ticks == -TimeSpan.TicksPerMillisecond)
            {
                return -1;
            }
            var milliseconds = delay.Ticks / TimeSpan.TicksPerMillisecond;
            return milliseconds > uint.MaxValue - 1L
                ? throw new ArgumentOutOfRangeException(nameof(delay))
                : milliseconds;
        }

        public static Task FromException(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException();
            }
            var task = new Task();
            task.SetException(exception);
            return task;
        }

        public static Task<TResult> FromResult<TResult>(TResult result)
        {
            var task = new Task<TResult>();
            task.SetResult(result);
            return task;
        }

        public static Task<TResult> FromException<TResult>(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException();
            }
            var task = new Task<TResult>();
            task.SetException(exception);
            return task;
        }

        public static Task<TResult> FromCanceled<TResult>(Threading.CancellationToken cancellationToken)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                throw new ArgumentOutOfRangeException();
            }
            var task = new Task<TResult>();
            task.SetCanceled(new TaskCanceledException(
                message: null,
                innerException: null,
                cancellationToken));
            return task;
        }

        public static Task FromCanceled(Threading.CancellationToken cancellationToken)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                throw new ArgumentOutOfRangeException();
            }
            var task = new Task();
            task.SetCanceled(new TaskCanceledException(
                message: null,
                innerException: null,
                cancellationToken));
            return task;
        }

        public static Task WhenAll(IEnumerable<Task> tasks) =>
            WhenAll(MaterializeTasks(tasks));

        public static Task WhenAll(params Task[] tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            if (tasks.Length == 0)
            {
                return CompletedTask;
            }
            var completion = new TaskCompletionSource();
            var remaining = tasks.Length;
            var errors = new List<Exception>();
            var canceled = false;
            var canceledToken = default(Threading.CancellationToken);
            void Observe(Task task)
            {
                if (task.IsFaulted)
                {
                    if (task._exception is AggregateException aggregate)
                    {
                        foreach (var exception in aggregate.InnerExceptions)
                        {
                            errors.Add(exception);
                        }
                    }
                    else if (task._exception is not null)
                    {
                        errors.Add(task._exception);
                    }
                }
                else if (task.IsCanceled)
                {
                    if (!canceled)
                    {
                        canceledToken = task.CancellationToken;
                    }
                    canceled = true;
                }
                if (--remaining != 0)
                {
                    return;
                }
                if (errors.Count != 0)
                {
                    completion.TrySetException(errors);
                }
                else if (canceled)
                {
                    completion.TrySetCanceled(canceledToken);
                }
                else
                {
                    completion.TrySetResult();
                }
            }
            foreach (var task in tasks)
            {
                if (task is null)
                {
                    throw new ArgumentException(nameof(tasks));
                }
                task.RegisterContinuation(
                    () => Observe(task),
                    continueOnCapturedContext: false,
                    flowExecutionContext: false,
                    runAsynchronously: false,
                    runInlineIfCompleted: true);
            }
            return completion.Task;
        }

        public static Task WhenAll(params ReadOnlySpan<Task> tasks)
        {
            var materialized = new Task[tasks.Length];
            for (var index = 0; index < tasks.Length; index++)
            {
                materialized[index] = tasks[index];
            }
            return WhenAll(materialized);
        }

        public static Task<TResult[]> WhenAll<TResult>(IEnumerable<Task<TResult>> tasks) =>
            WhenAll(MaterializeTasks(tasks));

        public static Task<TResult[]> WhenAll<TResult>(params Task<TResult>[] tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            if (tasks.Length == 0)
            {
                return FromResult(new TResult[0]);
            }
            var completion = new TaskCompletionSource<TResult[]>();
            var results = new TResult[tasks.Length];
            var remaining = tasks.Length;
            var errors = new List<Exception>();
            var canceled = false;
            var canceledToken = default(Threading.CancellationToken);
            void Observe(Task<TResult> task, int index)
            {
                if (task.IsFaulted)
                {
                    if (task._exception is AggregateException aggregate)
                    {
                        foreach (var exception in aggregate.InnerExceptions)
                        {
                            errors.Add(exception);
                        }
                    }
                    else if (task._exception is not null)
                    {
                        errors.Add(task._exception);
                    }
                }
                else if (task.IsCanceled)
                {
                    if (!canceled)
                    {
                        canceledToken = task.CancellationToken;
                    }
                    canceled = true;
                }
                else
                {
                    results[index] = task._result;
                }
                if (--remaining != 0)
                {
                    return;
                }
                if (errors.Count != 0)
                {
                    completion.TrySetException(errors);
                }
                else if (canceled)
                {
                    completion.TrySetCanceled(canceledToken);
                }
                else
                {
                    completion.TrySetResult(results);
                }
            }
            for (var index = 0; index < tasks.Length; index++)
            {
                var task = tasks[index] ?? throw new ArgumentException(nameof(tasks));
                var capturedIndex = index;
                task.RegisterContinuation(
                    () => Observe(task, capturedIndex),
                    continueOnCapturedContext: false,
                    flowExecutionContext: false,
                    runAsynchronously: false,
                    runInlineIfCompleted: true);
            }
            return completion.Task;
        }

        public static Task<TResult[]> WhenAll<TResult>(params ReadOnlySpan<Task<TResult>> tasks)
        {
            var materialized = new Task<TResult>[tasks.Length];
            for (var index = 0; index < tasks.Length; index++)
            {
                materialized[index] = tasks[index];
            }
            return WhenAll(materialized);
        }

        public static Task<Task> WhenAny(IEnumerable<Task> tasks) =>
            WhenAny(MaterializeTasks(tasks));

        public static Task<Task> WhenAny(Task task1, Task task2) =>
            WhenAny(new[] { task1, task2 });

        public static Task<Task> WhenAny(params Task[] tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            if (tasks.Length == 0)
            {
                throw new ArgumentException(nameof(tasks));
            }
            var completion = new TaskCompletionSource<Task>();
            foreach (var task in tasks)
            {
                if (task is null)
                {
                    throw new ArgumentException(nameof(tasks));
                }
                task.RegisterContinuation(
                    () => completion.TrySetResult(task),
                    continueOnCapturedContext: false,
                    flowExecutionContext: false,
                    runAsynchronously: false,
                    runInlineIfCompleted: true);
            }
            return completion.Task;
        }

        public static Task<Task> WhenAny(params ReadOnlySpan<Task> tasks)
        {
            var materialized = new Task[tasks.Length];
            for (var index = 0; index < tasks.Length; index++)
            {
                materialized[index] = tasks[index];
            }
            return WhenAny(materialized);
        }

        public static Task<Task<TResult>> WhenAny<TResult>(IEnumerable<Task<TResult>> tasks) =>
            WhenAny(MaterializeTasks(tasks));

        public static Task<Task<TResult>> WhenAny<TResult>(Task<TResult> task1, Task<TResult> task2) =>
            WhenAny(new[] { task1, task2 });

        public static Task<Task<TResult>> WhenAny<TResult>(params Task<TResult>[] tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            if (tasks.Length == 0)
            {
                throw new ArgumentException(nameof(tasks));
            }
            var completion = new TaskCompletionSource<Task<TResult>>();
            foreach (var task in tasks)
            {
                if (task is null)
                {
                    throw new ArgumentException(nameof(tasks));
                }
                task.RegisterContinuation(
                    () => completion.TrySetResult(task),
                    continueOnCapturedContext: false,
                    flowExecutionContext: false,
                    runAsynchronously: false,
                    runInlineIfCompleted: true);
            }
            return completion.Task;
        }

        public static Task<Task<TResult>> WhenAny<TResult>(params ReadOnlySpan<Task<TResult>> tasks)
        {
            var materialized = new Task<TResult>[tasks.Length];
            for (var index = 0; index < tasks.Length; index++)
            {
                materialized[index] = tasks[index];
            }
            return WhenAny(materialized);
        }

        public Task WaitAsync(Threading.CancellationToken cancellationToken)
        {
            if (IsCompleted)
            {
                return this;
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return FromCanceled(cancellationToken);
            }
            var completion = new TaskCompletionSource();
            CancellationTokenRegistration registration = default;
            void CompleteFromSource()
            {
                if (completion.Task.IsCompleted)
                {
                    return;
                }
                if (IsFaulted)
                {
                    completion.TrySetException(
                        _exception!,
                        ExceptionIsAggregateContainer);
                }
                else if (IsCanceled)
                {
                    completion.TrySetCanceled();
                }
                else
                {
                    completion.TrySetResult();
                }
                registration.Dispose();
            }
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(
                    static state =>
                    {
                        var cancellation =
                            ((TaskCompletionSource, Threading.CancellationToken))state!;
                        cancellation.Item1.TrySetCanceled(cancellation.Item2);
                    },
                    (completion, cancellationToken));
            }
            OnCompleted(CompleteFromSource, continueOnCapturedContext: false);
            return completion.Task;
        }

        public Task WaitAsync(TimeSpan timeout) =>
            WaitAsync(timeout, default(Threading.CancellationToken));

        public Task WaitAsync(TimeSpan timeout, TimeProvider timeProvider) =>
            WaitAsync(timeout, timeProvider, default);

        public Task WaitAsync(
            TimeSpan timeout,
            TimeProvider timeProvider,
            Threading.CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            if (ReferenceEquals(timeProvider, TimeProvider.System))
            {
                return WaitAsync(timeout, cancellationToken);
            }

            var milliseconds = DelayMilliseconds(timeout);
            if (IsCompleted)
            {
                return this;
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return FromCanceled(cancellationToken);
            }
            if (milliseconds == 0)
            {
                return FromException(new TimeoutException());
            }

            var completion = new TaskCompletionSource();
            Threading.ITimer? timer = null;
            CancellationTokenRegistration registration = default;
            void CompleteFromSource()
            {
                if (completion.Task.IsCompleted)
                {
                    return;
                }
                timer?.Dispose();
                timer = null;
                registration.Dispose();
                if (IsFaulted)
                {
                    completion.TrySetException(
                        _exception!,
                        ExceptionIsAggregateContainer);
                }
                else if (IsCanceled)
                {
                    completion.TrySetCanceled(CancellationToken);
                }
                else
                {
                    completion.TrySetResult();
                }
            }
            void Cancel()
            {
                timer?.Dispose();
                timer = null;
                completion.TrySetCanceled(cancellationToken);
            }
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(Cancel);
            }
            OnCompleted(CompleteFromSource, continueOnCapturedContext: false);
            if (milliseconds >= 0 && !completion.Task.IsCompleted)
            {
                timer = timeProvider.CreateTimer(
                    _ =>
                    {
                        timer = null;
                        registration.Dispose();
                        completion.TrySetException(new TimeoutException());
                    },
                    null,
                    TimeSpan.FromMilliseconds(milliseconds),
                    TimeSpan.FromMilliseconds(-1));
            }
            return completion.Task;
        }

        public Task WaitAsync(TimeSpan timeout, Threading.CancellationToken cancellationToken)
        {
            var milliseconds = DelayMilliseconds(timeout);
            if (IsCompleted)
            {
                return this;
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return FromCanceled(cancellationToken);
            }
            if (milliseconds == 0)
            {
                return FromException(new TimeoutException());
            }
            var completion = new TaskCompletionSource();
            IPlatformSchedule? scheduled = null;
            CancellationTokenRegistration registration = default;
            void CompleteFromSource()
            {
                if (completion.Task.IsCompleted)
                {
                    return;
                }
                scheduled?.Dispose();
                scheduled = null;
                registration.Dispose();
                if (IsFaulted)
                {
                    completion.TrySetException(
                        _exception!,
                        ExceptionIsAggregateContainer);
                }
                else if (IsCanceled)
                {
                    completion.TrySetCanceled();
                }
                else
                {
                    completion.TrySetResult();
                }
            }
            void Cancel()
            {
                scheduled?.Dispose();
                scheduled = null;
                completion.TrySetCanceled(cancellationToken);
            }
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(Cancel);
            }
            OnCompleted(CompleteFromSource, continueOnCapturedContext: false);
            if (milliseconds >= 0 && !completion.Task.IsCompleted)
            {
                scheduled = PlatformServices.Scheduler.Schedule(
                    () =>
                    {
                        scheduled = null;
                        registration.Dispose();
                        completion.TrySetException(new TimeoutException());
                    },
                    checked((ulong)milliseconds * 1_000_000));
            }
            return completion.Task;
        }

        private static Task[] MaterializeTasks(IEnumerable<Task> tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            var materialized = new List<Task>();
            foreach (var task in tasks)
            {
                materialized.Add(task ?? throw new ArgumentException(nameof(tasks)));
            }
            return materialized.ToArray();
        }

        private static Task<TResult>[] MaterializeTasks<TResult>(IEnumerable<Task<TResult>> tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            var materialized = new List<Task<TResult>>();
            foreach (var task in tasks)
            {
                materialized.Add(task ?? throw new ArgumentException(nameof(tasks)));
            }
            return materialized.ToArray();
        }

        public Task ContinueWith(Action<Task> continuationAction) =>
            AddContinuation(continuationAction, default, TaskContinuationOptions.None);

        public Task ContinueWith(
            Action<Task> continuationAction,
            Threading.CancellationToken cancellationToken) =>
            AddContinuation(continuationAction, cancellationToken, TaskContinuationOptions.None);

        public Task ContinueWith(
            Action<Task> continuationAction,
            TaskContinuationOptions continuationOptions) =>
            AddContinuation(continuationAction, default, continuationOptions);

        public Task ContinueWith(Action<Task, object?> continuationAction, object? state) =>
            AddContinuation(continuationAction, state, default, TaskContinuationOptions.None);

        public Task ContinueWith(
            Action<Task, object?> continuationAction,
            object? state,
            Threading.CancellationToken cancellationToken) =>
            AddContinuation(continuationAction, state, cancellationToken, TaskContinuationOptions.None);

        public Task ContinueWith(
            Action<Task, object?> continuationAction,
            object? state,
            TaskContinuationOptions continuationOptions) =>
            AddContinuation(continuationAction, state, default, continuationOptions);

        public Task<TResult> ContinueWith<TResult>(Func<Task, TResult> continuationFunction) =>
            AddContinuation(continuationFunction, null, default, TaskContinuationOptions.None);

        public Task<TResult> ContinueWith<TResult>(
            Func<Task, TResult> continuationFunction,
            Threading.CancellationToken cancellationToken) =>
            AddContinuation(continuationFunction, null, cancellationToken, TaskContinuationOptions.None);

        public Task<TResult> ContinueWith<TResult>(
            Func<Task, TResult> continuationFunction,
            TaskContinuationOptions continuationOptions) =>
            AddContinuation(continuationFunction, null, default, continuationOptions);

        public Task<TResult> ContinueWith<TResult>(
            Func<Task, object?, TResult> continuationFunction,
            object? state) =>
            AddContinuation(continuationFunction, state, default, TaskContinuationOptions.None);

        public Task<TResult> ContinueWith<TResult>(
            Func<Task, object?, TResult> continuationFunction,
            object? state,
            Threading.CancellationToken cancellationToken) =>
            AddContinuation(continuationFunction, state, cancellationToken, TaskContinuationOptions.None);

        public Task<TResult> ContinueWith<TResult>(
            Func<Task, object?, TResult> continuationFunction,
            object? state,
            TaskContinuationOptions continuationOptions) =>
            AddContinuation(continuationFunction, state, default, continuationOptions);

        private Task AddContinuation(
            Action<Task> continuationAction,
            Threading.CancellationToken cancellationToken,
            TaskContinuationOptions continuationOptions) =>
            AddContinuation(
                (task, _) => continuationAction(task),
                null,
                cancellationToken,
                continuationOptions);

        private Task AddContinuation(
            Action<Task, object?> continuationAction,
            object? state,
            Threading.CancellationToken cancellationToken,
            TaskContinuationOptions continuationOptions)
        {
            ArgumentNullException.ThrowIfNull(continuationAction);
            var continuation = new Task((object?)null, continuationOptions.HasFlag(TaskContinuationOptions.RunContinuationsAsynchronously)
                ? TaskCreationOptions.RunContinuationsAsynchronously
                : TaskCreationOptions.None);
            if (cancellationToken.IsCancellationRequested)
            {
                continuation.SetCanceled(new TaskCanceledException(
                    message: null,
                    innerException: null,
                    cancellationToken));
                return continuation;
            }

            CancellationTokenRegistration registration = default;
            void Run()
            {
                if (continuation.IsCompleted)
                {
                    return;
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    continuation.TrySetCanceled(new TaskCanceledException(
                        message: null,
                        innerException: null,
                        cancellationToken));
                }
                else if (!ShouldRunContinuation(continuationOptions))
                {
                    continuation.TrySetCanceled(new TaskCanceledException(
                        message: null,
                        innerException: null,
                        cancellationToken));
                }
                else
                {
                    try
                    {
                        continuation.ExecuteAsCurrent(() =>
                        {
                            continuationAction(this, state);
                            continuation.TrySetResult();
                        });
                    }
                    catch (OperationCanceledException exception)
                    {
                        continuation.TrySetCanceled(exception);
                    }
                    catch (Exception exception)
                    {
                        continuation.TrySetException(exception);
                    }
                }
                registration.Dispose();
            }

            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(
                    static state =>
                    {
                        var cancellation = ((Task, Threading.CancellationToken))state!;
                        cancellation.Item1.TrySetCanceled(new TaskCanceledException(
                            message: null,
                            innerException: null,
                            cancellation.Item2));
                    },
                    (continuation, cancellationToken));
            }
            RegisterContinuation(
                Run,
                continueOnCapturedContext: false,
                flowExecutionContext: true,
                runAsynchronously:
                    !continuationOptions.HasFlag(TaskContinuationOptions.ExecuteSynchronously),
                runInlineIfCompleted:
                    continuationOptions.HasFlag(TaskContinuationOptions.ExecuteSynchronously));
            return continuation;
        }

        private Task<TResult> AddContinuation<TResult>(
            Func<Task, TResult> continuationFunction,
            object? state,
            Threading.CancellationToken cancellationToken,
            TaskContinuationOptions continuationOptions) =>
            AddContinuation(
                (task, _) => continuationFunction(task),
                state,
                cancellationToken,
                continuationOptions);

        private Task<TResult> AddContinuation<TResult>(
            Func<Task, object?, TResult> continuationFunction,
            object? state,
            Threading.CancellationToken cancellationToken,
            TaskContinuationOptions continuationOptions)
        {
            ArgumentNullException.ThrowIfNull(continuationFunction);
            var continuation = new Task<TResult>((object?)null, continuationOptions.HasFlag(TaskContinuationOptions.RunContinuationsAsynchronously)
                ? TaskCreationOptions.RunContinuationsAsynchronously
                : TaskCreationOptions.None);
            if (cancellationToken.IsCancellationRequested)
            {
                continuation.SetCanceled(new TaskCanceledException(
                    message: null,
                    innerException: null,
                    cancellationToken));
                return continuation;
            }

            CancellationTokenRegistration registration = default;
            void Run()
            {
                if (continuation.IsCompleted)
                {
                    return;
                }
                if (cancellationToken.IsCancellationRequested || !ShouldRunContinuation(continuationOptions))
                {
                    continuation.TrySetCanceled(new TaskCanceledException(
                        message: null,
                        innerException: null,
                        cancellationToken));
                }
                else
                {
                    try
                    {
                        continuation.ExecuteAsCurrent(() =>
                            continuation.TrySetResult(continuationFunction(this, state)));
                    }
                    catch (OperationCanceledException exception)
                    {
                        continuation.TrySetCanceled(exception);
                    }
                    catch (Exception exception)
                    {
                        continuation.TrySetException(exception);
                    }
                }
                registration.Dispose();
            }

            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(
                    static state =>
                    {
                        var cancellation =
                            ((Task<TResult>, Threading.CancellationToken))state!;
                        cancellation.Item1.TrySetCanceled(new TaskCanceledException(
                            message: null,
                            innerException: null,
                            cancellation.Item2));
                    },
                    (continuation, cancellationToken));
            }
            RegisterContinuation(
                Run,
                continueOnCapturedContext: false,
                flowExecutionContext: true,
                runAsynchronously:
                    !continuationOptions.HasFlag(TaskContinuationOptions.ExecuteSynchronously),
                runInlineIfCompleted:
                    continuationOptions.HasFlag(TaskContinuationOptions.ExecuteSynchronously));
            return continuation;
        }

        private bool ShouldRunContinuation(TaskContinuationOptions options)
        {
            if ((options & TaskContinuationOptions.OnlyOnRanToCompletion) != 0 && !IsCompletedSuccessfully ||
                (options & TaskContinuationOptions.OnlyOnFaulted) != 0 && !IsFaulted ||
                (options & TaskContinuationOptions.OnlyOnCanceled) != 0 && !IsCanceled ||
                (options & TaskContinuationOptions.NotOnRanToCompletion) != 0 && IsCompletedSuccessfully ||
                (options & TaskContinuationOptions.NotOnFaulted) != 0 && IsFaulted ||
                (options & TaskContinuationOptions.NotOnCanceled) != 0 && IsCanceled)
            {
                return false;
            }
            return true;
        }

        public static Task Run(Action action) => throw new PlatformNotSupportedException();

        public static global::System.Runtime.CompilerServices.YieldAwaitable Yield() => new();

        internal void OnCompleted(
            Action continuation,
            bool continueOnCapturedContext = true,
            bool flowExecutionContext = false) =>
            RegisterContinuation(
                continuation,
                continueOnCapturedContext,
                flowExecutionContext,
                runAsynchronously: false,
                runInlineIfCompleted: false);

        internal void RegisterContinuation(
            Action continuation,
            bool continueOnCapturedContext,
            bool flowExecutionContext,
            bool runAsynchronously,
            bool runInlineIfCompleted)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException(nameof(continuation));
            }

            var registration = new ContinuationRegistration(
                continuation,
                continueOnCapturedContext
                    ? Threading.SynchronizationContext.Current
                    : null,
                flowExecutionContext
                    ? Threading.ExecutionContext.Capture()
                    : null,
                runAsynchronously);

            if (_status == 0)
            {
                (_continuations ??= new List<ContinuationRegistration>()).Add(registration);
                return;
            }

            if (runInlineIfCompleted && !registration.RequiresAsynchronousDispatch)
            {
                registration.Invoke();
            }
            else
            {
                registration.Schedule();
            }
        }

        public void Start()
        {
            if (_action is null || _started || IsCompleted)
            {
                throw new InvalidOperationException();
            }
            _started = true;
            PlatformServices.Scheduler.Schedule(ExecuteAction, 0);
        }

        public void RunSynchronously()
        {
            if (_action is null || _started || IsCompleted)
            {
                throw new InvalidOperationException();
            }

            _started = true;
            ExecuteAction();
        }

        protected virtual void ExecuteAction()
        {
            if (IsCompleted)
            {
                return;
            }
            var previousTask = s_currentTask;
            s_currentTask = this;
            try
            {
                if (_action is Action action)
                {
                    action();
                }
                else
                {
                    ((Action<object?>)_action!)(_asyncState);
                }
                TrySetResult();
            }
            catch (OperationCanceledException exception)
            {
                TrySetCanceled(exception);
            }
            catch (Exception exception)
            {
                TrySetException(exception);
            }
            finally
            {
                s_currentTask = previousTask;
            }
        }

        private void CancelFromToken()
        {
            if (!IsCompleted)
            {
                TrySetCanceled(new TaskCanceledException(
                    message: null,
                    innerException: null,
                    _cancellationToken));
            }
        }

        internal virtual bool TrySetResult()
        {
            if (_status != 0)
            {
                return false;
            }
            _status = 1;
            _cancellationRegistration.Dispose();
            DispatchContinuation();
            return true;
        }

        internal void SetResult() =>
            EnsureCompletion(TrySetResult());

        internal bool TrySetException(Exception exception) =>
            TrySetException(exception, aggregateContainer: false);

        internal bool TrySetException(
            Exception exception,
            bool aggregateContainer)
        {
            if (exception == null)
            {
                throw new ArgumentNullException();
            }
            if (_status != 0)
            {
                return false;
            }
            _exception = exception;
            _exceptionIsAggregateContainer = aggregateContainer;
            _exceptionTracker = new TaskExceptionTracker();
            _status = 2;
            _cancellationRegistration.Dispose();
            DispatchContinuation();
            return true;
        }

        internal void SetException(Exception exception) =>
            EnsureCompletion(TrySetException(exception));

        internal bool TrySetCanceled(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException();
            }
            if (_status != 0)
            {
                return false;
            }
            _exception = exception;
            if (exception is OperationCanceledException cancellation)
            {
                _cancellationToken = cancellation.CancellationToken;
            }
            _status = 3;
            _cancellationRegistration.Dispose();
            DispatchContinuation();
            return true;
        }

        internal void SetCanceled(Exception exception) =>
            EnsureCompletion(TrySetCanceled(exception));

        internal void SetCanceledForJavaScriptInterop() =>
            SetCanceled(new TaskCanceledException());

        internal void GetVoidResult() => GetVoidResult(suppressThrowing: false);

        internal void GetVoidResult(bool suppressThrowing)
        {
            if (!IsCompleted)
            {
                throw new PlatformNotSupportedException();
            }
            if (_exception != null)
            {
                _exceptionTracker?.Observe();
                if (!suppressThrowing)
                {
                    throw _exceptionIsAggregateContainer &&
                        _exception is AggregateException aggregate &&
                        aggregate.InnerExceptions.Count != 0
                        ? aggregate.InnerExceptions[0]
                        : _exception;
                }
            }
        }

        private AggregateException ObserveException(
            Exception exception,
            bool aggregateContainer)
        {
            _exceptionTracker?.Observe();
            return aggregateContainer && exception is AggregateException aggregate
                ? aggregate
                : new AggregateException(exception);
        }

        private void DispatchContinuation()
        {
            var continuations = _continuations;
            _continuations = null;
            if (continuations is null)
            {
                return;
            }

            var forceAsynchronous =
                (_creationOptions & TaskCreationOptions.RunContinuationsAsynchronously) != 0;
            foreach (var continuation in continuations)
            {
                if (forceAsynchronous || continuation.RequiresAsynchronousDispatch)
                {
                    continuation.Schedule();
                }
                else
                {
                    continuation.Invoke();
                }
            }
        }

        internal void ExecuteAsCurrent(Action action)
        {
            var previousTask = s_currentTask;
            s_currentTask = this;
            try
            {
                action();
            }
            finally
            {
                s_currentTask = previousTask;
            }
        }

        private sealed class ContinuationRegistration
        {
            private readonly Action _continuation;
            private readonly Threading.SynchronizationContext? _synchronizationContext;
            private readonly Threading.ExecutionContext? _executionContext;
            private readonly bool _runAsynchronously;

            internal ContinuationRegistration(
                Action continuation,
                Threading.SynchronizationContext? synchronizationContext,
                Threading.ExecutionContext? executionContext,
                bool runAsynchronously)
            {
                _continuation = continuation;
                _synchronizationContext = synchronizationContext;
                _executionContext = executionContext;
                _runAsynchronously = runAsynchronously;
            }

            internal bool RequiresAsynchronousDispatch =>
                _runAsynchronously;

            internal void Schedule() =>
                PlatformServices.Scheduler.Schedule(Invoke, 0);

            internal void Invoke()
            {
                if (_synchronizationContext is not null)
                {
                    _synchronizationContext.Post(
                        static state => ((ContinuationRegistration)state!).InvokeCore(),
                        this);
                    return;
                }

                InvokeCore();
            }

            private void InvokeCore()
            {
                if (_executionContext is null)
                {
                    _continuation();
                    return;
                }

                Threading.ExecutionContext.Run(
                    _executionContext,
                    static state => ((Action)state!).Invoke(),
                    _continuation);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            if (!IsCompleted)
            {
                throw new InvalidOperationException();
            }
            _disposed = true;
        }

        private static void EnsureCompletion(bool completed)
        {
            if (!completed)
            {
                throw new InvalidOperationException();
            }
        }
    }

    public class Task<T> : Task
    {
        internal T _result = default!;

        internal Task()
            : base()
        {
        }

        internal Task(object? state, TaskCreationOptions creationOptions)
            : base(state, creationOptions)
        {
        }

        public Task(Func<T> function)
            : this(function, default, TaskCreationOptions.None)
        {
        }

        public Task(Func<T> function, Threading.CancellationToken cancellationToken)
            : this(function, cancellationToken, TaskCreationOptions.None)
        {
        }

        public Task(
            Func<T> function,
            Threading.CancellationToken cancellationToken,
            TaskCreationOptions creationOptions)
            : base(function, null, cancellationToken, creationOptions)
        {
            _function = function ?? throw new ArgumentNullException(nameof(function));
            _functionState = null;
        }

        public Task(Func<T> function, TaskCreationOptions creationOptions)
            : this(function, default, creationOptions)
        {
        }

        public Task(Func<object?, T> function, object? state)
            : this(function, state, default, TaskCreationOptions.None)
        {
        }

        public Task(
            Func<object?, T> function,
            object? state,
            Threading.CancellationToken cancellationToken)
            : this(function, state, cancellationToken, TaskCreationOptions.None)
        {
        }

        public Task(
            Func<object?, T> function,
            object? state,
            TaskCreationOptions creationOptions)
            : this(function, state, default, creationOptions)
        {
        }

        public Task(
            Func<object?, T> function,
            object? state,
            Threading.CancellationToken cancellationToken,
            TaskCreationOptions creationOptions)
            : base(function, state, cancellationToken, creationOptions)
        {
            _function = function ?? throw new ArgumentNullException(nameof(function));
            _functionState = state;
        }

        private Delegate? _function;
        private object? _functionState;

        public T Result => GetResult();

        public static Task<T> FromResult(T result)
        {
            var task = new Task<T>();
            task.SetResult(result);
            return task;
        }

        public static new Task<T> FromException(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException();
            }
            var task = new Task<T>();
            task.SetException(exception);
            return task;
        }

        public static new Task<T> FromCanceled(Threading.CancellationToken cancellationToken)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                throw new ArgumentOutOfRangeException();
            }
            var task = new Task<T>();
            task.SetCanceled(new TaskCanceledException(
                message: null,
                innerException: null,
                cancellationToken));
            return task;
        }

        public new global::System.Runtime.CompilerServices.TaskAwaiter<T> GetAwaiter() => new(this);

        public new ConfiguredTaskAwaitable<T> ConfigureAwait(bool continueOnCapturedContext) =>
            new(new global::System.Runtime.CompilerServices.TaskAwaiter<T>(this), continueOnCapturedContext);

        public new ConfiguredTaskAwaitable<T> ConfigureAwait(ConfigureAwaitOptions options)
        {
            const ConfigureAwaitOptions validOptions =
                ConfigureAwaitOptions.ContinueOnCapturedContext |
                ConfigureAwaitOptions.ForceYielding;
            if ((options & ~validOptions) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }

            return new ConfiguredTaskAwaitable<T>(
                new global::System.Runtime.CompilerServices.TaskAwaiter<T>(this),
                options);
        }

        internal bool TrySetResult(T result)
        {
            if (IsCompleted)
            {
                return false;
            }
            _result = result;
            base.TrySetResult();
            return true;
        }

        internal void SetResult(T result)
        {
            if (!TrySetResult(result))
            {
                throw new InvalidOperationException();
            }
        }

        protected override void ExecuteAction()
        {
            if (IsCompleted)
            {
                return;
            }
            try
            {
                var result = default(T)!;
                ExecuteAsCurrent(() => result = _function is Func<T> function
                    ? function()
                    : ((Func<object?, T>)_function!)(_functionState));
                TrySetResult(result);
            }
            catch (OperationCanceledException exception)
            {
                TrySetCanceled(exception);
            }
            catch (Exception exception)
            {
                TrySetException(exception);
            }
        }

        public Task ContinueWith(Action<Task<T>> continuationAction) =>
            AddContinuation(continuationAction, default, TaskContinuationOptions.None);

        public Task ContinueWith(
            Action<Task<T>> continuationAction,
            Threading.CancellationToken cancellationToken) =>
            AddContinuation(continuationAction, cancellationToken, TaskContinuationOptions.None);

        public Task ContinueWith(
            Action<Task<T>> continuationAction,
            TaskContinuationOptions continuationOptions) =>
            AddContinuation(continuationAction, default, continuationOptions);

        public Task ContinueWith(Action<Task<T>, object?> continuationAction, object? state) =>
            AddContinuation(continuationAction, state, default, TaskContinuationOptions.None);

        public Task ContinueWith(
            Action<Task<T>, object?> continuationAction,
            object? state,
            Threading.CancellationToken cancellationToken) =>
            AddContinuation(continuationAction, state, cancellationToken, TaskContinuationOptions.None);

        public Task ContinueWith(
            Action<Task<T>, object?> continuationAction,
            object? state,
            TaskContinuationOptions continuationOptions) =>
            AddContinuation(continuationAction, state, default, continuationOptions);

        public Task<TResult> ContinueWith<TResult>(Func<Task<T>, TResult> continuationFunction) =>
            AddContinuation(continuationFunction, default, TaskContinuationOptions.None);

        public Task<TResult> ContinueWith<TResult>(
            Func<Task<T>, TResult> continuationFunction,
            Threading.CancellationToken cancellationToken) =>
            AddContinuation(continuationFunction, cancellationToken, TaskContinuationOptions.None);

        public Task<TResult> ContinueWith<TResult>(
            Func<Task<T>, TResult> continuationFunction,
            TaskContinuationOptions continuationOptions) =>
            AddContinuation(continuationFunction, default, continuationOptions);

        public Task<TResult> ContinueWith<TResult>(
            Func<Task<T>, object?, TResult> continuationFunction,
            object? state) =>
            AddContinuation(continuationFunction, state, default, TaskContinuationOptions.None);

        public Task<TResult> ContinueWith<TResult>(
            Func<Task<T>, object?, TResult> continuationFunction,
            object? state,
            Threading.CancellationToken cancellationToken) =>
            AddContinuation(continuationFunction, state, cancellationToken, TaskContinuationOptions.None);

        public Task<TResult> ContinueWith<TResult>(
            Func<Task<T>, object?, TResult> continuationFunction,
            object? state,
            TaskContinuationOptions continuationOptions) =>
            AddContinuation(continuationFunction, state, default, continuationOptions);

        private Task AddContinuation(
            Action<Task<T>> action,
            Threading.CancellationToken cancellationToken,
            TaskContinuationOptions options) =>
            AddContinuation((task, _) => action(task), null, cancellationToken, options);

        private Task AddContinuation(
            Action<Task<T>, object?> action,
            object? state,
            Threading.CancellationToken cancellationToken,
            TaskContinuationOptions options)
        {
            ArgumentNullException.ThrowIfNull(action);
            var continuation = new Task((object?)null, options.HasFlag(TaskContinuationOptions.RunContinuationsAsynchronously)
                ? TaskCreationOptions.RunContinuationsAsynchronously
                : TaskCreationOptions.None);
            AttachContinuation(continuation, cancellationToken, options, () => action(this, state));
            return continuation;
        }

        private Task<TResult> AddContinuation<TResult>(
            Func<Task<T>, TResult> function,
            Threading.CancellationToken cancellationToken,
            TaskContinuationOptions options) =>
            AddContinuation((task, _) => function(task), null, cancellationToken, options);

        private Task<TResult> AddContinuation<TResult>(
            Func<Task<T>, object?, TResult> function,
            object? state,
            Threading.CancellationToken cancellationToken,
            TaskContinuationOptions options)
        {
            ArgumentNullException.ThrowIfNull(function);
            var continuation = new Task<TResult>((object?)null, options.HasFlag(TaskContinuationOptions.RunContinuationsAsynchronously)
                ? TaskCreationOptions.RunContinuationsAsynchronously
                : TaskCreationOptions.None);
            AttachContinuation(continuation, cancellationToken, options, () => continuation.TrySetResult(function(this, state)));
            return continuation;
        }

        private void AttachContinuation(
            Task continuation,
            Threading.CancellationToken cancellationToken,
            TaskContinuationOptions options,
            Action action)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                continuation.TrySetCanceled(new TaskCanceledException(
                    message: null,
                    innerException: null,
                    cancellationToken));
                return;
            }
            CancellationTokenRegistration registration = default;
            void Run()
            {
                if (continuation.IsCompleted)
                {
                    return;
                }
                if (cancellationToken.IsCancellationRequested || !ShouldRunContinuation(options))
                {
                    continuation.TrySetCanceled(new TaskCanceledException(
                        message: null,
                        innerException: null,
                        cancellationToken));
                }
                else
                {
                    try
                    {
                        continuation.ExecuteAsCurrent(() =>
                        {
                            action();
                            if (!continuation.IsCompleted)
                            {
                                continuation.TrySetResult();
                            }
                        });
                    }
                    catch (OperationCanceledException exception)
                    {
                        continuation.TrySetCanceled(exception);
                    }
                    catch (Exception exception)
                    {
                        continuation.TrySetException(exception);
                    }
                }
                registration.Dispose();
            }
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(
                    static state =>
                    {
                        var cancellation = ((Task, Threading.CancellationToken))state!;
                        cancellation.Item1.TrySetCanceled(new TaskCanceledException(
                            message: null,
                            innerException: null,
                            cancellation.Item2));
                    },
                    (continuation, cancellationToken));
            }
            RegisterContinuation(
                Run,
                continueOnCapturedContext: false,
                flowExecutionContext: true,
                runAsynchronously:
                    !options.HasFlag(TaskContinuationOptions.ExecuteSynchronously),
                runInlineIfCompleted:
                    options.HasFlag(TaskContinuationOptions.ExecuteSynchronously));
        }

        private bool ShouldRunContinuation(TaskContinuationOptions options)
        {
            if ((options & TaskContinuationOptions.OnlyOnRanToCompletion) != 0 && !IsCompletedSuccessfully ||
                (options & TaskContinuationOptions.OnlyOnFaulted) != 0 && !IsFaulted ||
                (options & TaskContinuationOptions.OnlyOnCanceled) != 0 && !IsCanceled ||
                (options & TaskContinuationOptions.NotOnRanToCompletion) != 0 && IsCompletedSuccessfully ||
                (options & TaskContinuationOptions.NotOnFaulted) != 0 && IsFaulted ||
                (options & TaskContinuationOptions.NotOnCanceled) != 0 && IsCanceled)
            {
                return false;
            }
            return true;
        }

        internal T GetResult()
        {
            GetResultCore();
            return _result;
        }

        private void GetResultCore()
        {
            if (!IsCompleted)
            {
                throw new PlatformNotSupportedException();
            }
            if (IsFaulted || IsCanceled)
            {
                base.GetVoidResult();
            }
        }

        public new Task<T> WaitAsync(Threading.CancellationToken cancellationToken)
        {
            if (IsCompleted)
            {
                return this;
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return FromCanceled(cancellationToken);
            }
            var completion = new TaskCompletionSource<T>();
            CancellationTokenRegistration registration = default;
            void CompleteFromSource()
            {
                if (completion.Task.IsCompleted)
                {
                    return;
                }
                if (IsFaulted)
                {
                    completion.TrySetException(
                        _exception!,
                        ExceptionIsAggregateContainer);
                }
                else if (IsCanceled)
                {
                    completion.TrySetCanceled();
                }
                else
                {
                    completion.TrySetResult(_result);
                }
                registration.Dispose();
            }
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(
                    static state =>
                    {
                        var cancellation =
                            ((TaskCompletionSource<T>, Threading.CancellationToken))state!;
                        cancellation.Item1.TrySetCanceled(cancellation.Item2);
                    },
                    (completion, cancellationToken));
            }
            OnCompleted(CompleteFromSource, continueOnCapturedContext: false);
            return completion.Task;
        }

        public new Task<T> WaitAsync(TimeSpan timeout) =>
            WaitAsync(timeout, default(Threading.CancellationToken));

        public new Task<T> WaitAsync(TimeSpan timeout, TimeProvider timeProvider) =>
            WaitAsync(timeout, timeProvider, default);

        public new Task<T> WaitAsync(
            TimeSpan timeout,
            TimeProvider timeProvider,
            Threading.CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            if (ReferenceEquals(timeProvider, TimeProvider.System))
            {
                return WaitAsync(timeout, cancellationToken);
            }

            var milliseconds = Task.DelayMilliseconds(timeout);
            if (IsCompleted)
            {
                return this;
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return FromCanceled(cancellationToken);
            }
            if (milliseconds == 0)
            {
                return FromException(new TimeoutException());
            }

            var completion = new TaskCompletionSource<T>();
            Threading.ITimer? timer = null;
            CancellationTokenRegistration registration = default;
            void CompleteFromSource()
            {
                if (completion.Task.IsCompleted)
                {
                    return;
                }
                timer?.Dispose();
                timer = null;
                registration.Dispose();
                if (IsFaulted)
                {
                    completion.TrySetException(
                        _exception!,
                        ExceptionIsAggregateContainer);
                }
                else if (IsCanceled)
                {
                    completion.TrySetCanceled(CancellationToken);
                }
                else
                {
                    completion.TrySetResult(_result);
                }
            }
            void Cancel()
            {
                timer?.Dispose();
                timer = null;
                completion.TrySetCanceled(cancellationToken);
            }
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(Cancel);
            }
            OnCompleted(CompleteFromSource, continueOnCapturedContext: false);
            if (milliseconds >= 0 && !completion.Task.IsCompleted)
            {
                timer = timeProvider.CreateTimer(
                    _ =>
                    {
                        timer = null;
                        registration.Dispose();
                        completion.TrySetException(new TimeoutException());
                    },
                    null,
                    TimeSpan.FromMilliseconds(milliseconds),
                    TimeSpan.FromMilliseconds(-1));
            }
            return completion.Task;
        }

        public new Task<T> WaitAsync(TimeSpan timeout, Threading.CancellationToken cancellationToken)
        {
            var milliseconds = Task.DelayMilliseconds(timeout);
            if (IsCompleted)
            {
                return this;
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return FromCanceled(cancellationToken);
            }
            if (milliseconds == 0)
            {
                return FromException(new TimeoutException());
            }
            var completion = new TaskCompletionSource<T>();
            IPlatformSchedule? scheduled = null;
            CancellationTokenRegistration registration = default;
            void CompleteFromSource()
            {
                if (completion.Task.IsCompleted)
                {
                    return;
                }
                scheduled?.Dispose();
                scheduled = null;
                registration.Dispose();
                if (IsFaulted)
                {
                    completion.TrySetException(
                        _exception!,
                        ExceptionIsAggregateContainer);
                }
                else if (IsCanceled)
                {
                    completion.TrySetCanceled();
                }
                else
                {
                    completion.TrySetResult(_result);
                }
            }
            void Cancel()
            {
                scheduled?.Dispose();
                scheduled = null;
                completion.TrySetCanceled(cancellationToken);
            }
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(Cancel);
            }
            OnCompleted(CompleteFromSource, continueOnCapturedContext: false);
            if (milliseconds >= 0 && !completion.Task.IsCompleted)
            {
                scheduled = PlatformServices.Scheduler.Schedule(
                    () =>
                    {
                        scheduled = null;
                        registration.Dispose();
                        completion.TrySetException(new TimeoutException());
                    },
                    checked((ulong)milliseconds * 1_000_000));
            }
            return completion.Task;
        }

    }

    [AsyncMethodBuilder(typeof(AsyncValueTaskMethodBuilder))]
    public readonly struct ValueTask : IEquatable<ValueTask>
    {
        private readonly Task? _task;
        private readonly IValueTaskSource? _source;
        private readonly short _token;

        public ValueTask(Task task)
        {
            if (task == null)
            {
                throw new ArgumentNullException();
            }
            _task = task;
            _source = null;
            _token = 0;
        }

        public ValueTask(IValueTaskSource source, short token)
        {
            _source = source ?? throw new ArgumentNullException();
            _token = token;
            _task = null;
        }

        public bool IsCompleted => _source != null
            ? _source.GetStatus(_token) != ValueTaskSourceStatus.Pending
            : _task == null || _task.IsCompleted;
        public bool IsCompletedSuccessfully => _source != null
            ? _source.GetStatus(_token) == ValueTaskSourceStatus.Succeeded
            : _task == null || _task.IsCompletedSuccessfully;
        public bool IsFaulted => _source != null
            ? _source.GetStatus(_token) == ValueTaskSourceStatus.Faulted
            : _task != null && _task.IsFaulted;
        public bool IsCanceled => _source != null
            ? _source.GetStatus(_token) == ValueTaskSourceStatus.Canceled
            : _task != null && _task.IsCanceled;

        public static ValueTask CompletedTask => default;

        public static ValueTask<TResult> FromResult<TResult>(TResult result) => new(result);

        public static ValueTask FromCanceled(Threading.CancellationToken cancellationToken) =>
            new(Task.FromCanceled(cancellationToken));

        public static ValueTask<TResult> FromCanceled<TResult>(Threading.CancellationToken cancellationToken) =>
            new(Task<TResult>.FromCanceled(cancellationToken));

        public static ValueTask FromException(Exception exception) =>
            new(Task.FromException(exception));

        public static ValueTask<TResult> FromException<TResult>(Exception exception) =>
            new(Task<TResult>.FromException(exception));

        public override int GetHashCode() =>
            (_source?.GetHashCode() ?? _task?.GetHashCode() ?? 0) ^ _token;

        public override bool Equals(object? obj) => obj is ValueTask other && Equals(other);

        public bool Equals(ValueTask other) =>
            ReferenceEquals(_task, other._task) &&
            ReferenceEquals(_source, other._source) &&
            _token == other._token;

        public static bool operator ==(ValueTask left, ValueTask right) => left.Equals(right);

        public static bool operator !=(ValueTask left, ValueTask right) => !left.Equals(right);

        public global::System.Runtime.CompilerServices.ValueTaskAwaiter GetAwaiter() => new(this);

        public ConfiguredValueTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) =>
            new(new global::System.Runtime.CompilerServices.ValueTaskAwaiter(this), continueOnCapturedContext);

        public ValueTask Preserve() => _source == null ? this : new ValueTask(AsTask());

        public Task AsTask()
        {
            if (_source == null)
            {
                return _task ?? Task.CompletedTask;
            }

            var status = _source.GetStatus(_token);
            if (status != ValueTaskSourceStatus.Pending)
            {
                try
                {
                    _source.GetResult(_token);
                    return Task.CompletedTask;
                }
                catch (OperationCanceledException exception)
                {
                    var canceled = new Task();
                    canceled.SetCanceled(exception);
                    return canceled;
                }
                catch (Exception exception)
                {
                    return Task.FromException(exception);
                }
            }

            var completion = new ValueTaskSourceCompletion(_source, _token);
            _source.OnCompleted(
                static state => ((ValueTaskSourceCompletion)state!).Complete(),
                completion,
                _token,
                ValueTaskSourceOnCompletedFlags.None);
            return completion.Task;
        }

        private sealed class ValueTaskSourceCompletion
        {
            private readonly IValueTaskSource _source;
            private readonly short _token;
            private readonly TaskCompletionSource _completion = new();

            internal ValueTaskSourceCompletion(IValueTaskSource source, short token)
            {
                _source = source;
                _token = token;
            }

            internal Task Task => _completion.Task;

            internal void Complete()
            {
                try
                {
                    _source.GetResult(_token);
                    _completion.TrySetResult();
                }
                catch (OperationCanceledException exception)
                {
                    _completion.TrySetCanceled(exception);
                }
                catch (Exception exception)
                {
                    _completion.TrySetException(exception);
                }
            }
        }

        internal void OnCompleted(
            Action continuation,
            bool continueOnCapturedContext = true,
            bool flowExecutionContext = false)
        {
            if (_source != null)
            {
                _source.OnCompleted(
                    static state => ((Action)state!).Invoke(),
                    continuation,
                    _token,
                    (continueOnCapturedContext
                        ? ValueTaskSourceOnCompletedFlags.UseSchedulingContext
                        : ValueTaskSourceOnCompletedFlags.None) |
                    (flowExecutionContext
                        ? ValueTaskSourceOnCompletedFlags.FlowExecutionContext
                        : ValueTaskSourceOnCompletedFlags.None));
                return;
            }
            if (_task == null)
            {
                PlatformServices.Scheduler.Schedule(continuation, 0);
                return;
            }
            _task.OnCompleted(
                continuation,
                continueOnCapturedContext,
                flowExecutionContext);
        }

        internal void GetResult()
        {
            if (_source != null)
            {
                _source.GetResult(_token);
                return;
            }
            _task?.GetVoidResult();
        }
    }

    [AsyncMethodBuilder(typeof(AsyncValueTaskMethodBuilder<>))]
    public readonly struct ValueTask<T> : IEquatable<ValueTask<T>>
    {
        private readonly T _result;
        private readonly Task<T>? _task;
        private readonly IValueTaskSource<T>? _source;
        private readonly short _token;

        public ValueTask(T result)
        {
            _result = result;
            _task = null;
            _source = null;
            _token = 0;
        }

        public ValueTask(Task<T> task)
        {
            if (task == null)
            {
                throw new ArgumentNullException();
            }
            _result = default!;
            _task = task;
            _source = null;
            _token = 0;
        }

        public ValueTask(IValueTaskSource<T> source, short token)
        {
            _source = source ?? throw new ArgumentNullException();
            _token = token;
            _result = default!;
            _task = null;
        }

        public bool IsCompleted => _source != null
            ? _source.GetStatus(_token) != ValueTaskSourceStatus.Pending
            : _task == null || _task.IsCompleted;
        public bool IsCompletedSuccessfully => _source != null
            ? _source.GetStatus(_token) == ValueTaskSourceStatus.Succeeded
            : _task == null || _task.IsCompletedSuccessfully;
        public bool IsFaulted => _source != null
            ? _source.GetStatus(_token) == ValueTaskSourceStatus.Faulted
            : _task != null && _task.IsFaulted;
        public bool IsCanceled => _source != null
            ? _source.GetStatus(_token) == ValueTaskSourceStatus.Canceled
            : _task != null && _task.IsCanceled;

        public T Result => GetResult();

        public global::System.Runtime.CompilerServices.ValueTaskAwaiter<T> GetAwaiter() => new(this);

        public ConfiguredValueTaskAwaitable<T> ConfigureAwait(bool continueOnCapturedContext) =>
            new(new global::System.Runtime.CompilerServices.ValueTaskAwaiter<T>(this), continueOnCapturedContext);

        public ValueTask<T> Preserve() => _source == null ? this : new ValueTask<T>(AsTask());

        public Task<T> AsTask()
        {
            if (_source == null)
            {
                return _task ?? Task<T>.FromResult(_result);
            }

            var status = _source.GetStatus(_token);
            if (status != ValueTaskSourceStatus.Pending)
            {
                try
                {
                    return Task<T>.FromResult(_source.GetResult(_token));
                }
                catch (OperationCanceledException exception)
                {
                    var canceled = new Task<T>();
                    canceled.SetCanceled(exception);
                    return canceled;
                }
                catch (Exception exception)
                {
                    return Task<T>.FromException(exception);
                }
            }

            var completion = new ValueTaskSourceCompletion(_source, _token);
            _source.OnCompleted(
                static state => ((ValueTaskSourceCompletion)state!).Complete(),
                completion,
                _token,
                ValueTaskSourceOnCompletedFlags.None);
            return completion.Task;
        }

        public override int GetHashCode() =>
            _source != null || _task != null
                ? (_source?.GetHashCode() ?? _task!.GetHashCode()) ^ _token
                : global::System.Collections.Generic.EqualityComparer<T>.Default.GetHashCode(_result!);

        public override bool Equals(object? obj) => obj is ValueTask<T> other && Equals(other);

        public bool Equals(ValueTask<T> other) =>
            _source != null || other._source != null || _task != null || other._task != null
                ? ReferenceEquals(_source, other._source) &&
                  ReferenceEquals(_task, other._task) &&
                  _token == other._token
                : global::System.Collections.Generic.EqualityComparer<T>.Default.Equals(_result, other._result);

        public static bool operator ==(ValueTask<T> left, ValueTask<T> right) => left.Equals(right);

        public static bool operator !=(ValueTask<T> left, ValueTask<T> right) => !left.Equals(right);

        public override string ToString()
        {
            if (!IsCompletedSuccessfully)
            {
                return string.Empty;
            }
            if (_task != null)
            {
                return _task.Result?.ToString() ?? string.Empty;
            }
            if (_source != null)
            {
                return GetResult()?.ToString() ?? string.Empty;
            }
            return _result?.ToString() ?? string.Empty;
        }

        private sealed class ValueTaskSourceCompletion
        {
            private readonly IValueTaskSource<T> _source;
            private readonly short _token;
            private readonly TaskCompletionSource<T> _completion = new();

            internal ValueTaskSourceCompletion(IValueTaskSource<T> source, short token)
            {
                _source = source;
                _token = token;
            }

            internal Task<T> Task => _completion.Task;

            internal void Complete()
            {
                try
                {
                    _completion.TrySetResult(_source.GetResult(_token));
                }
                catch (OperationCanceledException exception)
                {
                    _completion.TrySetCanceled(exception);
                }
                catch (Exception exception)
                {
                    _completion.TrySetException(exception);
                }
            }
        }

        public static implicit operator ValueTask<T>(T result) => new(result);

        public static implicit operator ValueTask<T>(Task<T> task) => new(task);

        internal void OnCompleted(
            Action continuation,
            bool continueOnCapturedContext = true,
            bool flowExecutionContext = false)
        {
            if (_source != null)
            {
                _source.OnCompleted(
                    static state => ((Action)state!).Invoke(),
                    continuation,
                    _token,
                    (continueOnCapturedContext
                        ? ValueTaskSourceOnCompletedFlags.UseSchedulingContext
                        : ValueTaskSourceOnCompletedFlags.None) |
                    (flowExecutionContext
                        ? ValueTaskSourceOnCompletedFlags.FlowExecutionContext
                        : ValueTaskSourceOnCompletedFlags.None));
                return;
            }
            if (_task == null)
            {
                PlatformServices.Scheduler.Schedule(continuation, 0);
                return;
            }
            _task.OnCompleted(
                continuation,
                continueOnCapturedContext,
                flowExecutionContext);
        }

        internal T GetResult() => _source != null
            ? _source.GetResult(_token)
            : _task == null ? _result : _task.GetResult();
    }

}

namespace System.Threading.Tasks
{
    using System.Collections.Generic;

    internal sealed class TaskExceptionTracker
    {
        private bool _observed;

        internal void Observe() => _observed = true;

        ~TaskExceptionTracker()
        {
            if (!_observed)
            {
                TaskDiagnostics.ReportUnobservedException();
            }
        }
    }

    internal static class TaskDiagnostics
    {
        internal static void ReportUnobservedException()
        {
        }
    }

    public sealed class TaskCompletionSource
    {
        private readonly Task _task;

        public TaskCompletionSource()
            : this(TaskCreationOptions.None)
        {
        }

        public TaskCompletionSource(Threading.Tasks.TaskCreationOptions creationOptions)
        {
            ValidateCreationOptions(creationOptions);
            _task = new Task((object?)null, creationOptions);
        }

        public TaskCompletionSource(object? state)
            : this(state, TaskCreationOptions.None)
        {
        }

        public TaskCompletionSource(object? state, TaskCreationOptions creationOptions)
        {
            ValidateCreationOptions(creationOptions);
            _task = new Task(state, creationOptions);
        }

        public Task Task => _task;
        public void SetResult() => _task.SetResult();
        public void SetException(Exception exception) => _task.SetException(exception);
        public void SetException(IEnumerable<Exception> exceptions) =>
            EnsureSetException(exceptions);
        public void SetCanceled() => _task.SetCanceled(new TaskCanceledException());
        public void SetCanceled(Threading.CancellationToken cancellationToken) =>
            _task.SetCanceled(new TaskCanceledException(
                message: null,
                innerException: null,
                cancellationToken));
        public void SetFromTask(Task task)
        {
            if (!TrySetFromTask(task))
            {
                throw new InvalidOperationException();
            }
        }
        public bool TrySetResult() => _task.TrySetResult();
        public bool TrySetException(Exception exception) => _task.TrySetException(exception);
        public bool TrySetException(IEnumerable<Exception> exceptions) =>
            _task.TrySetException(
                new AggregateException(exceptions),
                aggregateContainer: true);
        public bool TrySetCanceled() => _task.TrySetCanceled(new TaskCanceledException());
        public bool TrySetCanceled(Threading.CancellationToken cancellationToken) =>
            _task.TrySetCanceled(new TaskCanceledException(
                message: null,
                innerException: null,
                cancellationToken));
        public bool TrySetFromTask(Task task)
        {
            ArgumentNullException.ThrowIfNull(task);
            if (task.IsCompleted)
            {
                return CompleteFromTask(task);
            }
            task.OnCompleted(() => CompleteFromTask(task), continueOnCapturedContext: false);
            return true;
        }
        internal bool TrySetCanceled(Exception exception) => _task.TrySetCanceled(exception);
        internal bool TrySetException(Exception exception, bool aggregateContainer) =>
            _task.TrySetException(exception, aggregateContainer);

        private bool CompleteFromTask(Task task)
        {
            if (task.IsFaulted)
            {
                return TrySetException(
                    task._exception!,
                    task.ExceptionIsAggregateContainer);
            }
            return task.IsCanceled
                ? TrySetCanceled()
                : TrySetResult();
        }

        private static void ValidateCreationOptions(TaskCreationOptions options)
        {
            if (options is not TaskCreationOptions.None and
                not TaskCreationOptions.RunContinuationsAsynchronously)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }
        }

        private void EnsureSetException(IEnumerable<Exception> exceptions)
        {
            if (!TrySetException(exceptions))
            {
                throw new InvalidOperationException();
            }
        }
    }

    public sealed class TaskCompletionSource<T>
    {
        private readonly Task<T> _task;

        public TaskCompletionSource()
            : this(TaskCreationOptions.None)
        {
        }

        public TaskCompletionSource(Threading.Tasks.TaskCreationOptions creationOptions)
        {
            ValidateCreationOptions(creationOptions);
            _task = new Task<T>((object?)null, creationOptions);
        }

        public TaskCompletionSource(object? state)
            : this(state, TaskCreationOptions.None)
        {
        }

        public TaskCompletionSource(object? state, TaskCreationOptions creationOptions)
        {
            ValidateCreationOptions(creationOptions);
            _task = new Task<T>(state, creationOptions);
        }

        public Task<T> Task => _task;
        public void SetResult(T result) => _task.SetResult(result);
        public void SetException(Exception exception) => _task.SetException(exception);
        public void SetException(IEnumerable<Exception> exceptions) =>
            EnsureSetException(exceptions);
        public void SetCanceled() => _task.SetCanceled(new TaskCanceledException());
        public void SetCanceled(Threading.CancellationToken cancellationToken) =>
            _task.SetCanceled(new TaskCanceledException(
                message: null,
                innerException: null,
                cancellationToken));
        public void SetFromTask(Task<T> task)
        {
            if (!TrySetFromTask(task))
            {
                throw new InvalidOperationException();
            }
        }
        public bool TrySetResult(T result) => _task.TrySetResult(result);
        public bool TrySetException(Exception exception) => _task.TrySetException(exception);
        public bool TrySetException(IEnumerable<Exception> exceptions) =>
            _task.TrySetException(
                new AggregateException(exceptions),
                aggregateContainer: true);
        public bool TrySetCanceled() => _task.TrySetCanceled(new TaskCanceledException());
        public bool TrySetCanceled(Threading.CancellationToken cancellationToken) =>
            _task.TrySetCanceled(new TaskCanceledException(
                message: null,
                innerException: null,
                cancellationToken));
        public bool TrySetFromTask(Task<T> task)
        {
            ArgumentNullException.ThrowIfNull(task);
            if (task.IsCompleted)
            {
                return CompleteFromTask(task);
            }
            task.OnCompleted(() => CompleteFromTask(task), continueOnCapturedContext: false);
            return true;
        }
        internal bool TrySetCanceled(Exception exception) => _task.TrySetCanceled(exception);
        internal bool TrySetException(Exception exception, bool aggregateContainer) =>
            _task.TrySetException(exception, aggregateContainer);

        private bool CompleteFromTask(Task<T> task)
        {
            if (task.IsFaulted)
            {
                return TrySetException(
                    task._exception!,
                    task.ExceptionIsAggregateContainer);
            }
            return task.IsCanceled
                ? TrySetCanceled()
                : TrySetResult(task._result);
        }

        private static void ValidateCreationOptions(TaskCreationOptions options)
        {
            if (options is not TaskCreationOptions.None and
                not TaskCreationOptions.RunContinuationsAsynchronously)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }
        }

        private void EnsureSetException(IEnumerable<Exception> exceptions)
        {
            if (!TrySetException(exceptions))
            {
                throw new InvalidOperationException();
            }
        }
    }
}
