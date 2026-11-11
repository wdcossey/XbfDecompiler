namespace LibXbf.Records.Types;

public readonly record struct TypeEntry(TypeFlags Flags, uint NamespaceId, uint StringId)
{
    internal const int Size = 12;

    internal static TypeEntry Read(BinaryReader br) =>
        new((TypeFlags)br.ReadUInt32(), br.ReadUInt32(), br.ReadUInt32());
}

[Flags]
public enum TypeFlags
{
    None = 0,
    IsMarkupDirective = 1,
}
