using System.Globalization;

namespace LibXbf.Output;

/// <summary>Produces a browsable view of the header and metadata tables of an <see cref="XbfFile"/>.</summary>
public sealed class MetadataOutput : IXbfOutput<XbfTreeItem>
{
    public XbfTreeItem GetOutput(XbfFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var h = file.Header;

        return new XbfTreeItem("Metadata",
        [
            new XbfTreeItem("Header",
            [
                new($"Version: {file.FileVersion}"),
                new($"Metadata size: {h.MetadataSize} bytes"),
                new($"Node stream size: {h.NodeSize} bytes"),
                new($"Hash: \"{h.Hash.Trim()}\""),
            ]),
            Table("Strings", file.StringTable, s => $"\"{s}\""),
            Table("Assemblies", file.AssemblyTable, a => $"{file.GetString(a.StringId)} ({a.ProviderKind})"),
            Table("Type namespaces", file.TypeNamespaceTable, n => file.GetString(n.StringId)),
            Table("Types", Enumerable.Range(0, file.TypeTable.Count), i => file.GetTypeInfo((uint)i).FullName),
            Table("Properties", Enumerable.Range(0, file.PropertyTable.Count), i => Describe(file.GetPropertyInfo((uint)i))),
            Table("XML namespaces", file.XmlNamespaceTable, file.GetString),
            Table("Namespace declarations", file.Namespaces,
                ns => $"xmlns{(ns.Prefix.Length > 0 ? ":" + ns.Prefix : "")}=\"{file.GetXmlNamespace(ns)}\""),
        ]);
    }

    private static string Describe(XbfPropertyInfo property) =>
        property.IsDirective
            ? $"x:{property.Name}"
            : $"{property.DeclaringType.FullName}.{property.Name}" + (property.IsImplicit ? " (implicit)" : "");

    private static XbfTreeItem Table<T>(string name, IEnumerable<T> entries, Func<T, string> describe)
    {
        XbfTreeItem[] items = [.. entries.Select((e, i) => new XbfTreeItem(
            string.Create(CultureInfo.InvariantCulture, $"[{i}] {describe(e)}")))];
        return new XbfTreeItem($"{name} ({items.Length})", items);
    }
}
