using LibXbf.Records.Nodes;
using LibXbf.Records.Types;

namespace LibXbf.Tests;

public class XbfFileTests
{
    private static XbfBuilder MinimalDocument()
    {
        var b = new XbfBuilder();
        uint grid = b.Type("Windows.UI.Xaml.Controls", "Grid");
        b.StartObject(grid).EndObject();
        return b;
    }

    [Fact]
    public void Load_reads_tables_and_root()
    {
        var b = new XbfBuilder();
        uint grid = b.Type("Windows.UI.Xaml.Controls", "Grid");
        uint width = b.Property(b.Type("Windows.UI.Xaml", "FrameworkElement"), "Width");
        uint pres = b.XmlNamespace("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        b.Namespace(pres, "").StartObject(grid).TextProperty(width, "100").EndObject();

        XbfFile file = b.Load();

        Assert.Equal(new Version(1, 0), file.FileVersion);
        Assert.Equal("Grid", file.GetTypeInfo(file.RootNode.Id).Name);
        Assert.Equal("Windows.UI.Xaml.Controls.Grid", file.GetTypeInfo(file.RootNode.Id).FullName);
        XbfProperty property = Assert.Single(file.RootNode.Properties);
        Assert.Equal("Width", file.GetPropertyInfo(property.Id).Name);
        Assert.Equal("100", file.GetString(Assert.IsType<XbfText>(Assert.Single(property.Values)).Id));
        XbfNamespace ns = Assert.Single(file.Namespaces);
        Assert.Equal("", ns.Prefix);
        Assert.Equal("http://schemas.microsoft.com/winfx/2006/xaml/presentation", file.GetXmlNamespace(ns));
    }

    [Fact]
    public void Strings_with_surrogate_pairs_do_not_desynchronise_the_stream()
    {
        var b = new XbfBuilder();
        uint text = b.Property(b.Type("Windows.UI.Xaml.Controls", "TextBlock"), "Text");
        b.StartObject(b.Type("Windows.UI.Xaml.Controls", "TextBlock")).TextProperty(text, "smile \U0001F600").EndObject();

        XbfFile file = b.Load();

        Assert.Contains("smile \U0001F600", file.StringTable);
    }

    [Fact]
    public async Task LoadAsync_reads_from_disk()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, MinimalDocument().Build(), TestContext.Current.CancellationToken);
            XbfFile file = await XbfFile.LoadAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal("Grid", file.GetTypeInfo(file.RootNode.Id).Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(3u)]
    public void Load_rejects_other_versions(uint major)
    {
        var ex = Assert.Throws<UnsupportedXbfVersionException>(() => MinimalDocument().Load(major));
        Assert.Equal(major, (uint)ex.Version!.Major);
    }

    [Fact]
    public void Load_rejects_bad_magic()
    {
        byte[] bytes = MinimalDocument().Build();
        bytes[0] = (byte)'Q';
        Assert.Throws<InvalidXbfException>(() => XbfFile.Load(new MemoryStream(bytes)));
    }

    [Fact]
    public void Load_rejects_tiny_file()
    {
        Assert.Throws<InvalidXbfException>(() => XbfFile.Load(new MemoryStream("XBF\0"u8.ToArray())));
    }

    [Fact]
    public void Load_rejects_truncated_file()
    {
        byte[] bytes = MinimalDocument().Build();
        Assert.Throws<InvalidXbfException>(() => XbfFile.Load(new MemoryStream(bytes[..^3])));
    }

    [Fact]
    public void Load_rejects_absurd_table_size_without_allocating()
    {
        byte[] bytes = MinimalDocument().Build();
        // String table count lives straight after the header.
        BitConverter.GetBytes(uint.MaxValue).CopyTo(bytes, 0x84);
        Assert.Throws<InvalidXbfException>(() => XbfFile.Load(new MemoryStream(bytes)));
    }

    [Fact]
    public void Load_rejects_unknown_node_type()
    {
        var b = new XbfBuilder();
        b.StartObject(b.Type("Windows.UI.Xaml.Controls", "Grid")).Raw(0x7F).EndObject();
        var ex = Assert.Throws<InvalidXbfException>(() => b.Load());
        Assert.Contains("Unknown node type", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_rejects_unknown_value_type()
    {
        var b = new XbfBuilder();
        uint p = b.Property(b.Type("Windows.UI.Xaml", "FrameworkElement"), "Width");
        b.StartObject(b.Type("Windows.UI.Xaml.Controls", "Grid")).ValueProperty(p, (XbfValueType)0x7F).EndObject();
        Assert.Throws<InvalidXbfException>(() => b.Load());
    }

    [Fact]
    public void Load_rejects_unbalanced_nodes()
    {
        var b = new XbfBuilder();
        b.StartObject(b.Type("Windows.UI.Xaml.Controls", "Grid")).EndObject().EndObject();
        Assert.Throws<InvalidXbfException>(() => b.Load());

        var unclosed = new XbfBuilder();
        unclosed.StartObject(unclosed.Type("Windows.UI.Xaml.Controls", "Grid"));
        Assert.Throws<InvalidXbfException>(() => unclosed.Load());
    }

    [Fact]
    public void Dangling_table_reference_is_reported_as_invalid_file()
    {
        var b = new XbfBuilder();
        b.StartObject(typeId: 42).EndObject();
        XbfFile file = b.Load();
        Assert.Throws<InvalidXbfException>(() => file.GetTypeInfo(file.RootNode.Id));
    }

    [Fact]
    public void Line_info_accumulates_signed_deltas()
    {
        var b = new XbfBuilder();
        uint grid = b.Type("Windows.UI.Xaml.Controls", "Grid");
        b.Raw((byte)XbfNodeType.LineInfoAbsolute).Raw(BitConverter.GetBytes(10u)).Raw(BitConverter.GetBytes(5u));
        b.Raw((byte)XbfNodeType.LineInfo).Raw(BitConverter.GetBytes((short)2)).Raw(BitConverter.GetBytes((short)-3));
        b.StartObject(grid).EndObject();

        Assert.Equal(new XbfLineInfo(12, 2), b.Load().RootNode.LineInfo);
    }

    [Fact]
    public void Directive_properties_resolve_to_the_x_namespace()
    {
        var b = new XbfBuilder();
        uint name = b.Property(0, "Name", PropertyFlags.IsMarkupDirective);
        b.StartObject(b.Type("Windows.UI.Xaml.Controls", "Grid")).TextProperty(name, "Root").EndObject();
        XbfFile file = b.Load();

        XbfPropertyInfo info = file.GetPropertyInfo(name);
        Assert.True(info.IsDirective);
        Assert.Equal("x", info.DeclaringType.Namespace);
    }

    [Fact]
    public void Load_accepts_non_seekable_streams()
    {
        byte[] bytes = MinimalDocument().Build();
        using var stream = new NonSeekableStream(bytes);
        Assert.Equal("Grid", XbfFile.Load(stream).GetTypeInfo(0).Name);
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
