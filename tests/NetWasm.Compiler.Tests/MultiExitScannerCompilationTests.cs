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
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
namespace NetWasm.Compiler.Tests;

public sealed class MultiExitScannerCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void PreservesSharedCleanupAfterScanningLoops(WasmTarget target, bool optimize)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario("MultiExitScanner", Source, "MultiExitScanner.EntryPoint", optimize, target, 41, []) { Optimization = FinalWasmOptimization.None }));
    }
    private const string Source = """
    // Adapted from Markdig LinkHelper at 1a1bbecc467a800dd6b39e68825df50309f6065c.
    // Copyright (c) Alexandre Mutel. Licensed under the BSD 2-Clause license.
    using System;
    namespace MultiExitScanner;
    public static class EntryPoint
    {
     public static int Run(int input)
     {
      var texts = new[] { "<http://example.com>", "<a@b.co>", "<x:>", "<http: space>", "<a@b->", "plain", "<>" };
      for (var i=0;i<texts.Length;i++)
      {
       var slice = new StringSlice(texts[i]);
       var accepted = TryParseAutolink(ref slice, out var link, out var email);
       if (accepted != (i<2)) return i+1;
       if (accepted && email != (i==1)) return 20+i;
      }
      return 42;
     }
        public static bool TryParseAutolink(ref StringSlice text, out string link, out bool isEmail)
        {
            link = null;
            isEmail = false;

            var c = text.CurrentChar;
            if (c != '<')
            {
                return false;
            }

            // An absolute URI, for these purposes, consists of a scheme followed by a colon (:)
            // followed by zero or more characters other than ASCII whitespace and control characters, <, and >.
            // If the URI includes these characters, they must be percent-encoded (e.g. %20 for a space).
            // A URI that would end with a full stop (.) is treated instead as ending immediately before the full stop.

            // a scheme is any sequence of 2–32 characters
            // beginning with an ASCII letter
            // and followed by any combination of ASCII letters, digits, or the symbols plus (”+”), period (”.”), or hyphen (”-”).

            // An email address, for these purposes, is anything that matches the non-normative regex from the HTML5 spec:
            // /^
            // [a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+
            // @
            // [a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?
            // (?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$/

            c = text.NextChar();

            // -1: scan email
            //  0: scan uri or email
            // +1: scan uri
            int state = 0;

            if (!c.IsAlpha())
            {
                // We may have an email char?
                if (c.IsDigit() || CharHelper.IsEmailUsernameSpecialChar(c))
                {
                    state = -1;
                }
                else
                {
                    return false;
                }
            }

            var builder = new ValueStringBuilder(stackalloc char[ValueStringBuilder.StackallocThreshold]);

            // ****************************
            // 1. Scan scheme or user email
            // ****************************
            builder.Append(c);
            while (true)
            {
                c = text.NextChar();

                // Chars valid for both scheme and email
                var isSpecialChar = c == '+' || c == '.' || c == '-';
                var isValidChar = c.IsAlphaNumeric() || isSpecialChar;
                if (state <= 0 && CharHelper.IsEmailUsernameSpecialChar(c))
                {
                    isValidChar = true;
                    // If this is not a special char valid also for url scheme, then we have an email
                    if (!isSpecialChar)
                    {
                        state = -1;
                    }
                }

                if (isValidChar)
                {
                    // a scheme is any sequence of 2–32 characters
                    if (state > 0 && builder.Length >= 32)
                    {
                        goto ReturnFalse;
                    }
                    builder.Append(c);
                }
                else if (c == ':')
                {
                    if (state < 0 || builder.Length <= 2)
                    {
                        goto ReturnFalse;
                    }
                    state = 1;
                    break;
                } else if (c == '@')
                {
                    if (state > 0)
                    {
                        goto ReturnFalse;
                    }
                    state = -1;
                    break;
                }
                else
                {
                    goto ReturnFalse;
                }
            }

            // append ':' or '@'
            builder.Append(c);

            if (state < 0)
            {
                isEmail = true;

                // scan an email
                // [a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?
                // (?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$/
                bool hasMinus = false;
                int domainCharCount = 0;
                char pc = '\0';
                while (true)
                {
                    c = text.NextChar();
                    if (c == '>')
                    {
                        if (domainCharCount == 0 || hasMinus)
                        {
                            break;
                        }

                        text.SkipChar();
                        link = builder.ToString();
                        return true;
                    }

                    if (c.IsAlphaNumeric() || (domainCharCount > 0 && (hasMinus = c == '-')))
                    {
                        domainCharCount++;
                        if (domainCharCount > 63)
                        {
                            break;
                        }
                    }
                    else if (c == '.')
                    {
                        if (pc == '.' || pc == '-')
                        {
                            break;
                        }
                        domainCharCount = 0;
                        hasMinus = false;
                    }
                    else
                    {
                        break;
                    }
                    builder.Append(c);
                    pc = c;
                }
            }
            else
            {
                // scan an uri
                // An absolute URI, for these purposes, consists of a scheme followed by a colon (:)
                // followed by zero or more characters other than ASCII whitespace and control characters, <, and >.
                // If the URI includes these characters, they must be percent-encoded (e.g. %20 for a space).

                while (true)
                {
                    c = text.NextChar();
                    if (c == '\0')
                    {
                        break;
                    }

                    if (c == '>')
                    {
                        text.SkipChar();
                        link = builder.ToString();
                        return true;
                    }

                    // Chars valid for both scheme and email
                    if (c <= 127)
                    {
                        if (c > ' ' && c != '>')
                        {
                            builder.Append(c);
                        }
                        else break;
                    }
                    else if (!c.IsSpaceOrPunctuation())
                    {
                        builder.Append(c);
                    }
                    else break;
                }
            }

        ReturnFalse:
            builder.Dispose();
            return false;
        }

    }
    public struct StringSlice(string value)
    {
     private int position;
     public char CurrentChar => position < value.Length ? value[position] : '\0';
     public char NextChar() { position++; return CurrentChar; }
     public void SkipChar() { position++; }
    }
    public ref struct ValueStringBuilder
    {
     public const int StackallocThreshold=64;
     public int Length { get; private set; }
     public ValueStringBuilder(Span<char> buffer) { Length=0; }
     public void Append(char value) { Length++; }
     public override string ToString() => "captured";
     public void Dispose() { Length=0; }
    }
    public static class CharHelper
    {
     public static bool IsAlpha(this char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
     public static bool IsDigit(this char c) => c >= '0' && c <= '9';
     public static bool IsAlphaNumeric(this char c) => c.IsAlpha() || c.IsDigit();
     public static bool IsEmailUsernameSpecialChar(char c) => c == '.' || c == '+' || c == '-';
     public static bool IsSpaceOrPunctuation(this char c) => c == '\u2000';
    }
    """;
}
