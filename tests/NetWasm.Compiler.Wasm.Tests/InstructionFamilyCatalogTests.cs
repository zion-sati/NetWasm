using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class InstructionFamilyCatalogTests
{
    [Theory]
    [InlineData(CilOperation.LoadArgument, "ConstantsStackLocalsArguments")]
    [InlineData(CilOperation.AddChecked, "Numeric")]
    [InlineData(CilOperation.CopyBlock, "ValueObjectBlockMemory")]
    [InlineData(CilOperation.StoreField, "ArraysFieldsStatics")]
    [InlineData(CilOperation.Box, "AllocationBoxingTypes")]
    [InlineData(CilOperation.CallVirtual, "CallsAndCallableLoading")]
    [InlineData(CilOperation.DelegateCombine, "Delegates")]
    [InlineData(CilOperation.BranchIfTrue, "StructuredControlFlow")]
    [InlineData(CilOperation.Throw, "ExceptionsAndRoots")]
    public void OperationHasMeaningfulFamily(
        CilOperation operation,
        string expected)
    {
        Assert.Equal(
            expected,
            Resolve(operation).ToString());
    }

    [Fact]
    public void DefaultCatalogResolvesEverySupportedOperation()
    {
        foreach (var operation in SupportedCil.Operations)
        {
            Assert.IsType<InstructionFamily>(Resolve(operation));
        }
    }

    [Fact]
    public void UnknownOperationIsRejected()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Resolve((CilOperation)(-1)));

        Assert.Equal("operation", exception.ParamName);
    }

    [Fact]
    public void DuplicateOperationRegistrationIsRejected()
    {
        var registrations = new[]
        {
            Registration(InstructionFamily.Numeric, CilOperation.Nop),
            Registration(InstructionFamily.Delegates, CilOperation.Nop),
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new InstructionFamilyCatalog(registrations));

        Assert.Equal(
            "CIL operation 'Nop' belongs to more than one family.",
            exception.Message);
    }

    [Fact]
    public void MissingOperationRegistrationIsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new InstructionFamilyCatalog(
                [Registration(InstructionFamily.Numeric, CilOperation.LoadInt32)]));

        Assert.Equal(
            "CIL operation 'Nop' has no instruction family.",
            exception.Message);
    }

    [Fact]
    public void NullRegistrationsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new InstructionFamilyCatalog(null!));
    }

    [Fact]
    public void NullRegistrationIsRejected()
    {
        var registrations = new InstructionFamilyRegistration[] { null! };

        Assert.Throws<ArgumentNullException>(() =>
            new InstructionFamilyCatalog(registrations));
    }

    private static InstructionFamilyRegistration Registration(
        InstructionFamily family,
        params CilOperation[] operations) =>
        new(family, [.. operations]);

    private static InstructionFamily Resolve(CilOperation operation) =>
        InstructionFamilyCatalogFactory.CreateDefault().Resolve(operation);
}
