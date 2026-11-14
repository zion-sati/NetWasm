// Portions derived from dotnet/runtime System.Linq.Expressions at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Linq.Expressions.Interpreter
{
    // Each variable's scopes are a list used as a stack: Stack<T> is System.Collections', not CoreLib's.
    internal sealed class LocalVariables
    {
        private readonly Dictionary<ParameterExpression, List<int>> _bindings =
            new();
        private int _currentLocalCount;

        internal int LocalCount { get; private set; }

        internal LocalDefinition Define(ParameterExpression variable)
        {
            var index = _currentLocalCount++;
            if (_currentLocalCount > LocalCount)
            {
                LocalCount = _currentLocalCount;
            }

            if (!_bindings.TryGetValue(variable, out var scopes))
            {
                scopes = new List<int>();
                _bindings.Add(variable, scopes);
            }
            scopes.Add(index);
            return new LocalDefinition(variable, index);
        }

        internal void Undefine(LocalDefinition definition)
        {
            if (!_bindings.TryGetValue(definition.Variable, out var scopes) ||
                scopes.Count == 0 || scopes[^1] != definition.Index)
            {
                throw new InvalidOperationException(
                    "The interpreted local scope is inconsistent.");
            }

            scopes.RemoveAt(scopes.Count - 1);
            if (scopes.Count == 0)
            {
                _bindings.Remove(definition.Variable);
            }
            _currentLocalCount--;
        }

        // The variable's innermost local, or false when this lambda does not define it (its closure may).
        internal bool TryResolve(ParameterExpression variable, out int index)
        {
            if (_bindings.TryGetValue(variable, out var scopes) && scopes.Count != 0)
            {
                index = scopes[^1];
                return true;
            }
            index = -1;
            return false;
        }

        internal int Resolve(ParameterExpression variable)
        {
            if (_bindings.TryGetValue(variable, out var scopes) &&
                scopes.Count != 0)
            {
                return scopes[^1];
            }
            throw new InvalidOperationException(
                "A parameter expression is referenced outside its defining scope.");
        }
    }

    internal readonly struct LocalDefinition
    {
        internal LocalDefinition(ParameterExpression variable, int index)
        {
            Variable = variable;
            Index = index;
        }

        internal ParameterExpression Variable { get; }

        internal int Index { get; }
    }
}
