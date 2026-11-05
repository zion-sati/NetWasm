using System;
using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO.Hashing;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace NetWasm.Compiler.Metadata.Inputs;

public interface ICompilationInputReader
{
    // This compile's inputs. An assembly's detail is read when its bytes differ from the
    // last compile's or the last compile has none for it; otherwise it is carried over.
    CompilationInputs Read(MetadataCompilationSnapshot snapshot, CompilationInputs previous);
}

/// <summary>
/// Fingerprints the input assemblies: each one's bytes, and for those that changed, its
/// shape and method bodies. The shape covers every metadata table except method bodies and
/// their local signatures, reading heaps by value (strings, blobs) rather than by offset,
/// since an edit to one body may move heap offsets used by unrelated rows. A body covers
/// its IL with each string literal read as its text, its locals, its exception regions
/// and its header.
/// </summary>
public sealed class CompilationInputReader : ICompilationInputReader
{
    public CompilationInputs Read(MetadataCompilationSnapshot snapshot, CompilationInputs previous)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(previous);
        var assemblies = new AssemblyInputs[snapshot.Assemblies.Length];
        Parallel.For(0, assemblies.Length, index =>
        {
            var assembly = snapshot.Assemblies[index];
            var name = assembly.Identity.Name;
            var content = Fingerprint(assembly.Image);
            var before = previous.Find(name);
            var detail = before is not null && before.Content == content && before.Detail is not null
                ? before.Detail
                : ReadDetail(assembly);
            assemblies[index] = new(name, content, detail);
        });
        return new([.. assemblies]);
    }

    private static InputFingerprint Fingerprint(ReadOnlySpan<byte> bytes)
    {
        var digest = XxHash128.HashToUInt128(bytes);
        return new((ulong)(digest >> 64), (ulong)digest);
    }

    private static AssemblyInputDetail ReadDetail(ManagedAssembly assembly)
    {
        var reader = assembly.Reader;
        var shape = new ShapeHash(reader);
        shape.Module();
        shape.Tables(assembly);
        shape.Resources(assembly.PortableExecutableReader);
        var bodies = new Dictionary<int, InputFingerprint>(reader.MethodDefinitions.Count);
        var body = new XxHash128();
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0) continue;
            body.Reset();
            BodyHash.Append(body, reader, assembly.PortableExecutableReader.GetMethodBody(method.RelativeVirtualAddress));
            var digest = body.GetCurrentHashAsUInt128();
            bodies[MetadataTokens.GetToken(handle)] = new((ulong)(digest >> 64), (ulong)digest);
        }
        return new(shape.Finish(), bodies.ToFrozenDictionary());
    }

    // Metadata tables by value, row by row in table order (rows are referenced by token).
    private sealed class ShapeHash(MetadataReader reader)
    {
        private readonly XxHash128 _hash = new();
        private readonly byte[] _scratch = new byte[8];

        internal InputFingerprint Finish()
        {
            var digest = _hash.GetCurrentHashAsUInt128();
            return new((ulong)(digest >> 64), (ulong)digest);
        }

        internal void Module()
        {
            // The MVID and EnC ids change with any edit; the name and generation do not.
            var module = reader.GetModuleDefinition();
            String(module.Name);
            Int(module.Generation);
            if (reader.IsAssembly)
            {
                var assembly = reader.GetAssemblyDefinition();
                String(assembly.Name);
                String(assembly.Version.ToString());
                String(assembly.Culture);
                Blob(assembly.PublicKey);
                Int((int)assembly.Flags);
                Int((int)assembly.HashAlgorithm);
            }
        }

        internal void Tables(ManagedAssembly assembly)
        {
            Tag(1);
            foreach (var handle in reader.AssemblyReferences)
            {
                var reference = reader.GetAssemblyReference(handle);
                String(reference.Name);
                String(reference.Version.ToString());
                String(reference.Culture);
                Blob(reference.PublicKeyOrToken);
                Int((int)reference.Flags);
            }
            Tag(2);
            foreach (var handle in reader.TypeReferences)
            {
                var reference = reader.GetTypeReference(handle);
                Token(reference.ResolutionScope);
                String(reference.Name);
                String(reference.Namespace);
            }
            Tag(3);
            foreach (var handle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(handle);
                Int((int)type.Attributes);
                String(type.Name);
                String(type.Namespace);
                Token(type.BaseType);
                Token(type.GetDeclaringType());
                var layout = type.GetLayout();
                Int(layout.PackingSize);
                Int(layout.Size);
                Int(type.GetFields().Count);
                foreach (var field in type.GetFields()) Token(field);
                Int(type.GetMethods().Count);
                foreach (var method in type.GetMethods()) Token(method);
                foreach (var implementation in type.GetInterfaceImplementations())
                    Token(reader.GetInterfaceImplementation(implementation).Interface);
                Tag(0);
                foreach (var implementation in type.GetMethodImplementations())
                {
                    var methodImplementation = reader.GetMethodImplementation(implementation);
                    Token(methodImplementation.MethodBody);
                    Token(methodImplementation.MethodDeclaration);
                }
                Tag(0);
                GenericParameters(type.GetGenericParameters());
                foreach (var property in type.GetProperties()) Token(property);
                Tag(0);
                foreach (var @event in type.GetEvents()) Token(@event);
                Tag(0);
            }
            Tag(4);
            foreach (var handle in reader.FieldDefinitions)
            {
                var field = reader.GetFieldDefinition(handle);
                Int((int)field.Attributes);
                String(field.Name);
                Blob(field.Signature);
                Int(field.GetOffset());
                Token(field.GetDefaultValue());
                Blob(field.GetMarshallingDescriptor());
                // Initial data (array and span literals) is read by bodies, so it is shape.
                if (assembly.Fields.TryGetValue(MetadataTokens.GetToken(handle), out var model))
                    Bytes(model.InitialData.AsSpan());
                else
                    Int(-1);
            }
            Tag(5);
            foreach (var handle in reader.MethodDefinitions)
            {
                var method = reader.GetMethodDefinition(handle);
                Int((int)method.Attributes);
                Int((int)method.ImplAttributes);
                String(method.Name);
                Blob(method.Signature);
                var import = method.GetImport();
                Int((int)import.Attributes);
                String(import.Name);
                Token(import.Module);
                foreach (var parameter in method.GetParameters())
                {
                    var definition = reader.GetParameter(parameter);
                    Int((int)definition.Attributes);
                    Int(definition.SequenceNumber);
                    String(definition.Name);
                    Token(definition.GetDefaultValue());
                    Blob(definition.GetMarshallingDescriptor());
                }
                Tag(0);
                GenericParameters(method.GetGenericParameters());
            }
            Tag(6);
            foreach (var handle in reader.MemberReferences)
            {
                var reference = reader.GetMemberReference(handle);
                Token(reference.Parent);
                String(reference.Name);
                Blob(reference.Signature);
            }
            Tag(7);
            for (var row = 1; row <= reader.GetTableRowCount(TableIndex.Constant); row++)
            {
                var constant = reader.GetConstant(MetadataTokens.ConstantHandle(row));
                Token(constant.Parent);
                Int((int)constant.TypeCode);
                Blob(constant.Value);
            }
            Tag(8);
            foreach (var handle in reader.CustomAttributes)
            {
                var attribute = reader.GetCustomAttribute(handle);
                Token(attribute.Parent);
                Token(attribute.Constructor);
                Blob(attribute.Value);
            }
            Tag(9);
            foreach (var handle in reader.DeclarativeSecurityAttributes)
            {
                var security = reader.GetDeclarativeSecurityAttribute(handle);
                Token(security.Parent);
                Int((int)security.Action);
                Blob(security.PermissionSet);
            }
            Tag(10);
            foreach (var handle in reader.PropertyDefinitions)
            {
                var property = reader.GetPropertyDefinition(handle);
                Int((int)property.Attributes);
                String(property.Name);
                Blob(property.Signature);
                var accessors = property.GetAccessors();
                Token(accessors.Getter);
                Token(accessors.Setter);
                foreach (var other in accessors.Others) Token(other);
                Tag(0);
                Token(property.GetDefaultValue());
            }
            Tag(11);
            foreach (var handle in reader.EventDefinitions)
            {
                var @event = reader.GetEventDefinition(handle);
                Int((int)@event.Attributes);
                String(@event.Name);
                Token(@event.Type);
                var accessors = @event.GetAccessors();
                Token(accessors.Adder);
                Token(accessors.Remover);
                Token(accessors.Raiser);
                foreach (var other in accessors.Others) Token(other);
                Tag(0);
            }
            Tag(12);
            for (var row = 1; row <= reader.GetTableRowCount(TableIndex.ModuleRef); row++)
                String(reader.GetModuleReference(MetadataTokens.ModuleReferenceHandle(row)).Name);
            Tag(13);
            for (var row = 1; row <= reader.GetTableRowCount(TableIndex.TypeSpec); row++)
                Blob(reader.GetTypeSpecification(MetadataTokens.TypeSpecificationHandle(row)).Signature);
            Tag(14);
            for (var row = 1; row <= reader.GetTableRowCount(TableIndex.MethodSpec); row++)
            {
                var specification = reader.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(row));
                Token(specification.Method);
                Blob(specification.Signature);
            }
            Tag(15);
            foreach (var handle in reader.ManifestResources)
            {
                var resource = reader.GetManifestResource(handle);
                String(resource.Name);
                Int((int)resource.Attributes);
                Long(resource.Offset);
                Token(resource.Implementation);
            }
            Tag(16);
            foreach (var handle in reader.ExportedTypes)
            {
                var exported = reader.GetExportedType(handle);
                Int((int)exported.Attributes);
                String(exported.Name);
                String(exported.Namespace);
                Token(exported.Implementation);
            }
            Tag(17);
            foreach (var handle in reader.AssemblyFiles)
            {
                var file = reader.GetAssemblyFile(handle);
                String(file.Name);
                Blob(file.HashValue);
                Int(file.ContainsMetadata ? 1 : 0);
            }
        }

        // Embedded resource data, which code may read.
        internal void Resources(PEReader pe)
        {
            Tag(18);
            var directory = pe.PEHeaders.CorHeader?.ResourcesDirectory;
            if (directory is not { Size: > 0 } resources)
            {
                Int(0);
                return;
            }
            var block = pe.GetSectionData(resources.RelativeVirtualAddress);
            var length = Math.Min(resources.Size, block.Length);
            Bytes(block.GetContent(0, length).AsSpan());
        }

        private void GenericParameters(GenericParameterHandleCollection parameters)
        {
            foreach (var handle in parameters)
            {
                var parameter = reader.GetGenericParameter(handle);
                Int(parameter.Index);
                Int((int)parameter.Attributes);
                String(parameter.Name);
                foreach (var constraint in parameter.GetConstraints())
                    Token(reader.GetGenericParameterConstraint(constraint).Type);
                Tag(0);
            }
            Tag(0);
        }

        private void Tag(int tag) => Int(-1000 - tag);

        private void Int(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(_scratch, value);
            _hash.Append(_scratch.AsSpan(0, 4));
        }

        private void Long(long value)
        {
            BinaryPrimitives.WriteInt64LittleEndian(_scratch, value);
            _hash.Append(_scratch);
        }

        private void Token(EntityHandle handle) => Int(handle.IsNil ? 0 : MetadataTokens.GetToken(handle));

        private void Token(Handle handle) => Int(handle.IsNil ? 0 : MetadataTokens.GetToken(handle));

        private void String(StringHandle handle)
        {
            if (handle.IsNil)
            {
                Int(-1);
                return;
            }
            String(reader.GetString(handle));
        }

        private void String(string value)
        {
            Int(value.Length);
            _hash.Append(MemoryMarshal.AsBytes(value.AsSpan()));
        }

        private void Blob(BlobHandle handle)
        {
            if (handle.IsNil)
            {
                Int(-1);
                return;
            }
            var blob = reader.GetBlobReader(handle);
            Bytes(blob.ReadBytes(blob.Length));
        }

        private void Bytes(ReadOnlySpan<byte> bytes)
        {
            Int(bytes.Length);
            _hash.Append(bytes);
        }
    }

    // A method body: header, locals by signature, exception regions, and IL with each
    // string literal's token replaced by its text.
    private static class BodyHash
    {
        public static void Append(XxHash128 hash, MetadataReader reader, MethodBodyBlock body)
        {
            void Int(int value) => Integer(hash, value);
            Int(body.MaxStack);
            Int(body.LocalVariablesInitialized ? 1 : 0);
            if (body.LocalSignature.IsNil)
            {
                Int(-1);
            }
            else
            {
                var signature = reader.GetBlobReader(reader.GetStandaloneSignature(body.LocalSignature).Signature);
                var bytes = signature.ReadBytes(signature.Length);
                Int(bytes.Length);
                hash.Append(bytes);
            }
            Int(body.ExceptionRegions.Length);
            foreach (var region in body.ExceptionRegions)
            {
                Int((int)region.Kind);
                Int(region.TryOffset);
                Int(region.TryLength);
                Int(region.HandlerOffset);
                Int(region.HandlerLength);
                Int(region.CatchType.IsNil ? 0 : MetadataTokens.GetToken(region.CatchType));
                Int(region.FilterOffset);
            }
            var il = body.GetILBytes() ?? [];
            Int(il.Length);
            AppendIl(hash, reader, il);
        }

        // IL as written, except that a string literal's token, an offset into a heap that
        // other bodies' edits may move, is replaced by the literal's text. Opcodes this scan
        // does not know end it; the rest is taken byte for byte (conservative: a moved
        // literal there reads as a change).
        private static void AppendIl(XxHash128 hash, MetadataReader reader, byte[] il)
        {
            var start = 0;
            var position = 0;
            while (position < il.Length)
            {
                var opcode = il[position];
                int size;
                if (opcode == 0xFE)
                {
                    if (position + 1 >= il.Length) break;
                    size = TwoByteOperandSize(il[position + 1]);
                    if (size < 0) break;
                    position += 2 + size;
                    continue;
                }
                if (opcode == 0x72 && position + 5 <= il.Length) // ldstr
                {
                    hash.Append(il.AsSpan(start, position + 1 - start));
                    var token = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(position + 1));
                    var text = reader.GetUserString(MetadataTokens.UserStringHandle(token & 0x00FFFFFF));
                    Integer(hash, text.Length);
                    hash.Append(MemoryMarshal.AsBytes(text.AsSpan()));
                    position += 5;
                    start = position;
                    continue;
                }
                if (opcode == 0x45) // switch
                {
                    if (position + 5 > il.Length) break;
                    var count = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(position + 1));
                    if (count < 0 || count > (il.Length - position) / 4) break;
                    position += 5 + count * 4;
                    continue;
                }
                size = OneByteOperandSize(opcode);
                if (size < 0) break;
                position += 1 + size;
            }
            hash.Append(il.AsSpan(start));
        }

        private static void Integer(XxHash128 hash, int value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            hash.Append(bytes);
        }

        private static int OneByteOperandSize(byte opcode) => opcode switch
        {
            <= 0x0D => 0,
            <= 0x13 => 1,             // ldarg.s .. stloc.s
            <= 0x1E => 0,             // ldnull, ldc.i4.m1 .. ldc.i4.8
            0x1F => 1,                // ldc.i4.s
            0x20 or 0x22 => 4,        // ldc.i4, ldc.r4
            0x21 or 0x23 => 8,        // ldc.i8, ldc.r8
            0x24 => -1,
            0x25 or 0x26 => 0,        // dup, pop
            0x27 or 0x28 or 0x29 => 4, // jmp, call, calli
            0x2A => 0,                // ret
            <= 0x37 => 1,             // short branches
            <= 0x44 => 4,             // long branches
            <= 0x6E => 0,             // indirect loads/stores, arithmetic, conversions
            <= 0x75 => 4,             // callvirt .. isinst
            0x76 => 0,                // conv.r.un
            0x77 or 0x78 => -1,
            0x79 => 4,                // unbox
            0x7A => 0,                // throw
            <= 0x81 => 4,             // field access, stobj
            <= 0x8B => 0,             // conv.ovf.*.un
            0x8C or 0x8D => 4,        // box, newarr
            0x8E => 0,                // ldlen
            0x8F => 4,                // ldelema
            <= 0xA2 => 0,             // typed ldelem/stelem
            <= 0xA5 => 4,             // ldelem, stelem, unbox.any
            <= 0xB2 => -1,
            <= 0xBA => 0,             // conv.ovf.*
            <= 0xC1 => -1,
            0xC2 => 4,                // refanyval
            0xC3 => 0,                // ckfinite
            0xC4 or 0xC5 => -1,
            0xC6 => 4,                // mkrefany
            <= 0xCF => -1,
            0xD0 => 4,                // ldtoken
            <= 0xDC => 0,             // conversions and checked arithmetic
            0xDD => 4,                // leave
            0xDE => 1,                // leave.s
            0xDF or 0xE0 => 0,        // stind.i, conv.u
            _ => -1,
        };

        private static int TwoByteOperandSize(byte opcode) => opcode switch
        {
            <= 0x05 => 0,             // arglist, ceq .. clt.un
            0x06 or 0x07 => 4,        // ldftn, ldvirtftn
            0x08 => -1,
            <= 0x0E => 2,             // ldarg .. stloc
            0x0F => 0,                // localloc
            0x10 => -1,
            0x11 => 0,                // endfilter
            0x12 => 1,                // unaligned.
            0x13 or 0x14 => 0,        // volatile., tail.
            0x15 or 0x16 => 4,        // initobj, constrained.
            0x17 or 0x18 => 0,        // cpblk, initblk
            0x19 => 1,                // no.
            0x1A => 0,                // rethrow
            0x1B => -1,
            0x1C => 4,                // sizeof
            0x1D or 0x1E => 0,        // refanytype, readonly.
            _ => -1,
        };
    }
}
