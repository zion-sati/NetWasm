// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.UnsafeAccessors;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class UnsafeAccessorMemberResolverTests
{
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly CliTypeIdentity Int64 = CliTypeIdentity.FromStackKind(CliValueKind.I8);
    private static readonly CliTypeIdentity Void = CliTypeIdentity.FromStackKind(CliValueKind.Void);
    private static readonly CliTypeIdentity Object = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);

    [Theory]
    [InlineData(UnsafeAccessorMemberKind.Constructor)]
    [InlineData(UnsafeAccessorMemberKind.Method)]
    [InlineData(UnsafeAccessorMemberKind.StaticMethod)]
    public void BindsOnlyTheDeclaredOwnerAndChecksConstraintsAfterMatching(UnsafeAccessorMemberKind kind)
    {
        var fixture = new Fixture();
        var constructor = kind == UnsafeAccessorMemberKind.Constructor;
        var name = constructor ? ".ctor" : "Target";
        var signature = Signature(constructor ? Void : Int32, [Int32]);
        var target = fixture.AddMethod(name, kind == UnsafeAccessorMemberKind.StaticMethod, signature);
        fixture.AddMethod(name, kind != UnsafeAccessorMemberKind.StaticMethod, signature);
        fixture.AddMethod("Other", target.IsStatic, signature);
        var request = fixture.Request(kind, name, Signature(constructor ? Object : Int32, constructor ? [Int32] : [Object, Int32]));
        var result = fixture.Methods().Resolve(request);

        Assert.Same(target, Assert.IsType<UnsafeAccessorMemberMatch.Method>(result).Definition);
        Assert.Equal(new[] { request.Accessor.Key, target.Key }, fixture.ConstraintReads);
        Assert.Equal(1, fixture.ValidationCount);
    }

    [Theory]
    [InlineData("return")]
    [InlineData("parameter")]
    [InlineData("count")]
    [InlineData("arity")]
    [InlineData("convention")]
    [InlineData("constructor-return")]
    public void SignatureMismatchPrecedesConstraintValidation(string mismatch)
    {
        var fixture = new Fixture();
        var kind = mismatch == "constructor-return" ? UnsafeAccessorMemberKind.Constructor : UnsafeAccessorMemberKind.Method;
        var targetSignature = mismatch switch
        {
            "return" => Signature(Int64, [Int32]),
            "parameter" => Signature(Int32, [Int64]),
            "count" => Signature(Int32, []),
            "arity" => Signature(Int32, [Int32], arity: 1),
            "convention" => Signature(Int32, [Int32], convention: SignatureCallingConvention.VarArgs),
            _ => Signature(Int32, [Int32]),
        };
        fixture.AddMethod("Target", false, targetSignature);
        var request = fixture.Request(kind, "Target", Signature(Int32, kind == UnsafeAccessorMemberKind.Constructor ? [Int32] : [Object, Int32]));
        Assert.Equal(new UnsafeAccessorMemberMatch.Failure(UnsafeAccessorFailure.MissingMethod), fixture.Methods().Resolve(request));
        Assert.Empty(fixture.ConstraintReads);
        Assert.Equal(0, fixture.ValidationCount);
    }

    [Fact]
    public void AFoundMethodWithIncompatibleConstraintsThrowsInvalidProgram()
    {
        var fixture = new Fixture { ConstraintsValid = false };
        fixture.AddMethod("Target", false, Signature(Int32, []));
        var result = fixture.Methods().Resolve(fixture.Request(UnsafeAccessorMemberKind.Method, "Target", Signature(Int32, [Object])));
        Assert.Equal(new UnsafeAccessorMemberMatch.Failure(UnsafeAccessorFailure.InvalidProgram), result);
        Assert.Equal(1, fixture.ValidationCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AmbiguousMethodsRetryWithExactModifiersAndNeverPickAnArbitraryMatch(int exactMatches)
    {
        var fixture = new Fixture();
        var modified = CliTypeIdentity.Modified(Int32, Object, true);
        var first = fixture.AddMethod("Target", false, Signature(exactMatches > 0 ? Int32 : modified, []));
        fixture.AddMethod("Target", false, Signature(exactMatches > 1 ? Int32 : modified, []));
        var result = fixture.Methods().Resolve(fixture.Request(UnsafeAccessorMemberKind.Method, "Target", Signature(Int32, [Object])));
        if (exactMatches == 1)
        {
            Assert.Same(first, Assert.IsType<UnsafeAccessorMemberMatch.Method>(result).Definition);
            Assert.Equal(1, fixture.ValidationCount);
        }
        else
        {
            Assert.Equal(new UnsafeAccessorMemberMatch.Failure(UnsafeAccessorFailure.AmbiguousMatch), result);
            Assert.Empty(fixture.ConstraintReads);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleMethodMatchIgnoresModifiersWithoutRetry(bool parameterModifier)
    {
        var fixture = new Fixture();
        var modified = CliTypeIdentity.Modified(Int32, Object, true);
        var target = fixture.AddMethod("Target", false, Signature(parameterModifier ? Int32 : modified, [parameterModifier ? modified : Int32]));
        var result = fixture.Methods().Resolve(fixture.Request(UnsafeAccessorMemberKind.Method, "Target", Signature(Int32, [Object, Int32])));
        Assert.Same(target, Assert.IsType<UnsafeAccessorMemberMatch.Method>(result).Definition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldLookupIgnoresModifiersAndDoesNotCheckMethodConstraints(bool isStatic)
    {
        var fixture = new Fixture { ConstraintsValid = false };
        fixture.AddField("Other", isStatic, Int32);
        fixture.AddField("Target", !isStatic, Int32);
        fixture.AddField("Target", isStatic, Int64);
        fixture.AddField("Target", isStatic, Int32, literal: true);
        var target = fixture.AddField("Target", isStatic, CliTypeIdentity.Modified(Int32, Object, true));
        var request = fixture.Request(isStatic ? UnsafeAccessorMemberKind.StaticField : UnsafeAccessorMemberKind.Field,
            "Target", Signature(CliTypeIdentity.Modified(CliTypeIdentity.ManagedByReference(Int32), Object, false), [Object], arity: 1));
        var result = fixture.Fields().Resolve(request);
        Assert.Same(target, Assert.IsType<UnsafeAccessorMemberMatch.Field>(result).Definition);
        Assert.Empty(fixture.ConstraintReads);
        Assert.Equal(0, fixture.ValidationCount);
    }

    [Fact]
    public void AbsentMembersReturnTheKindSpecificFailure()
    {
        var fixture = new Fixture();
        Assert.Equal(new UnsafeAccessorMemberMatch.Failure(UnsafeAccessorFailure.MissingMethod), fixture.Methods().Resolve(
            fixture.Request(UnsafeAccessorMemberKind.Method, "Absent", Signature(Int32, [Object]))));
        Assert.Equal(new UnsafeAccessorMemberMatch.Failure(UnsafeAccessorFailure.MissingField), fixture.Fields().Resolve(
            fixture.Request(UnsafeAccessorMemberKind.Field, "Absent", Signature(CliTypeIdentity.ManagedByReference(Int32), [Object]))));
        Assert.Throws<ArgumentNullException>(() => fixture.Methods().Resolve(null!));
        Assert.Throws<ArgumentNullException>(() => fixture.Fields().Resolve(null!));
    }

    private static MethodSignature<CliTypeIdentity> Signature(CliTypeIdentity result,
        ImmutableArray<CliTypeIdentity> parameters, int arity = 0,
        SignatureCallingConvention convention = SignatureCallingConvention.Default) =>
        new(new SignatureHeader(SignatureKind.Method, convention, arity == 0 ? SignatureAttributes.None : SignatureAttributes.Generic),
            result, parameters.Length, arity, parameters);

    private sealed class Fixture : IMethodRepository, IFieldRepository, IUnsafeAccessorSignatureReader,
        IUnsafeAccessorSignatureComparer, IUnsafeAccessorConstraintReader, IUnsafeAccessorConstraintValidator
    {
        private readonly Dictionary<EntityKey, MethodDefinitionModel> _methods = [];
        private readonly Dictionary<EntityKey, FieldDefinitionModel> _fields = [];
        private readonly Dictionary<EntityKey, MethodSignature<CliTypeIdentity>> _signatures = [];
        public List<EntityKey> ConstraintReads { get; } = [];
        public int ValidationCount { get; private set; }
        public bool ConstraintsValid { get; init; } = true;

        public MethodDefinitionModel AddMethod(string name, bool isStatic, MethodSignature<CliTypeIdentity> signature)
        {
            var key = new EntityKey(new("Test"), 0x06000001 + _methods.Count);
            var method = new MethodDefinitionModel(key, new(new("Test"), 0x02000001), name, isStatic,
                new(signature.ReturnType, signature.ParameterTypes), 1);
            _methods.Add(key, method);
            _signatures.Add(key, signature);
            return method;
        }

        public FieldDefinitionModel AddField(string name, bool isStatic, CliTypeIdentity type, bool literal = false)
        {
            var key = new EntityKey(new("Test"), 0x04000001 + _fields.Count);
            var field = new FieldDefinitionModel(key, new(new("Test"), 0x02000001), name, type, isStatic) { IsLiteral = literal };
            _fields.Add(key, field);
            return field;
        }

        public UnsafeAccessorMemberRequest Request(UnsafeAccessorMemberKind kind, string name, MethodSignature<CliTypeIdentity> signature)
        {
            var accessor = new MethodDefinitionModel(new(new("Test"), 0x06000100), new(new("Test"), 0x02000002), "Accessor", true,
                new(signature.ReturnType, signature.ParameterTypes), 0);
            return new(kind, name, accessor, signature,
                new(new(new("Test"), 0x02000001), "Test", "Owner", false, [.. _fields.Keys], [.. _methods.Keys]));
        }

        public IUnsafeAccessorMemberResolver Methods() => Assert.IsAssignableFrom<IUnsafeAccessorMemberResolver>(new UnsafeAccessorMethodResolver(this, this, this, this, this));
        public IUnsafeAccessorMemberResolver Fields() => Assert.IsAssignableFrom<IUnsafeAccessorMemberResolver>(new UnsafeAccessorFieldResolver(this, this, this));
        MethodDefinitionModel IMethodRepository.GetMethod(EntityKey key) => _methods[key];
        FieldDefinitionModel IFieldRepository.GetField(EntityKey key) => _fields[key];
        public MethodSignature<CliTypeIdentity> Read(MethodDefinitionModel method) => _signatures[method.Key];
        public CliTypeIdentity Read(FieldDefinitionModel field) => field.SignatureType;
        UnsafeAccessorGenericConstraints IUnsafeAccessorConstraintReader.Read(MethodDefinitionModel method)
        {
            ConstraintReads.Add(method.Key);
            return new([], []);
        }
        public bool Validate(UnsafeAccessorGenericConstraints accessor, UnsafeAccessorGenericConstraints target)
        {
            ValidationCount++;
            return ConstraintsValid;
        }
        public bool Compare(CliTypeIdentity left, CliTypeIdentity right, bool includeModifiers)
        {
            if (!includeModifiers)
            {
                while (left.Shape == CliTypeShape.Modified) left = left.ElementType!;
                while (right.Shape == CliTypeShape.Modified) right = right.ElementType!;
            }
            return left.Equals(right);
        }
    }
}
