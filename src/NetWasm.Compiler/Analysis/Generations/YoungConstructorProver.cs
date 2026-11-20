// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Analysis.NullChecks;

namespace NetWasm.Compiler.Analysis.Generations;

internal interface IYoungConstructorProver
{
    // The program with YoungThisAtEntry set on each class constructor whose this is young when it starts.
    ReachableProgram Prove(ReachableProgram program);
}

// A class constructor's this is young when it starts if every way the program reaches the constructor passes an
// object allocated since the last collection. newobj allocates one and calls the constructor at once, and a
// constructor that chains to another before anything in it can collect (YoungConstructorChains) passes its own
// this, young when its own was. Any other reference, a direct call on another object, a function pointer, a method
// handle, a delegate or member execution, can pass an older object: it disproves the constructor it names, and
// those that constructor chains to. A definition is judged as a whole, as the receiver proof judges it.
internal sealed class YoungConstructorProver(
    ICalledMethodResolver calledMethods,
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks) : IYoungConstructorProver
{
    private readonly ICalledMethodResolver _calledMethods = calledMethods ??
        throw new ArgumentNullException(nameof(calledMethods));
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));

    public ReachableProgram Prove(ReachableProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var disproven = new HashSet<EntityKey>();
        // The constructors each constructor chains to with its young this.
        var chains = new Dictionary<EntityKey, HashSet<EntityKey>>();
        var bodies = program.Methods.Values.Concat(program.ConstructedMethods.Values).ToArray();
        foreach (var method in bodies)
        {
            var young = YoungConstructorChains.Of(method.Method, method.Body).ToDictionary(
                static chain => chain.Index, static chain => chain.Target);
            foreach (var (index, target) in InstanceMethodReferences.Of(method.Body, _calledMethods))
            {
                if (!ConstructorReceivers.IsInstanceConstructor(target)) continue;
                if (young.ContainsKey(index))
                {
                    var caller = method.Method.Definition.Key;
                    if (!chains.TryGetValue(caller, out var callees)) chains[caller] = callees = [];
                    callees.Add(target.Definition.Key);
                    continue;
                }
                disproven.Add(target.Definition.Key);
            }
        }
        foreach (var target in InstanceMethodReferences.Invoked(program))
        {
            if (ConstructorReceivers.IsInstanceConstructor(target)) disproven.Add(target.Definition.Key);
        }
        // A constructor chained to with the this of one whose this may be old may get an old one too.
        var pending = new Queue<EntityKey>(disproven);
        while (pending.TryDequeue(out var caller))
        {
            if (!chains.TryGetValue(caller, out var callees)) continue;
            foreach (var callee in callees)
            {
                if (disproven.Add(callee)) pending.Enqueue(callee);
            }
        }
        var methods = program.Methods.ToBuilder();
        var constructedMethods = program.ConstructedMethods.ToBuilder();
        foreach (var method in bodies)
        {
            if (!ConstructorReceivers.IsInstanceConstructor(method.Method) ||
                disproven.Contains(method.Method.Definition.Key) || method.Body.YoungThisAtEntry) continue;
            var proven = new ManagedMethodBody(method.Method,
                _stacks.Validate(_graphs.Build(method.Body with { YoungThisAtEntry = true })));
            if (program.Methods.TryGetValue(method.Method.Definition.Key, out var entity) && ReferenceEquals(entity, method))
            {
                methods[method.Method.Definition.Key] = proven;
            }
            else
            {
                constructedMethods[method.Method.CanonicalName] = proven;
            }
        }
        return program with { Methods = methods.ToImmutable(), ConstructedMethods = constructedMethods.ToImmutable() };
    }
}

internal interface IYoungConstructorProverFactory
{
    IYoungConstructorProver Create(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols);
}

internal sealed class YoungConstructorProverFactory(
    ITypedStackValidatorFactory stackValidators,
    IControlFlowGraphBuilderFactory graphBuilders,
    ICalledMethodResolverFactory calledMethods) : IYoungConstructorProverFactory
{
    private readonly ITypedStackValidatorFactory _stackValidators = stackValidators ??
        throw new ArgumentNullException(nameof(stackValidators));
    private readonly IControlFlowGraphBuilderFactory _graphBuilders = graphBuilders ??
        throw new ArgumentNullException(nameof(graphBuilders));
    private readonly ICalledMethodResolverFactory _calledMethods = calledMethods ??
        throw new ArgumentNullException(nameof(calledMethods));

    public IYoungConstructorProver Create(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols) =>
        new YoungConstructorProver(
            _calledMethods.Create(methods, methodInstances, symbols),
            _graphBuilders.Create(),
            _stackValidators.Create(types, fields, methods));
}
