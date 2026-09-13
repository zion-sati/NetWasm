using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public sealed record RawFinalImportSignatureValidationRequest(
    RawWitBindingPlan Plan,
    ImmutableArray<RawCliFunctionImportSignature> JavaScriptImports,
    ImmutableArray<RawCliFunctionImportSignature> RuntimeImports,
    ImmutableArray<RawCoreFunctionImportSignature> ObservedImports)
{
    public ImmutableArray<RawCoreFunctionImportSignature> RuntimeCoreImports { get; init; } = [];
}

public sealed class RawValidatedBindingPlan
{
    internal RawValidatedBindingPlan(
        RawWitBindingPlan plan,
        ImmutableArray<RawCoreFunctionImportSignature> expectedImports,
        ImmutableArray<RawCoreFunctionImportSignature> observedImports)
    {
        Plan = plan;
        ExpectedImports = expectedImports;
        ObservedImports = observedImports;
    }

    public RawWitBindingPlan Plan { get; }

    public ImmutableArray<RawCoreFunctionImportSignature> ExpectedImports { get; }

    public ImmutableArray<RawCoreFunctionImportSignature> ObservedImports { get; }
}

public interface IRawFinalImportSignatureValidator
{
    RawValidatedBindingPlan Validate(RawFinalImportSignatureValidationRequest request);
}

public sealed class RawFinalImportSignatureValidator(
    IRawCliCoreSignatureProjector signatures) : IRawFinalImportSignatureValidator
{
    private readonly IRawCliCoreSignatureProjector _signatures = signatures ??
        throw new ArgumentNullException(nameof(signatures));

    public RawValidatedBindingPlan Validate(RawFinalImportSignatureValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Plan);
        ArgumentNullException.ThrowIfNull(request.Plan.Selection);
        ArgumentNullException.ThrowIfNull(request.Plan.Selection.Catalog);
        RequireExplicit(request.Plan.Imports, "raw WIT layouts");
        RequireExplicit(request.Plan.Selection.WitImports, "selected raw WIT imports");
        RequireExplicit(request.Plan.Selection.JavaScriptImports, "selected JavaScript imports");
        RequireExplicit(request.Plan.Selection.RuntimeImports, "selected runtime imports");
        RequireExplicit(request.JavaScriptImports, "declared JavaScript imports");
        RequireExplicit(request.RuntimeImports, "declared runtime imports");
        RequireExplicit(request.RuntimeCoreImports, "declared core runtime imports");
        RequireExplicit(request.ObservedImports, "observed raw imports");
        var target = request.Plan.Selection.Catalog.Target;
        if (target is not (WasmTarget.Wasm32 or WasmTarget.Wasm64))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        var expected = new Dictionary<RawCanonicalImportIdentity, RawCoreFunctionImportSignature>();
        AddWitImports(request.Plan, target, expected);
        AddExternalImports(
            request.Plan.Selection.JavaScriptImports,
            request.JavaScriptImports,
            [],
            target,
            "JavaScript",
            expected);
        AddExternalImports(
            request.Plan.Selection.RuntimeImports,
            request.RuntimeImports,
            request.RuntimeCoreImports,
            target,
            "runtime",
            expected);
        ValidateObserved(request.ObservedImports, expected);
        return new(
            request.Plan,
            [.. expected.Values.OrderBy(import => import.Identity.Module, StringComparer.Ordinal)
                .ThenBy(import => import.Identity.Name, StringComparer.Ordinal)],
            [.. request.ObservedImports]);
    }

    private void AddWitImports(
        RawWitBindingPlan plan,
        WasmTarget target,
        Dictionary<RawCanonicalImportIdentity, RawCoreFunctionImportSignature> expected)
    {
        if (plan.Imports.Length != plan.Selection.WitImports.Length)
        {
            throw ComponentException.Invalid("raw WIT layout selection is incomplete");
        }
        for (var index = 0; index < plan.Imports.Length; index++)
        {
            var layout = plan.Imports[index] ??
                throw ComponentException.Invalid("raw WIT layout cannot be null");
            if (layout.Identity != plan.Selection.WitImports[index] || layout.Target != target)
            {
                throw ComponentException.Invalid("raw WIT layout does not match its selected identity and target");
            }
            ArgumentNullException.ThrowIfNull(layout.Signature);
            AddExpected(_signatures.Project(new(
                layout.Identity,
                layout.Signature.Parameters,
                layout.Signature.Result), target), expected);
        }
    }

    private void AddExternalImports(
        ImmutableArray<RawCanonicalImportIdentity> selected,
        ImmutableArray<RawCliFunctionImportSignature> declarations,
        ImmutableArray<RawCoreFunctionImportSignature> coreDeclarations,
        WasmTarget target,
        string owner,
        Dictionary<RawCanonicalImportIdentity, RawCoreFunctionImportSignature> expected)
    {
        if (selected.Length != declarations.Length + coreDeclarations.Length)
        {
            throw ComponentException.Invalid($"raw {owner} signature declarations do not match their selected imports");
        }
        var remaining = selected.ToHashSet();
        if (remaining.Count != selected.Length)
        {
            throw ComponentException.Invalid($"selected raw {owner} imports contain duplicate identities");
        }
        foreach (var declaration in declarations)
        {
            ArgumentNullException.ThrowIfNull(declaration);
            if (!remaining.Remove(declaration.Identity))
            {
                throw ComponentException.Invalid($"raw {owner} signature declaration has no selected import");
            }
            AddExpected(_signatures.Project(declaration, target), expected);
        }
        foreach (var declaration in coreDeclarations)
        {
            ArgumentNullException.ThrowIfNull(declaration);
            ValidateCoreSignature(declaration, $"raw {owner} core signature declaration");
            if (!remaining.Remove(declaration.Identity))
            {
                throw ComponentException.Invalid($"raw {owner} core signature declaration has no selected import");
            }
            AddExpected(declaration, expected);
        }
    }

    private static void AddExpected(
        RawCoreFunctionImportSignature signature,
        Dictionary<RawCanonicalImportIdentity, RawCoreFunctionImportSignature> expected)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(signature.Identity);
        if (!expected.TryAdd(signature.Identity, signature))
        {
            throw ComponentException.Invalid("raw import signature has multiple declared owners");
        }
    }

    private static void ValidateObserved(
        ImmutableArray<RawCoreFunctionImportSignature> observed,
        Dictionary<RawCanonicalImportIdentity, RawCoreFunctionImportSignature> expected)
    {
        var seen = new HashSet<RawCanonicalImportIdentity>();
        foreach (var import in observed)
        {
            ArgumentNullException.ThrowIfNull(import);
            ValidateCoreSignature(import, "observed raw import");
            if (!seen.Add(import.Identity))
            {
                throw ComponentException.Invalid("observed raw imports contain duplicate identities");
            }
            if (!expected.TryGetValue(import.Identity, out var declaration))
            {
                throw ComponentException.Invalid("observed raw import has no declared signature");
            }
            if (!import.Parameters.SequenceEqual(declaration.Parameters)
                || !import.Results.SequenceEqual(declaration.Results))
            {
                throw ComponentException.Invalid("observed raw import signature does not match its declaration");
            }
        }
        if (seen.Count != expected.Count)
        {
            throw ComponentException.Invalid("declared raw import is absent from the final module");
        }
    }

    private static void ValidateCoreSignature(
        RawCoreFunctionImportSignature signature,
        string label)
    {
        ArgumentNullException.ThrowIfNull(signature.Identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(signature.Identity.Module);
        ArgumentException.ThrowIfNullOrWhiteSpace(signature.Identity.Name);
        RequireExplicit(signature.Parameters, $"{label} parameters");
        RequireExplicit(signature.Results, $"{label} results");
        if (signature.Parameters.Any(value => !Enum.IsDefined(value))
            || signature.Results.Any(value => !Enum.IsDefined(value)))
        {
            throw ComponentException.Invalid($"{label} contains an unsupported core value type");
        }
    }

    private static void RequireExplicit<T>(ImmutableArray<T> values, string label)
    {
        if (values.IsDefault)
        {
            throw ComponentException.Invalid($"{label} must be explicit");
        }
    }
}
