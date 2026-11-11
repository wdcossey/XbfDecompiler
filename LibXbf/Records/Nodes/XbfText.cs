namespace LibXbf.Records.Nodes;

/// <summary>A literal string value; <see cref="XbfNode.Id"/> indexes the string table.</summary>
public sealed class XbfText : XbfNode
{
    internal XbfText(BinaryReader br)
        : base(br)
    {
    }
}
