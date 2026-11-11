namespace LibXbf.Records.Nodes;

public abstract class XbfNode
{
    /// <summary>Creates a node with the standard id/flags prefix read from <paramref name="br"/>.</summary>
    private protected XbfNode(BinaryReader br)
    {
        Id = br.ReadUInt32();
        Flags = br.ReadUInt32();
    }

    /// <summary>Creates a node that has no id/flags prefix (currently only <see cref="XbfValue"/>).</summary>
    private protected XbfNode()
    {
    }

    /// <summary>
    /// Index into the table relevant to the node type: the type table for objects, the property table for
    /// properties, the string table for text and the XML namespace table for namespace declarations.
    /// </summary>
    public uint Id { get; }

    public uint Flags { get; }

    public XbfLineInfo LineInfo { get; internal set; }
}
