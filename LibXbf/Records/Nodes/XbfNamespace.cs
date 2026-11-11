namespace LibXbf.Records.Nodes;

/// <summary>An <c>xmlns</c> declaration; <see cref="XbfNode.Id"/> indexes the XML namespace table.</summary>
public sealed class XbfNamespace : XbfNode
{
    internal XbfNamespace(BinaryReader br)
        : base(br)
    {
        Prefix = br.ReadUtf16String(br.ReadUInt32());
    }

    /// <summary>The namespace prefix, or an empty string for the default namespace.</summary>
    public string Prefix { get; }
}
