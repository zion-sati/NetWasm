// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Linq.Expressions.Interpreter
{
    internal sealed class EqualInstruction(
        Type type,
        bool liftedToNull) : Instruction
    {
        internal override int ConsumedStack => 2;

        internal override int ProducedStack => 1;

        internal override int Run(InterpretedFrame frame)
        {
            var right = frame.Pop();
            var left = frame.Pop();
            if (left is null || right is null)
            {
                frame.Push(liftedToNull ? null : left is null && right is null);
                return 1;
            }

            frame.Push(AreEqual(left, right));
            return 1;
        }

        private bool AreEqual(object left, object right)
        {
            if (!type.IsValueType && type != typeof(string))
            {
                return ReferenceEquals(left, right);
            }

            return Type.GetTypeCode(left.GetType()) switch
            {
                TypeCode.Boolean => (bool)left == (bool)right,
                TypeCode.Char => (char)left == (char)right,
                TypeCode.SByte => (sbyte)left == (sbyte)right,
                TypeCode.Byte => (byte)left == (byte)right,
                TypeCode.Int16 => (short)left == (short)right,
                TypeCode.UInt16 => (ushort)left == (ushort)right,
                TypeCode.Int32 => (int)left == (int)right,
                TypeCode.UInt32 => (uint)left == (uint)right,
                TypeCode.Int64 => (long)left == (long)right,
                TypeCode.UInt64 => (ulong)left == (ulong)right,
                TypeCode.Single => (float)left == (float)right,
                TypeCode.Double => (double)left == (double)right,
                TypeCode.String => (string)left == (string)right,
                _ => throw new NotSupportedException(
                    "This equality operand type is outside the NetWasm expression profile."),
            };
        }
    }
}
