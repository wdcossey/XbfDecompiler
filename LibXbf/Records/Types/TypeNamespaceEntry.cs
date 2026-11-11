namespace LibXbf.Records.Types;

public readonly record struct TypeNamespaceEntry(uint AssemblyId, uint StringId)
{
    internal const int Size = 8;

    internal static TypeNamespaceEntry Read(BinaryReader br) => new(br.ReadUInt32(), br.ReadUInt32());
}
