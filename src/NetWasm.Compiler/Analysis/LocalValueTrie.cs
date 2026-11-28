// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;

namespace NetWasm.Compiler.Analysis;

// The values a forward analysis holds for the locals that cross blocks, one per slot of BlockLocalValues, kept
// persistent: a write copies the path to its slot and shares every other subtree, and a subtree whose slots all hold
// the default (nothing known) is absent. A block's state is then copied for nothing, and meeting or comparing two
// states costs only the subtrees they do not share. An array of every slot at every block cost slots times blocks in
// time and memory: a method with a loop in each of thousands of handlers, each loop's counter a local of its own, held
// gigabytes. The analysis's meet must keep a value met with itself and give the default when either side is it.
internal sealed class LocalValueTrie<T>
{
    private const int Bits = 5;
    private const int Width = 1 << Bits;
    private static readonly EqualityComparer<T> Comparer = EqualityComparer<T>.Default;

    private readonly int _levels;
    private readonly int _leafWidth;
    private readonly object? _root;

    private LocalValueTrie(int levels, int leafWidth, object? root)
    {
        _levels = levels;
        _leafWidth = leafWidth;
        _root = root;
    }

    // Nothing known of any slot. Up to 32 slots are one leaf as wide as they are, so a small method's state is an
    // array of its slots, as it was; past that, leaves of 32 under nodes of 32.
    internal static LocalValueTrie<T> Empty(int slots)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slots);
        var levels = 0;
        for (var capacity = Width; capacity < slots; capacity <<= Bits)
        {
            levels++;
        }
        return new(levels, levels == 0 ? slots : Width, null);
    }

    internal T Get(int slot)
    {
        var node = _root;
        for (var level = _levels; level > 0 && node is not null; level--)
        {
            node = ((object?[])node)[(slot >> (Bits * level)) & (Width - 1)];
        }
        return node is null ? default! : ((T[])node)[slot & (Width - 1)];
    }

    // This state with the slot's value replaced; this state itself where the slot holds the value already.
    internal LocalValueTrie<T> Set(int slot, T value)
    {
        var root = Set(_root, _levels, slot, value);
        return ReferenceEquals(root, _root) ? this : new(_levels, _leafWidth, root);
    }

    // The state each slot's values meet to; this state itself where the meet changes none of its slots, so whether a
    // block's entry changed is a reference comparison.
    internal LocalValueTrie<T> Meet(LocalValueTrie<T> other, Func<T, T, T> meet)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(meet);
        var root = Meet(_root, other._root, _levels, meet);
        return ReferenceEquals(root, _root) ? this : new(_levels, _leafWidth, root);
    }

    // Whether every slot holds the same value in both states.
    internal bool SameAs(LocalValueTrie<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Same(_root, other._root, _levels);
    }

    private object? Set(object? node, int level, int slot, T value)
    {
        if (level == 0)
        {
            var leaf = (T[]?)node;
            var index = slot & (Width - 1);
            if (Comparer.Equals(leaf is null ? default! : leaf[index], value))
            {
                return node;
            }
            var written = leaf is null ? new T[_leafWidth] : (T[])leaf.Clone();
            written[index] = value;
            return Comparer.Equals(value, default!) && AllDefault(written) ? null : written;
        }
        var children = (object?[]?)node;
        var childIndex = (slot >> (Bits * level)) & (Width - 1);
        var child = children?[childIndex];
        var updated = Set(child, level - 1, slot, value);
        if (ReferenceEquals(updated, child))
        {
            return node;
        }
        var copy = children is null ? new object?[Width] : (object?[])children.Clone();
        copy[childIndex] = updated;
        return updated is null && AllNull(copy) ? null : copy;
    }

    private static object? Meet(object? left, object? right, int level, Func<T, T, T> meet)
    {
        if (ReferenceEquals(left, right) || left is null)
        {
            return left;
        }
        if (right is null)
        {
            return null;
        }
        if (level == 0)
        {
            var (leftValues, rightValues) = ((T[])left, (T[])right);
            T[]? met = null;
            for (var index = 0; index < leftValues.Length; index++)
            {
                var value = meet(leftValues[index], rightValues[index]);
                if (!Comparer.Equals(value, leftValues[index]))
                {
                    (met ??= (T[])leftValues.Clone())[index] = value;
                }
            }
            return met is null ? left : AllDefault(met) ? null : met;
        }
        var (leftChildren, rightChildren) = ((object?[])left, (object?[])right);
        object?[]? children = null;
        for (var index = 0; index < Width; index++)
        {
            var child = Meet(leftChildren[index], rightChildren[index], level - 1, meet);
            if (!ReferenceEquals(child, leftChildren[index]))
            {
                (children ??= (object?[])leftChildren.Clone())[index] = child;
            }
        }
        return children is null ? left : AllNull(children) ? null : children;
    }

    private static bool Same(object? left, object? right, int level)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }
        // An absent subtree holds only defaults and a present one holds some other value.
        if (left is null || right is null)
        {
            return false;
        }
        if (level == 0)
        {
            return ((T[])left).AsSpan().SequenceEqual((T[])right, Comparer);
        }
        var (leftChildren, rightChildren) = ((object?[])left, (object?[])right);
        for (var index = 0; index < Width; index++)
        {
            if (!Same(leftChildren[index], rightChildren[index], level - 1))
            {
                return false;
            }
        }
        return true;
    }

    private static bool AllDefault(T[] values)
    {
        foreach (var value in values)
        {
            if (!Comparer.Equals(value, default!))
            {
                return false;
            }
        }
        return true;
    }

    private static bool AllNull(object?[] children)
    {
        foreach (var child in children)
        {
            if (child is not null)
            {
                return false;
            }
        }
        return true;
    }
}
