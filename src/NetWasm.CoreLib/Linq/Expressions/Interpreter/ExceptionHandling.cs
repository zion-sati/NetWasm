// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce (Interpreter/ControlFlowInstructions.cs, ExceptionHandler).
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.
// NetWasm adaptation: the interpreter keeps one table of try regions. An exception an instruction throws is matched
// against the innermost region around it: a catch in its try body takes it, and a finally or fault block runs with
// the exception held in a local, which the block's end rethrows with its original stack.

using System.Runtime.ExceptionServices;

namespace System.Linq.Expressions.Interpreter
{
    internal sealed class CatchHandler(Type test, int start, int exceptionLocal)
    {
        internal Type Test { get; } = test;

        internal int Start { get; } = start;

        internal int ExceptionLocal { get; } = exceptionLocal;
    }

    internal sealed class TryRegion
    {
        // [TryStart, TryEnd) is the try body; the catch handlers follow up to HandlersEnd; a finally or fault block,
        // if any, is [FinallyStart, FinallyEnd).
        internal int TryStart { get; set; }

        internal int TryEnd { get; set; }

        internal int HandlersEnd { get; set; }

        internal int FinallyStart { get; set; } = -1;

        internal int FinallyEnd { get; set; }

        // The evaluation stack depth when the try began, which a handler starts from.
        internal int StackDepth { get; set; }

        // The local an exceptional entry to the finally or fault block stores the exception in.
        internal int PendingLocal { get; set; }

        internal CatchHandler[] Catches { get; set; } = [];

        internal int End => FinallyStart >= 0 ? FinallyEnd : HandlersEnd;

        // Where execution continues after an exception at the instruction index, or -1 when this region does not
        // handle it: a catch of its try body that matches, else its finally or fault block for an exception in its
        // try body or a catch handler.
        internal int Handle(InterpretedFrame frame, int localCount, int index, Exception exception)
        {
            if (index < TryEnd)
            {
                foreach (var handler in Catches)
                {
                    if (handler.Test.IsInstanceOfType(exception))
                    {
                        frame.StackIndex = localCount + StackDepth;
                        frame.Data[handler.ExceptionLocal] = exception;
                        return handler.Start;
                    }
                }
            }
            if (index < HandlersEnd && FinallyStart >= 0)
            {
                frame.StackIndex = localCount + StackDepth;
                frame.Data[PendingLocal] = exception;
                return FinallyStart;
            }
            return -1;
        }
    }

    // The end of a finally or fault block: an exception that entered it is thrown again, with its original stack.
    internal sealed class EndFinallyInstruction(int pendingLocal) : Instruction
    {
        private readonly int _pendingLocal = pendingLocal;

        internal override int Run(InterpretedFrame frame)
        {
            if (frame.Data[_pendingLocal] is Exception pending)
            {
                frame.Data[_pendingLocal] = null;
                ExceptionDispatchInfo.Capture(pending).Throw();
            }
            return 1;
        }
    }

    // Rethrow: the exception the enclosing catch took, with its original stack.
    internal sealed class RethrowInstruction(int exceptionLocal) : Instruction
    {
        private readonly int _exceptionLocal = exceptionLocal;

        internal override int Run(InterpretedFrame frame)
        {
            ExceptionDispatchInfo.Capture((Exception)frame.Data[_exceptionLocal]!).Throw();
            return 1;
        }
    }
}
