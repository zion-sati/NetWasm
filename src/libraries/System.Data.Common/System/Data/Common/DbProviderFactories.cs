// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace System.Data.Common
{
    // NetWasm: upstream resolves deferred and Type-based registrations by loading the factory type and reading its static
    // Instance field, and records Type.AssemblyQualifiedName for instance registrations. NetWasm has neither dynamic type
    // lookup nor assembly metadata, so (as the API policy classifies them) GetFactory(DataRow), RegisterFactory(string, Type)
    // and resolving a deferred registration throw PlatformNotSupportedException; instance registration and lookup work,
    // their assembly-qualified name stays unknown, and GetFactoryClasses fails clearly rather than inventing one.
    public static partial class DbProviderFactories
    {
        private struct ProviderRegistration
        {
            internal ProviderRegistration(string? factoryTypeAssemblyQualifiedName, DbProviderFactory? factoryInstance)
            {
                this.FactoryTypeAssemblyQualifiedName = factoryTypeAssemblyQualifiedName;
                this.FactoryInstance = factoryInstance;
            }

            /// <summary>The supplied name of a deferred registration; null for an instance registration.</summary>
            internal string? FactoryTypeAssemblyQualifiedName { get; }

            /// <summary>
            /// The cached instance of the type in <see cref="FactoryTypeAssemblyQualifiedName"/>. If null, this registration is seen as a deferred registration
            /// and <see cref="FactoryTypeAssemblyQualifiedName"/> is checked the first time when this registration is requested through GetFactory().
            /// </summary>
            internal DbProviderFactory? FactoryInstance { get; }
        }

        private static readonly ConcurrentDictionary<string, ProviderRegistration> _registeredFactories = new ConcurrentDictionary<string, ProviderRegistration>();
        private const string AssemblyQualifiedNameColumnName = "AssemblyQualifiedName";
        private const string InvariantNameColumnName = "InvariantName";
        private const string NameColumnName = "Name";
        private const string DescriptionColumnName = "Description";
        private const string ProviderGroupColumnName = "DbProviderFactories";

        public static bool TryGetFactory(string providerInvariantName, [NotNullWhen(true)] out DbProviderFactory? factory)
        {
            factory = GetFactory(providerInvariantName, throwOnError: false);
            return factory != null;
        }

        public static DbProviderFactory GetFactory(string providerInvariantName)
        {
            return GetFactory(providerInvariantName, throwOnError: true)!;
        }

        [RequiresUnreferencedCode("Provider type and its members might be trimmed if not referenced directly.")]
        public static DbProviderFactory GetFactory(DataRow providerRow)
        {
            ADP.CheckArgumentNull(providerRow, nameof(providerRow));
            throw new PlatformNotSupportedException(SR.NetWasm_DbProviderFactoryTypeResolutionUnsupported);
        }

        public static DbProviderFactory? GetFactory(DbConnection connection)
        {
            ADP.CheckArgumentNull(connection, nameof(connection));

            return connection.ProviderFactory;
        }

        public static DataTable GetFactoryClasses()
        {
            DataColumn nameColumn = new DataColumn(NameColumnName, typeof(string)) { ReadOnly = true };
            DataColumn descriptionColumn = new DataColumn(DescriptionColumnName, typeof(string)) { ReadOnly = true };
            DataColumn invariantNameColumn = new DataColumn(InvariantNameColumnName, typeof(string)) { ReadOnly = true };
            DataColumn assemblyQualifiedNameColumn = new DataColumn(AssemblyQualifiedNameColumnName, typeof(string)) { ReadOnly = true };

            DataTable toReturn = new DataTable(ProviderGroupColumnName) { Locale = CultureInfo.InvariantCulture };
            toReturn.Columns.AddRange(new[] { nameColumn, descriptionColumn, invariantNameColumn, assemblyQualifiedNameColumn });
            toReturn.PrimaryKey = new[] { invariantNameColumn };
            foreach (var kvp in _registeredFactories)
            {
                DataRow newRow = toReturn.NewRow();
                newRow[InvariantNameColumnName] = kvp.Key;
                newRow[AssemblyQualifiedNameColumnName] = kvp.Value.FactoryTypeAssemblyQualifiedName
                    ?? throw new PlatformNotSupportedException(SR.Format(SR.NetWasm_DbProviderFactoryIdentityUnsupported, kvp.Key));
                newRow[NameColumnName] = string.Empty;
                newRow[DescriptionColumnName] = string.Empty;
                toReturn.AddRow(newRow);
            }
            return toReturn;
        }

        public static IEnumerable<string> GetProviderInvariantNames()
        {
            return _registeredFactories.Keys.ToList();
        }

        public static void RegisterFactory(string providerInvariantName, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] string factoryTypeAssemblyQualifiedName)
        {
            ADP.CheckArgumentLength(providerInvariantName, nameof(providerInvariantName));
            ADP.CheckArgumentLength(factoryTypeAssemblyQualifiedName, nameof(factoryTypeAssemblyQualifiedName));

            // this method performs a deferred registration: the type name specified is checked when the factory is requested for the first time.
            _registeredFactories[providerInvariantName] = new ProviderRegistration(factoryTypeAssemblyQualifiedName, null);
        }

        public static void RegisterFactory(string providerInvariantName, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type providerFactoryClass)
        {
            ADP.CheckArgumentNull(providerFactoryClass, nameof(providerFactoryClass));
            throw new PlatformNotSupportedException(SR.NetWasm_DbProviderFactoryTypeResolutionUnsupported);
        }

        public static void RegisterFactory(string providerInvariantName, DbProviderFactory factory)
        {
            ADP.CheckArgumentLength(providerInvariantName, nameof(providerInvariantName));
            ADP.CheckArgumentNull(factory, nameof(factory));

            _registeredFactories[providerInvariantName] = new ProviderRegistration(null, factory);
        }

        public static bool UnregisterFactory(string providerInvariantName)
        {
            return !string.IsNullOrWhiteSpace(providerInvariantName) && _registeredFactories.TryRemove(providerInvariantName, out _);
        }

        private static DbProviderFactory? GetFactory(string providerInvariantName, bool throwOnError)
        {
            if (throwOnError)
            {
                ADP.CheckArgumentLength(providerInvariantName, nameof(providerInvariantName));
            }
            else
            {
                if (string.IsNullOrWhiteSpace(providerInvariantName))
                {
                    return null;
                }
            }
            bool wasRegistered = _registeredFactories.TryGetValue(providerInvariantName, out ProviderRegistration registration);
            if (!wasRegistered)
            {
                return throwOnError ? throw ADP.Argument(SR.Format(SR.ADP_DbProviderFactories_InvariantNameNotFound, providerInvariantName)) : (DbProviderFactory?)null;
            }
            DbProviderFactory? toReturn = registration.FactoryInstance;
            if (toReturn == null)
            {
                // Deferred registration, do checks now on the type specified and register instance in storage.
                // Even in the case of throwOnError being false, this will throw when an exception occurs checking the registered type as the user has to be notified the
                // registration is invalid, even though the registration is there.
                throw new PlatformNotSupportedException(SR.NetWasm_DbProviderFactoryTypeResolutionUnsupported);
            }
            return toReturn;
        }
    }
}
