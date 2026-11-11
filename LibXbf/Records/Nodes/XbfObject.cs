namespace LibXbf.Records.Nodes;

public sealed class XbfObject : XbfNode
{
    internal XbfObject(BinaryReader br)
        : base(br)
    {
    }

    public List<XbfProperty> Properties { get; } = [];

    /// <summary>
    /// The object was not created by the markup but retrieved from its parent property; this is how collection
    /// properties such as <c>Panel.Children</c> or <c>Style.Setters</c> are represented.
    /// </summary>
    public bool IsRetrieved => (Flags & 1) != 0;
}
