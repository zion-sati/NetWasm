// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
namespace System.Reflection
{
    public abstract partial class Module
    {
        public MethodInfo? GetMethod(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            return GetMethodImpl(name, DefaultLookup, null, CallingConventions.Any, null, null);
        }

        public MethodInfo? GetMethod(string name, Type[] types) => GetMethod(name, DefaultLookup, null, CallingConventions.Any, types, null);
        public MethodInfo? GetMethod(string name, BindingFlags bindingAttr, Binder? binder, CallingConventions callConvention, Type[] types, ParameterModifier[]? modifiers)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(types);

            for (int i = 0; i < types.Length; i++)
            {
                ArgumentNullException.ThrowIfNull(types[i], nameof(types));
            }
            return GetMethodImpl(name, bindingAttr, binder, callConvention, types, modifiers);
        }

        protected virtual MethodInfo? GetMethodImpl(string name, BindingFlags bindingAttr, Binder? binder, CallingConventions callConvention, Type[]? types, ParameterModifier[]? modifiers) { throw Unavailable(); }

        public MethodInfo[] GetMethods() => GetMethods(DefaultLookup);
        public virtual MethodInfo[] GetMethods(BindingFlags bindingFlags) { throw Unavailable(); }

        public FieldInfo? GetField(string name) => GetField(name, DefaultLookup);
        public virtual FieldInfo? GetField(string name, BindingFlags bindingAttr) { throw Unavailable(); }

        public FieldInfo[] GetFields() => GetFields(DefaultLookup);
        public virtual FieldInfo[] GetFields(BindingFlags bindingFlags) { throw Unavailable(); }

        public virtual Type? GetType(string className) => GetType(className, throwOnError: false, ignoreCase: false);
        public virtual Type? GetType(string className, bool ignoreCase) => GetType(className, throwOnError: false, ignoreCase: ignoreCase);
        public virtual Type? GetType(string className, bool throwOnError, bool ignoreCase) { throw Unavailable(); }

        public virtual Type[] FindTypes(TypeFilter? filter, object? filterCriteria)
        {
            Type[] c = GetTypes();
            int cnt = 0;
            for (int i = 0; i < c.Length; i++)
            {
                if (filter != null && !filter(c[i], filterCriteria))
                    c[i] = null!;
                else
                    cnt++;
            }
            if (cnt == c.Length)
                return c;

            Type[] ret = new Type[cnt];
            cnt = 0;
            for (int i = 0; i < c.Length; i++)
            {
                if (c[i] != null)
                    ret[cnt++] = c[i];
            }
            return ret;
        }

        public virtual void GetPEKind(out PortableExecutableKinds peKind, out ImageFileMachine machine) => throw Unavailable();

        public virtual int MetadataToken => throw Unavailable();

        public FieldInfo? ResolveField(int metadataToken) => ResolveField(metadataToken, null, null);
        public virtual FieldInfo? ResolveField(int metadataToken, Type[]? genericTypeArguments, Type[]? genericMethodArguments) { throw Unavailable(); }

        public MemberInfo? ResolveMember(int metadataToken) => ResolveMember(metadataToken, null, null);
        public virtual MemberInfo? ResolveMember(int metadataToken, Type[]? genericTypeArguments, Type[]? genericMethodArguments) { throw Unavailable(); }

        public MethodBase? ResolveMethod(int metadataToken) => ResolveMethod(metadataToken, null, null);
        public virtual MethodBase? ResolveMethod(int metadataToken, Type[]? genericTypeArguments, Type[]? genericMethodArguments) { throw Unavailable(); }

        public virtual byte[] ResolveSignature(int metadataToken) { throw Unavailable(); }
        public virtual string ResolveString(int metadataToken) { throw Unavailable(); }

        public Type ResolveType(int metadataToken) => ResolveType(metadataToken, null, null);
        public virtual Type ResolveType(int metadataToken, Type[]? genericTypeArguments, Type[]? genericMethodArguments) { throw Unavailable(); }

        public static readonly TypeFilter FilterTypeName = (m, c) => FilterTypeNameImpl(m, c!, StringComparison.Ordinal);
        public static readonly TypeFilter FilterTypeNameIgnoreCase = (m, c) => FilterTypeNameImpl(m, c!, StringComparison.OrdinalIgnoreCase);

        private const BindingFlags DefaultLookup = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;

        // FilterTypeName
        // This method will filter the class based upon the name.  It supports
        //    a trailing wild card.
        private static bool FilterTypeNameImpl(Type cls, object filterCriteria, StringComparison comparison)
        {
            // Check that the criteria object is a String object
            if (filterCriteria is not string str)
            {
                throw new InvalidFilterCriteriaException("A String must be provided for the filter criteria.");
            }
            // Check to see if this is a prefix or exact match requirement
            if (str.EndsWith('*'))
            {
                ReadOnlySpan<char> slice = str.AsSpan(0, str.Length - 1);
                return cls.Name.AsSpan().StartsWith(slice, comparison);
            }

            return cls.Name.Equals(str, comparison);
        }
    }
}
