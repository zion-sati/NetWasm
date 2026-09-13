namespace NetWasm.Compiler.Analysis;

internal readonly record struct TypeRelationship(TypeRelationshipCharacteristics Characteristics)
{
    public bool IsAssignmentCompatible => Characteristics is not TypeRelationshipCharacteristics.None;

    public bool IsHierarchyAssignable =>
        (Characteristics &
         (TypeRelationshipCharacteristics.Hierarchy |
          TypeRelationshipCharacteristics.SzArrayGenericInterface)) is not 0;

    public bool RequiresVariantMethodResolution =>
        (Characteristics & TypeRelationshipCharacteristics.GenericVariance) is not 0;

    public bool IsSzArrayGenericInterface =>
        (Characteristics & TypeRelationshipCharacteristics.SzArrayGenericInterface) is not 0;
}

[System.Flags]
internal enum TypeRelationshipCharacteristics
{
    None = 0,
    Hierarchy = 1 << 0,
    GenericVariance = 1 << 1,
    SzArrayGenericInterface = 1 << 2,
}
