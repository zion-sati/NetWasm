using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class StaticInitializationFailureEmitterTests
{
    public static TheoryData<WasmTarget, uint, uint, bool> States => new()
    {
        { WasmTarget.Wasm32, 1, 0, false },
        { WasmTarget.Wasm32, 1, 1, true },
        { WasmTarget.Wasm32, 1, 0xfffffffd, true },
        { WasmTarget.Wasm32, 1, 0xfffffffe, false },
        { WasmTarget.Wasm32, 1, 0xffffffff, false },
        { WasmTarget.Wasm32, 43, 41, true },
        { WasmTarget.Wasm64, 1, 0, false },
        { WasmTarget.Wasm64, 1, 1, true },
        { WasmTarget.Wasm64, 1, 0xfffffffd, true },
        { WasmTarget.Wasm64, 1, 0xfffffffe, false },
        { WasmTarget.Wasm64, 1, 0xffffffff, false },
        { WasmTarget.Wasm64, 43, 41, true },
    };

    [Theory]
    [MemberData(nameof(States))]
    public void PublishesOriginalBeforeFilterDispatchAndRejectsInvalidHandles(
        WasmTarget target, uint initialState, uint handle, bool valid)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var writers = new StaticInitializerRecordingWriters();
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = Assert.IsAssignableFrom<IStaticInitializationFailureEmitter>(
            new StaticInitializationFailureEmitter(layouts, imports, writers));
        emitter.Emit(new([], 480, 102));
        var code = writers.Instructions.ToInstructions().ToArray();

        var execution = Execute(code, imports, initialState, handle);

        Assert.Equal(valid, execution.ThrownOriginal);
        Assert.Equal(initialState == 1 && valid, execution.EndedCatch);
        Assert.Equal(initialState == 1 ? 1 : 0, execution.HandleCreations);
        Assert.Equal(valid ? handle + 2 : initialState, execution.Guard);
        Assert.Equal(valid ? 1 : 0, execution.FilterSearches);
        Assert.DoesNotContain(code, instruction => instruction.Opcode == WasmOpcodes.Call &&
            (instruction.Operand.UnsignedValue == (uint)imports.Resolve(RuntimeImportSymbol.Allocate) ||
             instruction.Operand.UnsignedValue == (uint)imports.Resolve(RuntimeImportSymbol.RootFrameEnter) ||
             instruction.Operand.UnsignedValue == (uint)imports.Resolve(RuntimeImportSymbol.HandleRelease)));
    }

    // Execute the emitted guard/dispatch subset against in-memory imports. The
    // BeginThrow boundary models a filter observing the published failure.
    private static (bool ThrownOriginal, bool EndedCatch, uint Guard, int HandleCreations, int FilterSearches) Execute(
        WasmInstruction[] code, RuntimeImportCatalog imports, uint initialGuard, uint handle)
    {
        var stack = new Stack<uint>();
        var locals = new uint[] { 240, 400, 0 };
        var guard = initialGuard;
        var endedCatch = false;
        var handles = 0;
        var filters = 0;
        for (var index = 0; index < code.Length; index++)
        {
            var instruction = code[index];
            switch (instruction.Opcode)
            {
                case WasmOpcodes.LocalGet: stack.Push(locals[instruction.Operand.UnsignedValue]); break;
                case WasmOpcodes.LocalTee: locals[instruction.Operand.UnsignedValue] = stack.Peek(); break;
                case WasmOpcodes.I32Constant: stack.Push(unchecked((uint)instruction.Operand.SignedValue)); break;
                case WasmOpcodes.I32Load: Assert.Equal(240U, stack.Pop()); stack.Push(guard); break;
                case WasmOpcodes.I32Store: guard = stack.Pop(); Assert.Equal(240U, stack.Pop()); break;
                case WasmOpcodes.I32Equal: stack.Push(stack.Pop() == stack.Pop() ? 1U : 0U); break;
                case WasmOpcodes.I32Add: stack.Push(unchecked(stack.Pop() + stack.Pop())); break;
                case WasmOpcodes.I32Subtract:
                    var subtrahend = stack.Pop(); stack.Push(unchecked(stack.Pop() - subtrahend)); break;
                case WasmOpcodes.I32GreaterThanUnsigned:
                    var right = stack.Pop(); stack.Push(stack.Pop() > right ? 1U : 0U); break;
                case WasmOpcodes.If:
                    if (stack.Pop() != 0) break;
                    var nesting = 1;
                    while (nesting != 0)
                    {
                        var opcode = code[++index].Opcode;
                        if (opcode == WasmOpcodes.If) nesting++;
                        if (opcode == WasmOpcodes.End) nesting--;
                    }
                    break;
                case WasmOpcodes.End: break;
                case WasmOpcodes.Unreachable: return (false, endedCatch, guard, handles, filters);
                case WasmOpcodes.Call:
                    var called = instruction.Operand.UnsignedValue;
                    if (called == (uint)imports.Resolve(RuntimeImportSymbol.HandleNew))
                    {
                        Assert.Equal(400U, stack.Pop());
                        Assert.False(endedCatch);
                        Assert.Equal(1U, guard);
                        handles++;
                        stack.Push(handle);
                    }
                    else if (called == (uint)imports.Resolve(RuntimeImportSymbol.EndCatch))
                    {
                        Assert.Equal(handle + 2, guard);
                        endedCatch = true;
                    }
                    else
                    {
                        Assert.Equal((uint)imports.Resolve(RuntimeImportSymbol.BeginThrow), called);
                        Assert.Equal(400U, stack.Pop());
                        Assert.Equal(handle + 2, guard);
                        Assert.Equal(initialGuard == 1, endedCatch);
                        filters++;
                    }
                    break;
                case WasmOpcodes.Throw:
                    Assert.Equal(400U, stack.Pop());
                    Assert.Equal(1, filters);
                    return (true, endedCatch, guard, handles, filters);
                default: throw new InvalidOperationException("Unexpected failure-dispatch instruction.");
            }
        }
        throw new InvalidOperationException("The failure function did not throw or reject the handle.");
    }
}
