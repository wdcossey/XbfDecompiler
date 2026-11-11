namespace LibXbf.Records.Types;

public readonly record struct AssemblyEntry(AssemblyEntryType ProviderKind, uint StringId)
{
    internal const int Size = 8;

    internal static AssemblyEntry Read(BinaryReader br) =>
        new((AssemblyEntryType)br.ReadUInt32(), br.ReadUInt32());
}

public enum AssemblyEntryType
{
    Unknown = 0,
    Native = 1,
    Managed = 2,
    System = 3,
    Parser = 4,
    Alternate = 5,
}
