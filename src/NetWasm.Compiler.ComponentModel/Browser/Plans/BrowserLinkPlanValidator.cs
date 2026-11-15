// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

public interface IBrowserLinkPlanValidator
{
    void Validate(BrowserLinkPlan plan);
}

public sealed class BrowserLinkPlanValidator(
    IBrowserLinkPathValidator paths,
    IBrowserLinkOperationInspector operations) : IBrowserLinkPlanValidator
{
    private readonly IBrowserLinkPathValidator _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IBrowserLinkOperationInspector _operations = operations ?? throw new ArgumentNullException(nameof(operations));

    public void Validate(BrowserLinkPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.SchemaVersion != BrowserLinkPlan.CurrentSchemaVersion || plan.Operations.IsDefaultOrEmpty ||
            plan.RequiredCapabilities.IsDefault || plan.Outputs.IsDefaultOrEmpty)
            throw new ArgumentException("The browser link plan version and operation contract must be explicit.", nameof(plan));
        var inputs = ReadPaths(plan.Inputs);
        var outputs = ReadPaths(plan.Outputs);
        var temporary = ReadPaths(plan.TemporaryPaths);
        if (inputs.Overlaps(outputs) || inputs.Overlaps(temporary) || outputs.Overlaps(temporary))
            throw new ArgumentException("Browser link input, output and temporary ownership must be disjoint.", nameof(plan));
        var declaredPaths = new HashSet<string>(inputs, StringComparer.Ordinal);
        declaredPaths.UnionWith(outputs);
        declaredPaths.UnionWith(temporary);
        var rooted = declaredPaths.First().StartsWith('/');
        foreach (var path in declaredPaths)
        {
            if (path.StartsWith('/') != rooted)
                throw new ArgumentException("Virtual paths must use one consistent rooted or relative namespace.", nameof(plan));
            for (var index = path.IndexOf('/', 1); index >= 0; index = path.IndexOf('/', index + 1))
            {
                if (declaredPaths.Contains(path[..index]))
                    throw new ArgumentException("An artifact file cannot also own a virtual directory.", nameof(plan));
            }
        }
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in plan.RequiredCapabilities)
        {
            if (string.IsNullOrWhiteSpace(capability) || !required.Add(capability))
                throw new ArgumentException("Required capabilities must be explicit and unique.", nameof(plan));
        }
        var used = new HashSet<string>(StringComparer.Ordinal);
        var available = new HashSet<string>(inputs, StringComparer.Ordinal);
        var unread = new HashSet<string>(StringComparer.Ordinal);
        var validated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in plan.Operations)
        {
            var contract = _operations.Inspect(operation);
            used.Add(contract.Capability);
            if (!required.Contains(contract.Capability))
                throw new ArgumentException("An operation capability was not declared.", nameof(plan));
            foreach (var read in contract.Reads)
            {
                if (!available.Contains(read))
                    throw new ArgumentException("An operation reads an unavailable artifact.", nameof(plan));
                unread.Remove(read);
            }
            var preservesValidation = contract.PreservesValidation &&
                !contract.Reads.IsEmpty && contract.Reads.All(validated.Contains);
            foreach (var consumed in contract.Consumes)
            {
                if (!temporary.Contains(consumed) || !contract.Reads.Contains(consumed))
                    throw new ArgumentException("Only a read, owned temporary artifact may be consumed.", nameof(plan));
                available.Remove(consumed);
                validated.Remove(consumed);
            }
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (var write in contract.Writes)
            {
                if ((!temporary.Contains(write) && !outputs.Contains(write)) ||
                    !written.Add(write) || unread.Contains(write))
                    throw new ArgumentException("An operation writes an unowned, repeated or unread artifact.", nameof(plan));
                available.Add(write);
                unread.Add(write);
                validated.Remove(write);
                if (preservesValidation) validated.Add(write);
            }
            if (contract.ValidatesInputs) validated.UnionWith(contract.Reads);
        }
        if (!used.SetEquals(required))
            throw new ArgumentException("The declared capabilities do not match the operation sequence.", nameof(plan));
        if (!outputs.IsSubsetOf(available) || !outputs.IsSubsetOf(validated))
            throw new ArgumentException("Every final artifact must be produced and validated.", nameof(plan));
    }

    private HashSet<string> ReadPaths(ImmutableArray<string> paths)
    {
        if (paths.IsDefault)
            throw new ArgumentException("Artifact paths must be explicit.", nameof(paths));
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            _paths.Validate(path);
            if (!result.Add(path))
                throw new ArgumentException("Artifact paths must be unique.", nameof(paths));
        }
        return result;
    }
}
