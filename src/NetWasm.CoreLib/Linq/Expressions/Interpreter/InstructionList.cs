// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Reflection;

namespace System.Linq.Expressions.Interpreter
{
    internal sealed class InstructionList
    {
        private readonly List<Instruction> _instructions = new();
        private readonly List<BranchLabel> _labels = new();
        private int _currentStackDepth;
        private int _maxStackDepth;

        internal int Count => _instructions.Count;

        internal int CurrentStackDepth => _currentStackDepth;

        internal void Emit(Instruction instruction)
        {
            if (_currentStackDepth < instruction.ConsumedStack)
            {
                throw new InvalidOperationException(
                    "An interpreted instruction consumes beyond the evaluation stack.");
            }

            _instructions.Add(instruction);
            _currentStackDepth += instruction.StackBalance;
            if (_currentStackDepth > _maxStackDepth)
            {
                _maxStackDepth = _currentStackDepth;
            }
        }

        internal void EmitLoad(object? value) =>
            Emit(new LoadObjectInstruction(value));

        internal void EmitPop() => Emit(PopInstruction.Instance);

        internal void EmitLoadLocal(int index) =>
            Emit(new LoadLocalInstruction(index));

        internal void EmitAssignLocal(int index) =>
            Emit(new AssignLocalInstruction(index));

        internal void EmitStoreLocal(int index) =>
            Emit(new StoreLocalInstruction(index));

        internal void EmitInitializeLocal(int index, object? defaultValue) =>
            Emit(new InitializeLocalInstruction(index, defaultValue));

        internal void EmitLoadField(FieldInfo field) =>
            Emit(new LoadFieldInstruction(field));

        internal void EmitCall(MethodInfo method, int argumentCount) =>
            Emit(new CallInstruction(method, argumentCount));

        internal void EmitAdd(Type type) => Emit(AddInstruction.Create(type));

        internal void EmitNegate(Type type) =>
            Emit(NegateInstruction.Create(type));

        internal void EmitLessThan(Type type) =>
            Emit(ComparisonInstruction.Create(type, NumericComparison.LessThan));

        internal void EmitGreaterThan(Type type) =>
            Emit(ComparisonInstruction.Create(type, NumericComparison.GreaterThan));

        internal void EmitNumericConvert(TypeCode from, TypeCode to) =>
            Emit(new NumericConvertInstruction(from, to));

        internal BranchLabel MakeLabel()
        {
            var label = new BranchLabel();
            _labels.Add(label);
            return label;
        }

        internal void MarkLabel(BranchLabel label) => label.Mark(this);

        internal void EmitBranchFalse(BranchLabel label)
        {
            var instruction = new BranchFalseInstruction();
            Emit(instruction);
            label.AddBranch(this, Count - 1, _currentStackDepth);
        }

        internal void EmitBranch(BranchLabel label, bool preservesValue)
        {
            var targetStackDepth = _currentStackDepth;
            var instruction = new BranchInstruction(preservesValue);
            Emit(instruction);
            label.AddBranch(this, Count - 1, targetStackDepth);
        }

        internal void FixupBranch(int instructionIndex, int offset)
        {
            if (_instructions[instructionIndex] is not OffsetInstruction branch)
            {
                throw new InvalidOperationException(
                    "An interpreted branch fixup targeted another instruction kind.");
            }
            branch.Fixup(offset);
        }

        internal InstructionArray ToArray(int expectedStackDepth)
        {
            foreach (var label in _labels)
            {
                if (!label.IsMarked)
                {
                    throw new InvalidOperationException(
                        "An interpreted branch label was not marked.");
                }
            }
            if (_currentStackDepth != expectedStackDepth)
            {
                throw new InvalidOperationException(
                    "The interpreted instruction list has an invalid final stack depth.");
            }
            return new InstructionArray(_instructions.ToArray(), _maxStackDepth);
        }
    }
}
