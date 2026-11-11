using LibXbf.Records.Nodes;

namespace LibXbf.Output;

public sealed record XbfTreeItem(string Display, IReadOnlyList<XbfTreeItem> Children)
{
    public XbfTreeItem(string display)
        : this(display, [])
    {
    }
}

/// <summary>Produces the object/property hierarchy of an <see cref="XbfFile"/>.</summary>
public sealed class TreeOutput : IXbfOutput<XbfTreeItem>
{
    public XbfTreeItem GetOutput(XbfFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return DumpObject(file, file.RootNode);
    }

    private static XbfTreeItem DumpObject(XbfFile file, XbfObject obj)
    {
        string display = file.GetTypeInfo(obj.Id).Name;
        if (obj.IsRetrieved)
        {
            display += " (retrieved)";
        }

        return new XbfTreeItem(display, [.. obj.Properties.Select(p => DumpProperty(file, p))]);
    }

    private static XbfTreeItem DumpProperty(XbfFile file, XbfProperty xp)
    {
        XbfPropertyInfo property = file.GetPropertyInfo(xp.Id);
        string name = property.IsDirective ? $"x:{property.Name}" : property.Name;

        // Show simple values inline rather than as a single child.
        if (xp.Values is [XbfText or XbfValue] && DumpNode(file, xp.Values[0]) is { } only)
        {
            return new XbfTreeItem($"{name} = {only.Display}");
        }

        return new XbfTreeItem(name, [.. xp.Values.Select(v => DumpNode(file, v)).OfType<XbfTreeItem>()]);
    }

    private static XbfTreeItem? DumpNode(XbfFile file, XbfNode node) => node switch
    {
        XbfObject obj => DumpObject(file, obj),
        XbfText text => new XbfTreeItem($"\"{file.GetString(text.Id)}\""),
        XbfValue val => new XbfTreeItem($"{val.Value} ({val.Type})"),
        _ => null,
    };
}
