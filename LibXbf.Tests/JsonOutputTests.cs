using System.Text.Json.Nodes;
using System.Xml.Linq;
using LibXbf.Output;

namespace LibXbf.Tests;

public class JsonOutputTests
{
    private const string Dictionary = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <ResourceDictionary.ThemeDictionaries>
            <ResourceDictionary x:Key="Default">
              <SolidColorBrush x:Key="PhoneAccentBrush" Color="{ThemeResource SystemColorControlAccentColor}" />
            </ResourceDictionary>
            <ResourceDictionary x:Key="Light">
              <Color x:Key="PhoneChromeColor">#FFDDDDDD</Color>
            </ResourceDictionary>
          </ResourceDictionary.ThemeDictionaries>
          <x:Double x:Key="PhoneButtonMinHeight">57.5</x:Double>
          <Style x:Key="BaseTextBlockStyle" TargetType="TextBlock">
            <Setter Property="LineHeight" Value="21.5" />
          </Style>
          <Style TargetType="Button">
            <Style.Setters>
              <Setter Property="MinHeight" Value="{ThemeResource PhoneButtonMinHeight}" />
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="Button">
                    <Grid x:Name="Grid" Background="Transparent">
                      <VisualStateManager.VisualStateGroups>
                        <VisualStateGroup x:Name="CommonStates">
                          <VisualStateGroup.Transitions>
                            <VisualTransition From="Pressed" To="PointerOver">
                              <Storyboard>
                                <PointerUpThemeAnimation Storyboard.TargetName="Grid" />
                              </Storyboard>
                            </VisualTransition>
                          </VisualStateGroup.Transitions>
                          <VisualState x:Name="Normal" />
                          <VisualState x:Name="Pressed">
                            <Storyboard RepeatBehavior="Forever">
                              <DoubleAnimationUsingKeyFrames Storyboard.TargetName="Border" Storyboard.TargetProperty="Opacity">
                                <EasingDoubleKeyFrame KeyTime="0:0:0.167" Value="0.5">
                                  <EasingDoubleKeyFrame.EasingFunction>
                                    <ExponentialEase EasingMode="EaseOut" Exponent="6" />
                                  </EasingDoubleKeyFrame.EasingFunction>
                                </EasingDoubleKeyFrame>
                              </DoubleAnimationUsingKeyFrames>
                            </Storyboard>
                          </VisualState>
                        </VisualStateGroup>
                      </VisualStateManager.VisualStateGroups>
                      <Border x:Name="Border" Margin="{ThemeResource PhoneTouchTargetOverhang}">
                        <ContentPresenter x:Name="ContentPresenter" />
                      </Border>
                      <Rectangle Width="38.5" />
                    </Grid>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style.Setters>
          </Style>
        </ResourceDictionary>
        """;

    private static JsonObject Convert() => JsonOutput.Convert(XDocument.Parse(Dictionary));

    [Fact]
    public void Resources_carry_their_key_theme_type_and_value()
    {
        JsonArray resources = Convert()["resources"]!.AsArray();

        Assert.Equal(3, resources.Count);
        JsonNode accent = resources.Single(r => (string?)r!["key"] == "PhoneAccentBrush")!;
        Assert.Equal("Default", (string?)accent["theme"]);
        Assert.Equal("SolidColorBrush", (string?)accent["type"]);
        Assert.Equal("{ThemeResource SystemColorControlAccentColor}", (string?)accent["attributes"]!["Color"]);

        JsonNode chrome = resources.Single(r => (string?)r!["key"] == "PhoneChromeColor")!;
        Assert.Equal("Light", (string?)chrome["theme"]);
        Assert.Equal("#FFDDDDDD", (string?)chrome["text"]);

        JsonNode minHeight = resources.Single(r => (string?)r!["key"] == "PhoneButtonMinHeight")!;
        Assert.Null(minHeight["theme"]);
        Assert.Equal("x:Double", (string?)minHeight["type"]);
        Assert.Equal("57.5", (string?)minHeight["text"]);
    }

    [Fact]
    public void Styles_list_setters_and_lift_the_template_out()
    {
        JsonArray styles = Convert()["styles"]!.AsArray();

        Assert.Equal("BaseTextBlockStyle", (string?)styles[0]!["key"]);
        JsonNode button = styles[1]!;
        Assert.Null(button["key"]);
        Assert.Equal("Button", (string?)button["targetType"]);
        JsonNode setter = Assert.Single(button["setters"]!.AsArray())!;
        Assert.Equal("MinHeight", (string?)setter["property"]);
        Assert.Equal("{ThemeResource PhoneButtonMinHeight}", (string?)setter["value"]);
        Assert.Equal("Button", (string?)button["template"]!["targetType"]);
    }

    [Fact]
    public void Template_lists_parts_with_paths_and_keeps_unnamed_elements_in_the_tree()
    {
        JsonNode template = Convert()["styles"]![1]!["template"]!;

        Assert.Equal(
            ["Grid#Grid", "Grid#Grid/Border#Border", "Grid#Grid/Border#Border/ContentPresenter#ContentPresenter"],
            template["parts"]!.AsArray().Select(p => (string?)p!["path"]));

        JsonNode tree = template["tree"]!;
        Assert.Equal("Grid", (string?)tree["type"]);
        Assert.Null(tree["properties"]);  // The visual state groups are lifted out of the tree.
        JsonNode rectangle = tree["children"]!.AsArray().Single(c => (string?)c!["type"] == "Rectangle")!;
        Assert.Equal("38.5", (string?)rectangle["attributes"]!["Width"]);
    }

    [Fact]
    public void Visual_states_list_animations_key_frames_and_easing()
    {
        JsonNode group = Assert.Single(Convert()["styles"]![1]!["template"]!["visualStateGroups"]!.AsArray())!;

        Assert.Equal("CommonStates", (string?)group["name"]);
        Assert.Equal("Grid#Grid", (string?)group["declaredOn"]);
        Assert.Equal(["Normal", "Pressed"], group["states"]!.AsArray().Select(s => (string?)s!["name"]));

        JsonNode storyboard = group["states"]![1]!["storyboard"]!;
        Assert.Equal("Forever", (string?)storyboard["attributes"]!["RepeatBehavior"]);
        JsonNode animation = Assert.Single(storyboard["animations"]!.AsArray())!;
        Assert.Equal("Border", (string?)animation["target"]);
        Assert.Equal("Opacity", (string?)animation["property"]);
        JsonNode keyFrame = Assert.Single(animation["keyFrames"]!.AsArray())!;
        Assert.Equal("0:0:0.167", (string?)keyFrame["attributes"]!["KeyTime"]);
        Assert.Equal("ExponentialEase", (string?)keyFrame["easing"]!["type"]);
        Assert.Equal("6", (string?)keyFrame["easing"]!["attributes"]!["Exponent"]);

        JsonNode transition = Assert.Single(group["transitions"]!.AsArray())!;
        Assert.Equal("Pressed", (string?)transition["from"]);
        Assert.Equal("PointerUpThemeAnimation", (string?)transition["storyboard"]!["animations"]![0]!["type"]);
    }

    [Fact]
    public void Xbf_files_give_the_same_shape_and_name_their_source()
    {
        var b = new XbfBuilder();
        uint style = b.Type("Windows.UI.Xaml", "Style");
        uint targetType = b.Property(style, "TargetType");
        uint items = b.Property(b.Type("Windows.UI.Xaml", "ResourceDictionary"), "__implicit_items", Records.Types.PropertyFlags.IsImplicitProperty);
        b.Namespace(b.XmlNamespace("http://schemas.microsoft.com/winfx/2006/xaml/presentation"), "")
            .StartObject(b.Type("Windows.UI.Xaml", "ResourceDictionary"))
            .StartProperty(items).StartObject(style).TextProperty(targetType, "Button").EndObject().EndProperty()
            .EndObject();

        JsonObject json = new JsonOutput().GetOutput(b.Load());

        Assert.Equal(JsonOutput.FormatId, (string?)json["format"]);
        Assert.Equal("xbf", (string?)json["source"]!["kind"]);
        Assert.Equal("Byte", (string?)json["source"]!["nodeEncoding"]);
        Assert.Equal("Button", (string?)json["styles"]![0]!["targetType"]);
        Assert.Contains("\"{ThemeResource", JsonOutput.ToJsonString(Convert()), StringComparison.Ordinal);
    }
}
