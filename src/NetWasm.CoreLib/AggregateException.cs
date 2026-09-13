// Portions derived from dotnet/runtime System.Private.CoreLib AggregateException at
// commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace System
{
    public class AggregateException : Exception
    {
        private readonly Exception[] _innerExceptions;
        private ReadOnlyCollection<Exception>? _innerView;

        public AggregateException() : this("One or more errors occurred.") { }

        public AggregateException(string? message) : base(message ?? "One or more errors occurred.")
        { _innerExceptions = new Exception[0]; }

        public AggregateException(string? message, Exception innerException)
            : base(message ?? "One or more errors occurred.", innerException)
        {
            ArgumentNullException.ThrowIfNull(innerException);
            _innerExceptions = new[] { innerException };
        }

        public AggregateException(IEnumerable<Exception> innerExceptions)
            : this("One or more errors occurred.", innerExceptions) { }

        public AggregateException(params Exception[] innerExceptions)
            : this("One or more errors occurred.", innerExceptions) { }

        public AggregateException(string? message, IEnumerable<Exception> innerExceptions)
            : this(message, Materialize(innerExceptions), cloneExceptions: false) { }

        public AggregateException(string? message, params Exception[] innerExceptions)
            : this(message, innerExceptions ?? throw new ArgumentNullException(nameof(innerExceptions)), cloneExceptions: true) { }

        private AggregateException(string? message, Exception[] innerExceptions, bool cloneExceptions)
            : base(message ?? "One or more errors occurred.", innerExceptions.Length == 0 ? null : innerExceptions[0])
        {
            _innerExceptions = cloneExceptions ? new Exception[innerExceptions.Length] : innerExceptions;
            for (var index = 0; index < innerExceptions.Length; index++)
            {
                _innerExceptions[index] = innerExceptions[index] ?? throw new ArgumentException();
            }
        }

        public ReadOnlyCollection<Exception> InnerExceptions
        {
            get => _innerView ??= new ReadOnlyCollection<Exception>(ToList(_innerExceptions));
        }

        public override Exception GetBaseException()
        {
            Exception current = this;
            while (current is AggregateException aggregate && aggregate._innerExceptions.Length == 1)
                current = aggregate._innerExceptions[0];
            return current;
        }

        public void Handle(Func<Exception, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            List<Exception>? unhandled = null;
            for (var index = 0; index < _innerExceptions.Length; index++)
            {
                if (!predicate(_innerExceptions[index])) (unhandled ??= new List<Exception>()).Add(_innerExceptions[index]);
            }
            if (unhandled is not null) throw new AggregateException(Message, unhandled.ToArray());
        }

        public AggregateException Flatten()
        {
            var flattened = new List<Exception>();
            var pending = new List<AggregateException> { this };
            for (var index = 0; index < pending.Count; index++)
            {
                var current = pending[index];
                for (var innerIndex = 0; innerIndex < current._innerExceptions.Length; innerIndex++)
                {
                    var inner = current._innerExceptions[innerIndex];
                    if (inner is AggregateException nested) pending.Add(nested);
                    else flattened.Add(inner);
                }
            }
            return new AggregateException(Message, flattened.ToArray());
        }

        public override string Message
        {
            get
            {
                if (_innerExceptions.Length == 0) return base.Message;
                var builder = new Text.StringBuilder();
                builder.Append(base.Message);
                builder.Append(' ');
                for (var index = 0; index < _innerExceptions.Length; index++)
                {
                    builder.Append('(');
                    builder.Append(_innerExceptions[index].Message);
                    builder.Append(") ");
                }
                return builder.ToString().Substring(0, builder.Length - 1);
            }
        }

        public override string ToString()
        {
            var builder = new Text.StringBuilder();
            builder.Append(base.ToString());
            for (var index = 0; index < _innerExceptions.Length; index++)
            {
                if (ReferenceEquals(_innerExceptions[index], InnerException)) continue;
                builder.Append("\n ---> ");
                builder.Append(_innerExceptions[index].ToString());
            }
            return builder.ToString();
        }

        private static Exception[] Materialize(IEnumerable<Exception> source)
        {
            ArgumentNullException.ThrowIfNull(source);
            var list = new List<Exception>();
            foreach (var exception in source) list.Add(exception ?? throw new ArgumentException());
            return list.ToArray();
        }

        private static List<Exception> ToList(Exception[] source)
        {
            var list = new List<Exception>(source.Length);
            for (var index = 0; index < source.Length; index++) list.Add(source[index]);
            return list;
        }

    }
}
