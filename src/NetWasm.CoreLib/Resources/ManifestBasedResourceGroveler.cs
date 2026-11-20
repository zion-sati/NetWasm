// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptations: NetWasm runs upstream's invariant globalization mode with predefined
// cultures only, where upstream takes the neutral resources from the main assembly without reading
// its NeutralResourcesLanguageAttribute; NetWasm reads no attributes at run time, so that is the
// only path. No satellite assembly is compiled into a program, so a satellite lookup finds none, as
// upstream's does when the file is absent. Custom readers and resource sets that need reflection
// (CustomResourceTypesSupport) are not supported; a set the ResourceManager names is created
// through Activator as upstream does.

using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace System.Resources
{
    // Reads resources from an assembly's manifest (the program's embedded resources).
    internal sealed class ManifestBasedResourceGroveler : IResourceGroveler
    {
        private readonly ResourceManager.ResourceManagerMediator _mediator;

        public ManifestBasedResourceGroveler(ResourceManager.ResourceManagerMediator mediator)
        {
            // here and below: convert asserts to preconditions where appropriate when we get
            // contracts story in place.
            Debug.Assert(mediator != null, "mediator shouldn't be null; check caller");
            _mediator = mediator;
        }

        public ResourceSet? GrovelForResourceSet(CultureInfo culture, Dictionary<string, ResourceSet> localResourceSets, bool tryParents, bool createIfNotExists)
        {
            Debug.Assert(culture != null, "culture shouldn't be null; check caller");
            Debug.Assert(localResourceSets != null, "localResourceSets shouldn't be null; check caller");

            ResourceSet? rs = null;
            Stream? stream = null;
            Assembly? satellite;

            // 1. Fixups for ultimate fallbacks
            CultureInfo lookForCulture = UltimateFallbackFixup(culture);

            // 2. Look for satellite assembly or main assembly, as appropriate
            if (lookForCulture.Name.Length == 0 && _mediator.FallbackLoc == UltimateResourceFallbackLocation.MainAssembly)
            {
                // don't bother looking in satellites in this case
                satellite = _mediator.MainAssembly;
            }
            else
            {
                // A program has no satellite assemblies.
                satellite = null;
                bool raiseException = (culture.Name.Length == 0 && (_mediator.FallbackLoc == UltimateResourceFallbackLocation.Satellite));
                if (raiseException)
                {
                    HandleSatelliteMissing();
                }
            }

            // get resource file name we'll search for. Note, be careful if you're moving this statement
            // around because lookForCulture may be modified from originally requested culture above.
            string fileName = _mediator.GetResourceFileName(lookForCulture);

            // 3. If we identified an assembly to search; look in manifest resource stream for resource file
            if (satellite != null)
            {
                // Handle case in here where someone added a callback for assembly load events.
                // While no other threads have called into GetResourceSet, our own thread can!
                // At that point, we could already have an RS in our hash table, and we don't
                // want to add it twice.
                lock (localResourceSets)
                {
                    localResourceSets.TryGetValue(culture.Name, out rs);
                }

                stream = GetManifestResourceStream(satellite, fileName);
            }

            // 4a. Found a stream; create a ResourceSet if possible
            if (createIfNotExists && stream != null && rs == null)
            {
                Debug.Assert(satellite != null, "satellite should not be null when stream is set");
                rs = CreateResourceSet(stream, satellite);
            }
            else if (stream == null && tryParents)
            {
                // 4b. Didn't find stream; give error if necessary
                bool raiseException = culture.Name.Length == 0;
                if (raiseException)
                {
                    HandleResourceStreamMissing(fileName);
                }
            }

            return rs;
        }

        private CultureInfo UltimateFallbackFixup(CultureInfo lookForCulture)
        {
            CultureInfo returnCulture = lookForCulture;

            // If our neutral resources were written in this culture AND we know the main assembly
            // does NOT contain neutral resources, don't probe for this satellite.
            Debug.Assert(_mediator.NeutralResourcesCulture != null);
            if (lookForCulture.Name == _mediator.NeutralResourcesCulture.Name &&
                _mediator.FallbackLoc == UltimateResourceFallbackLocation.MainAssembly)
            {
                returnCulture = CultureInfo.InvariantCulture;
            }
            else if (lookForCulture.Name.Length == 0 && _mediator.FallbackLoc == UltimateResourceFallbackLocation.Satellite)
            {
                returnCulture = _mediator.NeutralResourcesCulture;
            }

            return returnCulture;
        }

        // Upstream's answer in invariant globalization mode with predefined cultures only: the neutral resources are
        // the main assembly's invariant ones.
        internal static CultureInfo GetNeutralResourcesLanguage(Assembly a, out UltimateResourceFallbackLocation fallbackLocation)
        {
            Debug.Assert(a != null, "assembly != null");
            fallbackLocation = UltimateResourceFallbackLocation.MainAssembly;
            return CultureInfo.InvariantCulture;
        }

        // Constructs a new ResourceSet for a given file name.
        // Use the assembly to resolve assembly manifest resource references.
        // Note that is can be null, but probably shouldn't be.
        // This method could use some refactoring. One thing at a time.
        internal ResourceSet CreateResourceSet(Stream store, Assembly assembly)
        {
            Debug.Assert(store != null, "I need a Stream!");
            // Check to see if this is a Stream the ResourceManager understands,
            // and check for the correct resource reader type.
            if (store.CanSeek && store.Length > 4)
            {
                long startPos = store.Position;

                // not disposing because we want to leave stream open
                BinaryReader br = new BinaryReader(store);

                // Look for our magic number as a little endian int.
                int bytes = br.ReadInt32();
                if (bytes == ResourceManager.MagicNumber)
                {
                    int resMgrHeaderVersion = br.ReadInt32();
                    string? readerTypeName, resSetTypeName;
                    if (resMgrHeaderVersion == ResourceManager.HeaderVersionNumber)
                    {
                        br.ReadInt32();  // We don't want the number of bytes to skip.
                        readerTypeName = br.ReadString();
                        resSetTypeName = br.ReadString();
                    }
                    else if (resMgrHeaderVersion > ResourceManager.HeaderVersionNumber)
                    {
                        // Assume that the future ResourceManager headers will
                        // have two strings for us - the reader type name and
                        // resource set type name.  Read those, then use the num
                        // bytes to skip field to correct our position.
                        int numBytesToSkip = br.ReadInt32();
                        long endPosition = br.BaseStream.Position + numBytesToSkip;

                        readerTypeName = br.ReadString();
                        resSetTypeName = br.ReadString();

                        br.BaseStream.Seek(endPosition, SeekOrigin.Begin);
                    }
                    else
                    {
                        // resMgrHeaderVersion is older than this ResMgr version.
                        // We should add in backwards compatibility support here.
                        Debug.Assert(_mediator.MainAssembly != null);
                        throw new NotSupportedException(SR.Format(SR.NotSupported_ObsoleteResourcesFile, _mediator.MainAssembly.GetName().Name));
                    }

                    store.Position = startPos;
                    // Perf optimization - Don't use Reflection for our defaults.
                    // Note there are two different sets of strings here - the
                    // assembly qualified strings emitted by ResourceWriter, and
                    // the abbreviated ones emitted by InternalResGen.
                    if (CanUseDefaultResourceClasses(readerTypeName, resSetTypeName))
                    {
                        return new RuntimeResourceSet(store, permitDeserialization: true);
                    }

                    // Custom reader and set types are found by reflection (CustomResourceTypesSupport).
                    throw new NotSupportedException(SR.ResourceManager_ReflectionNotAllowed);
                }
                else
                {
                    store.Position = startPos;
                }
            }

            if (_mediator.UserResourceSet == null)
            {
                return new RuntimeResourceSet(store, permitDeserialization: true);
            }
            else
            {
                object[] args = [store, assembly];
                try
                {
                    try
                    {
                        return (ResourceSet)Activator.CreateInstance(_mediator.UserResourceSet, args)!;
                    }
                    catch (MissingMethodException) { }

                    args = new object[1];
                    args[0] = store;
                    return (ResourceSet)Activator.CreateInstance(_mediator.UserResourceSet, args)!;
                }
                catch (MissingMethodException e)
                {
                    throw new InvalidOperationException(SR.Format(SR.InvalidOperation_ResMgrBadResSet_Type, _mediator.UserResourceSet.AssemblyQualifiedName), e);
                }
            }
        }

        private static Stream? GetManifestResourceStream(Assembly satellite, string fileName)
        {
            Debug.Assert(satellite != null, "satellite shouldn't be null; check caller");
            Debug.Assert(fileName != null, "fileName shouldn't be null; check caller");

            return satellite.GetManifestResourceStream(fileName) ??
                CaseInsensitiveManifestResourceStreamLookup(satellite, fileName);
        }

        // Looks up a .resources file in the assembly manifest using
        // case-insensitive lookup rules.  Yes, this is slow.  The metadata
        // dev lead refuses to make all assembly manifest resource lookups case-insensitive,
        // even optionally case-insensitive.
        private static Stream? CaseInsensitiveManifestResourceStreamLookup(Assembly satellite, string name)
        {
            Debug.Assert(satellite != null, "satellite shouldn't be null; check caller");
            Debug.Assert(name != null, "name shouldn't be null; check caller");

            string? canonicalName = null;
            foreach (string existingName in satellite.GetManifestResourceNames())
            {
                if (string.Equals(existingName, name, StringComparison.InvariantCultureIgnoreCase))
                {
                    if (canonicalName == null)
                    {
                        canonicalName = existingName;
                    }
                    else
                    {
                        throw new MissingManifestResourceException(SR.Format(SR.MissingManifestResource_MultipleBlobs, name, satellite.ToString()));
                    }
                }
            }

            if (canonicalName == null)
            {
                return null;
            }

            return satellite.GetManifestResourceStream(canonicalName);
        }

        private bool CanUseDefaultResourceClasses(string readerTypeName, string resSetTypeName)
        {
            Debug.Assert(readerTypeName != null, "readerTypeName shouldn't be null; check caller");
            Debug.Assert(resSetTypeName != null, "resSetTypeName shouldn't be null; check caller");

            if (_mediator.UserResourceSet != null)
                return false;

            // Ignore the actual version of the ResourceReader and
            // RuntimeResourceSet classes.  Let those classes deal with
            // versioning themselves.

            if (readerTypeName != null)
            {
                if (!ResourceManager.IsDefaultType(readerTypeName, ResourceManager.ResReaderTypeName))
                    return false;
            }

            if (resSetTypeName != null)
            {
                if (!ResourceManager.IsDefaultType(resSetTypeName, ResourceManager.ResSetTypeName))
                    return false;
            }

            return true;
        }

        private void HandleSatelliteMissing()
        {
            Debug.Assert(_mediator.MainAssembly != null);
            Debug.Assert(_mediator.NeutralResourcesCulture != null);
            string satAssemName = _mediator.MainAssembly.GetName().Name + ".resources.dll";
            string missingCultureName = _mediator.NeutralResourcesCulture.Name;
            if (missingCultureName.Length == 0)
            {
                missingCultureName = "<invariant>";
            }
            throw new MissingSatelliteAssemblyException(SR.Format(SR.MissingSatelliteAssembly_Culture_Name, _mediator.NeutralResourcesCulture, satAssemName), missingCultureName);
        }

        private static string GetManifestResourceNamesList(Assembly assembly)
        {
            try
            {
                string[] resourceSetNames = assembly.GetManifestResourceNames();
                int length = resourceSetNames.Length;
                string postfix = "\"";

                const int MaxLength = 10;
                if (length > MaxLength)
                {
                    length = MaxLength;
                    postfix = "\", ...";
                }

                return "\"" + string.Join("\", \"", resourceSetNames, 0, length) + postfix;
            }
            catch
            {
                return "\"\"";
            }
        }

        private void HandleResourceStreamMissing(string fileName)
        {
            // We really don't think this should happen - we always
            // expect the neutral locale's resources to be present.
            Debug.Assert(_mediator.MainAssembly != null);
            throw new MissingManifestResourceException(
                            SR.Format(SR.MissingManifestResource_NoNeutralAsm,
                            fileName, _mediator.MainAssembly.GetName().Name, GetManifestResourceNamesList(_mediator.MainAssembly)));
        }
    }
}
