namespace LibXbf.Records.Nodes;

public sealed class XbfProperty : XbfNode
{
    internal XbfProperty(BinaryReader br)
        : base(br)
    {
    }

    public List<XbfNode> Values { get; } = [];
}
