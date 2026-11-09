using System.Text;

namespace NetWasm.Hosting.Build.JavaScript;

/// <summary>
/// A worker client's file mounts (#142): the module exports mount, unmount and sync, and every
/// worker it creates sees what the page mounted, before and after the worker started.
/// </summary>
internal static class WorkerClientFileMounts
{
    public static void AppendModuleScope(StringBuilder source)
    {
        source.AppendLine("const fileMounts = createWorkerFileMounts();");
        source.AppendLine("export const { mount, unmount, sync } = fileMounts;");
    }

    /// <summary>Attaches a ready session; a mount it cannot take ends it.</summary>
    public static void AppendAttach(StringBuilder source, string indent) =>
        source.Append(indent).AppendLine(
            "const detach = await fileMounts.attach(session).catch(cause => { session.terminate(); throw cause; });");

    public static string Dispose => "dispose: () => { detach(); return session.dispose(); },";

    public static string Terminate => "terminate: () => { detach(); session.terminate(); },";

    public static void AppendDeclarations(StringBuilder source)
    {
        source.AppendLine("/** What the user picked: a File or Blob mounts read-only; a handle mounts read-write. */");
        source.AppendLine("export type NetWasmMountSource = Blob | FileSystemFileHandle | FileSystemDirectoryHandle;");
        source.AppendLine("/** Mounts a picked file or directory, at an absolute path, in every worker this module creates. */");
        source.AppendLine("export declare function mount(guestPath: string, source: NetWasmMountSource): Promise<void>;");
        source.AppendLine("export declare function unmount(guestPath: string): Promise<void>;");
        source.AppendLine("/** Writes each worker's changes to a handle mount back to what the user picked. */");
        source.AppendLine("export declare function sync(guestPath: string): Promise<void>;");
    }
}
