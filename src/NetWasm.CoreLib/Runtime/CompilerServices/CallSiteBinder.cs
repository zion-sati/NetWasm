// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: NetWasm has no dynamic language runtime, so the binding machinery (BindCore,
// Stitch and LambdaSignature) is not ported. The abstract Bind, BindDelegate's null default, UpdateLabel,
// CacheTarget and the rule cache are upstream's.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic.Utils;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Class responsible for runtime binding of the dynamic operations on the dynamic call site.
    /// </summary>
    public abstract class CallSiteBinder
    {
        /// <summary>
        /// The Level 2 cache - all rules produced for the same binder.
        /// </summary>
        internal Dictionary<Type, object>? Cache;

        /// <summary>
        /// Initializes a new instance of the <see cref="CallSiteBinder"/> class.
        /// </summary>
        protected CallSiteBinder()
        {
        }

        /// <summary>
        /// Gets a label that can be used to cause the binding to be updated. It
        /// indicates that the expression's binding is no longer valid.
        /// This is typically used when the "version" of a dynamic object has
        /// changed.
        /// </summary>
        public static LabelTarget UpdateLabel { get; } = Expression.Label("CallSiteBinder.UpdateLabel");

        /// <summary>
        /// Performs the runtime binding of the dynamic operation on a set of arguments.
        /// </summary>
        /// <param name="args">An array of arguments to the dynamic operation.</param>
        /// <param name="parameters">The array of <see cref="ParameterExpression"/> instances that represent the parameters of the call site in the binding process.</param>
        /// <param name="returnLabel">A LabelTarget used to return the result of the dynamic binding.</param>
        /// <returns>
        /// An Expression that performs tests on the dynamic operation arguments, and
        /// performs the dynamic operation if the tests are valid. If the tests fail on
        /// subsequent occurrences of the dynamic operation, Bind will be called again
        /// to produce a new <see cref="Expression"/> for the new argument types.
        /// </returns>
        public abstract Expression Bind(object[] args, ReadOnlyCollection<ParameterExpression> parameters, LabelTarget returnLabel);

        /// <summary>
        /// Provides low-level runtime binding support.  Classes can override this and provide a direct
        /// delegate for the implementation of rule.  This can enable saving rules to disk, having
        /// specialized rules available at runtime, or providing a different caching policy.
        /// </summary>
        /// <typeparam name="T">The target type of the CallSite.</typeparam>
        /// <param name="site">The CallSite the bind is being performed for.</param>
        /// <param name="args">The arguments for the binder.</param>
        /// <returns>A new delegate which replaces the CallSite Target.</returns>
        public virtual T? BindDelegate<T>(CallSite<T> site, object[] args) where T : class
        {
            return null;
        }

        /// <summary>
        /// Adds a target to the cache of known targets.  The cached targets will
        /// be scanned before calling BindDelegate to produce the new rule.
        /// </summary>
        /// <typeparam name="T">The type of target being added.</typeparam>
        /// <param name="target">The target delegate to be added to the cache.</param>
        protected void CacheTarget<T>(T target) where T : class
        {
            GetRuleCache<T>().AddRule(target);
        }

        internal RuleCache<T> GetRuleCache<T>() where T : class
        {
            // make sure we have cache.
            if (Cache == null)
            {
                Interlocked.CompareExchange(ref Cache, new Dictionary<Type, object>(), null);
            }

            object? ruleCache;
            var cache = Cache;
            lock (cache)
            {
                if (!cache.TryGetValue(typeof(T), out ruleCache))
                {
                    cache[typeof(T)] = ruleCache = new RuleCache<T>();
                }
            }

            RuleCache<T>? result = ruleCache as RuleCache<T>;
            Debug.Assert(result != null);
            return result;
        }
    }
}
