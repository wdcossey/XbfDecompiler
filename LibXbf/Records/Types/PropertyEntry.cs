namespace LibXbf.Records.Types;

public readonly record struct PropertyEntry(PropertyFlags Flags, uint TypeId, uint StringId)
{
    internal const int Size = 12;

    internal static PropertyEntry Read(BinaryReader br) =>
        new((PropertyFlags)br.ReadUInt32(), br.ReadUInt32(), br.ReadUInt32());
}

[Flags]
public enum PropertyFlags
{
    None = 0,
    IsXmlProperty = 1,
    IsMarkupDirective = 2,
    IsImplicitProperty = 4,
}
