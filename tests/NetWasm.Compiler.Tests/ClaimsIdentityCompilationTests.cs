using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ClaimsIdentityCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ClaimsIdentitiesPrincipalsAndBinaryFormatsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ClaimsFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ClaimsFixture.EntryPoint",
            "Run", [new("run", "ClaimsFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    internal const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.IO;
        using System.Security.Claims;
        using System.Security.Principal;
        using System.Text;
        using System.Threading;
        namespace ClaimsFixture;

        sealed class Identity : IIdentity
        {
            public Identity(string? name, string? type) { Name = name; AuthenticationType = type; }
            public string? AuthenticationType { get; }
            public bool IsAuthenticated => true;
            public string? Name { get; }
        }

        sealed class Principal : IPrincipal
        {
            public Principal(IIdentity? identity) => Identity = identity;
            public IIdentity? Identity { get; }
            public bool IsInRole(string role) => false;
        }

        class TaggedClaim : Claim
        {
            public TaggedClaim(BinaryReader reader, ClaimsIdentity? subject) : base(reader, subject) { }
            public TaggedClaim(string type, string value) : base(type, value) { }
            protected TaggedClaim(TaggedClaim other, ClaimsIdentity? subject) : base(other, subject) { }
            public override Claim Clone(ClaimsIdentity? identity) => new TaggedClaim(this, identity);
            public byte[]? Custom => CustomSerializationData;
            public override void WriteTo(BinaryWriter writer) => WriteTo(writer, new byte[] { 7, 8, 9 });
        }

        class TaggedIdentity : ClaimsIdentity
        {
            public int Created;
            public TaggedIdentity(string type) : base(type) { }
            public TaggedIdentity(BinaryReader reader) : base(reader) { }
            protected TaggedIdentity(TaggedIdentity other) : base(other) { }
            public override ClaimsIdentity Clone() => new TaggedIdentity(this);
            protected override Claim CreateClaim(BinaryReader reader) { Created++; return new TaggedClaim(reader, this); }
            public byte[]? Custom => CustomSerializationData;
            public override void WriteTo(BinaryWriter writer) => WriteTo(writer, new byte[] { 1, 2 });
        }

        class TaggedPrincipal : ClaimsPrincipal
        {
            public TaggedPrincipal(IEnumerable<ClaimsIdentity> identities) : base(identities) { }
            public TaggedPrincipal(BinaryReader reader) : base(reader) { }
            protected override ClaimsIdentity CreateClaimsIdentity(BinaryReader reader) => new TaggedIdentity(reader);
            public byte[]? Custom => CustomSerializationData;
            public override void WriteTo(BinaryWriter writer) => WriteTo(writer, new byte[] { 5 });
        }

        sealed class Failing : IEnumerable<Claim>
        {
            public IEnumerator<Claim> GetEnumerator()
            {
                yield return new Claim("first", "1");
                throw new InvalidOperationException("enumeration");
            }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                int code;
                if ((code = ClaimDefaults()) != 0) return code;
                if ((code = ClaimBinary()) != 0) return 100 + code;
                if ((code = IdentityState()) != 0) return 200 + code;
                if ((code = IdentityQueries()) != 0) return 300 + code;
                if ((code = IdentityBinary()) != 0) return 400 + code;
                if ((code = Principals()) != 0) return 500 + code;
                if ((code = PrincipalBinary()) != 0) return 600 + code;
                if ((code = Generic()) != 0) return 700 + code;
                if ((code = CurrentPrincipal()) != 0) return 800 + code;
                if ((code = Schema()) != 0) return 900 + code;
                return input + 1;
            }

            static uint Fnv(byte[] bytes)
            {
                uint hash = 2166136261;
                foreach (byte b in bytes) hash = (hash ^ b) * 16777619;
                return hash;
            }

            static bool Matches(byte[] bytes, uint hash, int length) => Fnv(bytes) == hash && bytes.Length == length;

            static byte[] Write(Action<BinaryWriter> write)
            {
                var stream = new MemoryStream();
                var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
                write(writer);
                return stream.ToArray();
            }

            static int ClaimDefaults()
            {
                var claim = new Claim("t", "");
                if (claim.ValueType != ClaimValueTypes.String || claim.Issuer != ClaimsIdentity.DefaultIssuer || claim.OriginalIssuer != ClaimsIdentity.DefaultIssuer) return 1;
                if (claim.Subject != null || claim.Value != "" || claim.ToString() != "t: ") return 2;
                var issued = new Claim("t", "v", "", "iss", null);
                if (issued.ValueType != ClaimValueTypes.String || issued.Issuer != "iss" || issued.OriginalIssuer != "iss") return 3;
                try { _ = new Claim(null!, "v"); return 4; } catch (ArgumentNullException e) { if (e.ParamName != "type") return 5; }
                try { _ = new Claim("t", null!); return 6; } catch (ArgumentNullException e) { if (e.ParamName != "value") return 7; }
                var owner = new ClaimsIdentity();
                claim.Properties["k"] = "v";
                var copy = claim.Clone(owner);
                if (!ReferenceEquals(copy.Subject, owner) || claim.Subject != null) return 8;
                copy.Properties["k"] = "changed";
                if (claim.Properties["k"] != "v") return 9;
                var plain = claim.Clone();
                if (plain.Subject != null || plain.Properties.Count != 1) return 10;
                try { claim.Properties.Add("k", "again"); return 11; } catch (ArgumentException) { }
                return 0;
            }

            static int ClaimBinary()
            {
                var claim = new Claim(ClaimTypes.Name, "alice", ClaimValueTypes.Email, "iss", "orig");
                claim.Properties["p1"] = "v1";
                claim.Properties["p2"] = "";
                var bytes = Write(claim.WriteTo);
                if (!Matches(bytes, 0x93E41958u, 104)) return 1;
                var read = new Claim(new BinaryReader(new MemoryStream(bytes)));
                if (read.Type != ClaimTypes.Name || read.Value != "alice" || read.ValueType != ClaimValueTypes.Email || read.Issuer != "iss" || read.OriginalIssuer != "orig") return 2;
                if (read.Properties.Count != 2 || read.Properties["p2"] != "" || read.Subject != null) return 3;
                var role = Write(new Claim(ClaimTypes.Role, "admin", null, "iss").WriteTo);
                if (!Matches(role, 0x6DDD87CDu, 18)) return 4;
                var roleRead = new Claim(new BinaryReader(new MemoryStream(role)));
                if (roleRead.Type != ClaimTypes.Role || roleRead.OriginalIssuer != "iss") return 5;

                // Extra trailing fields a newer writer adds are skipped.
                var extra = Write(w => { w.Write(0); w.Write(4); w.Write("v"); w.Write("t"); w.Write("x1"); w.Write("x2"); w.Write("next"); });
                var reader = new BinaryReader(new MemoryStream(extra));
                var skipped = new Claim(reader);
                if (skipped.Type != "t" || skipped.Value != "v" || reader.ReadString() != "next") return 6;
                try { _ = new Claim(new BinaryReader(new MemoryStream(bytes, 0, 20))); return 7; } catch (EndOfStreamException) { }
                var duplicate = Write(w => { w.Write(64); w.Write(2); w.Write("v"); w.Write("t"); w.Write(2); w.Write("k"); w.Write("a"); w.Write("k"); w.Write("b"); });
                try { _ = new Claim(new BinaryReader(new MemoryStream(duplicate))); return 8; } catch (ArgumentException) { }

                var tagged = new TaggedClaim("t", "v");
                var taggedBytes = Write(tagged.WriteTo);
                var taggedRead = new TaggedClaim(new BinaryReader(new MemoryStream(taggedBytes)), null);
                if (taggedRead.Custom is not { Length: 3 } custom || custom[0] != 7 || custom[2] != 9) return 9;
                var owner = new ClaimsIdentity();
                var clone = (TaggedClaim)taggedRead.Clone(owner);
                if (ReferenceEquals(clone.Custom, taggedRead.Custom) || clone.Custom![1] != 8 || !ReferenceEquals(clone.Subject, owner)) return 10;
                // A plain claim read from tagged bytes does not write the custom bytes back.
                var rewritten = Write(new Claim(new BinaryReader(new MemoryStream(taggedBytes))).WriteTo);
                if (rewritten.Length != taggedBytes.Length - 7) return 12;

                // WriteTo flushes and leaves the caller's stream open.
                var stream = new MemoryStream();
                var writer = new BinaryWriter(stream);
                claim.WriteTo(writer);
                if (stream.Length != bytes.Length || !stream.CanWrite) return 11;
                return 0;
            }

            static int IdentityState()
            {
                var source = new ClaimsIdentity(new[] { new Claim("n1", "bob"), new Claim("r1", "admin") }, "auth", "n1", "r1") { Label = "label", BootstrapContext = new object() };
                var actor = new ClaimsIdentity("actor");
                source.Actor = actor;
                var wrapped = new ClaimsIdentity(source, null, null, "n2", null);
                if (wrapped.AuthenticationType != "auth" || wrapped.NameClaimType != "n2" || wrapped.RoleClaimType != "r1") return 1;
                if (wrapped.Label != "label" || !ReferenceEquals(wrapped.BootstrapContext, source.BootstrapContext) || !ReferenceEquals(wrapped.Actor, actor)) return 2;
                if (wrapped.Name != null || !wrapped.HasClaim("N1", "bob")) return 3;
                var overridden = new ClaimsIdentity(source, null, "other", null, null);
                if (overridden.AuthenticationType != "other" || overridden.NameClaimType != "n1" || overridden.Name != "bob") return 4;
                foreach (var claim in wrapped.Claims) if (!ReferenceEquals(claim.Subject, wrapped)) return 5;

                var clone = source.Clone();
                if (ReferenceEquals(clone.Actor, actor) || clone.Actor!.AuthenticationType != "actor" || clone.Label != "label") return 6;
                if (!ReferenceEquals(clone.BootstrapContext, source.BootstrapContext)) return 7;

                var fromIdentity = new ClaimsIdentity(new Identity("carol", "basic"));
                if (fromIdentity.Name != "carol" || fromIdentity.AuthenticationType != "basic" || !fromIdentity.IsAuthenticated) return 8;
                var empty = new ClaimsIdentity(new Identity("", null));
                if (empty.IsAuthenticated || empty.Name != null || empty.Claims.GetEnumerator().MoveNext()) return 9;
                if (new ClaimsIdentity("").IsAuthenticated || !new ClaimsIdentity("x").IsAuthenticated) return 10;

                try { actor.Actor = actor; return 11; } catch (InvalidOperationException) { }
                var child = new ClaimsIdentity("child");
                actor.Actor = child;
                try { child.Actor = source; return 12; } catch (InvalidOperationException) { }
                try { _ = new ClaimsIdentity(child) { Actor = child }; } catch (InvalidOperationException) { return 13; }
                return 0;
            }

            static int IdentityQueries()
            {
                var identity = new ClaimsIdentity("auth");
                var owned = new Claim("type", "Value", null, null, null, identity);
                identity.AddClaim(owned);
                if (!ReferenceEquals(identity.FindFirst("TYPE"), owned)) return 1;
                var foreign = new Claim("type", "second");
                identity.AddClaim(foreign);
                var added = identity.FindAll("type");
                var count = 0;
                Claim? last = null;
                foreach (var claim in added) { count++; last = claim; }
                if (count != 2 || ReferenceEquals(last, foreign) || !ReferenceEquals(last!.Subject, identity) || foreign.Subject != null) return 2;
                if (!identity.HasClaim("Type", "Value") || identity.HasClaim("type", "value")) return 3;
                if (!identity.HasClaim(c => c.Value == "second") || identity.FindFirst(c => c.Value == "none") != null) return 4;
                try { identity.AddClaims(null!); return 5; } catch (ArgumentNullException) { }
                identity.AddClaims(new Claim?[] { null, new Claim("other", "x") });
                if (identity.FindFirst("other") == null) return 6;
                try { identity.AddClaims(new Failing()); return 7; } catch (InvalidOperationException) { }
                if (identity.FindFirst("first") == null) return 8;
                if (identity.TryRemoveClaim(foreign) || identity.TryRemoveClaim(null)) return 9;
                if (!identity.TryRemoveClaim(owned) || identity.HasClaim("type", "Value")) return 10;
                try { identity.RemoveClaim(owned); return 11; } catch (InvalidOperationException) { }
                try { identity.FindAll((string)null!); return 12; } catch (ArgumentNullException) { }
                try { identity.FindFirst((Predicate<Claim>)null!); return 13; } catch (ArgumentNullException) { }
                var visited = new List<string>();
                identity.FindFirst(c => { visited.Add(c.Type); return false; });
                if (string.Join(",", visited) != "type,other,first") return 14;
                return 0;
            }

            static int IdentityBinary()
            {
                var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "dave"), new Claim("custom", "1", ClaimValueTypes.Integer) }, "auth", null, "roles") { Label = "lbl", BootstrapContext = "boot" };
                identity.Actor = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "r") });
                var bytes = Write(identity.WriteTo);
                if (!Matches(bytes, 0xC0E5C4ECu, 125)) return 1;
                var read = new ClaimsIdentity(new BinaryReader(new MemoryStream(bytes)));
                if (read.AuthenticationType != "auth" || read.Label != "lbl" || (string?)read.BootstrapContext != "boot" || read.RoleClaimType != "roles" || read.NameClaimType != ClaimsIdentity.DefaultNameClaimType) return 2;
                if (read.Name != "dave" || read.Actor?.FindFirst(ClaimTypes.Role)?.Value != "r") return 3;
                foreach (var claim in read.Claims) if (!ReferenceEquals(claim.Subject, read)) return 4;
                var objectBoot = new ClaimsIdentity("a") { BootstrapContext = new object() };
                var withoutBoot = new ClaimsIdentity(new BinaryReader(new MemoryStream(Write(objectBoot.WriteTo))));
                if (withoutBoot.BootstrapContext != null || withoutBoot.AuthenticationType != "a") return 5;

                var tagged = new TaggedIdentity("tag");
                tagged.AddClaim(new TaggedClaim("t", "v"));
                var taggedBytes = Write(tagged.WriteTo);
                var taggedRead = new TaggedIdentity(new BinaryReader(new MemoryStream(taggedBytes)));
                if (taggedRead.Created != 1 || taggedRead.Custom is not { Length: 2 } || taggedRead.FindFirst("t") is not TaggedClaim { Custom.Length: 3 }) return 6;
                var taggedClone = (TaggedIdentity)taggedRead.Clone();
                if (ReferenceEquals(taggedClone.Custom, taggedRead.Custom) || taggedClone.Custom![1] != 2) return 7;
                try { _ = new ClaimsIdentity((BinaryReader)null!); return 8; } catch (ArgumentNullException) { }
                try { _ = new ClaimsIdentity(new BinaryReader(new MemoryStream(bytes, 0, 40))); return 9; } catch (EndOfStreamException) { }
                return 0;
            }

            static int Principals()
            {
                var first = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "admin"), new Claim("k", "v") }, "a");
                var second = new ClaimsIdentity(new[] { new Claim("role2", "Ops") }, "b", null, "role2");
                var principal = new ClaimsPrincipal(new ClaimsIdentity?[] { null, first, second }!);
                if (!ReferenceEquals(principal.Identity, first)) return 1;
                if (!principal.IsInRole("admin") || principal.IsInRole("ADMIN") || !principal.IsInRole("Ops") || principal.IsInRole("v")) return 2;
                var clone = principal.Clone();
                var enumerator = clone.Identities.GetEnumerator();
                enumerator.MoveNext(); enumerator.MoveNext();
                if (!ReferenceEquals(enumerator.Current, first)) return 3;
                try { principal.AddIdentity(null!); return 4; } catch (ArgumentNullException) { }
                try { principal.AddIdentities(null!); return 5; } catch (ArgumentNullException) { }
                principal.AddIdentities(new ClaimsIdentity?[] { null }!);
                var count = 0;
                foreach (var _ in principal.Identities) count++;
                if (count != 4) return 6;
                if (principal.FindFirst("K")?.Value != "v" || !principal.HasClaim("k", "v") || principal.HasClaim("k", "V")) return 7;
                try { foreach (var _ in principal.Claims) { } return 8; } catch (NullReferenceException) { }
                count = 0;
                foreach (var _ in new ClaimsPrincipal(new[] { first, second }).Claims) count++;
                if (count != 3) return 15;

                var selector = ClaimsPrincipal.PrimaryIdentitySelector;
                ClaimsPrincipal.PrimaryIdentitySelector = identities => second;
                var selected = ReferenceEquals(principal.Identity, second);
                ClaimsPrincipal.PrimaryIdentitySelector = null!;
                var restored = ReferenceEquals(principal.Identity, first);
                ClaimsPrincipal.PrimaryIdentitySelector = selector;
                if (!selected || !restored) return 9;

                var wrapped = new ClaimsPrincipal(new Identity("erin", "basic"));
                if (wrapped.Identity is not ClaimsIdentity { Name: "erin" }) return 10;
                var fromPrincipal = new ClaimsPrincipal(new Principal(new Identity("fred", null)));
                if (fromPrincipal.Identity?.Name != "fred") return 11;
                var shared = new ClaimsPrincipal(principal);
                enumerator = shared.Identities.GetEnumerator(); enumerator.MoveNext(); enumerator.MoveNext();
                if (!ReferenceEquals(enumerator.Current, first)) return 12;
                try { _ = new ClaimsPrincipal((IIdentity)null!); return 13; } catch (ArgumentNullException) { }
                if (new ClaimsPrincipal().Identity != null) return 14;
                return 0;
            }

            static int PrincipalBinary()
            {
                var principal = new ClaimsPrincipal(new[] { new ClaimsIdentity(new[] { new Claim("a", "1") }, "x"), new ClaimsIdentity("y") });
                var bytes = Write(principal.WriteTo);
                if (!Matches(bytes, 0x429EEEC0u, 48)) return 1;
                var read = new ClaimsPrincipal(new BinaryReader(new MemoryStream(bytes)));
                if (read.FindFirst("a")?.Value != "1" || read.Identity?.AuthenticationType != "x") return 2;
                var tagged = new TaggedPrincipal(new[] { new ClaimsIdentity(new[] { new Claim("b", "2") }) });
                var taggedRead = new TaggedPrincipal(new BinaryReader(new MemoryStream(Write(tagged.WriteTo))));
                if (taggedRead.Custom is not { Length: 1 } || taggedRead.Identity is not TaggedIdentity { Created: 1 }) return 3;
                if (!Matches(Write(new ClaimsPrincipal().WriteTo), 0x9BE17165u, 8)) return 4;
                return 0;
            }

            static int Generic()
            {
                var identity = new GenericIdentity("", "");
                if (identity.IsAuthenticated || identity.Name != "" || identity.AuthenticationType != "") return 1;
                if (identity.FindFirst(ClaimTypes.Name)?.Value != "") return 2;
                var named = new GenericIdentity("gina");
                if (!named.IsAuthenticated || named.AuthenticationType != "" || named.Clone() is not GenericIdentity { Name: "gina" }) return 3;
                try { _ = new GenericIdentity(null!); return 4; } catch (ArgumentNullException) { }
                try { _ = new GenericIdentity("a", null!); return 5; } catch (ArgumentNullException) { }

                var roles = new[] { "Admin", " ", null!, "user" };
                var principal = new GenericPrincipal(named, roles);
                roles[0] = "changed";
                if (!ReferenceEquals(principal.Identity, named) || !principal.IsInRole("admin") || principal.IsInRole("changed") || principal.IsInRole(null!)) return 6;
                ClaimsIdentity? inner = null;
                foreach (var candidate in principal.Identities) inner = candidate;
                if (ReferenceEquals(inner, named) || inner is not GenericIdentity) return 7;
                var roleClaims = 0;
                foreach (var claim in inner!.Claims) if (claim.Type == ClaimTypes.Role) { roleClaims++; if (!ReferenceEquals(claim.Subject, inner)) return 8; }
                if (roleClaims != 2) return 9;
                foreach (var claim in inner.Clone().Claims) if (claim.Type == ClaimTypes.Role) return 10;
                var roleClaim = inner.FindFirst(ClaimTypes.Role)!;
                if (inner.TryRemoveClaim(roleClaim)) return 11;
                try { inner.RemoveClaim(roleClaim); return 12; } catch (InvalidOperationException) { }

                var withRole = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "owner") }, "a");
                if (new GenericPrincipal(withRole, null).IsInRole("owner")) return 13;
                if (!new GenericPrincipal(withRole, new string[0]).IsInRole("owner")) return 14;
                if (!new GenericPrincipal(withRole, new[] { " " }).IsInRole(" ")) return 15;
                try { _ = new GenericPrincipal(null!, null); return 16; } catch (ArgumentNullException) { }
                return 0;
            }

            static int CurrentPrincipal()
            {
                var domain = AppDomain.CurrentDomain;
                Thread.CurrentPrincipal = null;
                if (ClaimsPrincipal.Current != null) return 1;
                var claims = new ClaimsPrincipal(new ClaimsIdentity("x"));
                Thread.CurrentPrincipal = claims;
                if (!ReferenceEquals(ClaimsPrincipal.Current, claims)) return 2;
                Thread.CurrentPrincipal = new Principal(new Identity("hank", "t"));
                if (ClaimsPrincipal.Current?.Identity?.Name != "hank") return 3;
                var selected = new ClaimsPrincipal();
                ClaimsPrincipal.ClaimsPrincipalSelector = () => selected;
                var viaSelector = ReferenceEquals(ClaimsPrincipal.Current, selected);
                ClaimsPrincipal.ClaimsPrincipalSelector = null;
                if (!viaSelector || ClaimsPrincipal.Current?.Identity?.Name != "hank") return 4;

                Thread.CurrentPrincipal = null;
                domain.SetPrincipalPolicy(PrincipalPolicy.UnauthenticatedPrincipal);
                var unauthenticated = Thread.CurrentPrincipal;
                domain.SetPrincipalPolicy(PrincipalPolicy.NoPrincipal);
                if (unauthenticated is not GenericPrincipal generic || generic.Identity is not GenericIdentity { Name: "", AuthenticationType: "", IsAuthenticated: false }) return 5;
                if (!generic.IsInRole("") || generic.IsInRole("x") || !ReferenceEquals(Thread.CurrentPrincipal, generic)) return 6;
                if (!ReferenceEquals(ClaimsPrincipal.Current, generic)) return 7;
                Thread.CurrentPrincipal = null;
                return 0;
            }

            static int Schema()
            {
                if (!Is(ClaimTypes.Name, "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")) return 1;
                if (!Is(ClaimTypes.Role, "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")) return 2;
                if (!Is(ClaimTypes.Actor, "http://schemas.xmlsoap.org/ws/2009/09/identity/claims/actor")) return 3;
                if (!Is(ClaimValueTypes.String, "http://www.w3.org/2001/XMLSchema#string") || !Is(ClaimValueTypes.RsaKeyValue, "http://www.w3.org/2000/09/xmldsig#RSAKeyValue")) return 4;
                if (!Is(ClaimValueTypes.Rsa, "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/rsa") || !Is(ClaimValueTypes.Sid, "http://www.w3.org/2001/XMLSchema#sid")) return 5;
                if (!Is(ClaimTypes.Sid, "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/sid") || !Is(ClaimTypes.PrimarySid, "http://schemas.microsoft.com/ws/2008/06/identity/claims/primarysid")) return 6;
                if (!Is(ClaimsIdentity.DefaultIssuer, "LOCAL AUTHORITY") || !Is(ClaimsIdentity.DefaultNameClaimType, ClaimTypes.Name) || !Is(ClaimsIdentity.DefaultRoleClaimType, ClaimTypes.Role)) return 7;
                return 0;
            }

            static bool Is(string actual, string expected) => string.Equals(actual, expected);
        }
        """;
}
