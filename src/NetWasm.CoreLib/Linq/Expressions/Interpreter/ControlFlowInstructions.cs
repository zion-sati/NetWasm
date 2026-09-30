// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions.Interpreter
{
    internal abstract class OffsetInstruction : Instruction
    {
        private const int Unknown = int.MinValue;
        private int _offset = Unknown;

        internal void Fixup(int offset)
        {
            if (_offset != Unknown || offset == Unknown)
            {
                throw new InvalidOperationException(
                    "An interpreted branch has an invalid fixup.");
            }
            _offset = offset;
        }

        protected int Offset => _offset != Unknown
            ? _offset
            : throw new InvalidOperationException(
                "An interpreted branch was executed before fixup.");
    }

    internal sealed class BranchFalseInstruction : OffsetInstruction
    {
        internal override int ConsumedStack => 1;

        internal override int Run(InterpretedFrame frame) =>
            !(bool)frame.Pop()! ? Offset : 1;
    }

    internal sealed class BranchInstruction(bool preservesValue) :
        OffsetInstruction
    {
        internal bool PreservesValue { get; } = preservesValue;

        internal override int ConsumedStack => PreservesValue ? 1 : 0;

        internal override int Run(InterpretedFrame frame) => Offset;
    }
}
