// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Linq.Expressions.Interpreter
{
    internal sealed class BranchLabel
    {
        private const int Unknown = int.MinValue;

        private int _targetIndex = Unknown;
        private int _stackDepth = Unknown;
        private List<BranchFixup>? _forwardFixups;

        internal bool IsMarked => _targetIndex != Unknown;

        internal void Mark(InstructionList instructions)
        {
            if (IsMarked)
            {
                throw new InvalidOperationException(
                    "An interpreted branch label was marked more than once.");
            }

            _targetIndex = instructions.Count;
            _stackDepth = instructions.CurrentStackDepth;
            if (_forwardFixups is null)
            {
                return;
            }

            foreach (var fixup in _forwardFixups)
            {
                Fixup(instructions, fixup);
            }
            _forwardFixups = null;
        }

        internal void AddBranch(
            InstructionList instructions,
            int instructionIndex,
            int targetStackDepth)
        {
            var fixup = new BranchFixup(instructionIndex, targetStackDepth);
            if (IsMarked)
            {
                Fixup(instructions, fixup);
                return;
            }

            _forwardFixups ??= new List<BranchFixup>();
            _forwardFixups.Add(fixup);
        }

        private void Fixup(InstructionList instructions, BranchFixup fixup)
        {
            if (fixup.TargetStackDepth != _stackDepth)
            {
                throw new InvalidOperationException(
                    "Interpreted control-flow paths have inconsistent stack depths.");
            }
            instructions.FixupBranch(
                fixup.InstructionIndex,
                _targetIndex - fixup.InstructionIndex);
        }

        private readonly struct BranchFixup
        {
            internal BranchFixup(int instructionIndex, int targetStackDepth)
            {
                InstructionIndex = instructionIndex;
                TargetStackDepth = targetStackDepth;
            }

            internal int InstructionIndex { get; }

            internal int TargetStackDepth { get; }
        }
    }
}
