// From the compiler harness's StringCultureComparisonCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
namespace CompareFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var strings=new string[]{null,"","a","A","a\0b","a\0c","Å","å","Σ","σ","😀"};
        for(var mode=0;mode<6;mode++)
        {
            var comparison=(StringComparison)mode;
            var ordinal=(mode==1 || mode==3 || mode==5)?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
            for(var i=0;i<strings.Length;i++) for(var j=0;j<strings.Length;j++)
            {
                var expected=Math.Sign(string.Compare(strings[i],strings[j],ordinal));
                if(Math.Sign(string.Compare(strings[i],strings[j],comparison))!=expected)return 1;
            }
            if(string.Compare("xabc",1,"yabc",1,99,comparison)!=0 || string.Compare("abc",3,"abc",3,99,comparison)!=0 || string.Compare(null,-1,null,-2,-3,comparison)!=0 || string.Compare(null,0,"x",0,1,comparison)>=0 || string.Compare("x",0,null,0,1,comparison)<=0)return 2;
            if(string.Compare("xabc",1,"yabd",1,99,comparison)>=0 || string.Compare("ab",1,"bc",1,0,comparison)!=0)return 3;
            var invalid=0;
            try{string.Compare("a",0,"b",0,-1,comparison);}catch(ArgumentOutOfRangeException e){if(e.ParamName=="length")invalid++;}
            try{string.Compare("a",-1,"b",0,1,comparison);}catch(ArgumentOutOfRangeException e){if(e.ParamName=="indexA")invalid++;}
            try{string.Compare("a",0,"b",-1,1,comparison);}catch(ArgumentOutOfRangeException e){if(e.ParamName=="indexB")invalid++;}
            try{string.Compare("a",2,"b",0,1,comparison);}catch(ArgumentOutOfRangeException e){if(e.ParamName=="indexA")invalid++;}
            try{string.Compare("a",0,"b",2,1,comparison);}catch(ArgumentOutOfRangeException e){if(e.ParamName=="indexB")invalid++;}
            if(invalid!=5)return 4;
        }
        var selectors=0;
        try{string.Compare(null,null,(StringComparison)(-1));}catch(ArgumentException e){if(e.ParamName=="comparisonType")selectors++;}
        try{string.Compare(null,0,null,0,0,(StringComparison)6);}catch(ArgumentException e){if(e.ParamName=="comparisonType")selectors++;}
        if(selectors!=2)return 5;
        if(string.Compare("a","A",true)!=0 || string.Compare("a","b")>=0 || string.Compare("xa",1,"yA",1,1,true)!=0 || string.Compare("xa",1,"yb",1,1)>=0)return 6;
        var cultureErrors=0;
        try{string.Compare(null,-1,null,-2,-3,true);}catch(ArgumentOutOfRangeException e){if(e.ParamName=="length1")cultureErrors++;}
        try{string.Compare(null,0,"x",0,1,true);}catch(ArgumentOutOfRangeException e){if(e.ParamName=="string1")cultureErrors++;}
        try{string.Compare("x",0,null,0,1,false);}catch(ArgumentOutOfRangeException e){if(e.ParamName=="string2")cultureErrors++;}
        try{string.Compare("a",2,"a",0,1);}catch(ArgumentOutOfRangeException e){if(e.ParamName=="length1")cultureErrors++;}
        if(cultureErrors!=4)return 7;
        var comparer=System.Globalization.CultureInfo.InvariantCulture.CompareInfo;
        if(comparer.Compare("xa",1,"ya",1)!=0 || comparer.Compare("xa",1,"yA",1,System.Globalization.CompareOptions.IgnoreCase)!=0 || comparer.Compare("xa",1,1,"ya",1,1)!=0 || comparer.Compare(null,0,0,"",0,0,System.Globalization.CompareOptions.None)>=0)return 8;
        var bounds=0;
        try{comparer.Compare("a",0,-1,"b",0,1,(System.Globalization.CompareOptions)(-1));}catch(ArgumentOutOfRangeException e){if(e.ParamName=="length1")bounds++;}
        try{comparer.Compare(null,0,0,null,0,0,(System.Globalization.CompareOptions)(-1));}catch(ArgumentException e){if(e.ParamName=="options")bounds++;}
        try{comparer.Compare("a",0,1,"b",2,1,System.Globalization.CompareOptions.None);}catch(ArgumentOutOfRangeException e){if(e.ParamName=="string2")bounds++;}
        if(bounds!=3)return 9;
        return 42;
    }
}
