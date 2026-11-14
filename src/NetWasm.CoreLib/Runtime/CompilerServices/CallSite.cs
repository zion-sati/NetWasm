// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: NetWasm has no dynamic language runtime. Create validates its arguments, then
// throws PlatformNotSupportedException where upstream builds the generic site and its update
// delegate; Update throws likewise. The site cache, rules and matchmaker are not ported.
using System.Linq.Expressions;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// A Dynamic Call Site base class. This type is used as a parameter type to the
    /// dynamic site targets. The first parameter of the delegate (T) below must be
    /// of this type.
    /// </summary>
    public class CallSite
    {
        /// <summary>
        /// String used for generated CallSite methods.
        /// </summary>
        internal const string CallSiteTargetMethodName = "CallSite.Target";

        /// <summary>
        /// The Binder responsible for binding operations at this call site.
        /// This binder is invoked by the UpdateAndExecute below if all Level 0,
        /// Level 1 and Level 2 caches experience cache miss.
        /// </summary>
        internal readonly CallSiteBinder? _binder;

        // only CallSite<T> derives from this
        internal CallSite(CallSiteBinder? binder)
        {
            _binder = binder;
        }

        /// <summary>
        /// Used by Matchmaker sites to indicate rule match.
        /// </summary>
        internal bool _match;

        /// <summary>
        /// Class responsible for binding dynamic operations on the dynamic site.
        /// </summary>
        public CallSiteBinder? Binder => _binder;

        public static CallSite Create(Type delegateType, CallSiteBinder binder)
        {
            ArgumentNullException.ThrowIfNull(delegateType);
            ArgumentNullException.ThrowIfNull(binder);
            if (!delegateType.IsSubclassOf(typeof(MulticastDelegate))) throw System.Linq.Expressions.Error.TypeMustBeDerivedFromSystemDelegate();

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_CallSiteCreation);
        }
    }

    /// <summary>
    /// Dynamic site type.
    /// </summary>
    /// <typeparam name="T">The delegate type.</typeparam>
    public class CallSite<T> : CallSite where T : class
    {
        /// <summary>
        /// The update delegate. Called when the dynamic site experiences cache miss.
        /// </summary>
        /// <returns>The update delegate.</returns>
        public T Update => throw new PlatformNotSupportedException(SR.PlatformNotSupported_CallSiteUpdate);

        /// <summary>
        /// The Level 0 cache - a delegate specialized based on the site history.
        /// </summary>
        public T Target = default!;

        private CallSite()
            : base(null)
        {
        }

        /// <summary>
        /// Creates an instance of the dynamic call site, initialized with the binder responsible for the
        /// runtime binding of the dynamic operations at this call site.
        /// </summary>
        /// <param name="binder">The binder responsible for the runtime binding of the dynamic operations at this call site.</param>
        /// <returns>The new instance of dynamic call site.</returns>
        public static CallSite<T> Create(CallSiteBinder binder)
        {
            if (!typeof(T).IsSubclassOf(typeof(MulticastDelegate))) throw System.Linq.Expressions.Error.TypeMustBeDerivedFromSystemDelegate();
            ArgumentNullException.ThrowIfNull(binder);
            throw new PlatformNotSupportedException(SR.PlatformNotSupported_CallSiteCreation);
        }
    }
}
