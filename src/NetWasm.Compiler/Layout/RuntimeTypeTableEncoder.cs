using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Layout;

// Builder: the runtime type table (format 1), every registered type's runtime information in one
// bit stream. Fields are read least significant bit first; every offset is a bit offset from the
// table's start. The runtime decodes the GC, allocator, boxing and casting fields once at startup;
// CoreLib reads flags, generic definitions and arguments, related types and assemblies in place, in
// constant time or a bounded search.
//
// Header, 20 32-bit fields:
//   0 format                              10 offset of the layouts
//   1 capacity N (largest type ID + 1)    11 widths: size | bitmap index << 6 | payload offset << 12
//   2 widths: type ID | shape index << 6 | 12 bitmap pool size P (index 0 means no references)
//     flag index << 12 | layout index << 18 | shape << 24
//   3 widths: assignable count | generic   13 offset of the pool's entry offsets (P fields)
//     count << 6 | rank directory entry << 12 | list sample << 18 | definition << 24
//   4 flag words K (from the header's end) 14 widths: pool entry offset | pool entry length << 6
//   5 offset of the default base types    15 offset of the explicit base types
//   6 offset of the type records          16 offset of the extra assignable types
//   7 offset of the assembly runs, or 0   17 offset of the generic arguments, or 0
//   8 offset of the shapes                18 generic argument lists G
//   9 layouts L                           19 offset of the related types, or 0
//
// Flag words: CoreLib's flags (bits 0-23), the TypeCode (24-28), finalizer (29), value layout (30)
// and registered (31); where Type.Attributes is reachable, the distinct TypeAttributes values follow
// them, without the interface, abstract and sealed bits the flag words hold. Default base types: per flag word, the base most of its types have.
// Type records: N shape indexes; shape 0 marks an unused ID. Shapes: the distinct flag index,
// layout index and, where reachable, generic definition (GetGenericTypeDefinition) and index of the
// TypeAttributes value (Type.Attributes), which takes the shape's bits past the others.
// Assembly runs, where Type.Assembly is reachable: the assembly index width (6 bits), the entry
// assembly's index, a run count (type ID width + 1 bits), then per run of consecutive registered
// types in one assembly its first type ID and assembly index; a type's assembly is its run's,
// found by a binary search.
// Layouts: object size, object bitmap index, value size, boxed payload offset, value bitmap index.
// Bitmap pool entries: a length, then that many reference-slot bits (trailing zeros trimmed).
// Explicit base types: N presence bits (base differs from the default), then the marked types'
// base IDs in ID order. Extra assignable types: N presence bits, then per marked type a count and
// the IDs not in its base chain or its base types' lists, which a cast walks anyway; where
// GetInterfaces is reachable, a bit per listed ID follows the lists, set where the type implements
// that interface itself (not through variance), and a type keeps an interface it implements that its
// base types reach only through variance.
// Generic arguments: an ID dictionary, N presence bits, a rank directory (set bits before each
// 256-bit block), the offset of every 8th list from the first (ceil(G / 8) samples), then per
// marked type a count and its arguments.
// Related types: an ID dictionary, N presence bits, a rank directory, then the marked types' related
// types: a nested type's declaring type or a generic parameter's owner, or an array, pointer or
// by-reference type's element.
// An ID dictionary is a count (type ID width + 1 bits) and that many IDs. With none, a section's
// values are type IDs; with one, they are indexes into it, at the narrowest width that holds them.
// The encoder uses one where it makes the section smaller.
internal sealed class RuntimeTypeTableEncoder : IRuntimeTypeTableEncoder
{
    internal const int Format = 1;
    internal const int HeaderFields = 20;
    internal const int RankBlockBits = 256;
    internal const int ListSampleInterval = 8;
    internal const int Padding = 8;
    // TypeAttributes.Interface | Abstract | Sealed.
    private const uint FlagAttributes = 0x20 | 0x80 | 0x100;
    internal const uint FinalizerFlag = 1u << 29;
    internal const uint ValueLayoutFlag = 1u << 30;
    internal const uint RegisteredFlag = 1u << 31;
    internal const uint DeclaredFlagMask = (1u << 29) - 1;

    public ImmutableArray<byte> Encode(RuntimeTypeTableRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Entries.IsDefault)
        {
            throw new ArgumentException("The type table entries must be initialized.", nameof(request));
        }
        if (request.EntryAssemblyIndex < 0 || request.EntryAssemblyIndex != 0 && !request.IncludeAssemblies)
        {
            throw new ArgumentException("An entry assembly needs the types' assembly indexes.", nameof(request));
        }
        if (request.ReferenceSize is not (4 or 8))
        {
            throw new ArgumentException("A reference is 4 or 8 bytes.", nameof(request));
        }
        var entries = request.Entries.OrderBy(entry => entry.TypeId).ToArray();
        var byId = new Dictionary<int, RuntimeTypeTableEntry>();
        foreach (var entry in entries)
        {
            if (entry.TypeId <= 0 || !byId.TryAdd(entry.TypeId, entry))
            {
                throw new ArgumentException($"Type ID {entry.TypeId} is not a unique positive ID.", nameof(request));
            }
            if ((entry.Flags & ~DeclaredFlagMask) != 0)
            {
                throw new ArgumentException($"Type {entry.TypeId} sets flags the table reserves.", nameof(request));
            }
        }
        var capacity = entries.Length == 0 ? 1 : entries[^1].TypeId + 1;
        foreach (var entry in entries)
        {
            CheckReference(entry.BaseTypeId, capacity, byId, entry.TypeId, "base type");
            CheckReference(entry.RelatedTypeId, capacity, byId, entry.TypeId, "related type");
            CheckReference(entry.GenericDefinitionTypeId, capacity, byId, entry.TypeId, "generic definition");
            if (request.IncludeAssemblies ? entry.AssemblyIndex <= 0 : entry.AssemblyIndex < 0)
            {
                throw new ArgumentException($"Type {entry.TypeId} has no assembly index.", nameof(request));
            }
            foreach (var id in entry.AssignableTypeIds.Concat(entry.GenericArgumentTypeIds))
            {
                CheckReference(id, capacity, byId, entry.TypeId, "related type");
            }
        }

        var words = entries.Select(Word).Distinct().Order().Prepend(0u).ToArray();
        var wordIndex = words.Select((word, index) => (word, index)).ToDictionary(pair => pair.word, pair => pair.index);
        var bitmaps = new Dictionary<string, int>(StringComparer.Ordinal);
        var bitmapList = new List<bool[]>();
        var layouts = new List<(int Size, int Bitmap, int ValueSize, int Payload, int ValueBitmap)>();
        var layoutIndex = new Dictionary<(int, int, int, int, int), int>();
        var typeLayouts = new Dictionary<int, int>();
        foreach (var entry in entries)
        {
            var layout = (entry.ObjectSize, Bitmap(entry.ObjectSize, entry.ReferenceOffsets),
                entry.Value?.Size ?? 0, entry.Value?.BoxedPayloadOffset ?? 0,
                entry.Value is { } value ? Bitmap(value.Size, value.ReferenceOffsets) : 0);
            if (!layoutIndex.TryGetValue(layout, out var index))
            {
                index = layouts.Count;
                layouts.Add(layout);
                layoutIndex.Add(layout, index);
            }
            typeLayouts.Add(entry.TypeId, index);
        }
        // The interface, abstract and sealed bits are the flag word's, which CoreLib adds back.
        uint Attributes(RuntimeTypeTableEntry entry) => (uint)entry.TypeAttributes & ~FlagAttributes;
        uint[] attributeWords = request.IncludeTypeAttributes
            ? [.. entries.Select(Attributes).Distinct().Order()]
            : [];
        var attributeIndex = attributeWords.Select((word, index) => (word, index)).ToDictionary(pair => pair.word, pair => pair.index);
        (int Flag, int Layout, int Definition, int Attributes) Shape(RuntimeTypeTableEntry entry) => (wordIndex[Word(entry)],
            typeLayouts[entry.TypeId], request.IncludeGenericDefinitions ? entry.GenericDefinitionTypeId : 0,
            request.IncludeTypeAttributes ? attributeIndex[Attributes(entry)] : 0);
        var shapes = entries.Select(Shape).Distinct().Order().Prepend((0, 0, 0, 0)).ToArray();
        var shapeIndex = shapes.Select((shape, index) => (shape, index)).ToDictionary(pair => pair.shape, pair => pair.index);

        var idBits = RuntimeTypeTableBitWriter.Width(capacity - 1);
        var shapeIndexBits = RuntimeTypeTableBitWriter.Width(shapes.Length - 1);
        var flagIndexBits = RuntimeTypeTableBitWriter.Width(words.Length - 1);
        var layoutIndexBits = RuntimeTypeTableBitWriter.Width(Math.Max(0, layouts.Count - 1));
        var definitionBits = request.IncludeGenericDefinitions ? idBits : 0;
        var assemblyBits = request.IncludeAssemblies
            ? RuntimeTypeTableBitWriter.Width(entries.Select(entry => entry.AssemblyIndex).Append(request.EntryAssemblyIndex).Max())
            : 0;
        var attributeBits = RuntimeTypeTableBitWriter.Width(Math.Max(0, attributeWords.Length - 1));
        var shapeBits = flagIndexBits + layoutIndexBits + definitionBits + attributeBits;
        var assemblyRuns = entries.Where((entry, index) => index == 0 || entries[index - 1].AssemblyIndex != entry.AssemblyIndex)
            .Select(entry => (First: entry.TypeId, Assembly: entry.AssemblyIndex)).ToArray();
        var defaultBases = words.Select(word => entries
            .Where(entry => Word(entry) == word)
            .GroupBy(entry => entry.BaseTypeId)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => group.Key)
            .FirstOrDefault()).ToArray();
        var extraAssignable = entries.ToDictionary(entry => entry.TypeId,
            entry => ExtraAssignable(entry, byId, request.IncludeInterfaces));
        var assignableCountBits = RuntimeTypeTableBitWriter.Width(
            extraAssignable.Values.Select(list => list.Length).DefaultIfEmpty(0).Max());
        var genericEntries = request.IncludeGenericArguments
            ? entries.Where(entry => !entry.GenericArgumentTypeIds.IsEmpty).ToArray()
            : [];
        var genericCountBits = RuntimeTypeTableBitWriter.Width(
            genericEntries.Select(entry => entry.GenericArgumentTypeIds.Length).DefaultIfEmpty(0).Max());
        var genericIds = IdDictionary.For([.. genericEntries.SelectMany(entry => entry.GenericArgumentTypeIds)], idBits);
        var relatedEntries = request.IncludeRelatedTypes
            ? entries.Where(entry => entry.RelatedTypeId != 0).ToArray()
            : [];
        var relatedIds = IdDictionary.For([.. relatedEntries.Select(entry => entry.RelatedTypeId)], idBits);
        var rankBits = RuntimeTypeTableBitWriter.Width(capacity);
        var sampleBits = RuntimeTypeTableBitWriter.Width(genericEntries.Sum(entry =>
            genericCountBits + (long)entry.GenericArgumentTypeIds.Length * genericIds.ValueBits));

        var writer = new RuntimeTypeTableBitWriter();
        for (var field = 0; field < HeaderFields; field++)
        {
            writer.Write(0, 32);
        }
        Header(0, Format);
        Header(1, (uint)capacity);
        Header(2, (uint)(idBits | shapeIndexBits << 6 | flagIndexBits << 12 | layoutIndexBits << 18 | shapeBits << 24));
        Header(3, (uint)(assignableCountBits | genericCountBits << 6 | rankBits << 12 | sampleBits << 18 | definitionBits << 24));
        Header(4, (uint)words.Length);
        foreach (var word in words.Concat(attributeWords))
        {
            writer.Write(word, 32);
        }
        Header(5, (uint)writer.Length);
        foreach (var baseType in defaultBases)
        {
            writer.Write((uint)baseType, idBits);
        }
        Header(6, (uint)writer.Length);
        for (var id = 0; id < capacity; id++)
        {
            writer.Write(byId.TryGetValue(id, out var entry) ? (uint)shapeIndex[Shape(entry)] : 0u, shapeIndexBits);
        }
        Header(8, (uint)writer.Length);
        foreach (var (flag, layout, definition, attributes) in shapes)
        {
            writer.Write((uint)flag, flagIndexBits);
            writer.Write((uint)layout, layoutIndexBits);
            writer.Write((uint)definition, definitionBits);
            writer.Write((uint)attributes, attributeBits);
        }

        var sizeBits = RuntimeTypeTableBitWriter.Width(layouts.Select(layout => Math.Max(layout.Size, layout.ValueSize)).DefaultIfEmpty(0).Max());
        var bitmapIndexBits = RuntimeTypeTableBitWriter.Width(bitmapList.Count);
        var payloadBits = RuntimeTypeTableBitWriter.Width(layouts.Select(layout => layout.Payload).DefaultIfEmpty(0).Max());
        Header(9, (uint)layouts.Count);
        Header(10, (uint)writer.Length);
        Header(11, (uint)(sizeBits | bitmapIndexBits << 6 | payloadBits << 12));
        foreach (var layout in layouts)
        {
            writer.Write((uint)layout.Size, sizeBits);
            writer.Write((uint)layout.Bitmap, bitmapIndexBits);
            writer.Write((uint)layout.ValueSize, sizeBits);
            writer.Write((uint)layout.Payload, payloadBits);
            writer.Write((uint)layout.ValueBitmap, bitmapIndexBits);
        }

        var lengthBits = RuntimeTypeTableBitWriter.Width(bitmapList.Select(bits => bits.Length).DefaultIfEmpty(0).Max());
        var poolBits = bitmapList.Sum(bits => (long)lengthBits + bits.Length);
        var poolOffsetBits = RuntimeTypeTableBitWriter.Width(poolBits);
        Header(12, (uint)bitmapList.Count);
        Header(13, (uint)writer.Length);
        Header(14, (uint)(poolOffsetBits | lengthBits << 6));
        var poolOffset = 0L;
        foreach (var bits in bitmapList)
        {
            writer.Write((ulong)poolOffset, poolOffsetBits);
            poolOffset += lengthBits + bits.Length;
        }
        foreach (var bits in bitmapList)
        {
            writer.Write((uint)bits.Length, lengthBits);
            foreach (var bit in bits)
            {
                writer.Write(bit ? 1u : 0u, 1);
            }
        }

        Header(15, (uint)writer.Length);
        var explicitBases = entries.Where(entry => entry.BaseTypeId != defaultBases[wordIndex[Word(entry)]]).ToArray();
        Presence(explicitBases);
        foreach (var entry in explicitBases)
        {
            writer.Write((uint)entry.BaseTypeId, idBits);
        }

        Header(16, (uint)writer.Length);
        var extras = entries.Where(entry => !extraAssignable[entry.TypeId].IsEmpty).ToArray();
        Presence(extras);
        foreach (var entry in extras)
        {
            var list = extraAssignable[entry.TypeId];
            writer.Write((uint)list.Length, assignableCountBits);
            foreach (var id in list)
            {
                writer.Write((uint)id, idBits);
            }
        }
        if (request.IncludeInterfaces)
        {
            foreach (var entry in extras)
            {
                foreach (var id in extraAssignable[entry.TypeId])
                {
                    writer.Write(entry.ImplementedInterfaceTypeIds.Contains(id) ? 1u : 0u, 1);
                }
            }
        }

        if (request.IncludeGenericArguments)
        {
            Header(17, (uint)writer.Length);
            Header(18, (uint)genericEntries.Length);
            genericIds.Write(writer, idBits);
            Presence(genericEntries);
            RankDirectory(genericEntries);
            var listOffset = 0L;
            for (var index = 0; index < genericEntries.Length; index++)
            {
                if (index % ListSampleInterval == 0)
                {
                    writer.Write((ulong)listOffset, sampleBits);
                }
                listOffset += genericCountBits + (long)genericEntries[index].GenericArgumentTypeIds.Length * genericIds.ValueBits;
            }
            foreach (var entry in genericEntries)
            {
                writer.Write((uint)entry.GenericArgumentTypeIds.Length, genericCountBits);
                foreach (var id in entry.GenericArgumentTypeIds)
                {
                    writer.Write(genericIds.Value(id), genericIds.ValueBits);
                }
            }
        }
        if (request.IncludeAssemblies)
        {
            Header(7, (uint)writer.Length);
            writer.Write((uint)assemblyBits, 6);
            writer.Write((uint)request.EntryAssemblyIndex, assemblyBits);
            writer.Write((uint)assemblyRuns.Length, idBits + 1);
            foreach (var (first, assembly) in assemblyRuns)
            {
                writer.Write((uint)first, idBits);
                writer.Write((uint)assembly, assemblyBits);
            }
        }
        if (request.IncludeRelatedTypes)
        {
            Header(19, (uint)writer.Length);
            relatedIds.Write(writer, idBits);
            Presence(relatedEntries);
            RankDirectory(relatedEntries);
            foreach (var entry in relatedEntries)
            {
                writer.Write(relatedIds.Value(entry.RelatedTypeId), relatedIds.ValueBits);
            }
        }
        return writer.ToBytes(Padding);

        void Header(int field, uint value) => writer.Patch(field * 32L, value, 32);

        void Presence(IReadOnlyCollection<RuntimeTypeTableEntry> marked)
        {
            var ids = marked.Select(entry => entry.TypeId).ToHashSet();
            for (var id = 0; id < capacity; id++)
            {
                writer.Write(ids.Contains(id) ? 1u : 0u, 1);
            }
        }

        void RankDirectory(IReadOnlyCollection<RuntimeTypeTableEntry> marked)
        {
            var ids = marked.Select(entry => entry.TypeId).Order().ToArray();
            var before = 0;
            for (var block = 0; block * RankBlockBits < capacity; block++)
            {
                while (before < ids.Length && ids[before] < block * RankBlockBits)
                {
                    before++;
                }
                writer.Write((uint)before, rankBits);
            }
        }

        int Bitmap(int size, ImmutableArray<int> referenceOffsets)
        {
            if (referenceOffsets.IsDefaultOrEmpty)
            {
                return 0;
            }
            var bits = new bool[referenceOffsets.Max() / request.ReferenceSize + 1];
            foreach (var offset in referenceOffsets)
            {
                if (offset < 0 || offset % request.ReferenceSize != 0 || offset >= size)
                {
                    throw new ArgumentException($"Reference offset {offset} is not a slot of a {size}-byte layout.", nameof(request));
                }
                bits[offset / request.ReferenceSize] = true;
            }
            var key = new string([.. bits.Select(bit => bit ? '1' : '0')]);
            if (!bitmaps.TryGetValue(key, out var index))
            {
                bitmapList.Add(bits);
                index = bitmapList.Count;
                bitmaps.Add(key, index);
            }
            return index;
        }
    }

    // A section's type IDs, either as they are or as indexes into the distinct IDs, whichever is
    // smaller.
    private sealed class IdDictionary
    {
        private readonly int[] _ids;
        private readonly Dictionary<int, int> _indexes;

        private IdDictionary(int[] ids, int valueBits)
        {
            _ids = ids;
            _indexes = ids.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);
            ValueBits = valueBits;
        }

        internal int ValueBits { get; }

        internal static IdDictionary For(int[] values, int idBits)
        {
            var distinct = values.Distinct().Order().ToArray();
            var indexBits = RuntimeTypeTableBitWriter.Width(Math.Max(0, distinct.Length - 1));
            return values.Length > 0 &&
                (long)distinct.Length * idBits + (long)values.Length * indexBits < (long)values.Length * idBits
                ? new IdDictionary(distinct, indexBits)
                : new IdDictionary([], idBits);
        }

        internal uint Value(int id) => _ids.Length == 0 ? (uint)id : (uint)_indexes[id];

        internal void Write(RuntimeTypeTableBitWriter writer, int idBits)
        {
            writer.Write((uint)_ids.Length, idBits + 1);
            foreach (var id in _ids)
            {
                writer.Write((uint)id, idBits);
            }
        }
    }

    private static uint Word(RuntimeTypeTableEntry entry) => entry.Flags | RegisteredFlag |
        (entry.HasFinalizer ? FinalizerFlag : 0) | (entry.Value is not null ? ValueLayoutFlag : 0);

    // The assignable types a cast would not reach by walking the base chain and the chain's lists.
    private static ImmutableArray<int> ExtraAssignable(
        RuntimeTypeTableEntry entry,
        Dictionary<int, RuntimeTypeTableEntry> byId,
        bool includeInterfaces)
    {
        var reached = new HashSet<int> { entry.TypeId };
        var inherited = new HashSet<int>();
        var visited = new HashSet<int> { entry.TypeId };
        for (var baseId = entry.BaseTypeId; baseId != 0 && visited.Add(baseId); baseId = byId[baseId].BaseTypeId)
        {
            reached.Add(baseId);
            reached.UnionWith(byId[baseId].AssignableTypeIds);
            inherited.UnionWith(byId[baseId].ImplementedInterfaceTypeIds);
        }
        return [.. entry.AssignableTypeIds.Where(id => !reached.Contains(id) ||
            includeInterfaces && entry.ImplementedInterfaceTypeIds.Contains(id) && !inherited.Contains(id)).Distinct().Order()];
    }

    private static void CheckReference(int id, int capacity, Dictionary<int, RuntimeTypeTableEntry> byId,
        int owner, string role)
    {
        if (id != 0 && (id >= capacity || !byId.ContainsKey(id)))
        {
            throw new ArgumentException($"Type {owner} names {role} {id}, which the table does not register.");
        }
    }
}
