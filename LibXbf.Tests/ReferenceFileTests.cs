using System.Xml.Linq;
using LibXbf.Output;

namespace LibXbf.Tests;

/// <summary>
/// Runs the decompiler over real Windows Phone 8.1 XBF files. These are not redistributable, so the tests are
/// skipped unless the directory in <c>XBF_SAMPLES_DIR</c> (or the default location below) exists.
/// </summary>
public class ReferenceFileTests
{
    private const string DefaultSamplesDirectory = @"R:\cornflower-reference\flash-vhd\system32";

    private const string ButtonFile = "256-BUTTON.GENERIC.XBF-1033-WINDOWS.UI.XAML.PHONE.xbf";
    private const string AppBarButtonFile = "256-APPBARBUTTON.GENERIC.XBF-1033-WINDOWS.UI.XAML.PHONE.xbf";

    private static string SamplePath(string fileName)
    {
        string directory = Environment.GetEnvironmentVariable("XBF_SAMPLES_DIR") ?? DefaultSamplesDirectory;
        string path = Path.Combine(directory, fileName);
        Assert.SkipUnless(File.Exists(path), $"Sample file not found: {path} (set XBF_SAMPLES_DIR)");
        return path;
    }

    private static string Decompile(string fileName)
    {
        XbfFile file = XbfFile.Load(SamplePath(fileName));
        string xaml = XamlOutput.ToXamlString(new XamlOutput().GetOutput(file));

        // All views must handle the file, and the XAML must be well-formed XML.
        _ = new TreeOutput().GetOutput(file);
        _ = new MetadataOutput().GetOutput(file);
        _ = XDocument.Parse(xaml);
        return xaml;
    }

    [Theory]
    [InlineData(ButtonFile)]
    [InlineData(AppBarButtonFile)]
    public void Output_contains_no_internal_or_retrieved_objects(string fileName)
    {
        string xaml = Decompile(fileName);

        Assert.DoesNotContain("Collection", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("implicit", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Deferred", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ThemeResource", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<TemplateBinding", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Style.Setters", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlTemplate.Template", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Button_template_matches_expected_markup()
    {
        string xaml = Decompile(ButtonFile);

        Assert.Contains("<Style TargetType=\"Button\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"BorderBrush\" Value=\"{ThemeResource PhoneForegroundBrush}\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter.Value>", xaml, StringComparison.Ordinal);
        Assert.Contains("<ControlTemplate TargetType=\"Button\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<VisualStateManager.VisualStateGroups>", xaml, StringComparison.Ordinal);
        Assert.Contains("<VisualStateGroup.Transitions>", xaml, StringComparison.Ordinal);
        Assert.Contains("<PointerDownThemeAnimation Storyboard.TargetName=\"Grid\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"{TemplateBinding BorderBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AccessibilityView=\"Raw\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AppBarButton_template_matches_expected_markup()
    {
        string xaml = Decompile(AppBarButtonFile);

        Assert.Contains("<StackPanel x:Name=\"LayoutRoot\" Width=\"80\" Background=\"Transparent\">", xaml, StringComparison.Ordinal);
        // Encoded as IsBoolFalse; the original decompiler printed "True".
        Assert.Contains("UseLayoutRounding=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"0,12,0,4\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{TemplateBinding Icon}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_template_in_the_phone_dll_extracts_and_decompiles()
    {
        IReadOnlyList<Pe.PeResource> resources = Pe.PeResource.Read(SamplePath("WINDOWS.UI.XAML.PHONE.DLL"), type: "256");

        Assert.Equal(60, resources.Count);
        Assert.All(resources, r => Assert.True(r.IsXbf, r.Name));
        Assert.Contains(resources, r => r is { Name: "BUTTON.GENERIC.XBF", Language: 1033 });

        // The extracted resource is the reference sample, byte for byte.
        Assert.Equal(File.ReadAllBytes(SamplePath(ButtonFile)), resources.Single(r => r.Name == "BUTTON.GENERIC.XBF").Data);

        foreach (Pe.PeResource resource in resources)
        {
            XbfFile file = XbfFile.Load(new MemoryStream(resource.Data));
            _ = XDocument.Parse(XamlOutput.ToXamlString(new XamlOutput().GetOutput(file)));
            _ = new JsonOutput().GetOutput(file);

            // Only ListViewBaseItem uses the word encoding, and only it carries a hash.
            Assert.Equal(
                resource.Name == "LISTVIEWBASEITEM.GENERIC.XBF" ? Records.Nodes.XbfNodeEncoding.Word : Records.Nodes.XbfNodeEncoding.Byte,
                file.NodeEncoding);
        }

        string progressBar = XamlOutput.ToXamlString(new XamlOutput().GetOutput(
            XbfFile.Load(new MemoryStream(resources.Single(r => r.Name == "PROGRESSBAR.GENERIC.XBF").Data))));
        // The SDK's generic.xaml has the same values: Duration="0:0:3.917", KeyTime="0:0:0.167".
        Assert.Contains("Duration=\"0:0:3.917\"", progressBar, StringComparison.Ordinal);
        Assert.Contains("KeyTime=\"0:0:0.167\"", progressBar, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_layout_matches_file()
    {
        XbfFile file = XbfFile.Load(SamplePath(ButtonFile));

        Assert.Equal(new Version(1, 0), file.FileVersion);
        Assert.Equal(new FileInfo(SamplePath(ButtonFile)).Length, file.Header.NodeStreamOffset + file.Header.NodeSize);
    }
}
