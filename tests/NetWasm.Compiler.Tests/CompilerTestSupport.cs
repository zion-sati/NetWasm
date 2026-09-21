using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.TestInfrastructure;

using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Interop;

namespace NetWasm.Compiler.Tests;

internal static class CompilerTestSupport
{
    internal static CompilerOptions CreateReactorOptions(
        TestAssets assets,
        string assembly,
        string entryType,
        string entryMethod,
        WasmTarget target = WasmTarget.Wasm32) => new(
            assembly,
            [assets.CoreLib],
            entryType,
            entryMethod,
            [new RequestedExport("run", entryType, entryMethod)],
            Target: target,
            WitPath: Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform@1.0.0/async-platform");

    internal static IManagedLayoutCompiler CreateManagedLayoutCompiler(
        IManagedTypeLayoutCompilerFactory types,
        IManagedStaticDataBuilderFactory staticData) =>
        new ManagedLayoutCompiler(types, staticData);

    internal static IWholeProgramAnalyzer CreateAnalyzer(
        MetadataCompilationSnapshot metadata,
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        ITypeFinder typeFinder,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IBaseTypeIdentityResolver baseTypeIdentities,
        IImplementedInterfaceResolver implementedInterfaces,
        IMethodBodyReader bodies,
        IMethodInstanceResolver methodInstances,
        IMethodImplementationResolver methodImplementations,
        ISymbolFormatter symbols)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        using var services = new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider();
        var intrinsics = new RuntimeIntrinsicRegistry(symbols, metadata.Methods);
        return services.GetRequiredService<IWholeProgramAnalyzerFactory>().Create(
            metadata,
            types,
            fields,
            methods,
            typeFinder,
            typeDefinitions,
            identities,
            baseTypeIdentities,
            implementedInterfaces,
            bodies,
            methodInstances,
            methodImplementations,
            symbols,
            _ => null,
            intrinsics);
    }

    internal static ManagedLayoutSnapshot CreateLayouts(
        MetadataCompilationSnapshot metadata,
        ITypeRepository types,
        IFieldRepository fields,
        ITypeFinder typeFinder,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IMetadataEntityBaseTypeResolver entityBaseTypes,
        IMetadataIdentityBaseTypeResolver identityBaseTypes,
        ReachableProgram program,
        WasmTarget target = WasmTarget.Wasm32)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(program);
        using var services = new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider();
        return services.GetRequiredService<IManagedLayoutCompiler>()
            .Compile(
                metadata,
                types,
                fields,
                typeFinder,
                typeDefinitions,
                identities,
                entityBaseTypes,
                identityBaseTypes,
                program,
                target);
    }

    internal static IValueLayoutResolver CreateValueLayouts(
        ITypeDefinitionResolver types,
        IFieldRepository fields,
        ManagedLayoutSnapshot layouts) => new ValueLayoutResolver(
        types,
        fields,
        new ExplicitValueLayoutResolver(),
        layouts.Target,
        layouts.TypeLayouts.ValueLayoutState);

    internal static ITypeLayoutProvider CreateTypeLayouts(
        ITypeDefinitionResolver types,
        ManagedLayoutSnapshot layouts) => new TypeLayoutProvider(types, layouts);

    internal static IInstanceFieldLayoutProvider CreateInstanceFields(
        ManagedLayoutSnapshot layouts) => new InstanceFieldLayoutProvider(layouts);

    internal static IStaticFieldLayoutProvider CreateStaticFields(
        ManagedLayoutSnapshot layouts) => new StaticFieldLayoutProvider(layouts);

    internal static IStaticDataLayout CreateStaticData(ManagedLayoutSnapshot layouts) =>
        new StaticDataLayoutProvider(layouts);

    internal static IManagedExceptionObjectProvider CreateExceptionObjects(
        ManagedLayoutSnapshot layouts) => new ManagedExceptionObjectProvider(layouts);

    internal static CompilerOptions Options(
        TestAssets assets,
        ImmutableArray<RequestedExport> exports,
        string entryType = "NetWasm.Fixtures.Application.EntryPoint",
        string entryMethod = "Run") => new(
            assets.Application,
            [assets.Library, assets.CoreLib],
            entryType,
        entryMethod,
        exports);

    internal static int ExecuteWithNode(
        byte[] module,
        string directory,
        int input,
        int invocationCount = 1,
        string exportName = "run",
        int? expectedEnvironmentReadsBeforeRun = null,
        bool drainReactor = false,
        string? observeExportName = null,
        WasmTarget target = WasmTarget.Wasm32)
    {
        var path = Path.Combine(directory, "application.wasm");
        File.WriteAllBytes(path, module);
        var inputText = input.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var invocationCountText = invocationCount.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var expectedEnvironmentReadsText = expectedEnvironmentReadsBeforeRun?.ToString(
            System.Globalization.CultureInfo.InvariantCulture) ?? "null";
        var drainReactorText = drainReactor ? "true" : "false";
        var observeExportText = System.Text.Json.JsonSerializer.Serialize(
            observeExportName ?? exportName);
        var memoryDescriptorText = target == WasmTarget.Wasm64
            ? "{initial:1n,address:'i64'}"
            : "{initial:1}";
        var script =
            "const fs=require('node:fs');" +
            $"const memory=new WebAssembly.Memory({memoryDescriptorText});" +
            "let heap=16;let initialized=false;let environmentReads=0;" +
            "let lastException=0;" +
            "let targetFrame=0;let targetClause=0;" +
            "let filterSearchFloor=0;let application=null;" +
            "let nextPollable=1;const watchedTokens=[];const weakHandles=[0];const gcHandles=[null];const canceledTokens=new Set();" +
            "const typeBases=new Map(),typeSizes=new Map(),typeAssignable=new Map(),valueTypeSizes=new Map();const typeObjects=new Map();const exceptionFrames=[];" +
            "const z=()=>0;" +
            "const raw=size=>{const p=heap;heap=(heap+size+3)&~3;" +
            "const pages=Math.ceil(heap/65536);const current=memory.buffer.byteLength/65536;" +
            "if(pages>current)memory.grow(pages-current);return p;};" +
            "const valueEnter=size=>{const p=raw(Math.max(16,size+15));return(p+15)&~15;};" +
            "const valueLeave=p=>{};" +
            "const rootEnter=count=>{const frame=raw(4+count*4);" +
            "const v=new DataView(memory.buffer);v.setInt32(frame,0,true);" +
            "for(let i=0;i<count;i++)v.setInt32(frame+4+i*4,0,true);return frame+4;};" +
            "const rootLeave=slots=>{};" +
            "const componentReallocate=(oldAddress,oldSize,alignment,newSize)=>{" +
            "if(newSize===0)return 0;if(alignment<=0||(alignment&(alignment-1))!==0)" +
            "throw Error('invalid component alignment');" +
            "const p=raw(newSize+alignment-1);const aligned=(p+alignment-1)&~(alignment-1);" +
            "if(oldAddress){const count=Math.min(oldSize,newSize);" +
            "new Uint8Array(memory.buffer,aligned,count).set(" +
            "new Uint8Array(memory.buffer,oldAddress,count));}return aligned;};" +
            "const nativeSizes=new Map();" +
            "const nativeAlloc=size=>{const count=Math.max(1,Number(size));" +
            "const p=raw(count);nativeSizes.set(p,count);return p;};" +
            "const nativeRealloc=(oldAddress,size)=>{const p=nativeAlloc(size);" +
            "if(oldAddress){const count=Math.min(nativeSizes.get(oldAddress)||0,Number(size));" +
            "new Uint8Array(memory.buffer,p,count).set(" +
            "new Uint8Array(memory.buffer,oldAddress,count));nativeSizes.delete(oldAddress);}" +
            "return p;};" +
            "const nativeAlignedAlloc=(size,alignment)=>{" +
            "const a=Number(alignment);if(a<=0||(a&(a-1))!==0)return 0;" +
            "const count=Math.max(1,Number(size));const rawPointer=raw(count+a-1);" +
            "const p=(rawPointer+a-1)&~(a-1);nativeSizes.set(p,count);return p;};" +
            "const nativeAlignedRealloc=(oldAddress,size,alignment)=>{" +
            "const p=nativeAlignedAlloc(size,alignment);if(!p)return 0;" +
            "if(oldAddress){const count=Math.min(nativeSizes.get(oldAddress)||0,Number(size));" +
            "new Uint8Array(memory.buffer,p,count).set(" +
            "new Uint8Array(memory.buffer,oldAddress,count));nativeSizes.delete(oldAddress);}" +
            "return p;};" +
            "const allocate=(size,type)=>{if(!typeBases.has(type))" +
            "throw Error(`allocation used unregistered type ${type}`);const p=raw(size);" +
            "new DataView(memory.buffer).setInt32(p,type,true);return p;};" +
            "const allocateString=(value,length,type)=>{" +
            "if(length<0||length>0x7ffffffb)return 0;" +
            "const p=allocate(8+length*2,type);const v=new DataView(memory.buffer);" +
            "v.setInt32(p+4,length,true);" +
            "for(let i=0;i<length;i++)v.setUint16(p+8+i*2,value,true);return p;};" +
            "const getTypeObject=(semantic,type)=>{if(typeObjects.has(semantic))" +
            "return typeObjects.get(semantic);const p=allocate(8,type);" +
            "new DataView(memory.buffer).setInt32(p+4,semantic,true);" +
            "typeObjects.set(semantic,p);return p;};" +
            "const array=(length,type,elementType,size=4)=>{const data=raw(Math.max(4,length*size));" +
            "const p=allocate(16,type);const v=new DataView(memory.buffer);" +
            "v.setInt32(p+4,length,true);v.setInt32(p+8,data,true);" +
            "v.setInt32(p+12,elementType,true);return p;};" +
            "const rectangularArray=(rank,dims,type,element,size,refs)=>{" +
            "if(rank<=0||rank>32)return 0;" +
            "const v=new DataView(memory.buffer),lengths=[];let total=1,stride=1;" +
            "for(let d=rank-1;d>=0;d--){const n=v.getInt32(dims+d*4,true);lengths[d]=n;" +
            "if(n<0||n>1073741823||n&&total>1073741823/n)return 0;total*=n;}" +
            "const shape=raw(rank*8),data=total?raw(total*(refs?4:size)):0;" +
            "const result=allocate(24,type),out=new DataView(memory.buffer);" +
            "for(let d=rank-1;d>=0;d--){const n=lengths[d];out.setInt32(shape+d*8,n,true);" +
            "out.setInt32(shape+d*8+4,stride,true);stride*=n;}" +
            "out.setInt32(result+4,total,true);out.setInt32(result+8,data,true);" +
            "out.setInt32(result+12,element,true);out.setInt32(result+16,rank,true);" +
            "out.setInt32(result+20,shape,true);return result;};" +
            "const isAssignable=(object,target)=>{if(!object)return 0;" +
            "let type=new DataView(memory.buffer).getInt32(object,true);" +
            "while(type){if(type===target||typeAssignable.get(type)?.has(target))return 1;type=typeBases.get(type)||0;}return 0;};" +
            "const arrayCopy=(source,sourceIndex,destination,destinationIndex,length)=>{" +
            "const v=new DataView(memory.buffer),sourceElement=v.getInt32(source+12,true)," +
            "destinationElement=v.getInt32(destination+12,true)," +
            "sourceSize=valueTypeSizes.get(sourceElement),destinationSize=valueTypeSizes.get(destinationElement);" +
            "if((sourceSize!==undefined)!==(destinationSize!==undefined))return 0;" +
            "const sourceData=v.getInt32(source+8,true),destinationData=v.getInt32(destination+8,true);" +
            "if(sourceSize!==undefined){if(sourceElement!==destinationElement||sourceSize!==destinationSize)return 0;" +
            "const bytes=length*sourceSize,copy=new Uint8Array(memory.buffer,sourceData+sourceIndex*sourceSize,bytes).slice();" +
            "new Uint8Array(memory.buffer,destinationData+destinationIndex*destinationSize,bytes).set(copy);return 1;}" +
            "const values=[];for(let i=0;i<length;i++){const value=v.getInt32(sourceData+(sourceIndex+i)*4,true);" +
            "if(value&&!isAssignable(value,destinationElement))return 0;values.push(value);}" +
            "for(let i=0;i<length;i++)v.setInt32(destinationData+(destinationIndex+i)*4,values[i],true);return 1;};" +
            "const arrayClear=(array,index,length)=>{const v=new DataView(memory.buffer)," +
            "element=v.getInt32(array+12,true),size=valueTypeSizes.get(element)||4,data=v.getInt32(array+8,true);" +
            "new Uint8Array(memory.buffer,data+index*size,length*size).fill(0);};" +
            "const arrayClone=source=>{const v=new DataView(memory.buffer),type=v.getInt32(source,true)," +
            "length=v.getInt32(source+4,true),element=v.getInt32(source+12,true)," +
            "size=valueTypeSizes.get(element);let clone;if(typeSizes.get(type)===24){" +
            "const rank=v.getInt32(source+16,true),shape=v.getInt32(source+20,true),dims=raw(rank*4);" +
            "for(let d=0;d<rank;d++)v.setInt32(dims+d*4,v.getInt32(shape+d*8,true),true);" +
            "clone=rectangularArray(rank,dims,type,element,size||0,size===undefined?1:0);}" +
            "else clone=array(length,type,element,size||4);" +
            "return arrayCopy(source,0,clone,0,length)?clone:0;};" +
            "const beginThrow=exception=>{lastException=exception;targetFrame=0;targetClause=0;" +
            "const v=new DataView(memory.buffer);" +
            "for(let frame=exceptionFrames.length;frame>filterSearchFloor;frame--){" +
            "const f=exceptionFrames[frame-1];const raw=f.count>>>0;" +
            "const filtered=(raw>>>31)!==0;const count=raw&0x7fffffff;" +
            "for(let clause=0;clause<count;clause++){let accepted=0;" +
            "if(filtered){const entry=f.metadata+clause*12;const kind=v.getInt32(entry,true);" +
            "if(kind===0)accepted=isAssignable(exception,v.getInt32(entry+4,true));" +
            "else{const saved=[lastException,targetFrame,targetClause,filterSearchFloor];" +
            "filterSearchFloor=frame;accepted=application.exports['netwasm.filter'](" +
            "v.getInt32(entry+4,true),exception,f.environment);" +
            "[lastException,targetFrame,targetClause,filterSearchFloor]=saved;}}" +
            "else accepted=isAssignable(exception,v.getInt32(f.metadata+clause*4,true));" +
            "if(accepted){targetFrame=frame;targetClause=clause+1;return;}}}};" +
            "const runtime={memory,initialize:staticEnd=>{" +
            "if(!initialized){heap=(Number(staticEnd)+3)&~3;initialized=true;}},allocate," +
            "component_realloc:componentReallocate,component_free:z," +
            "native_alloc:nativeAlloc,native_realloc:nativeRealloc,native_free:p=>nativeSizes.delete(p)," +
            "native_aligned_alloc:nativeAlignedAlloc,native_aligned_realloc:nativeAlignedRealloc," +
            "native_aligned_free:p=>nativeSizes.delete(p)," +
            "register_type:(type,base,size,bitmap,bits,assignables,count)=>{" +
            "typeBases.set(type,base);typeSizes.set(type,size);const ids=new Set(),v=new DataView(memory.buffer);" +
            "for(let i=0;i<count;i++)ids.add(v.getInt32(Number(assignables)+i*4,true));typeAssignable.set(type,ids);}," +
            "register_value_type:(type,size)=>valueTypeSizes.set(type,size)," +
            "register_static_root:z,root_frame_enter:rootEnter,root_frame_leave:rootLeave," +
            "value_frame_enter:valueEnter,value_frame_leave:valueLeave," +
            "handle_new:()=>0,handle_get:()=>0,handle_release:z," +
            "begin_throw:beginThrow,begin_rethrow:beginThrow," +
            "allocate_reference_array:array,allocate_value_array:array," +
            "allocate_rectangular_array:rectangularArray," +
            "array_rank:array=>typeSizes.get(new DataView(memory.buffer).getInt32(array,true))===24" +
            "?new DataView(memory.buffer).getInt32(array+16,true):1," +
            "array_get_length:(array,dimension)=>{const v=new DataView(memory.buffer)," +
            "rect=typeSizes.get(v.getInt32(array,true))===24;if(!rect)return dimension===0" +
            "?v.getInt32(array+4,true):-1;const rank=v.getInt32(array+16,true);" +
            "return dimension<0||dimension>=rank?-1:" +
            "v.getInt32(v.getInt32(array+20,true)+dimension*8,true);}," +
            "array_copy:arrayCopy,array_clear:arrayClear,array_clone:arrayClone," +
            "allocate_string:allocateString,get_type_object:getTypeObject," +
            "suppress_finalize:z,weak_handle_new:(target,trackResurrection)=>{weakHandles.push(target);return weakHandles.length-1;},weak_handle_get:handle=>weakHandles[handle]??0,weak_handle_set:(handle,target)=>{weakHandles[handle]=target;},weak_handle_release:handle=>{weakHandles[handle]=0;},gc_handle_new:(target,kind)=>{gcHandles.push({target,kind});return((gcHandles.length-1)<<2)|kind;},gc_handle_get:handle=>gcHandles[handle>>2]?.target??0,gc_handle_set:(handle,target)=>{const entry=gcHandles[handle>>2];if(entry)entry.target=target;},gc_handle_release:handle=>{gcHandles[handle>>2]=null;},gc_handle_address:handle=>gcHandles[handle>>2]?.target??0,reregister_for_finalize:z," +
            "gc_get_metric:metric=>0n," +
            "gc_metric_is_supported:metric=>metric!==4," +
            "gc_wait_for_pending_finalizers:()=>{}," +
            "object_identity_hash:value=>value|0," +
            "report_unobserved_task_exception:z," +
            "is_assignable:isAssignable,end_catch:()=>{lastException=0;}," +
            "exception_frame_enter:(metadata,count)=>{" +
            "exceptionFrames.push({metadata,count,environment:0});return exceptionFrames.length;}," +
            "exception_frame_set_environment:(token,environment)=>{" +
            "exceptionFrames[token-1].environment=environment;}," +
            "exception_frame_leave:token=>{if(token!==exceptionFrames.length)throw Error('EH frame');" +
            "exceptionFrames.pop();}," +
            "exception_frame_target_clause:token=>token===targetFrame?targetClause:0," +
            "finalizer_safepoint:z,collect:z};" +
            "const wasiWallClock={now:result=>{const view=new DataView(memory.buffer);" +
            "view.setBigUint64(result,0n,true);view.setUint32(result+8,123000000,true);}," +
            "resolution:result=>{const view=new DataView(memory.buffer);" +
            "view.setBigUint64(result,0n,true);view.setUint32(result+8,1,true);}};" +
            "const wasiMonotonicClock={'subscribe-duration':()=>nextPollable++};" +
            "let randomByte=0;const wasiRandom={'get-random-bytes':(length,result)=>{" +
            "const count=Number(length),address=componentReallocate(0,0,1,count);" +
            "const bytes=new Uint8Array(memory.buffer,address,count);" +
            "for(let i=0;i<count;i++)bytes[i]=(++randomByte)&255;" +
            "const view=new DataView(memory.buffer),wide=typeof result==='bigint',base=Number(result);" +
            "if(wide){view.setBigUint64(base,BigInt(address),true);" +
            "view.setBigUint64(base+8,BigInt(count),true);}" +
            "else{view.setUint32(base,address,true);view.setUint32(base+4,count,true);}}};" +
            "const reactorHost={watch:(pollable,token)=>watchedTokens.push(token)," +
            "cancel:token=>canceledTokens.add(token)};" +
            "const wasiEnvironment={'get-environment':result=>{" +
            "environmentReads++;" +
            "const view=new DataView(memory.buffer);view.setUint32(result,0,true);" +
            "view.setUint32(result+4,0,true);}};" +
            "const unusedWasiFilesystem=new Proxy({}, {get:()=>z});" +
            "const host=new Proxy({ write_i32: z, report_terminal_exception_v1: (typeId, messageReference, messageLength) => { let message = null; if (messageReference !== 0 && messageReference !== 0n) { const characters = new Uint16Array(memory.buffer, Number(messageReference) + 8, Number(messageLength)); message = String.fromCharCode(...characters); } throw new Error('terminal exception type=' + typeId + ' message=' + message); } }, {get:(target,name)=>target[name]||z});" +
            "WebAssembly.instantiate(fs.readFileSync(process.argv[1]),{" +
            "'netwasm.runtime.v1':runtime,'netwasm.host.v1':host," +
            "'cm32p2|wasi:clocks/wall-clock@0.2':wasiWallClock," +
            "'cm32p2|wasi:clocks/monotonic-clock@0.2':wasiMonotonicClock," +
            "'cm32p2|wasi:random/random@0.2':wasiRandom," +
            "'cm64p2|wasi:random/random@0.2':wasiRandom," +
            "'cm32p2|netwasm:runtime/reactor-host@1':reactorHost," +
            "'cm32p2|wasi:cli/environment@0.2':wasiEnvironment," +
            "'cm32p2|wasi:filesystem/preopens@0.2':unusedWasiFilesystem," +
            "'cm32p2|wasi:filesystem/types@0.2':unusedWasiFilesystem})" +
            ".then(x=>{application=x.instance;" +
            "const initialize=x.instance.exports.cm32p2_initialize;" +
            "if(typeof initialize==='function')initialize();" +
            "const expectedEnvironmentReads=" + expectedEnvironmentReadsText + ";" +
            "if(expectedEnvironmentReads!==null&&environmentReads!==expectedEnvironmentReads)" +
            "throw Error(`expected ${expectedEnvironmentReads} environment reads before run, got ${environmentReads}`);" +
            "let result=0;for(let i=0;i<" + invocationCountText + ";i++)" +
            "result=x.instance.exports[" + System.Text.Json.JsonSerializer.Serialize(exportName) + "](" +
            inputText + ");" +
            "if(" + drainReactorText + "){" +
            "const wake=x.instance.exports['cm32p2|netwasm:runtime/reactor-guest@1|wake'];" +
            "while(watchedTokens.length){const token=watchedTokens.shift();" +
            "if(!canceledTokens.has(token))wake(token);}" +
            "result=x.instance.exports[" + observeExportText + "](" + inputText + ");}" +
            "process.stdout.write(String(result));})" +
            ".catch(e=>{const type=lastException?new DataView(memory.buffer).getInt32(lastException,true):0;" +
            "process.stderr.write(`managed exception pointer=${lastException} type=${type}\\n${e.stack}\\n`);" +
            "process.exitCode=1;});";
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(path);
        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("failed to start Node.js");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return int.Parse(output, System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static int ExecuteWithStandardWasiNode(
        byte[] module,
        string directory,
        int input,
        WasmTarget target,
        int staticDataEnd,
        bool drainReactor = false,
        string? observeExportName = null,
        string? entryExportName = null,
        bool observeAsyncProcess = false) =>
        ExecuteWithStandardWasiNode(
            module,
            directory,
            input,
            target,
            staticDataEnd,
            ImmutableDictionary<string, string>.Empty,
            null,
            null,
            drainReactor,
            observeExportName,
            entryExportName,
            observeAsyncProcess);

    internal static int ExecuteWithStandardWasiNode(
        byte[] module,
        string directory,
        int input,
        WasmTarget target,
        int staticDataEnd,
        IReadOnlyDictionary<string, string> environment,
        string? timeZoneAssetPath,
        byte[]? stackTraceSymbols = null,
        bool drainReactor = false,
        string? observeExportName = null,
        string? entryExportName = null,
        bool observeAsyncProcess = false,
        int expectedEnvironmentReads = 1,
        int expectedPreopenReads = 1,
        int entryInvocationCount = 1)
    {
        var modulePath = Path.Combine(directory, $"application-{target}.wasm");
        var configurationPath = Path.Combine(directory, $"wasi-host-{target}.json");
        var stackTraceSymbolsPath = stackTraceSymbols is null
            ? null
            : Path.Combine(directory, $"application-{target}.netwasm.stacktrace.json");
        File.WriteAllBytes(modulePath, module);
        if (stackTraceSymbols is not null)
        {
            File.WriteAllBytes(stackTraceSymbolsPath!, stackTraceSymbols);
        }
        File.WriteAllText(configurationPath, System.Text.Json.JsonSerializer.Serialize(new
        {
            environment,
            timeZoneAssetPath,
            stackTraceSymbolsPath,
            drainReactor,
            observeExportName,
            entryExportName,
            observeAsyncProcess,
            entryInvocationCount,
        }));
        var oraclePath = Path.Combine(
            AppContext.BaseDirectory,
            "Correctness",
            "netwasm-oracle.mjs");
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add(oraclePath);
        start.ArgumentList.Add(modulePath);
        start.ArgumentList.Add(input.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add(target == WasmTarget.Wasm64 ? "wasm64" : "wasm32");
        start.ArgumentList.Add(staticDataEnd.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add(configurationPath);
        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("failed to start Node.js");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        using var observation = System.Text.Json.JsonDocument.Parse(output);
        var root = observation.RootElement;
        Assert.True(
            root.GetProperty("kind").GetString() == "value",
            root.TryGetProperty("message", out var message)
                ? message.GetString()
                : output);
        if (environment.Count != 0)
        {
            Assert.Equal(expectedEnvironmentReads, root.GetProperty("wasiEnvironmentReads").GetInt32());
            Assert.True(
                root.GetProperty("wasiPreopenReads").GetInt32() == expectedPreopenReads,
                output);
        }
        return root.GetProperty("value").GetInt32();
    }

    internal static void ValidateWithNode(byte[] module, string directory)
    {
        var path = Path.Combine(directory, "validation.wasm");
        File.WriteAllBytes(path, module);
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add(
            "const fs=require('node:fs');new WebAssembly.Module(fs.readFileSync(process.argv[1]));");
        start.ArgumentList.Add(path);
        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("failed to start Node.js");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{output}{error}");
    }
}
