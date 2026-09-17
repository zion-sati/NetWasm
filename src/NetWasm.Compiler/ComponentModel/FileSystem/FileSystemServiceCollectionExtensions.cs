using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.ComponentModel.FileSystem;

internal static class FileSystemServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerComponentModelFileSystem(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IFileExistence, SystemFileExistence>();
        services.AddSingleton<IDirectoryExistence, SystemDirectoryExistence>();
        services.AddSingleton<IByteFileReader, SystemByteFileReader>();
        services.AddSingleton<IByteFileWriter, SystemByteFileWriter>();
        services.AddSingleton<ITextFileWriter, SystemTextFileWriter>();
        services.AddSingleton<IFileDeleter, SystemFileDeleter>();
        services.AddSingleton<IDirectoryCreator, SystemDirectoryCreator>();
        services.AddSingleton<IDirectoryDeleter, SystemDirectoryDeleter>();
        services.AddSingleton<IFileMover, SystemFileMover>();
        services.AddSingleton<IFileCopier, SystemFileCopier>();
        services.AddSingleton<IWasmCoreModuleValidator, WasmCoreModuleValidator>();
        return services;
    }
}
