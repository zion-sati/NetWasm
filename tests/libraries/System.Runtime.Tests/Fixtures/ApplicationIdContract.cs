// From the compiler harness's ApplicationIdContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
namespace ApplicationIdFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var token = new byte[] { 0, 15, 128, 255 };
        var version = new Version(1, 2, 3, 4);
        var id = new ApplicationId(token, "app", version, "wasm32", "neutral");
        token[0] = 42;
        if (id.PublicKeyToken[0] != 0 || id.Name != "app" || id.Version != version || id.Culture != "neutral" || id.ProcessorArchitecture != "wasm32") return 1;
        var copy = id.Copy();
        var exposed = copy.PublicKeyToken; exposed[1] = 99;
        if (copy.PublicKeyToken[1] != 15 || !id.Equals(copy) || id.GetHashCode() != copy.GetHashCode() || ReferenceEquals(id, copy)) return 2;
        if (id.Equals(null) || id.Equals("app") || id.Equals(new ApplicationId(new byte[] { 1 }, "app", version, "wasm32", "neutral"))) return 3;
        if (id.Equals(new ApplicationId(id.PublicKeyToken, "other", version, "wasm32", "neutral")) || id.Equals(new ApplicationId(id.PublicKeyToken, "app", new Version(2, 0), "wasm32", "neutral"))) return 4;
        if (id.Equals(new ApplicationId(id.PublicKeyToken, "app", version, null, "neutral")) || id.Equals(new ApplicationId(id.PublicKeyToken, "app", version, "wasm32", null))) return 5;
        if (id.ToString() != "app, culture=\"neutral\", version=\"1.2.3.4\", publicKeyToken=\"000F80FF\", processorArchitecture =\"wasm32\"") return 6;
        if (new ApplicationId(new byte[0], "minimal", new Version(1, 0), null, null).ToString() != "minimal, version=\"1.0\", publicKeyToken=\"\"") return 7;
        var longName = new string('x', 160);
        if (!new ApplicationId(id.PublicKeyToken, longName, version, null, null).ToString().StartsWith(longName)) return 8;
        var caught = 0;
        try { new ApplicationId(null, "app", version, null, null); } catch (ArgumentNullException e) { if (e.ParamName == "publicKeyToken") caught++; }
        try { new ApplicationId(token, null, version, null, null); } catch (ArgumentNullException e) { if (e.ParamName == "name") caught++; }
        try { new ApplicationId(token, "", version, null, null); } catch (ArgumentException e) { if (e.ParamName == "name") caught++; }
        try { new ApplicationId(token, "app", null, null, null); } catch (ArgumentNullException e) { if (e.ParamName == "version") caught++; }
        return caught == 4 ? input + 1 : 9;
    }
}
