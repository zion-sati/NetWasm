namespace NetWasm.Sdk.Pack.Packing;

internal sealed record PackTaskDependencies(
    IMsBuildPackInputAdapter InputAdapter,
    IProjectReferenceAdapter ProjectReferenceAdapter,
    IPackageBuilder PackageBuilder,
    IPackDiagnosticWriter DiagnosticWriter);
