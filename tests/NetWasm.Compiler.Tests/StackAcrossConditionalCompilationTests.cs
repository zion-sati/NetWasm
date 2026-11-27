// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// `Selected = count == 0 ? false : count == items.Count ? true : null;` pushes the receiver before a nested conditional
// whose arms each build a bool? (newobj, or initobj and a load), so the receiver is on the stack across both branches
// (Devolutions.AvaloniaControls' MultiComboBoxGroupHeaderItem.UpdateSelection). At O3 and Oz the optimized method
// failed NW1002, an incompatible evaluation stack at a merge, where O0 compiled it.
public sealed class StackAcrossConditionalCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O1)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Os)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void AReceiverPushedBeforeANestedConditionalReachesTheMerge(WasmTarget target,
        FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "StackAcrossConditionalFixture",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;

            public sealed class Property<T> { public T Value; }

            public class Element
            {
                private readonly Dictionary<object, object> _values = new();
                public T GetValue<T>(Property<T> property) =>
                    _values.TryGetValue(property, out var value) ? (T)value : property.Value;
                public void SetValue<T>(Property<T> property, T value) => _values[property] = value;
            }

            public static class ElementExtensions
            {
                public static void SetValueIfChanged<T>(this Element element, Property<T?> property, T? value,
                    bool canSetToNull = true) where T : struct, IEquatable<T>
                {
                    T? current = element.GetValue(property);
                    if ((value.HasValue | canSetToNull) && !current.Equals(value))
                    {
                        element.SetValue(property, value);
                    }
                }
            }

            public sealed class Parent
            {
                public IList SelectedItems { get; } = new List<object>();
            }

            public sealed class GroupHeader : Element
            {
                public static readonly Property<bool?> SelectedProperty = new() { Value = false };
                private bool _updating;
                private readonly Parent _parent;

                public GroupHeader(Parent parent, IReadOnlyList<object> items)
                {
                    _parent = parent;
                    Items = items;
                }

                public IReadOnlyList<object> Items { get; }
                public bool? Selected
                {
                    get => GetValue(SelectedProperty);
                    set => this.SetValueIfChanged(SelectedProperty, value, true);
                }

                public void UpdateSelection()
                {
                    _updating = true;
                    IReadOnlyList<object> items = Items;
                    IList selected = _parent?.SelectedItems;
                    if (items == null || items.Count == 0 || selected == null || selected.Count == 0)
                    {
                        Selected = false;
                    }
                    else
                    {
                        var count = 0;
                        foreach (var item in items)
                        {
                            if (selected.Contains(item)) count++;
                        }
                        Selected = count == 0 ? false : count == items.Count ? true : null;
                    }
                    _updating = false;
                }
            }

            public static class EntryPoint
            {
                private static int Code(bool? value) => value switch { null => 3, true => 2, false => 1 };

                public static int Run(int input)
                {
                    var parent = new Parent();
                    var header = new GroupHeader(parent, ["a", "b", "c"]);
                    var result = 0;
                    header.UpdateSelection();
                    result = result * 10 + Code(header.Selected);
                    parent.SelectedItems.Add("x");
                    header.UpdateSelection();
                    result = result * 10 + Code(header.Selected);
                    parent.SelectedItems.Add("b");
                    header.UpdateSelection();
                    result = result * 10 + Code(header.Selected);
                    parent.SelectedItems.Add("a");
                    parent.SelectedItems.Add("c");
                    header.UpdateSelection();
                    result = result * 10 + Code(header.Selected);
                    return result + input;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization));

        // Nothing selected, none of the items, one of three, all three: false, false, null, true.
        Assert.Equal(1132 + 5, ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 5, target, result.StaticDataEnd));
    }
}
