using System.Globalization;
using System.Xml.Linq;
using LibXbf.Output;
using LibXbf.Records.Nodes;
using LibXbf.Records.Types;

namespace LibXbf.Tests;

public class XamlOutputTests
{
    private const string Xaml = "Windows.UI.Xaml";
    private const string Controls = "Windows.UI.Xaml.Controls";
    private const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static string Decompile(XbfBuilder b) => XamlOutput.ToXamlString(new XamlOutput().GetOutput(b.Load()));

    private static XElement DecompileRoot(XbfBuilder b) => new XamlOutput().GetOutput(b.Load()).Root!;

    /// <summary>A Border with a single property set from a value node.</summary>
    private static XElement BorderWithValue(string property, XbfValueType type, Action<BinaryWriter>? payload = null)
    {
        var b = new XbfBuilder();
        uint p = b.Property(b.Type(Xaml, "FrameworkElement"), property);
        b.StartObject(b.Type(Controls, "Border")).ValueProperty(p, type, payload).EndObject();
        return DecompileRoot(b);
    }

    [Theory]
    [InlineData(XbfValueType.IsBoolFalse, "False")]
    [InlineData(XbfValueType.IsBoolTrue, "True")]
    public void Booleans_are_not_inverted(XbfValueType type, string expected)
    {
        Assert.Equal(expected, (string?)BorderWithValue("UseLayoutRounding", type).Attribute("UseLayoutRounding"));
    }

    [Fact]
    public void Colors_are_written_as_argb_hex()
    {
        XElement root = BorderWithValue("Color", XbfValueType.IsColor, w => w.Write(0xFF00A2E8u));
        Assert.Equal("#FF00A2E8", (string?)root.Attribute("Color"));
    }

    [Fact]
    public void Numbers_use_invariant_culture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            Assert.Equal("2.5", (string?)BorderWithValue("Opacity", XbfValueType.IsFloat, w => w.Write(2.5f)).Attribute("Opacity"));
            Assert.Equal("0,12.5,0,4", (string?)BorderWithValue("Margin", XbfValueType.IsThickness, w =>
            {
                w.Write(0f);
                w.Write(12.5f);
                w.Write(0f);
                w.Write(4f);
            }).Attribute("Margin"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(1f, 2f, 3f, 4f, "1,2,3,4")]
    [InlineData(1f, 2f, 1f, 2f, "1,2")]
    [InlineData(5f, 5f, 5f, 5f, "5")]
    public void Thickness_uses_shortest_equivalent_form(float l, float t, float r, float bottom, string expected)
    {
        XElement root = BorderWithValue("Margin", XbfValueType.IsThickness, w =>
        {
            w.Write(l);
            w.Write(t);
            w.Write(r);
            w.Write(bottom);
        });
        Assert.Equal(expected, (string?)root.Attribute("Margin"));
    }

    [Theory]
    [InlineData(0u, 1f, "Auto")]
    [InlineData(1u, 120f, "120")]
    [InlineData(2u, 1f, "*")]
    [InlineData(2u, 2.5f, "2.5*")]
    public void GridLength_formats(uint unit, float value, string expected)
    {
        XElement root = BorderWithValue("Width", XbfValueType.IsGridLength, w =>
        {
            w.Write(unit);
            w.Write(value);
        });
        Assert.Equal(expected, (string?)root.Attribute("Width"));
    }

    [Fact]
    public void NaN_length_is_auto()
    {
        Assert.Equal("Auto", (string?)BorderWithValue("Width", XbfValueType.IsLengthConverter, w => w.Write(float.NaN)).Attribute("Width"));
    }

    [Fact]
    public void Content_property_with_retrieved_collection_is_flattened()
    {
        var b = new XbfBuilder();
        uint grid = b.Type(Controls, "Grid");
        uint collection = b.Type(Controls, "UIElementCollection");
        uint border = b.Type(Controls, "Border");
        uint children = b.Property(b.Type(Controls, "Panel"), "Children");
        uint items = b.Property(b.Type(Xaml, "ResourceDictionary"), "__implicit_items", PropertyFlags.IsImplicitProperty);

        b.StartObject(grid)
            .StartProperty(children)
                .StartObject(collection, retrieved: true)
                    .StartProperty(items)
                        .StartObject(border).EndObject()
                        .StartObject(border).EndObject()
                    .EndProperty()
                .EndObject()
            .EndProperty()
            .EndObject();

        XElement root = DecompileRoot(b);

        Assert.Equal(XName.Get("Grid", Presentation), root.Name);
        Assert.Equal(2, root.Elements(XName.Get("Border", Presentation)).Count());
        Assert.DoesNotContain("Collection", root.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Children", root.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Template_content_is_never_a_property_element()
    {
        var b = new XbfBuilder();
        uint template = b.Property(b.Type(Xaml, "FrameworkTemplate"), "Template");
        b.StartObject(b.Type(Controls, "ControlTemplate"))
            .StartProperty(template).StartObject(b.Type(Controls, "Grid")).EndObject().EndProperty()
            .EndObject();

        Assert.Equal("<ControlTemplate xmlns=\"" + Presentation + "\">\n  <Grid />\n</ControlTemplate>",
            Decompile(b).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Non_content_object_values_become_owner_qualified_property_elements()
    {
        var b = new XbfBuilder();
        uint background = b.Property(b.Type(Controls, "Panel"), "Background");
        b.StartObject(b.Type(Controls, "Grid"))
            .StartProperty(background).StartObject(b.Type("Windows.UI.Xaml.Media", "SolidColorBrush")).EndObject().EndProperty()
            .EndObject();

        XElement propertyElement = Assert.Single(DecompileRoot(b).Elements());
        Assert.Equal(XName.Get("Grid.Background", Presentation), propertyElement.Name);
    }

    [Fact]
    public void Attached_properties_are_qualified_with_their_owner()
    {
        var b = new XbfBuilder();
        uint row = b.Property(b.Type(Controls, "Grid"), "Row");
        uint targetName = b.Property(b.Type("Windows.UI.Xaml.Media.Animation", "Storyboard"), "TargetName");
        uint access = b.Property(b.Type($"{Xaml}.Automation", "AutomationProperties"), "AccessibilityView");
        uint vsGroups = b.Property(b.Type(Xaml, "VisualStateManager"), "VisualStateGroups");

        b.StartObject(b.Type(Controls, "Grid")) // Grid.Row on a Grid is still attached
            .TextProperty(row, "1")
            .TextProperty(targetName, "Target")
            .TextProperty(access, "Raw")
            .StartProperty(vsGroups).StartObject(b.Type(Xaml, "VisualStateGroup")).EndObject().EndProperty()
            .EndObject();

        XElement root = DecompileRoot(b);

        Assert.Equal("1", (string?)root.Attribute("Grid.Row"));
        Assert.Equal("Target", (string?)root.Attribute("Storyboard.TargetName"));
        Assert.Equal("Raw", (string?)root.Attribute("AutomationProperties.AccessibilityView"));
        Assert.NotNull(root.Element(XName.Get("VisualStateManager.VisualStateGroups", Presentation)));
    }

    [Fact]
    public void Grid_own_properties_are_not_treated_as_attached()
    {
        var b = new XbfBuilder();
        uint rows = b.Property(b.Type(Controls, "Grid"), "RowDefinitions");
        b.StartObject(b.Type(Controls, "Grid"))
            .StartProperty(rows).StartObject(b.Type(Controls, "RowDefinition")).EndObject().EndProperty()
            .EndObject();

        Assert.NotNull(DecompileRoot(b).Element(XName.Get("Grid.RowDefinitions", Presentation)));
    }

    [Fact]
    public void Markup_extensions_are_written_as_attributes()
    {
        var b = new XbfBuilder();
        uint background = b.Property(b.Type(Controls, "Panel"), "Background");
        uint foreground = b.Property(b.Type(Controls, "Control"), "Foreground");
        uint themeResource = b.Type(Xaml, "ThemeResource");
        uint resourceKey = b.Property(themeResource, "ResourceKey");
        uint templateBinding = b.Type($"{Xaml}.Data", "TemplateBinding");
        uint property = b.Property(templateBinding, "Property");

        b.StartObject(b.Type(Controls, "Grid"))
            .StartProperty(background).StartObject(themeResource).TextProperty(resourceKey, "AccentBrush").EndObject().EndProperty()
            .StartProperty(foreground).StartObject(templateBinding).TextProperty(property, "Foreground").EndObject().EndProperty()
            .EndObject();

        XElement root = DecompileRoot(b);

        Assert.Equal("{ThemeResource AccentBrush}", (string?)root.Attribute("Background"));
        Assert.Equal("{TemplateBinding Foreground}", (string?)root.Attribute("Foreground"));
        Assert.Empty(root.Elements());
    }

    [Fact]
    public void Bindings_format_positional_named_and_nested_arguments()
    {
        var b = new XbfBuilder();
        uint text = b.Property(b.Type(Controls, "TextBlock"), "Text");
        uint binding = b.Type($"{Xaml}.Data", "Binding");
        uint path = b.Property(binding, "Path");
        uint mode = b.Property(binding, "Mode");
        uint converter = b.Property(binding, "Converter");
        uint fallback = b.Property(binding, "FallbackValue");
        uint staticResource = b.Type(Xaml, "StaticResource");
        uint resourceKey = b.Property(staticResource, "ResourceKey");

        b.StartObject(b.Type(Controls, "TextBlock"))
            .StartProperty(text)
                .StartObject(binding)
                    .TextProperty(mode, "TwoWay")
                    .TextProperty(path, "User.Name")
                    .StartProperty(converter).StartObject(staticResource).TextProperty(resourceKey, "Upper").EndObject().EndProperty()
                    .TextProperty(fallback, "it's, complicated")
                .EndObject()
            .EndProperty()
            .EndObject();

        Assert.Equal(@"{Binding User.Name, Mode=TwoWay, Converter={StaticResource Upper}, FallbackValue='it\'s, complicated'}",
            (string?)DecompileRoot(b).Attribute("Text"));
    }

    [Fact]
    public void Types_filed_under_a_coarse_namespace_are_still_recognised()
    {
        // The Windows Phone 8.1 compiler files Binding and ScrollViewer under "Windows.UI.Xaml", and x:Null as
        // "Windows.UI.Xaml.Null" flagged as a directive (AUTOSUGGESTBOX.GENERIC.XBF, THEMERESOURCES.XBF).
        var b = new XbfBuilder();
        uint header = b.Property(b.Type(Controls, "TextBox"), "Header");
        uint foreground = b.Property(b.Type(Controls, "Control"), "Foreground");
        uint bringIntoView = b.Property(b.Type(Xaml, "ScrollViewer"), "BringIntoViewOnFocusChange");
        uint binding = b.Type(Xaml, "Binding");
        uint path = b.Property(binding, "Path");
        uint relativeSource = b.Property(binding, "RelativeSource");
        uint relativeSourceType = b.Type($"{Xaml}.Data", "RelativeSource");
        uint mode = b.Property(relativeSourceType, "Mode");
        uint nullType = b.Type(Xaml, "Null", TypeFlags.IsMarkupDirective);

        b.StartObject(b.Type(Controls, "TextBox"))
            .StartProperty(header)
                .StartObject(binding)
                    .StartProperty(relativeSource).StartObject(relativeSourceType).TextProperty(mode, "TemplatedParent").EndObject().EndProperty()
                    .TextProperty(path, "Header")
                .EndObject()
            .EndProperty()
            .StartProperty(foreground).StartObject(nullType).EndObject().EndProperty()
            .TextProperty(bringIntoView, "False")
            .EndObject();

        XElement root = DecompileRoot(b);

        Assert.Equal("{Binding Header, RelativeSource={RelativeSource TemplatedParent}}", (string?)root.Attribute("Header"));
        Assert.Equal("{x:Null}", (string?)root.Attribute("Foreground"));
        Assert.Equal("False", (string?)root.Attribute("ScrollViewer.BringIntoViewOnFocusChange"));
        Assert.Empty(root.Elements());
    }

    [Fact]
    public void ScrollViewer_attached_properties_on_a_ScrollViewer_are_unqualified_but_Grid_Row_on_a_Grid_is_not()
    {
        var b = new XbfBuilder();
        uint zoomMode = b.Property(b.Type(Controls, "ScrollViewer"), "ZoomMode");
        uint row = b.Property(b.Type(Controls, "Grid"), "Row");
        uint children = b.Property(b.Type(Controls, "Panel"), "Children");

        b.StartObject(b.Type(Controls, "Grid"))
            .StartProperty(children)
                .StartObject(b.Type(Controls, "ScrollViewer")).TextProperty(zoomMode, "Disabled").EndObject()
                .StartObject(b.Type(Controls, "Grid")).TextProperty(row, "1").EndObject()
            .EndProperty()
            .EndObject();

        XElement[] items = DecompileRoot(b).Elements().ToArray();

        Assert.Equal("Disabled", (string?)items[0].Attribute("ZoomMode"));
        Assert.Equal("1", (string?)items[1].Attribute("Grid.Row"));
    }

    [Fact]
    public void ContentPresenter_content_is_written_inline()
    {
        var b = new XbfBuilder();
        uint content = b.Property(b.Type(Controls, "ContentPresenter"), "Content");
        b.StartObject(b.Type(Controls, "ContentPresenter"))
            .StartProperty(content).StartObject(b.Type(Controls, "TextBlock")).EndObject().EndProperty()
            .EndObject();

        Assert.Equal("TextBlock", DecompileRoot(b).Elements().Single().Name.LocalName);
    }

    [Fact]
    public void Literal_values_starting_with_a_brace_are_escaped()
    {
        var b = new XbfBuilder();
        uint text = b.Property(b.Type(Controls, "TextBlock"), "Text");
        b.StartObject(b.Type(Controls, "TextBlock")).TextProperty(text, "{not an extension}").EndObject();

        Assert.Equal("{}{not an extension}", (string?)DecompileRoot(b).Attribute("Text"));
    }

    [Fact]
    public void Directives_use_the_x_namespace()
    {
        var b = new XbfBuilder();
        uint name = b.Property(0, "Name", PropertyFlags.IsMarkupDirective);
        uint key = b.Property(0, "Key", PropertyFlags.IsMarkupDirective);
        b.StartObject(b.Type(Controls, "Grid")).TextProperty(name, "LayoutRoot").TextProperty(key, "K").EndObject();

        XElement root = DecompileRoot(b);
        Assert.Equal("LayoutRoot", (string?)root.Attribute(XamlOutput.XamlNamespace + "Name"));
        Assert.Equal("K", (string?)root.Attribute(XamlOutput.XamlNamespace + "Key"));
    }

    [Fact]
    public void Implicit_initialization_becomes_element_text()
    {
        var b = new XbfBuilder();
        uint init = b.Property(b.Type(Xaml, "ResourceDictionary"), "__implicit_initialization", PropertyFlags.IsImplicitProperty);
        uint key = b.Property(0, "Key", PropertyFlags.IsMarkupDirective);
        b.StartObject(b.Type("Windows.Foundation", "String")).TextProperty(key, "Greeting").TextProperty(init, "Hello").EndObject();

        XElement root = DecompileRoot(b);
        Assert.Equal(XamlOutput.XamlNamespace + "String", root.Name);
        Assert.Equal("Hello", root.Value);
    }

    [Fact]
    public void Repeated_namespace_prefixes_do_not_throw()
    {
        var b = new XbfBuilder();
        uint x = b.XmlNamespace("http://schemas.microsoft.com/winfx/2006/xaml");
        uint pres = b.XmlNamespace(Presentation);
        b.Namespace(pres, "").Namespace(x, "x").Namespace(x, "x").Namespace(pres, "")
            .StartObject(b.Type(Controls, "Grid")).EndObject();

        Assert.StartsWith("<Grid xmlns=\"" + Presentation + "\" xmlns:x=", Decompile(b), StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_types_use_declared_using_prefix_and_keep_their_names()
    {
        var b = new XbfBuilder();
        uint local = b.XmlNamespace("using:My.App_Controls");
        b.Namespace(local, "local").StartObject(b.Type("My.App_Controls", "Fancy_Button")).EndObject();

        Assert.StartsWith("<local:Fancy_Button xmlns:local=\"using:My.App_Controls\"", Decompile(b), StringComparison.Ordinal);
    }

    [Fact]
    public void Deferred_storyboard_is_written_as_visual_state_content()
    {
        var b = new XbfBuilder();
        uint deferred = b.Property(b.Type(Xaml, "VisualState"), "__DeferredStoryboard");
        b.StartObject(b.Type(Xaml, "VisualState"))
            .StartProperty(deferred).StartObject(b.Type("Windows.UI.Xaml.Media.Animation", "Storyboard")).EndObject().EndProperty()
            .EndObject();

        XElement child = Assert.Single(DecompileRoot(b).Elements());
        Assert.Equal(XName.Get("Storyboard", Presentation), child.Name);
    }

    [Fact]
    public void Save_writes_utf8_xml()
    {
        var b = new XbfBuilder();
        b.StartObject(b.Type(Controls, "Grid")).EndObject();
        using var stream = new MemoryStream();

        XamlOutput.Save(new XamlOutput().GetOutput(b.Load()), stream);

        stream.Position = 0;
        Assert.Equal(XName.Get("Grid", Presentation), XDocument.Load(stream).Root!.Name);
    }
}
