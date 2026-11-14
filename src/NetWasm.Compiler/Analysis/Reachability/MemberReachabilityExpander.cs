using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Analysis.ManagedCallSites;
using NetWasm.Compiler.Analysis.Delegates;

namespace NetWasm.Compiler.Analysis.Reachability;

internal sealed class MemberReachabilityExpander(
    IMemberDescriptorPlanner memberDescriptors,
    IMemberExecutionPlanner memberExecutions,
    IDispatchTargetResolver dispatchTargets,
    IDispatchTargetBodyClassifier dispatchTargetBodies) : IMemberReachabilityExpander
{
    private readonly IMemberDescriptorPlanner _memberDescriptors = memberDescriptors ??
        throw new ArgumentNullException(nameof(memberDescriptors));
    private readonly IMemberExecutionPlanner _memberExecutions = memberExecutions ??
        throw new ArgumentNullException(nameof(memberExecutions));
    private readonly IDispatchTargetResolver _dispatchTargets = dispatchTargets ??
        throw new ArgumentNullException(nameof(dispatchTargets));
    private readonly IDispatchTargetBodyClassifier _dispatchTargetBodies = dispatchTargetBodies ??
        throw new ArgumentNullException(nameof(dispatchTargetBodies));

    public void Expand(ReachabilitySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var memberDescriptorPlan = _memberDescriptors.Build(
            new MemberDescriptorPlanningRequest(
                session.State.MethodDescriptors.ToImmutable(),
                session.State.FieldDescriptors.ToImmutable(),
                IncludePropertyAssociations: true,
                IncludeNames: session.State.RequiresMemberNames));
        foreach (var method in memberDescriptorPlan.Methods.Values)
        {
            session.AddMethodDescriptor(method);
        }
        foreach (var field in memberDescriptorPlan.Fields.Values)
        {
            session.AddFieldDescriptor(field);
        }
        foreach (var property in memberDescriptorPlan.Properties.Values)
        {
            session.AddPropertyDescriptor(property);
        }
        foreach (var type in memberDescriptorPlan.RequiredTypes)
        {
            session.AddDescriptorType(type);
        }
        session.State.NamedMemberDescriptors.UnionWith(
            memberDescriptorPlan.NamedDescriptors);
        session.State.Strings.UnionWith(memberDescriptorPlan.NameStrings);
        var memberExecution = _memberExecutions.Plan(
            session.State.MethodInstances.Values,
            session.State.MethodDescriptors.Values,
            session.State.FieldDescriptors.Values);
        if (memberExecution.UnsupportedTarget is not null)
        {
            session.EnqueueInstance(memberExecution.UnsupportedTarget);
        }
        if (memberExecution.DelegateInvocation is { } delegateInvocation)
        {
            session.EnqueueInstance(delegateInvocation.UnsupportedTarget);
            session.EnqueueInstance(delegateInvocation.ArgumentTarget);
            session.EnqueueInstance(delegateInvocation.ParameterCountTarget);
            session.EnqueueInstance(delegateInvocation.InvocationTarget);
            foreach (var method in delegateInvocation.Methods.Values)
            {
                foreach (var parameter in method.Signature.ParameterSignatureTypes)
                {
                    session.AddMemberExecutionValue(
                        parameter.StackKind == CliValueKind.ManagedAddress
                            ? parameter.ElementType!
                            : parameter);
                }
                session.AddMemberExecutionValue(method.Signature.ReturnSignatureType);
            }
        }
        var plannedExecutions = memberExecution.Methods.ToBuilder();
        foreach (var pair in memberExecution.Methods)
        {
            var execution = pair.Value;
            MethodInstanceModel method = execution.Descriptor;
            if (method.Definition.Name == ".ctor")
            {
                // New runs the constructor on an object or box the intrinsic allocates.
                session.VisitMethod(CilOperation.NewObject, method);
                session.AddAllocatedType(method.DeclaringType);
            }
            else if (!execution.RequiresDispatch)
            {
                session.VisitMethod(CilOperation.Call, method);
            }
            else
            {
                var targets = session.State.AllocatedTypes
                    .OrderBy(type => type.CanonicalName, StringComparer.Ordinal)
                    .Select(type => _dispatchTargets.Resolve(method, type))
                    .Where(target => target is not null && _dispatchTargetBodies.RunsAsBody(target.Method))
                    .Select(target => target!)
                    .DistinctBy(target => (
                        target.ReceiverType.CanonicalName,
                        target.Method.CanonicalName))
                    .ToImmutableArray();
                foreach (var target in targets)
                {
                    session.VisitMethod(CilOperation.CallVirtual, target.Method);
                }
                plannedExecutions[pair.Key] = execution with
                {
                    Targets = targets,
                };
            }
            foreach (var parameter in method.Signature.ParameterSignatureTypes)
            {
                session.AddMemberExecutionValue(parameter);
            }
            session.AddMemberExecutionValue(method.Signature.ReturnSignatureType);
            if (!method.Definition.IsStatic && method.DeclaringType.IsValueType)
            {
                // The receiver arrives boxed, and the intrinsic checks the box's type.
                session.AddMemberExecutionValue(method.DeclaringType);
            }
        }
        foreach (var field in memberExecution.Fields.Values)
        {
            session.VisitField(field);
            session.AddMemberExecutionValue(field.FieldType);
            if (!field.Definition.IsStatic && field.DeclaringType.IsValueType)
            {
                session.AddMemberExecutionValue(field.DeclaringType);
            }
        }
        session.State.MemberExecution = memberExecution with
        {
            Methods = plannedExecutions.ToImmutable(),
        };
    }
}
