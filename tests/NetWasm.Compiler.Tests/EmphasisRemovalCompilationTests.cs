// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

/*
Copyright (c) 2018-2019, Alexandre Mutel
All rights reserved.

Redistribution and use in source and binary forms, with or without modification
, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/
// Reduced from Markdig at 1a1bbecc467a800dd6b39e68825df50309f6065c.
// Tree operations are inert test doubles; list mutation and parser control flow remain.
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
namespace NetWasm.Compiler.Tests;

public sealed class EmphasisRemovalCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void RemovesMatchedDelimitersWithoutReusingTheirIndices(WasmTarget target, bool optimize)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario("EmphasisRemoval", Source,
            "EmphasisRemoval.EntryPoint", optimize, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void BreakAfterPairedRemovalSkipsUnmatchedAlternative(WasmTarget target, bool optimize)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario("PairedRemoval", SmallSource,
            "PairedRemoval.EntryPoint", optimize, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string SmallSource = """
        using System;
        using System.Collections.Generic;
        namespace PairedRemoval;
        public sealed class Value { public int Count = 2; public bool Open; }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var values = new List<Value> { new Value { Open = true }, new Value() };
                try { Process(values); }
                catch (ArgumentOutOfRangeException) { return -2; }
                return values.Count == 0 ? 42 : 1;
            }
            private static void Process(List<Value> values)
            {
                for (var i = 0; i < values.Count; i++)
                {
                    var close = values[i];
                    if (close.Open) continue;
                    while (true)
                    {
                        Value open = null;
                        var openIndex = -1;
                        for (var j = i - 1; j >= 0; j--)
                        {
                            if (values[j].Open) { open = values[j]; openIndex = j; break; }
                        }
                        if (open != null)
                        {
                        again:
                            var delta = Math.Min(Math.Min(open.Count, close.Count), 2);
                            open.Count -= delta;
                            close.Count -= delta;
                            for (var k = i - 1; k >= openIndex + 1; k--)
                            {
                                values.RemoveAt(k); i--;
                            }
                            if (close.Count == 0)
                            {
                                values.RemoveAt(i); i--;
                                if (open.Count == 0) { values.RemoveAt(openIndex); i--; }
                                break;
                            }
                            if (open.Count >= 1) goto again;
                            values.RemoveAt(openIndex); i--;
                        }
                        else if (!close.Open) { values.RemoveAt(i); i--; break; }
                        else break;
                    }
                }
                values.Clear();
            }
        }
        """;

    private const string Source = """
        using System;
        using System.Collections.Generic;
        namespace EmphasisRemoval;
        public sealed class EntryPoint
        {
         private EmphasisDescriptor[] emphasisMap = new EmphasisDescriptor[128];
         private delegate EmphasisInline Legacy(char c, bool isStrong);
         private Legacy CreateEmphasisInline;
         private List<Func<char,int,EmphasisInline>> TryCreateEmphasisInlineList = new();
         public static int Run(int input)
         {
          var parser = new EntryPoint();
          parser.emphasisMap['*'] = new EmphasisDescriptor();
          var values = new List<EmphasisDelimiterInline>();
          values.Add(new EmphasisDelimiterInline { DelimiterChar='*', DelimiterCount=2, Type=DelimiterType.Open });
          values.Add(new EmphasisDelimiterInline { DelimiterChar='*', DelimiterCount=2, Type=DelimiterType.Close });
          try { parser.ProcessEmphasis(values); }
          catch (ArgumentOutOfRangeException) { return -2; }
          catch (NullReferenceException) { return -3; }
          catch (Exception) { return -4; }
          return values.Count == 0 ? 42 : 1;
         }
            private void ProcessEmphasis(List<EmphasisDelimiterInline> delimiters)
            {
                // The following method is inspired by the "An algorithm for parsing nested emphasis and links"
                // at the end of the CommonMark specs.

                // TODO: Benchmark difference between using List and LinkedList here since there could be a few Remove calls

                // Move current_position forward in the delimiter stack (if needed) until
                // we find the first potential closer with delimiter * or _. (This will be the potential closer closest to the beginning of the input – the first one in parse order.)
                for (int i = 0; i < delimiters.Count; i++)
                {
                    var closeDelimiter = delimiters[i];
                    // Skip delimiters not supported by this instance
                    EmphasisDescriptor? emphasisDesc = emphasisMap![closeDelimiter.DelimiterChar];
                    if (emphasisDesc is null)
                    {
                        continue;
                    }

                    if ((closeDelimiter.Type & DelimiterType.Close) != 0 && closeDelimiter.DelimiterCount >= emphasisDesc.MinimumCount)
                    {
                        while (true)
                        {
                            // Now, look back in the stack (staying above stack_bottom and the openers_bottom for this delimiter type)
                            // for the first matching potential opener (“matching” means same delimiter).
                            EmphasisDelimiterInline? openDelimiter = null;
                            int openDelimiterIndex = -1;
                            for (int j = i - 1; j >= 0; j--)
                            {
                                var previousOpenDelimiter = delimiters[j];

                                var isOddMatch = ((closeDelimiter.Type & DelimiterType.Open) != 0 ||
                                                 (previousOpenDelimiter.Type & DelimiterType.Close) != 0) &&
                                                 previousOpenDelimiter.DelimiterCount != closeDelimiter.DelimiterCount &&
                                                 (previousOpenDelimiter.DelimiterCount + closeDelimiter.DelimiterCount) % 3 == 0 &&
                                                 (previousOpenDelimiter.DelimiterCount % 3 != 0 || closeDelimiter.DelimiterCount % 3 != 0);

                                if (previousOpenDelimiter.DelimiterChar == closeDelimiter.DelimiterChar &&
                                    (previousOpenDelimiter.Type & DelimiterType.Open) != 0 &&
                                    previousOpenDelimiter.DelimiterCount >= emphasisDesc.MinimumCount && !isOddMatch)
                                {
                                    openDelimiter = previousOpenDelimiter;
                                    openDelimiterIndex = j;
                                    break;
                                }
                            }

                            if (openDelimiter != null)
                            {
                            process_delims:
                                System.Diagnostics.Debug.Assert(openDelimiter.DelimiterCount >= emphasisDesc.MinimumCount, "Extra emphasis should have been discarded by now");
                                System.Diagnostics.Debug.Assert(closeDelimiter.DelimiterCount >= emphasisDesc.MinimumCount, "Extra emphasis should have been discarded by now");
                                int delimiterDelta = Math.Min(Math.Min(openDelimiter.DelimiterCount, closeDelimiter.DelimiterCount), emphasisDesc.MaximumCount);

                                // Insert an emph or strong emph node accordingly, after the text node corresponding to the opener.
                                EmphasisInline? emphasis = null;
                                {
                                    if (delimiterDelta <= 2) // We can try using the legacy delegate
                                    {
                                        #pragma warning disable CS0618 // Support fields marked as obsolete
                                        emphasis = CreateEmphasisInline?.Invoke(closeDelimiter.DelimiterChar, isStrong: delimiterDelta == 2);
                                        #pragma warning restore CS0618 // Support fields marked as obsolete
                                    }
                                    if (emphasis is null)
                                    {
                                        // Go in backwards order to give priority to newer delegates
                                        for (int delegateIndex = TryCreateEmphasisInlineList.Count - 1; delegateIndex >= 0; delegateIndex--)
                                        {
                                            emphasis = TryCreateEmphasisInlineList[delegateIndex].Invoke(closeDelimiter.DelimiterChar, delimiterDelta);
                                            if (emphasis != null) break;
                                        }

                                        if (emphasis is null)
                                        {
                                            emphasis = new EmphasisInline()
                                            {
                                                DelimiterChar = closeDelimiter.DelimiterChar,
                                                DelimiterCount = delimiterDelta
                                            };
                                        }
                                    }
                                }
                                System.Diagnostics.Debug.Assert(emphasis != null);

                                // Update position for emphasis
                                var openDelimitercount = openDelimiter.DelimiterCount;
                                var closeDelimitercount = closeDelimiter.DelimiterCount;

                                emphasis!.Span.Start = openDelimiter.Span.Start;
                                emphasis.Line = openDelimiter.Line;
                                emphasis.Column = openDelimiter.Column;
                                emphasis.Span.End = closeDelimiter.Span.End - closeDelimitercount + delimiterDelta;

                                openDelimiter.Content.Start += delimiterDelta;
                                openDelimiter.Span.Start += delimiterDelta;
                                openDelimiter.Column += delimiterDelta;
                                closeDelimiter.Content.Start += delimiterDelta;
                                closeDelimiter.Span.Start += delimiterDelta;
                                closeDelimiter.Column += delimiterDelta;

                                openDelimiter.DelimiterCount -= delimiterDelta;
                                closeDelimiter.DelimiterCount -= delimiterDelta;

                                var embracer = (ContainerInline)openDelimiter;

                                // Copy attributes attached to delimiter to the emphasis
                                var attributes = closeDelimiter.TryGetAttributes();
                                if (attributes != null)
                                {
                                    emphasis.SetAttributes(attributes);
                                }

                                // Embrace all delimiters
                                embracer.EmbraceChildrenBy(emphasis);

                                // Remove any intermediate emphasis
                                for (int k = i - 1; k >= openDelimiterIndex + 1; k--)
                                {
                                    var literalDelimiter = delimiters[k];
                                    literalDelimiter.ReplaceBy(literalDelimiter.AsLiteralInline());

                                    delimiters.RemoveAt(k);
                                    i--;
                                }

                                if (closeDelimiter.DelimiterCount == 0)
                                {
                                    var newParent = openDelimiter.DelimiterCount > 0 ? emphasis : emphasis.Parent!;
                                    closeDelimiter.MoveChildrenAfter(newParent);
                                    closeDelimiter.Remove();

                                    delimiters.RemoveAt(i);
                                    i--;

                                    // Remove the open delimiter if it is also empty
                                    if (openDelimiter.DelimiterCount == 0)
                                    {
                                        openDelimiter.MoveChildrenAfter(openDelimiter);
                                        openDelimiter.Remove();

                                        delimiters.RemoveAt(openDelimiterIndex);
                                        i--;
                                    }
                                    break;
                                }

                                // The current delimiters are matching
                                if (openDelimiter.DelimiterCount >= emphasisDesc.MinimumCount)
                                {
                                    goto process_delims;
                                }
                                else if (openDelimiter.DelimiterCount > 0)
                                {
                                    // There are still delimiter characters left, there's just not enough of them
                                    openDelimiter.ReplaceBy(openDelimiter.AsLiteralInline());

                                    delimiters.RemoveAt(openDelimiterIndex);
                                    i--;
                                }
                                else
                                {
                                    // Remove the open delimiter if it is also empty
                                    var firstChild = openDelimiter.FirstChild!;
                                    firstChild.Remove();
                                    openDelimiter.ReplaceBy(firstChild);
                                    firstChild.IsClosed = true;
                                    closeDelimiter.Remove();
                                    firstChild.InsertAfter(closeDelimiter);

                                    delimiters.RemoveAt(openDelimiterIndex);
                                    i--;
                                }
                            }
                            else if ((closeDelimiter.Type & DelimiterType.Open) == 0)
                            {
                                closeDelimiter.ReplaceBy(closeDelimiter.AsLiteralInline());

                                delimiters.RemoveAt(i);
                                i--;
                                break;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }

                // Any delimiters left must be literal
                for (int i = 0; i < delimiters.Count; i++)
                {
                    var delimiter = delimiters[i];
                    delimiter.ReplaceBy(delimiter.AsLiteralInline());
                }
                delimiters.Clear();
            }

        }
        [Flags] public enum DelimiterType { Undefined=0, Open=1, Close=2 }
        public sealed class EmphasisDescriptor { public int MinimumCount=1; public int MaximumCount=2; }
        public struct Position { public int Start; public int End; }
        public class Inline
        {
         public Position Span;
         public int Line; public int Column; public bool IsClosed;
         public ContainerInline Parent;
         public void Remove() { }
         public void InsertAfter(Inline value) { }
        }
        public class ContainerInline : Inline
        {
         public Inline FirstChild=new Inline();
         public void EmbraceChildrenBy(EmphasisInline value) { }
        }
        public sealed class EmphasisInline : ContainerInline
        {
         public char DelimiterChar; public int DelimiterCount;
         public void SetAttributes(object value) { }
        }
        public sealed class EmphasisDelimiterInline : ContainerInline
        {
         public char DelimiterChar; public int DelimiterCount; public DelimiterType Type; public Position Content;
         public object TryGetAttributes() => null;
         public void MoveChildrenAfter(Inline value) { }
         public void ReplaceBy(Inline value) { }
         public Inline AsLiteralInline() => new Inline();
        }
        """;
}
