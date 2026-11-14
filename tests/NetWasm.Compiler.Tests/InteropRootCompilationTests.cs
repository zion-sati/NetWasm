using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class InteropRootCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void HandlesWrappersAttributesAndExceptionsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("InteropRootFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "InteropRootFixture.EntryPoint",
            "Run", [new("run", "InteropRootFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    internal const string Source = """
        using System;
        using System.Runtime.InteropServices;
        using Microsoft.Win32.SafeHandles;
        [assembly: ComCompatibleVersion(1, 2, 3, 4), PrimaryInteropAssembly(1, 0), ImportedFromTypeLib("lib")]
        namespace InteropRootFixture;

        sealed class CountingHandle : CriticalHandleZeroOrMinusOneIsInvalid
        {
            public int Released;
            public CountingHandle(IntPtr value) => SetHandle(value);
            public IntPtr Value => handle;
            protected override bool ReleaseHandle() { Released++; return true; }
        }

        sealed class MinusOneHandle : CriticalHandleMinusOneIsInvalid
        {
            public MinusOneHandle(IntPtr value) => SetHandle(value);
            protected override bool ReleaseHandle() => true;
        }

        sealed class Adapter : ICustomAdapter, ICustomQueryInterface, ICustomMarshaler
        {
            public object GetUnderlyingObject() => "inner";
            public CustomQueryInterfaceResult GetInterface(ref Guid iid, out IntPtr ppv)
            {
                ppv = (IntPtr)7;
                return iid == Guid.Empty ? CustomQueryInterfaceResult.Handled : CustomQueryInterfaceResult.NotHandled;
            }
            public object MarshalNativeToManaged(IntPtr pNativeData) => (int)pNativeData;
            public IntPtr MarshalManagedToNative(object ManagedObj) => (IntPtr)(int)ManagedObj;
            public void CleanUpNativeData(IntPtr pNativeData) { }
            public void CleanUpManagedData(object ManagedObj) { }
            public int GetNativeDataSize() => 4;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int Callback(int value);

        [ComConversionLoss, TypeLibType(TypeLibTypeFlags.FDual)]
        public static class EntryPoint
        {
            static bool Throws<T>(Action action) where T : Exception
            {
                try { action(); return false; } catch (T) { return true; }
            }

            [ComRegisterFunction] static void Register(Type type) { }
            [ComUnregisterFunction] static void Unregister(Type type) { }

            public static int Run(int input)
            {
                // ArrayWithOffset sizes primitive arrays and validates the offset.
                var data = new[] { 1, 2, 3 };
                var a = new ArrayWithOffset(data, 4);
                if (a.GetArray() != data || a.GetOffset() != 4 || a.GetHashCode() != 12) return 1;
                if (a != new ArrayWithOffset(data, 4) || a == new ArrayWithOffset(data, 8) || !a.Equals((object)new ArrayWithOffset(data, 4))) return 2;
                if (new ArrayWithOffset(data, 12).GetHashCode() != 12 || new ArrayWithOffset(null, 0).GetArray() != null) return 3;
                if (!Throws<IndexOutOfRangeException>(() => new ArrayWithOffset(data, 13)) || !Throws<IndexOutOfRangeException>(() => new ArrayWithOffset(null, 1))) return 4;
                if (!Throws<ArgumentException>(() => new ArrayWithOffset(new string[1], 0)) || !Throws<ArgumentException>(() => new ArrayWithOffset("text", 0))) return 5;
                if (new ArrayWithOffset(new byte[5], 1).GetHashCode() != 5 || new ArrayWithOffset(new double[2], 0).GetHashCode() != 16) return 6;

                // CriticalHandle releases a valid handle once and never an invalid one.
                var valid = new CountingHandle((IntPtr)5);
                if (valid.IsInvalid || valid.IsClosed || valid.Value != (IntPtr)5) return 7;
                valid.Dispose();
                valid.Close();
                if (!valid.IsClosed || valid.Released != 1) return 8;
                var zero = new CountingHandle(IntPtr.Zero);
                var minusOne = new CountingHandle((IntPtr)(-1));
                if (!zero.IsInvalid || !minusOne.IsInvalid) return 9;
                zero.Close();
                minusOne.Dispose();
                if (!zero.IsClosed || zero.Released != 0 || minusOne.Released != 0) return 10;
                var abandoned = new CountingHandle((IntPtr)9);
                abandoned.SetHandleAsInvalid();
                abandoned.Dispose();
                if (!abandoned.IsClosed || abandoned.Released != 0) return 11;
                using (var other = new MinusOneHandle(IntPtr.Zero))
                {
                    if (other.IsInvalid) return 12;
                }
                using (var other = new MinusOneHandle((IntPtr)(-1)))
                {
                    if (!other.IsInvalid) return 13;
                }

                // HandleCollector counts, validates and collects past its threshold.
                var collector = new HandleCollector(null, 2);
                if (collector.Name != "" || collector.InitialThreshold != 2 || collector.MaximumThreshold != int.MaxValue || collector.Count != 0) return 14;
                for (int i = 0; i < 4; i++) collector.Add();
                if (collector.Count != 4) return 15;
                for (int i = 0; i < 4; i++) collector.Remove();
                if (collector.Count != 0 || !Throws<InvalidOperationException>(() => collector.Remove())) return 16;
                var bounded = new HandleCollector("files", 1, 3);
                if (bounded.Name != "files" || bounded.MaximumThreshold != 3) return 17;
                try { _ = new HandleCollector("x", -1); return 18; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "initialThreshold") return 18; }
                try { _ = new HandleCollector("x", 1, -1); return 19; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "maximumThreshold") return 19; }
                try { _ = new HandleCollector("x", 5, 4); return 20; } catch (ArgumentException e) { if (e.ParamName != "initialThreshold") return 20; }

                // HandleRef and the variant wrappers.
                var owner = new object();
                var reference = new HandleRef(owner, (IntPtr)5);
                if (reference.Wrapper != owner || reference.Handle != (IntPtr)5 || (IntPtr)reference != (IntPtr)5 || HandleRef.ToIntPtr(reference) != (IntPtr)5) return 21;
                if ((string)new BStrWrapper("b").WrappedObject != "b" || (string)new BStrWrapper((object)"c").WrappedObject != "c") return 22;
                if ((decimal)new CurrencyWrapper(1.5m).WrappedObject != 1.5m || (decimal)new CurrencyWrapper((object)2m).WrappedObject != 2m) return 23;
                try { _ = new CurrencyWrapper((object)1); return 24; } catch (ArgumentException e) { if (e.ParamName != "obj") return 24; }
                if (new ErrorWrapper(5).ErrorCode != 5 || new ErrorWrapper((object)6).ErrorCode != 6 || new ErrorWrapper(new InvalidOperationException()).ErrorCode != unchecked((int)0x80131509)) return 25;
                try { _ = new ErrorWrapper((object)"x"); return 26; } catch (ArgumentException e) { if (e.ParamName != "errorCode") return 26; }
                if (new UnknownWrapper(owner).WrappedObject != owner || new VariantWrapper(owner).WrappedObject != owner || new DispatchWrapper(null).WrappedObject != null) return 27;
                if (!Throws<PlatformNotSupportedException>(() => new DispatchWrapper(owner))) return 28;

                // OSPlatform names compare without case.
                if (OSPlatform.Linux.ToString() != "LINUX" || OSPlatform.OSX.ToString() != "OSX" || OSPlatform.Windows.ToString() != "WINDOWS" || OSPlatform.FreeBSD.ToString() != "FREEBSD") return 29;
                var wasi = OSPlatform.Create("wasi");
                if (wasi != OSPlatform.Create("WASI") || wasi.GetHashCode() != OSPlatform.Create("WASI").GetHashCode() || wasi == OSPlatform.Linux || wasi.Equals((object)"wasi")) return 30;
                if (!Throws<ArgumentNullException>(() => OSPlatform.Create(null!)) || !Throws<ArgumentException>(() => OSPlatform.Create(""))) return 31;

                // SEH and external exceptions keep their HRESULT in ToString.
                var seh = new SEHException();
                if (seh.HResult != unchecked((int)0x80004005) || seh.CanResume() || new SEHException("m", new Exception()).Message != "m") return 32;
                if (seh.ToString() != typeof(SEHException).FullName + " (0x80004005): " + seh.Message) return 33;
                if (new ExternalException("x", 5).ToString() != typeof(ExternalException).FullName + " (0x00000005): x") return 34;
                var inner = new InvalidOperationException("inner");
                var com = new COMException("outer", inner);
                if (com.ToString() != typeof(COMException).FullName + " (0x80004005): outer" + Environment.NewLine + " ---> " + inner.ToString()) return 35;
                if (new COMException(null, 1).ToString() != typeof(COMException).FullName + " (0x00000001): " + new COMException(null, 1).Message) return 36;

                // Interface contracts and the no-COM event helpers.
                var adapter = new Adapter();
                Guid iid = Guid.Empty;
                if ((string)((ICustomAdapter)adapter).GetUnderlyingObject() != "inner" || ((ICustomQueryInterface)adapter).GetInterface(ref iid, out IntPtr ppv) != CustomQueryInterfaceResult.Handled || ppv != (IntPtr)7) return 37;
                ICustomMarshaler marshaler = adapter;
                if ((int)marshaler.MarshalNativeToManaged((IntPtr)3) != 3 || marshaler.MarshalManagedToNative(4) != (IntPtr)4 || marshaler.GetNativeDataSize() != 4) return 38;
                if (!Throws<PlatformNotSupportedException>(() => ComEventsHelper.Combine(owner, Guid.Empty, 1, new Action(() => { }))) || !Throws<PlatformNotSupportedException>(() => ComEventsHelper.Remove(owner, Guid.Empty, 1, new Action(() => { })))) return 39;

                // Attribute and enumeration values.
                var version = new ComCompatibleVersionAttribute(1, 2, 3, 4);
                var function = new UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl) { CharSet = CharSet.Unicode, SetLastError = true, BestFitMapping = true, ThrowOnUnmappableChar = true };
                var stub = new ManagedToNativeComInteropStubAttribute(typeof(EntryPoint), "Run");
                if (version.MajorVersion + version.MinorVersion + version.BuildNumber + version.RevisionNumber != 10 || function.CallingConvention != CallingConvention.Cdecl || function.CharSet != CharSet.Unicode || !function.SetLastError || !function.BestFitMapping || !function.ThrowOnUnmappableChar || stub.ClassType != typeof(EntryPoint) || stub.MethodName != "Run") return 40;
                if (new TypeLibImportClassAttribute(typeof(EntryPoint)).Value != typeof(EntryPoint).ToString() || !new AutomationProxyAttribute(true).Value || new ComAliasNameAttribute("alias").Value != "alias" || new ImportedFromTypeLibAttribute("lib").Value != "lib") return 41;
                var assembly = new PrimaryInteropAssemblyAttribute(5, 6);
                var typeLibVersion = new TypeLibVersionAttribute(7, 8);
                if (assembly.MajorVersion != 5 || assembly.MinorVersion != 6 || typeLibVersion.MajorVersion != 7 || typeLibVersion.MinorVersion != 8) return 43;
                if (new TypeLibFuncAttribute(TypeLibFuncFlags.FHidden).Value != TypeLibFuncFlags.FHidden || new TypeLibFuncAttribute((short)1).Value != TypeLibFuncFlags.FRestricted) return 44;
                if (new TypeLibTypeAttribute((short)0x40).Value != TypeLibTypeFlags.FDual || new TypeLibVarAttribute(TypeLibVarFlags.FReadOnly).Value != TypeLibVarFlags.FReadOnly) return 45;
                if ((int)ComMemberType.PropSet != 2 || (int)CustomQueryInterfaceResult.Failed != 2 || (int)TypeLibFuncFlags.FImmediateBind != 0x1000 || (int)TypeLibTypeFlags.FAggregatable != 0x400 || (int)TypeLibVarFlags.FDefaultBind != 0x20) return 46;
                if (new ComSourceInterfacesAttribute(typeof(IDisposable), typeof(ICustomAdapter)).Value != typeof(IDisposable).FullName + "\0" + typeof(ICustomAdapter).FullName) return 50;
                if (new ComSourceInterfacesAttribute(typeof(System.Collections.Generic.IList<int>)).Value != typeof(System.Collections.Generic.IList<int>).FullName || !Throws<NullReferenceException>(() => new ComSourceInterfacesAttribute((Type)null!))) return 51;
                Callback callback = value => value + 1;
                if (callback(1) != 2) return 47;
                Register(typeof(int));
                Unregister(typeof(int));
        #if !DESKTOP_ORACLE
                // NetWasm boundaries: only primitive arrays have a known element size, and COM-aware
                // event lookup needs reflection.
                if (!Throws<ArgumentException>(() => new ArrayWithOffset(new Guid[1], 0))) return 48;
                if (!Throws<PlatformNotSupportedException>(() => new ComAwareEventInfo(typeof(EntryPoint), "Changed"))) return 49;
        #endif
                return input + 1;
            }
        }
        """;
}
