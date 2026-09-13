// Contract adapted from dotnet/runtime System.Private.CoreLib.
// Licensed under the MIT license; see the upstream repository for the
// complete copyright notice.
namespace System
{
    public delegate TOutput Converter<in TInput, out TOutput>(TInput input)
        where TInput : allows ref struct
        where TOutput : allows ref struct;
}
