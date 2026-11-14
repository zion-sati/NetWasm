// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptations: creating a proxy generates a type at run time, which NetWasm does not
// provide, so Create<T, TProxy> throws PlatformNotSupportedException instead of calling
// DispatchProxyGenerator. Create(Type, Type) is newer than the netstandard surface and is not
// ported. Subclasses still construct and implement Invoke as usual.

using System.Diagnostics.CodeAnalysis;

namespace System.Reflection
{
    /// <summary>
    /// DispatchProxy provides a mechanism for the instantiation of proxy objects and handling of
    /// their method dispatch.
    /// </summary>
    public abstract class DispatchProxy
    {
        protected DispatchProxy()
        {
        }

        /// <summary>
        /// Whenever any method on the generated proxy type is called, this method is invoked to dispatch control.
        /// </summary>
        /// <param name="targetMethod">The method the caller invoked</param>
        /// <param name="args">The arguments the caller passed to the method</param>
        /// <returns>The object to return to the caller, or <c>null</c> for void methods</returns>
        protected abstract object? Invoke(MethodInfo? targetMethod, object?[]? args);

        /// <summary>
        /// Creates an object instance that derives from class <typeparamref name="TProxy"/>
        /// and implements interface <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The interface the proxy should implement.</typeparam>
        /// <typeparam name="TProxy">The base class to use for the proxy class.</typeparam>
        /// <returns>An object instance that implements <typeparamref name="T"/>.</returns>
        /// <exception cref="System.ArgumentException"><typeparamref name="T"/> is a class,
        /// or <typeparamref name="TProxy"/> is sealed or does not have a parameterless constructor</exception>
        //
        // https://github.com/dotnet/runtime/issues/73136 - we can remove the RequiresDynamicCode annotation.
        // This has been done AOT-safely with .NET Native in the past.
        [RequiresDynamicCode("Creating a proxy instance requires generating code at runtime")]
        public static T Create<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TProxy>()
            where TProxy : DispatchProxy
        {
            throw new PlatformNotSupportedException(SR.DispatchProxy_RuntimeProxyNotSupported);
        }
    }
}
