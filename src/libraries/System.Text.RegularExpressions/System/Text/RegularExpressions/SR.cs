// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Text.RegularExpressions
{
    // Invariant resource projection from the pinned System.Text.RegularExpressions resource set.
    // NetWasm does not deploy satellite resource assemblies.
    internal static class SR
    {
        internal const string AlternationHasNamedCapture = "Alternation conditions do not capture and cannot be named.";
        internal const string AlternationHasComment = "Alternation conditions cannot be comments.";
        internal const string Arg_ArrayPlusOffTooSmall = "Destination array is not long enough to copy all the items in the collection. Check array index and length.";
        internal const string ShorthandClassInCharacterRange = "Cannot include class \\{0} in character range.";
        internal const string BeginIndexNotNegative = "Start index cannot be less than 0 or greater than input length.";
        internal const string QuantifierOrCaptureGroupOutOfRange = "Quantifier and capture group numbers must be less than or equal to Int32.MaxValue.";
        internal const string CaptureGroupOfZero = "Capture number cannot be zero.";
        internal const string CountTooSmall = "Count cannot be less than -1.";
        internal const string EnumNotStarted = "Enumeration has either not started or has already finished.";
        internal const string AlternationHasMalformedCondition = "Illegal conditional (?(...)) expression.";
        internal const string IllegalDefaultRegexMatchTimeoutInAppDomain = "AppDomain data '{0}' contains the invalid value or object '{1}' for specifying a default matching timeout for System.Text.RegularExpressions.Regex.";
        internal const string UnescapedEndingBackslash = "Illegal \\ at end of pattern.";
        internal const string ReversedQuantifierRange = "Illegal {x,y} with x > y.";
        internal const string InvalidUnicodePropertyEscape = "Incomplete \\p{X} character escape.";
        internal const string CaptureGroupNameInvalid = "Invalid group name: Group names must begin with a word character.";
        internal const string LengthNotNegative = "Length cannot be less than 0 or exceed input length.";
        internal const string MalformedNamedReference = "Malformed \\k<...> named back reference.";
        internal const string AlternationHasMalformedReference = "Conditional alternation is missing a closing parenthesis after the group number {0}.";
        internal const string MalformedUnicodePropertyEscape = "Malformed \\p{X} character escape.";
        internal const string MakeException = "Invalid pattern '{0}' at offset {1}. {2}";
        internal const string MissingControlCharacter = "Missing control character.";
        internal const string NestedQuantifiersNotParenthesized = "Nested quantifier '{0}'.";
        internal const string NoResultOnFailed = "Result cannot be called on a failed Match.";
        internal const string NotSupported_NonBacktrackingConflictingExpression = "RegexOptions.NonBacktracking is not supported in conjunction with expressions containing: '{0}'.";
        internal const string NotSupported_NonBacktrackingUnsafeSize = "The specified pattern with RegexOptions.NonBacktracking could result in an automata as large as '{0}' nodes, which is larger than the configured limit of '{1}'.";
        internal const string InsufficientClosingParentheses = "Not enough )'s.";
        internal const string NotSupported_ReadOnlyCollection = "Collection is read-only.";
        internal const string QuantifierAfterNothing = "Quantifier '{0}' following nothing.";
        internal const string RegexMatchTimeoutException_Occurred = "The Regex engine has timed out while trying to match a pattern to an input string. This can occur for many reasons, including very large inputs or excessive backtracking caused by nested quantifiers, back-references and other factors.";
        internal const string ReversedCharacterRange = "[x-y] range in reverse order.";
        internal const string ExclusionGroupNotLast = "A subtraction must be the last element in a character class.";
        internal const string InsufficientOrInvalidHexDigits = "Insufficient or invalid hexadecimal digits.";
        internal const string AlternationHasTooManyConditions = "Too many | in (?()|).";
        internal const string InsufficientOpeningParentheses = "Too many )'s.";
        internal const string UndefinedNumberedReference = "Reference to undefined group number {0}.";
        internal const string UndefinedNamedReference = "Reference to undefined group name '{0}'.";
        internal const string AlternationHasUndefinedReference = "Conditional alternation refers to an undefined group number {0}.";
        internal const string UnrecognizedUnicodeProperty = "Unknown property '{0}'.";
        internal const string UnrecognizedControlCharacter = "Unrecognized control character.";
        internal const string UnrecognizedEscape = "Unrecognized escape sequence \\{0}.";
        internal const string InvalidGroupingConstruct = "Unrecognized grouping construct.";
        internal const string UnterminatedBracket = "Unterminated [] set.";
        internal const string UnterminatedComment = "Unterminated (?#...) comment.";
        internal const string UsingSpanAPIsWithCompiledToAssembly = "Searching an input span using a pre-compiled Regex assembly is not supported. Please use the string overloads or use a newer Regex implementation.";
        internal const string ExpressionDescription_AtomicSubexpressions = "atomic subexpressions (?> pattern)";
        internal const string ExpressionDescription_Backreference = "backreference (\\ number)";
        internal const string ExpressionDescription_BalancingGroup = "balancing group (?<name1-name2>subexpression) or (?'name1-name2' subexpression)";
        internal const string ExpressionDescription_Conditional = "captured group conditional (?( name ) yes-pattern | no-pattern ) or (?( number ) yes-pattern| no-pattern )";
        internal const string ExpressionDescription_ContiguousMatches = "contiguous matches (\\G)";
        internal const string ExpressionDescription_IfThenElse = "test conditional (?( test-pattern ) yes-pattern | no-pattern )";
        internal const string ExpressionDescription_NegativeLookaround = "negative lookahead (?! pattern) or negative lookbehind (?<! pattern)";
        internal const string ExpressionDescription_PositiveLookaround = "positive lookahead (?= pattern) or positive lookbehind (?<= pattern)";

        internal static string Format(string format, object? arg0) => string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) => string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);
    }
}
