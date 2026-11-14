// From the compiler harness's ContextStackCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel.Design.Serialization;
namespace StackFixture;
interface IMarker {}
class Base {}
sealed class Derived : Base, IMarker {}
public static class EntryPoint
{
    public static int Run(int input)
    {
        var stack = new ContextStack();
        if(stack.Current!=null || stack.Pop()!=null || stack[0]!=null || stack[typeof(object)]!=null)return 1;
        try{var ignored=stack[-1];return 2;}catch(ArgumentOutOfRangeException e){if(e.ParamName!="level")return 3;}
        try{var ignored=stack[(Type)null];return 4;}catch(ArgumentNullException e){if(e.ParamName!="type")return 5;}
        try{stack.Push(null);return 6;}catch(ArgumentNullException e){if(e.ParamName!="context")return 7;}
        try{stack.Append(null);return 8;}catch(ArgumentNullException e){if(e.ParamName!="context")return 9;}
        var first=new Base();var second=new Derived();var bottom=new object();
        stack.Push(first);stack.Push(second);stack.Append(bottom);
        if(stack.Current!=second || stack[0]!=second || stack[1]!=first || stack[2]!=bottom || stack[3]!=null)return 10;
        if(stack[typeof(Base)]!=second || stack[typeof(IMarker)]!=second || stack[typeof(string)]!=null || stack[typeof(object)]!=second)return 11;
        if(stack.Pop()!=second || stack[typeof(Base)]!=first || stack[typeof(IMarker)]!=null)return 12;
        stack.Push(42);if((int)stack[typeof(int)]!=42 || (int)stack.Pop()!=42)return 13;
        if(stack.Pop()!=first || stack.Pop()!=bottom || stack.Pop()!=null || stack.Current!=null)return 14;
        stack.Append(first);stack.Append(second);if(stack.Pop()!=first || stack.Pop()!=second)return 15;
        for(var i=0;i<32;i++)stack.Push(i);
        for(var i=31;i>=0;i--)if((int)stack.Pop()!=i)return 16;
        if(stack.Current!=null || stack[typeof(object)]!=null)return 17;
        return 42;
    }
}
