using LibXbf.Output;
using LibXbf.Records.Nodes;

namespace LibXbf.Tests;

public class NodeEncodingTests
{
    private static XbfBuilder Style(XbfNodeEncoding encoding, string hash)
    {
        var b = new XbfBuilder(encoding) { Hash = hash };
        uint presentation = b.XmlNamespace("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        uint style = b.Type("Windows.UI.Xaml", "Style");
        uint targetType = b.Property(style, "TargetType");
        b.Namespace(presentation, "")
            .LineInfo(3, 10)
            .StartObject(style)
            .LineInfo(4, 35)
            .TextProperty(targetType, "ListViewBaseItem")
            .EndObject();
        return b;
    }

    [Fact]
    public void Byte_encoded_stream_is_read_as_bytes()
    {
        XbfFile file = Style(XbfNodeEncoding.Byte, hash: "").Load();

        Assert.Equal(XbfNodeEncoding.Byte, file.NodeEncoding);
        Assert.Equal("Style", file.GetTypeInfo(file.RootNode.Id).Name);
        // Byte-encoded line info holds deltas.
        Assert.Equal(new XbfLineInfo(7, 45), file.RootNode.Properties[0].LineInfo);
    }

    [Fact]
    public void Word_encoded_stream_with_hash_is_read_as_words()
    {
        XbfFile file = Style(XbfNodeEncoding.Word, hash: "2A8D843DAE7CBF3926738C23E8DDAAD7").Load();

        Assert.Equal(XbfNodeEncoding.Word, file.NodeEncoding);
        Assert.Equal("", Assert.Single(file.Namespaces).Prefix);
        Assert.Equal(new XbfLineInfo(3, 10), file.RootNode.LineInfo);
        // Word-encoded line info is absolute.
        Assert.Equal(new XbfLineInfo(4, 35), file.RootNode.Properties[0].LineInfo);
        Assert.Contains("TargetType=\"ListViewBaseItem\"",
            XamlOutput.ToXamlString(new XamlOutput().GetOutput(file)), StringComparison.Ordinal);
    }

    [Fact]
    public void Encoding_falls_back_when_the_hash_hint_is_wrong()
    {
        Assert.Equal(XbfNodeEncoding.Word, Style(XbfNodeEncoding.Word, hash: "").Load().NodeEncoding);
        Assert.Equal(XbfNodeEncoding.Byte, Style(XbfNodeEncoding.Byte, hash: "ABCDEF").Load().NodeEncoding);
    }

    [Fact]
    public void Stream_that_is_neither_encoding_reports_the_likelier_error()
    {
        var b = new XbfBuilder();
        b.StartObject(b.Type("Windows.UI.Xaml.Controls", "Grid")).Raw(0x7F).EndObject();

        var ex = Assert.Throws<InvalidXbfException>(() => b.Load());
        Assert.Contains("byte-encoded", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0f, "0")]
    [InlineData(0.167f, "0:0:0.167")]
    [InlineData(3.917f, "0:0:3.917")]
    [InlineData(1f, "0:0:1")]
    [InlineData(90.5f, "0:1:30.5")]
    [InlineData(3725f, "1:2:5")]
    public void Key_times_and_durations_are_written_as_xaml_time_spans(float seconds, string expected)
    {
        foreach (XbfValueType type in new[] { XbfValueType.IsKeyTime, XbfValueType.IsDuration })
        {
            var b = new XbfBuilder();
            uint p = b.Property(b.Type("Windows.UI.Xaml.Media.Animation", "Timeline"), "Duration");
            b.StartObject(b.Type("Windows.UI.Xaml.Media.Animation", "DoubleAnimation"))
                .ValueProperty(p, type, w => w.Write(seconds))
                .EndObject();

            XbfValue value = Assert.IsType<XbfValue>(Assert.Single(b.Load().RootNode.Properties[0].Values));
            Assert.Equal(expected, value.Value);
        }
    }
}
