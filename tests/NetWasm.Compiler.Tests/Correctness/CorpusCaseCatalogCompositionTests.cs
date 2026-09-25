using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusCaseCatalogCompositionTests
{
    [Theory]
    [InlineData(CorpusCaseTestData.CollectorRetention, 12, 7)]
    [InlineData(CorpusCaseTestData.CollectorLifecycle, 7, 7)]
    [InlineData(CorpusCaseTestData.NativeOwnership, 12, 0)]
    [InlineData(CorpusCaseTestData.ExactRootSlots, 4, 5)]
    [InlineData(CorpusCaseTestData.AsyncLifetime, 8, 1)]
    [InlineData(CorpusCaseTestData.ImplicitExceptionLifetime, 30, 1)]
    [InlineData(CorpusCaseTestData.AsyncEnumeration, 12, 1)]
    [InlineData(CorpusCaseTestData.UnwindRoots, 6, 1)]
    public void RealRuntimeCasesDeclareOnlyLinkedCellsAndIndependentContracts(string caseId, int inputs, int capabilities)
    {
        var declaration = CorpusCaseTestData.Get(caseId);
        var rows = CorpusCaseTestData.Cells(caseId).ToArray();
        var expected = declaration.MatrixProfile switch
        {
            CorpusMatrixProfile.Fast => 1,
            CorpusMatrixProfile.Family => 4,
            _ => 8,
        };

        Assert.Equal(expected, rows.Length);
        Assert.Equal(expected * inputs, CorpusCaseTestData.InputCells(caseId).Count);
        Assert.Equal(CorpusExecutionBackend.Linked, declaration.ExecutionBackend);
        Assert.All(rows, row =>
        {
            Assert.Equal(caseId, row[0]);
            var cell = Assert.IsType<string>(row[1]);
            Assert.EndsWith("-Linked", cell, StringComparison.Ordinal);
            var fixture = Factory().Create(declaration, cell);
            Assert.Equal(CorpusExecutionBackend.Linked, fixture.Matrix!.Backend);
            Assert.Equal(inputs, fixture.Inputs.Length);
            Assert.All(fixture.Inputs, input =>
            {
                var hasReturn = fixture.ExpectedReturnValues.TryGetValue(input, out var returnValue);
                var hasException = fixture.ExpectedExceptionTypes.TryGetValue(input, out var exceptionType);
                Assert.NotEqual(hasReturn, hasException);
                if (hasReturn)
                    Assert.Equal(42, returnValue);
                else
                    Assert.Equal("System.OverflowException", exceptionType);
            });
            Assert.Equal((OracleRuntimeCapabilities)capabilities, fixture.RequiredRuntimeCapabilities);
            Assert.Equal(OracleMode.SameIl, fixture.OracleMode);
            Assert.False(fixture.SupportsBatchedOracle);
            Assert.True(fixture.ReportAllMismatches);
            Assert.Throws<ArgumentException>(() => Factory().Create(declaration, cell[..^7]));
        });
    }

    [Fact]
    public void EmbeddedCatalogBindsEveryDeclaredCaseToAnExistingTheoryWithOnlyExplicitFixtureSkips()
    {
        var catalog = CorpusCaseTestData.Catalog;

        Assert.Equal(75, catalog.Cases.Count);
        Assert.Equal(75, CorpusCaseTestData.Bindings.Length);
        Assert.Equal(75, CorpusCaseTestData.Assets.Manifests.Length);
        Assert.Equal(72, CorpusCaseTestData.Assets.SourceFiles.Length);
        Assert.Equal(32, CorpusCaseTestData.Assets.Features.Ids.Count);
        foreach (var binding in CorpusCaseTestData.Bindings)
        {
            var declaration = catalog.Cases[binding.CaseId];
            Assert.Equal(binding.TestMethod, declaration.TestMethod);
            Assert.Equal(binding.InputKind, declaration.InputKind);
            var separator = binding.TestMethod.LastIndexOf('.');
            var owner = typeof(CorpusCaseCatalogCompositionTests).Assembly.GetType(binding.TestMethod[..separator], throwOnError: true)!;
            var method = owner.GetMethod(binding.TestMethod[(separator + 1)..], BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(method);
            var expectedSkip = binding.CaseId switch
            {
                CorpusCaseTestData.OpenDelegateReceiver or CorpusCaseTestData.NullDelegateReceiver =>
                    KnownNonBugSkipReasons.EmittedOpenDelegate,
                CorpusCaseTestData.ZeroBound => KnownNonBugSkipReasons.ZeroBoundRankOneArrayIdentity,
                _ => null,
            };
            Assert.Equal(expectedSkip, Assert.IsType<TheoryAttribute>(method.GetCustomAttribute<TheoryAttribute>()).Skip);
            Assert.NotNull(method.GetCustomAttribute<MemberDataAttribute>());
            Assert.Contains(method.GetParameters(), parameter => parameter.Name == "caseId");
            Assert.Contains(method.GetParameters(), parameter => parameter.Name == "cell");
        }
        Assert.Throws<KeyNotFoundException>(() => CorpusCaseTestData.Get("unregistered.case"));
    }

    [Theory]
    [InlineData(CorpusCaseTestData.RttiCasts, 16)]
    [InlineData(CorpusCaseTestData.RttiArrays, 16)]
    [InlineData(CorpusCaseTestData.BoxedRepresentation, 16)]
    [InlineData(CorpusCaseTestData.DispatchRelationships, 16)]
    [InlineData(CorpusCaseTestData.ArrayShapes, 16)]
    [InlineData(CorpusCaseTestData.GenericRecursion, 16)]
    [InlineData(CorpusCaseTestData.UnsafeBoundaries, 16)]
    [InlineData(CorpusCaseTestData.LayoutBoundaries, 16)]
    [InlineData(CorpusCaseTestData.ReadonlyCopies, 16)]
    [InlineData(CorpusCaseTestData.ExceptionOrder, 16)]
    [InlineData(CorpusCaseTestData.StaticInitialization, 16)]
    [InlineData(CorpusCaseTestData.IteratorOrder, 16)]
    [InlineData(CorpusCaseTestData.EncodingStreams, 16)]
    [InlineData(CorpusCaseTestData.CollectionState, 16)]
    [InlineData(CorpusCaseTestData.ClosedDelegateReceiver, 8)]
    [InlineData(CorpusCaseTestData.OpenDelegateReceiver, 8)]
    [InlineData(CorpusCaseTestData.NullDelegateReceiver, 8)]
    [InlineData(CorpusCaseTestData.Math, 16)]
    [InlineData(CorpusCaseTestData.ManagedMathFullRange, 16)]
    [InlineData(CorpusCaseTestData.NumericPrecision, 16)]
    [InlineData(CorpusCaseTestData.CheckedNonFinite, 16)]
    [InlineData(CorpusCaseTestData.FiniteFloating, 16)]
    [InlineData(CorpusCaseTestData.UnsignedPrecision, 16)]
    [InlineData(CorpusCaseTestData.FloatingNarrowing, 16)]
    [InlineData(CorpusCaseTestData.FloatingComparisons, 16)]
    [InlineData(CorpusCaseTestData.FloatingArithmetic, 16)]
    [InlineData(CorpusCaseTestData.FloatingKeys, 16)]
    [InlineData(CorpusCaseTestData.IntegerCasts, 16)]
    [InlineData(CorpusCaseTestData.NativeCasts, 16)]
    [InlineData(CorpusCaseTestData.FloatingNative, 16)]
    [InlineData(CorpusCaseTestData.NativeFloating, 16)]
    [InlineData(CorpusCaseTestData.FloatingConvertInteger, 16)]
    [InlineData(CorpusCaseTestData.FloatingSmallInteger, 16)]
    [InlineData(CorpusCaseTestData.DecimalEdges, 16)]
    [InlineData(CorpusCaseTestData.DecimalConversions, 16)]
    [InlineData(CorpusCaseTestData.DecimalArithmetic, 16)]
    [InlineData(CorpusCaseTestData.DecimalBinary, 16)]
    [InlineData(CorpusCaseTestData.BinaryDecimal, 16)]
    [InlineData(CorpusCaseTestData.FixedIntegerArithmetic, 16)]
    [InlineData(CorpusCaseTestData.FixedIntegerShiftsUnary, 16)]
    [InlineData(CorpusCaseTestData.NativeArithmetic, 16)]
    [InlineData(CorpusCaseTestData.NativeShiftsUnary, 16)]
    [InlineData(CorpusCaseTestData.HalfWidening, 16)]
    [InlineData(CorpusCaseTestData.HalfNarrowing, 16)]
    [InlineData(CorpusCaseTestData.HalfClassification, 16)]
    [InlineData(CorpusCaseTestData.WideArithmetic, 16)]
    [InlineData(CorpusCaseTestData.WideConversions, 16)]
    [InlineData(CorpusCaseTestData.WideFloating, 16)]
    [InlineData(CorpusCaseTestData.FloatingWide, 16)]
    [InlineData(CorpusCaseTestData.GenericFloatingWide, 16)]
    [InlineData(CorpusCaseTestData.GenericWideFloating, 16)]
    [InlineData(CorpusCaseTestData.NumericTransport, 16)]
    [InlineData(CorpusCaseTestData.ConversionTransport, 16)]
    [InlineData(CorpusCaseTestData.AsyncNumericTransport, 16)]
    [InlineData(CorpusCaseTestData.AsyncConversionTransport, 16)]
    [InlineData(CorpusCaseTestData.FloatingMathExact, 16)]
    [InlineData(CorpusCaseTestData.FloatingMathDomains, 16)]
    [InlineData(CorpusCaseTestData.FloatingMathAccuracy, 16)]
    [InlineData(CorpusCaseTestData.BinaryMathEdges, 16)]
    [InlineData(CorpusCaseTestData.FloatingRounding, 16)]
    [InlineData(CorpusCaseTestData.FloatingRoundingDigits, 16)]
    [InlineData(CorpusCaseTestData.RoundingArguments, 16)]
    [InlineData(CorpusCaseTestData.EmittedFloatingComparisons, 8)]
    [InlineData(CorpusCaseTestData.MultiFile, 16)]
    [InlineData(CorpusCaseTestData.ZeroBound, 8)]
    [InlineData(CorpusCaseTestData.NonZeroBound, 8)]
    public void DiscoveryExposesEveryCaseAndCellAsAnOrdinaryTheoryRow(string caseId, int count)
    {
        var rows = CorpusCaseTestData.Cells(caseId).ToArray();

        var selectedCount = CorpusCaseTestData.ProfileOverride switch
        {
            CorpusMatrixProfile.Fast => 1,
            CorpusMatrixProfile.Family => count / 4,
            _ => count,
        };
        Assert.Equal(selectedCount, rows.Length);
        Assert.Equal(selectedCount, rows.Select(row => row[1]).Distinct().Count());
        Assert.All(rows, row =>
        {
            Assert.Equal(caseId, row[0]);
            Assert.IsType<string>(row[1]);
        });
    }

    [Fact]
    public void FloatingArithmeticRetainsStrictRemainderBitsAndTheOriginalInputs()
    {
        var declaration = CorpusCaseTestData.Catalog.Cases[CorpusCaseTestData.FloatingArithmetic];
        var fixture = Factory().Create(declaration, "Release-Wasm64-Optimized-Linked");

        Assert.Equal(2, declaration.SourceFiles.Length);
        Assert.Equal(1216, fixture.Inputs.Length);
        Assert.Equal(Enumerable.Range(0, 160), fixture.Inputs.Take(160));
        Assert.All(fixture.Inputs, input => Assert.Equal(42, fixture.ExpectedReturnValues[input]));
        Assert.Equal(OracleMode.SameIl, fixture.OracleMode);
        Assert.True(fixture.UsesTypedTrace);
        Assert.True(fixture.ReportAllMismatches);
    }

    [Fact]
    public void DecimalBinaryUsesExactProfileOracleForBothPrecisionsAndPublicApiPaths()
    {
        var declaration = CorpusCaseTestData.Get(CorpusCaseTestData.DecimalBinary);
        var fixture = Factory().Create(declaration, "Release-Wasm32-Direct");

        Assert.Equal("Run", fixture.WasmEntryMethod);
        Assert.Equal("RunExactOracle", fixture.DesktopEntryMethod);
        Assert.Equal(2, declaration.SourceFiles.Length);
        Assert.Equal(5712, fixture.Inputs.Length);
        Assert.Empty(fixture.ExpectedExceptionTypes);
        Assert.Equal(5712, fixture.ExpectedReturnValues.Count);
        Assert.All(fixture.ExpectedReturnValues, pair => Assert.Equal(42, pair.Value));
        Assert.Equal(OracleMode.SameIl, fixture.OracleMode);
        Assert.Equal("NetWasm.CoreLib", fixture.ReferenceAssemblyAliases["System.Runtime.Numerics"]);
    }

    [Fact]
    public void BinaryDecimalUsesExactProfileOracleWithoutChangingTheTargetEntry()
    {
        var declaration = CorpusCaseTestData.Get(CorpusCaseTestData.BinaryDecimal);
        var fixture = Factory().Create(declaration, "Release-Wasm32-Direct");

        Assert.Equal("Run", fixture.WasmEntryMethod);
        Assert.Equal("RunExactOracle", fixture.DesktopEntryMethod);
        Assert.Equal(2, declaration.SourceFiles.Length);
        Assert.Equal(776, fixture.Inputs.Length);
        Assert.Equal(96, fixture.ExpectedExceptionTypes.Count);
        Assert.Equal(680, fixture.ExpectedReturnValues.Count);
        Assert.All(fixture.ExpectedReturnValues, pair => Assert.Equal(42, pair.Value));
        Assert.All(fixture.ExpectedExceptionTypes, pair => Assert.Equal("System.OverflowException", pair.Value));
        Assert.Equal(OracleMode.SameIl, fixture.OracleMode);
        Assert.Equal("NetWasm.CoreLib", fixture.ReferenceAssemblyAliases["System.Runtime.Numerics"]);
    }

    [Fact]
    public void AsyncNumericCaseUsesTheExistingReactorWithoutClaimingRealCollection()
    {
        var declaration = CorpusCaseTestData.Get(CorpusCaseTestData.AsyncNumericTransport);
        var fixture = Factory().Create(declaration, "Release-Wasm32-Direct");

        Assert.True(fixture.RequiresReactor);
        Assert.Equal(OracleRuntimeCapabilities.None, fixture.RequiredRuntimeCapabilities);
        Assert.Equal(OracleMode.SameIl, fixture.OracleMode);
        Assert.Equal(416, fixture.Inputs.Length);
        Assert.All(fixture.Inputs, input => Assert.Equal(42, fixture.ExpectedReturnValues[input]));
    }

    [Fact]
    public void FloatingKeysMapsDesktopCollectionsToTheProfileWithoutChangingTheOracle()
    {
        var declaration = CorpusCaseTestData.Get(CorpusCaseTestData.FloatingKeys);
        var fixture = Factory().Create(declaration, "Release-Wasm32-Direct");
        Assert.Equal("NetWasm.CoreLib", fixture.ReferenceAssemblyAliases["System.Collections"]);
        Assert.Equal(OracleMode.SameIl, fixture.OracleMode);
        Assert.Equal(128, fixture.Inputs.Length);
        Assert.All(fixture.Inputs, input => Assert.Equal(42, fixture.ExpectedReturnValues[input]));
    }

    [Fact]
    public void InputFirstRttiRowsRetainExactInputCellReplayAndIndependentExpectations()
    {
        var rows = CorpusCaseTestData.InputCells(CorpusCaseTestData.RankOne).ToArray();
        var count = CorpusCaseTestData.ProfileOverride switch
        {
            CorpusMatrixProfile.Fast => 5,
            CorpusMatrixProfile.Family => 10,
            _ => 40,
        };
        Assert.Equal(count, rows.Length);
        Assert.Equal(count, rows.Select(row => (row[0], row[2])).Distinct().Count());
        Assert.All(rows, row => Assert.Equal(CorpusCaseTestData.RankOne, row[1]));
        var declaration = CorpusCaseTestData.Get(CorpusCaseTestData.RankOne);
        var factory = Factory();
        foreach (var row in rows)
        {
            var input = Assert.IsType<int>(row[0]);
            var cell = Assert.IsType<string>(row[2]);
            var fixture = factory.Create(declaration, cell, input);
            Assert.Equal(input, Assert.Single(fixture.Inputs));
            Assert.Equal(input, fixture.ReplayInput);
            Assert.Equal("NetWasm.CoreLib", fixture.ReferenceAssemblyAliases["System.Private.CoreLib"]);
            if (input == 2)
            {
                Assert.Empty(fixture.ExpectedReturnValues);
                Assert.Equal("System.ArrayTypeMismatchException", fixture.ExpectedExceptionTypes[input]);
            }
            else
            {
                Assert.Empty(fixture.ExpectedExceptionTypes);
                Assert.Equal(42, fixture.ExpectedReturnValues[input]);
            }
        }
    }

    [Fact]
    public void MathMigrationPreservesOriginalSourceAndAllEightIndependentSuccessContracts()
    {
        var declaration = CorpusCaseTestData.Get(CorpusCaseTestData.Math);
        var fixture = Factory().Create(declaration, "Release-Wasm32-Direct");
        var source = new EmbeddedCorpusSourceReader().Read(Assert.Single(declaration.SourceFiles));

        Assert.Equal("41863545be951f4a244105f9440f3f7e21d6a8851c38587e7d80c2b3a0fefa74",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source))));
        Assert.Equal(Enumerable.Range(1, 8), fixture.Inputs);
        Assert.Equal(8, fixture.ExpectedReturnValues.Count);
        Assert.All(fixture.ExpectedReturnValues, pair => Assert.Equal(100, pair.Value));
        Assert.True(fixture.SupportsBatchedOracle);
        Assert.Equal(OracleMode.SameSource, fixture.OracleMode);
        Assert.Equal("The fixture compares portable managed Math and MathF behavior across targets.", fixture.SameSourceReason);
    }

    [Fact]
    public void RttiCastMigrationPreservesAllOriginalSourceCasesAndIndependentSuccessContracts()
    {
        var declaration = CorpusCaseTestData.Get(CorpusCaseTestData.RttiCasts);
        var fixture = Factory().Create(declaration, "Release-Wasm32-Direct");
        var source = new EmbeddedCorpusSourceReader().Read(Assert.Single(declaration.SourceFiles));

        Assert.Equal("6f4ca5a85ecc1fded2ae91bf982443166ccc4af7271a899eeb08341551f37b69",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source))));
        Assert.Equal(Enumerable.Range(0, 27), fixture.Inputs);
        Assert.Equal(27, fixture.ExpectedReturnValues.Count);
        Assert.All(fixture.ExpectedReturnValues, pair => Assert.Equal(42, pair.Value));
        Assert.True(fixture.SupportsBatchedOracle);
        Assert.True(fixture.ReportAllMismatches);
        Assert.False(fixture.ExposesLegacyTrace);
        Assert.Equal(OracleMode.SameIl, fixture.OracleMode);
    }

    [Fact]
    public void ArrayMigrationPreservesOriginalCopyStoreAddressAndExceptionContracts()
    {
        var declaration = CorpusCaseTestData.Get(CorpusCaseTestData.RttiArrays);
        var fixture = Factory().Create(declaration, "Release-Wasm32-Direct");
        var source = new EmbeddedCorpusSourceReader().Read(Assert.Single(declaration.SourceFiles));

        Assert.Equal("86f25e3567142988e7f6cda06a031dabbe7e3513617fb1171b252a10dfe4f3dd",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source))));
        Assert.Equal(Enumerable.Range(0, 15), fixture.Inputs);
        Assert.Equal(15, fixture.ExpectedReturnValues.Count);
        Assert.All(fixture.ExpectedReturnValues, pair => Assert.Equal(42, pair.Value));
        Assert.True(fixture.SupportsBatchedOracle);
        Assert.True(fixture.ReportAllMismatches);
        Assert.False(fixture.ExposesLegacyTrace);
        Assert.Equal(OracleMode.SameIl, fixture.OracleMode);
    }

    [Fact]
    public void BoxedRepresentationDeclaresEachTypeAndOperationAsAnIndependentSuccess()
    {
        var declaration = CorpusCaseTestData.Get(CorpusCaseTestData.BoxedRepresentation);
        var fixture = Factory().Create(declaration, "Release-Wasm32-Direct");

        Assert.Equal(Enumerable.Range(0, 45), fixture.Inputs);
        Assert.Equal(45, fixture.ExpectedReturnValues.Count);
        Assert.All(fixture.ExpectedReturnValues, pair => Assert.Equal(42, pair.Value));
        Assert.Empty(fixture.ExpectedExceptionTypes);
        Assert.True(fixture.SupportsBatchedOracle);
        Assert.True(fixture.ReportAllMismatches);
        Assert.Equal(OracleMode.SameIl, fixture.OracleMode);
        Assert.Equal("NetWasm.CoreLib", fixture.ReferenceAssemblyAliases["System.Collections"]);
    }

    [Fact]
    public void DispatchRelationshipsKeepEveryOperationIndependentlyAsserted()
    {
        var declaration = CorpusCaseTestData.Get(CorpusCaseTestData.DispatchRelationships);
        var fixture = Factory().Create(declaration, "Release-Wasm32-Direct");

        Assert.Equal(Enumerable.Range(0, 21), fixture.Inputs);
        Assert.Equal(21, fixture.ExpectedReturnValues.Count);
        Assert.All(fixture.ExpectedReturnValues, pair => Assert.Equal(42, pair.Value));
        Assert.True(fixture.AllowUnsafe);
        Assert.True(fixture.SupportsBatchedOracle);
        Assert.True(fixture.ReportAllMismatches);
        Assert.Equal(OracleMode.SameIl, fixture.OracleMode);
        Assert.Equal("NetWasm.CoreLib", fixture.ReferenceAssemblyAliases["System.Threading"]);
    }

    [Fact]
    public void ArrayShapesDeclareIndependentShapeStoreAndPostFailureStateContracts()
    {
        var declaration = CorpusCaseTestData.Get(CorpusCaseTestData.ArrayShapes);
        var fixture = Factory().Create(declaration, "Release-Wasm32-Direct");

        Assert.Equal(Enumerable.Range(0, 18), fixture.Inputs);
        Assert.Equal(18, fixture.ExpectedReturnValues.Count);
        Assert.All(fixture.ExpectedReturnValues, pair => Assert.Equal(42, pair.Value));
        Assert.True(fixture.SupportsBatchedOracle);
        Assert.True(fixture.ReportAllMismatches);
        Assert.Equal(OracleMode.SameIl, fixture.OracleMode);
    }

    [Fact]
    public void UnsafeBoundaryCasesHaveIndependentExpectationsWithoutCollectorClaims()
    {
        var declaration = CorpusCaseTestData.Get(CorpusCaseTestData.UnsafeBoundaries);
        var fixture = Factory().Create(declaration, "Release-Wasm32-Direct");

        Assert.Equal(Enumerable.Range(0, 47), fixture.Inputs);
        Assert.Equal(47, fixture.ExpectedReturnValues.Count);
        Assert.All(fixture.ExpectedReturnValues, pair => Assert.Equal(42, pair.Value));
        Assert.True(fixture.AllowUnsafe);
        Assert.True(fixture.ReportAllMismatches);
        Assert.Equal(OracleRuntimeCapabilities.None, fixture.RequiredRuntimeCapabilities);
        Assert.Equal(OracleMode.SameIl, fixture.OracleMode);
    }

    private static ICorpusCaseFixtureFactory Factory() => Assert.IsAssignableFrom<ICorpusCaseFixtureFactory>(new CorpusCaseFixtureFactory(
        new CorpusCaseManifestVerifier(CorpusCaseTestData.Assets.Features, new CorpusSourceNamesVerifier(),
            new CorpusMatrixExpander(), new CorpusReplayCommandFormatter()),
        new CorpusMatrixExpander(), CorrectnessTestAssets.CreateOracleModes()));
}
