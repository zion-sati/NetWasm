using System;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;
using Xunit;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class StructuredMethodEmissionPlannerTests
{
    [Fact]
    public void PlanPreservesDefinitionAndConstructedMethodIdentities()
    {
        var baseline = EmitterTestSupport.CreateEmissionRequest();
        var definition = baseline.EntryPoint;
        var secondDefinition = definition with
        {
            Key = new EntityKey(definition.Key.Assembly, definition.Key.MetadataToken + 1),
            Name = "Secondary",
        };
        var openDefinition = definition with
        {
            Key = new EntityKey(definition.Key.Assembly, definition.Key.MetadataToken + 2),
            Name = "OpenTemplate",
            GenericArity = 1,
        };
        var structured = baseline.Methods[definition.Key];
        var secondStructured = structured with { Header = structured.Header with { Method = secondDefinition } };
        var openStructured = structured with { Header = structured.Header with { Method = openDefinition } };
        var typeIdentities = new TestCilTypeIdentityResolver();
        var identities = new ManagedMethodIdentityFactory();
        var instance = new MethodInstanceModel(
            definition,
            typeIdentities.Resolve(definition.DeclaringType),
            ImmutableArray.Create(typeIdentities.Resolve(definition.DeclaringType)),
            definition.Signature);
        var definitionIdentity = identities.Create(definition, instance.DeclaringType);
        var secondDefinitionIdentity = identities.Create(secondDefinition, instance.DeclaringType);
        var definitionInstance = new MethodInstanceModel(
            definition,
            typeIdentities.Resolve(definition.DeclaringType),
            [],
            definition.Signature);
        var secondDefinitionInstance = new MethodInstanceModel(
            secondDefinition,
            typeIdentities.Resolve(secondDefinition.DeclaringType),
            [],
            secondDefinition.Signature);
        var instanceIdentity = identities.Create(instance);
        var request = baseline with
        {
            Methods = ImmutableDictionary<EntityKey, StructuredMethod>.Empty
                .Add(secondDefinition.Key, secondStructured).Add(definition.Key, structured)
                .Add(openDefinition.Key, openStructured).Add(definition.Key, structured),
            ConstructedMethods = ImmutableDictionary<string, StructuredMethod>.Empty
                .Add(instanceIdentity.CanonicalName, structured),
            MethodInstances = ImmutableDictionary<string, MethodInstanceModel>.Empty
                .Add(instanceIdentity.CanonicalName, instance),
            CallableMethods = ImmutableDictionary<string, MethodInstanceModel>.Empty
                .Add(definitionIdentity.CanonicalName, definitionInstance)
                .Add(secondDefinitionIdentity.CanonicalName, secondDefinitionInstance)
                .Add(instanceIdentity.CanonicalName, instance)
        };
        var planner = new StructuredMethodEmissionPlanner(identities,
            new TestCilTypeIdentityResolver(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StructuredMethodEmissionPlanner>.Instance);

        var result = ((IStructuredMethodEmissionPlanner)planner).Plan(request);

        Assert.Equal(3, result.Length);
        var expectedIdentities = new[]
        {
            definitionIdentity.CanonicalName,
            secondDefinitionIdentity.CanonicalName,
        }
        .OrderBy(identity => identity, StringComparer.Ordinal)
        .Append(instanceIdentity.CanonicalName);
        Assert.Equal(expectedIdentities.ElementAt(0), result.ElementAt(0).Identity.CanonicalName);
        Assert.Equal(expectedIdentities.ElementAt(1), result.ElementAt(1).Identity.CanonicalName);
        Assert.Equal(expectedIdentities.ElementAt(2), result.ElementAt(2).Identity.CanonicalName);
        Assert.Equal(definitionIdentity, result[0].Identity);
        Assert.Same(structured, result[0].Method);
        Assert.Equal(secondDefinitionIdentity, result[1].Identity);
        Assert.Same(secondStructured, result[1].Method);
        Assert.Same(structured, result[2].Method);
    }

    [Fact]
    public void PlanRejectsMissingAndMismatchedConstructedMethodIdentities()
    {
        var baseline = EmitterTestSupport.CreateEmissionRequest();
        var definition = baseline.EntryPoint;
        var structured = baseline.Methods[definition.Key];
        var typeIdentities = new TestCilTypeIdentityResolver();
        var identities = new ManagedMethodIdentityFactory();
        var instance = new MethodInstanceModel(
            definition,
            typeIdentities.Resolve(definition.DeclaringType),
            ImmutableArray.Create(typeIdentities.Resolve(definition.DeclaringType)),
            definition.Signature);
        var identity = identities.Create(instance);
        var request = baseline with
        {
            Methods = ImmutableDictionary<EntityKey, StructuredMethod>.Empty,
            ConstructedMethods = ImmutableDictionary<string, StructuredMethod>.Empty
                .Add(identity.CanonicalName, structured)
        };
        var planner = new StructuredMethodEmissionPlanner(identities,
            new TestCilTypeIdentityResolver(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StructuredMethodEmissionPlanner>.Instance);

        var missing = Assert.Throws<CompilerException>(
            () => ((IStructuredMethodEmissionPlanner)planner).Plan(request));

        Assert.Equal(DiagnosticCode.CompilerInvariant, missing.Diagnostic.Code);
        Assert.Contains("MISSING_CONSTRUCTED_METHOD_INSTANCE", missing.Diagnostic.Message, StringComparison.Ordinal);

        request = request with
        {
            ConstructedMethods = ImmutableDictionary<string, StructuredMethod>.Empty
                .Add("mismatched-identity", structured),
            MethodInstances = ImmutableDictionary<string, MethodInstanceModel>.Empty
                .Add("mismatched-identity", instance)
        };

        var mismatched = Assert.Throws<CompilerException>(
            () => ((IStructuredMethodEmissionPlanner)planner).Plan(request));

        Assert.Equal(DiagnosticCode.CompilerInvariant, mismatched.Diagnostic.Code);
        Assert.Contains("CONSTRUCTED_METHOD_IDENTITY_MISMATCH", mismatched.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanPreservesTheStructuredMethodInstanceAsTheDirectCallerIdentity()
    {
        var baseline = EmitterTestSupport.CreateEmissionRequest();
        var definition = baseline.EntryPoint;
        var structured = baseline.Methods[definition.Key];
        var declaringType = CliTypeIdentity.Named(
            definition.Key.Assembly,
            "Test",
            "CanonicalCaller",
            isValueType: false);
        var methodInstance = new MethodInstanceModel(
            definition,
            declaringType,
            [],
            definition.Signature);
        var request = baseline with
        {
            Methods = ImmutableDictionary<EntityKey, StructuredMethod>.Empty.Add(
                definition.Key,
                structured with
                {
                    Header = structured.Header with { MethodInstance = methodInstance },
                }),
            ConstructedMethods = ImmutableDictionary<string, StructuredMethod>.Empty,
            MethodInstances = ImmutableDictionary<string, MethodInstanceModel>.Empty,
        };
        var identities = new ManagedMethodIdentityFactory();
        var planner = new StructuredMethodEmissionPlanner(
            identities,
            new TestCilTypeIdentityResolver(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                StructuredMethodEmissionPlanner>.Instance);

        var result = ((IStructuredMethodEmissionPlanner)planner).Plan(request);

        Assert.Equal(identities.Create(methodInstance), Assert.Single(result).Identity);
    }

    [Fact]
    public void PlanRejectsANullRequest()
    {
        var definition = EmitterTestSupport.CreateEmissionRequest().EntryPoint;
        var identities = new ManagedMethodIdentityFactory();
        var planner = new StructuredMethodEmissionPlanner(identities,
            new TestCilTypeIdentityResolver(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StructuredMethodEmissionPlanner>.Instance);

        Assert.Throws<ArgumentNullException>(
            () => ((IStructuredMethodEmissionPlanner)planner).Plan(null!));
    }

    [Fact]
    public void PlanLogsEveryExcludedOpenDefinitionWhenTraceIsEnabled()
    {
        var baseline = EmitterTestSupport.CreateEmissionRequest();
        var definition = baseline.EntryPoint;
        var structured = baseline.Methods[definition.Key];
        var firstOpenDefinition = definition with
        {
            Key = new EntityKey(definition.Key.Assembly, definition.Key.MetadataToken + 1),
            Name = "FirstOpenTemplate",
            GenericArity = 1,
        };
        var secondOpenDefinition = firstOpenDefinition with
        {
            Key = new EntityKey(definition.Key.Assembly, definition.Key.MetadataToken + 2),
            Name = "SecondOpenTemplate",
        };
        var request = baseline with
        {
            Methods = ImmutableDictionary<EntityKey, StructuredMethod>.Empty
                .Add(
                    firstOpenDefinition.Key,
                    structured with
                    {
                        Header = structured.Header with { Method = firstOpenDefinition },
                    })
                .Add(
                    secondOpenDefinition.Key,
                    structured with
                    {
                        Header = structured.Header with { Method = secondOpenDefinition },
                    }),
            ConstructedMethods = ImmutableDictionary<string, StructuredMethod>.Empty,
            MethodInstances = ImmutableDictionary<string, MethodInstanceModel>.Empty,
            CallableMethods = ImmutableDictionary<string, MethodInstanceModel>.Empty,
        };
        var logger = new RecordingLogger();
        var planner = new StructuredMethodEmissionPlanner(
            new ManagedMethodIdentityFactory(),
            new TestCilTypeIdentityResolver(),
            logger);

        var result = ((IStructuredMethodEmissionPlanner)planner).Plan(request);

        Assert.Empty(result);
        Assert.Equal(1, logger.Events.Count(eventId => eventId.Id == 4100));
        Assert.Equal(2, logger.Events.Count(eventId => eventId.Id == 4101));
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<EventId> Events { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Events.Add(eventId);

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

}
