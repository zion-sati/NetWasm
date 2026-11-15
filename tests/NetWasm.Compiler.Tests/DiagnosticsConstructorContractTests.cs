// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class DiagnosticsConstructorContractTests
{
    [Fact]
    public void EveryDiagnosticsActorRejectsEachMissingInjectedCollaborator()
    {
        using var services = new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider();
        var actors = typeof(CompilerCilOperandFormatter).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(CompilerCilOperandFormatter).Namespace)
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => !type.IsGenericType)
            .Where(type => type.GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(constructor => constructor.GetParameters())
                .All(parameter => parameter.ParameterType.IsInterface))
            .ToArray();

        foreach (var actor in actors)
        {
            var constructor = Assert.Single(actor.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            var arguments = constructor.GetParameters()
                .Select(parameter => services.GetRequiredService(parameter.ParameterType))
                .ToArray();
            for (var index = 0; index < arguments.Length; index++)
            {
                if (constructor.GetParameters()[index] is
                    { HasDefaultValue: true, DefaultValue: null })
                {
                    continue;
                }
                var original = arguments[index];
                arguments[index] = null!;
                var invocation = Record.Exception(() => constructor.Invoke(arguments));
                Assert.True(
                    invocation is TargetInvocationException,
                    $"{actor.FullName} accepted a missing {constructor.GetParameters()[index].Name} collaborator.");
                var targetInvocation = (TargetInvocationException)invocation;
                Assert.IsType<ArgumentNullException>(targetInvocation.InnerException);
                arguments[index] = original;
            }
        }
    }
}
