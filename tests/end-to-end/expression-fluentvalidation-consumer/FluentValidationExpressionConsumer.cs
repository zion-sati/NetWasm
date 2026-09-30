#region License
// Copyright (c) .NET Foundation and contributors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// Source-adapted from FluentValidation 12.1.0. The package-level concurrent
// cache is replaced with a Dictionary for NetWasm's no-thread profile.
#endregion

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace NetWasm.Tests.ExpressionTrees.FluentValidationSourceAdaptation;

internal static class ExpressionExtensions
{
    public static MemberInfo? GetMember<T, TProperty>(
        this Expression<Func<T, TProperty>> expression)
    {
        var memberExpression = RemoveUnary(expression.Body) as MemberExpression;
        if (memberExpression is null)
        {
            return null;
        }

        Expression? currentExpression = memberExpression.Expression;
        while (true)
        {
            currentExpression = RemoveUnary(currentExpression);
            if (currentExpression?.NodeType == ExpressionType.MemberAccess)
            {
                currentExpression = ((MemberExpression)currentExpression).Expression;
            }
            else
            {
                break;
            }
        }

        return currentExpression?.NodeType == ExpressionType.Parameter
            ? memberExpression.Member
            : null;
    }

    public static bool IsParameterExpression(this LambdaExpression expression) =>
        expression.Body.NodeType == ExpressionType.Parameter;

    private static Expression? RemoveUnary(Expression? expression) =>
        expression is UnaryExpression unary ? unary.Operand : expression;
}

internal static class AccessorCache<T>
{
    private static readonly Dictionary<Key, Delegate> Cache = new();

    public static int Count => Cache.Count;

    public static string? LastRootIdentity { get; private set; }

    public static Func<T, TProperty> GetCachedAccessor<TProperty>(
        MemberInfo? member,
        Expression<Func<T, TProperty>> expression,
        string? cachePrefix = null)
    {
        Key key;
        if (member is null)
        {
            if (!expression.IsParameterExpression() || typeof(T) != typeof(TProperty))
            {
                return expression.Compile();
            }

            LastRootIdentity = typeof(T).FullName + ":" + cachePrefix;
            key = new Key(null, expression, LastRootIdentity);
        }
        else
        {
            key = new Key(member, expression, cachePrefix);
        }

        if (Cache.TryGetValue(key, out var cached))
        {
            return (Func<T, TProperty>)cached;
        }

        var compiled = expression.Compile();
        Cache.Add(key, compiled);
        return compiled;
    }

    private sealed class Key
    {
        private readonly MemberInfo? _memberInfo;
        private readonly string _expressionKey;

        public Key(MemberInfo? member, Expression expression, string? cachePrefix)
        {
            _memberInfo = member;
            _expressionKey = cachePrefix is not null
                ? cachePrefix + expression.ToString()
                : expression.ToString();
        }

        public override bool Equals(object? value) =>
            value is Key other &&
            Equals(_memberInfo, other._memberInfo) &&
            string.Equals(_expressionKey, other._expressionKey);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((_memberInfo is not null ? _memberInfo.GetHashCode() : 0) * 397) ^
                    _expressionKey.GetHashCode();
            }
        }
    }
}

internal sealed class PropertyRule<T, TProperty>
{
    private readonly Func<T, TProperty> _accessor;

    private PropertyRule(
        MemberInfo? member,
        Expression<Func<T, TProperty>> expression,
        Func<T, TProperty> accessor)
    {
        Member = member;
        ExpressionText = expression.ToString();
        _accessor = accessor;
    }

    public MemberInfo? Member { get; }

    public string ExpressionText { get; }

    public static PropertyRule<T, TProperty> Create(
        Expression<Func<T, TProperty>> expression)
    {
        var member = expression.GetMember();
        var accessor = AccessorCache<T>.GetCachedAccessor(member, expression);
        return new PropertyRule<T, TProperty>(member, expression, accessor);
    }

    public TProperty GetValue(T instance) => _accessor(instance);
}

public sealed class Model
{
    public int Value { get; set; }
}

public static class EntryPoint
{
    public static int Run(int input)
    {
        var model = new Model { Value = input };
        var first = PropertyRule<Model, int>.Create(value => value.Value);
        if (first.GetValue(model) != input)
        {
            return 1;
        }
        if (first.Member?.Name != "Value")
        {
            return 2;
        }
        if (first.ExpressionText != "value => value.Value")
        {
            return 3;
        }

        var second = PropertyRule<Model, int>.Create(value => value.Value);
        if (second.GetValue(model) != input || AccessorCache<Model>.Count != 1)
        {
            return 4;
        }

        var root = PropertyRule<Model, Model>.Create(value => value);
        if (!ReferenceEquals(root.GetValue(model), model))
        {
            return 5;
        }
        if (AccessorCache<Model>.Count != 2)
        {
            return 6;
        }
        if (AccessorCache<Model>.LastRootIdentity !=
            "NetWasm.Tests.ExpressionTrees.FluentValidationSourceAdaptation.Model:")
        {
            return 7;
        }
        return 42;
    }
}
