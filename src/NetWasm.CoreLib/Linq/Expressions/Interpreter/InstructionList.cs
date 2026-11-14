// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
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

        internal void EmitCall(MethodBase method, int argumentCount, bool producesValue = true) =>
            Emit(new CallInstruction(method, argumentCount, producesValue));

        internal void EmitBinaryOperator(ExpressionType @operator, Type type) =>
            Emit(new BinaryOperatorInstruction(@operator, type));

        internal void EmitUnaryOperator(ExpressionType @operator, Type type) =>
            Emit(new UnaryOperatorInstruction(@operator, type));

        internal void EmitComparison(Type type, NumericComparison comparison, bool liftedToNull) =>
            Emit(new ComparisonInstruction(type, comparison, liftedToNull));

        internal void EmitEqual(Type type, bool liftedToNull, bool notEqual, bool comparesStringValues) =>
            Emit(new EqualInstruction(type, liftedToNull, notEqual, comparesStringValues));

        internal void EmitNumericConvert(TypeCode from, TypeCode to, bool liftedToNull, bool isChecked) =>
            Emit(new NumericConvertInstruction(from, to, liftedToNull, isChecked));

        internal void EmitNullableValue() => Emit(new NullableValueInstruction());

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

        internal void EmitCoalescingBranch(BranchLabel label)
        {
            var targetStackDepth = _currentStackDepth;
            Emit(new CoalescingBranchInstruction());
            label.AddBranch(this, Count - 1, targetStackDepth);
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
