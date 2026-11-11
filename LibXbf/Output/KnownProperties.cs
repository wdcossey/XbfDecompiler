using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace LibXbf.Output;

internal sealed record MarkupExtensionInfo(string Name, string? PositionalProperty);

/// <summary>
/// Framework knowledge that XBF v1 does not encode: which properties are attached properties, which are content
/// properties (written as direct children rather than property elements), and which types are markup extensions.
/// Covers the Windows 8.1 / Windows Phone 8.1 <c>Windows.UI.Xaml</c> framework.
/// </summary>
internal static class KnownProperties
{
    private const string Xaml = "Windows.UI.Xaml";
    private const string Controls = "Windows.UI.Xaml.Controls";
    private const string Animation = "Windows.UI.Xaml.Media.Animation";
    private const string Media = "Windows.UI.Xaml.Media";
    private const string Documents = "Windows.UI.Xaml.Documents";

    /// <summary>Types whose every declared property is an attached property.</summary>
    private static readonly FrozenSet<string> AttachedOnlyOwners = new[]
    {
        $"{Xaml}.VisualStateManager",
        $"{Xaml}.Automation.AutomationProperties",
        $"{Controls}.Canvas",
        $"{Controls}.ToolTipService",
        $"{Documents}.Typography",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Attached properties declared on types that also have ordinary properties.</summary>
    private static readonly FrozenSet<string> AttachedProperties = new[]
    {
        $"{Controls}.Grid.Row",
        $"{Controls}.Grid.Column",
        $"{Controls}.Grid.RowSpan",
        $"{Controls}.Grid.ColumnSpan",
        $"{Controls}.VariableSizedWrapGrid.RowSpan",
        $"{Controls}.VariableSizedWrapGrid.ColumnSpan",
        $"{Animation}.Storyboard.TargetName",
        $"{Animation}.Storyboard.TargetProperty",
        $"{Controls}.Primitives.FlyoutBase.AttachedFlyout",
        $"{Controls}.Pivot.SlideInAnimationGroup",
        $"{Controls}.ScrollViewer.HorizontalScrollBarVisibility",
        $"{Controls}.ScrollViewer.VerticalScrollBarVisibility",
        $"{Controls}.ScrollViewer.HorizontalScrollMode",
        $"{Controls}.ScrollViewer.VerticalScrollMode",
        $"{Controls}.ScrollViewer.IsHorizontalRailEnabled",
        $"{Controls}.ScrollViewer.IsVerticalRailEnabled",
        $"{Controls}.ScrollViewer.IsHorizontalScrollChainingEnabled",
        $"{Controls}.ScrollViewer.IsVerticalScrollChainingEnabled",
        $"{Controls}.ScrollViewer.IsZoomChainingEnabled",
        $"{Controls}.ScrollViewer.IsScrollInertiaEnabled",
        $"{Controls}.ScrollViewer.IsZoomInertiaEnabled",
        $"{Controls}.ScrollViewer.ZoomMode",
        $"{Controls}.ScrollViewer.IsDeferredScrollingEnabled",
        $"{Controls}.ScrollViewer.BringIntoViewOnFocusChange",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Content properties, keyed by declaring type. Only properties known to be the type's
    /// <c>[ContentProperty]</c> are listed: missing one just produces a (valid) explicit property element,
    /// whereas a wrong entry would produce invalid markup.
    /// </summary>
    private static readonly FrozenSet<string> ContentProperties = new[]
    {
        // Not a real property: the content of a ControlTemplate/DataTemplate. Must never be a property element.
        $"{Xaml}.FrameworkTemplate.Template",
        $"{Xaml}.Style.Setters",
        $"{Xaml}.VisualStateGroup.States",
        $"{Xaml}.VisualState.Storyboard",
        $"{Xaml}.VisualTransition.Storyboard",
        $"{Controls}.Panel.Children",
        $"{Controls}.Border.Child",
        $"{Controls}.ContentControl.Content",
        $"{Controls}.UserControl.Content",
        $"{Controls}.ContentPresenter.Content",
        $"{Controls}.ItemsControl.Items",
        $"{Controls}.Viewbox.Child",
        $"{Controls}.Primitives.Popup.Child",
        $"{Controls}.TextBlock.Inlines",
        $"{Controls}.RichTextBlock.Blocks",
        $"{Documents}.Paragraph.Inlines",
        $"{Documents}.Span.Inlines",
        $"{Animation}.Storyboard.Children",
        $"{Animation}.ObjectAnimationUsingKeyFrames.KeyFrames",
        $"{Animation}.DoubleAnimationUsingKeyFrames.KeyFrames",
        $"{Animation}.ColorAnimationUsingKeyFrames.KeyFrames",
        $"{Animation}.PointAnimationUsingKeyFrames.KeyFrames",
        $"{Media}.GradientBrush.GradientStops",
        $"{Media}.TransformGroup.Children",
        $"{Media}.GeometryGroup.Children",
        $"{Media}.PathGeometry.Figures",
        $"{Media}.PathFigure.Segments",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, MarkupExtensionInfo> MarkupExtensions =
        new Dictionary<string, MarkupExtensionInfo>(StringComparer.Ordinal)
        {
            [$"{Xaml}.ThemeResource"] = new("ThemeResource", "ResourceKey"),
            [$"{Xaml}.StaticResource"] = new("StaticResource", "ResourceKey"),
            [$"{Xaml}.CustomResource"] = new("CustomResource", "ResourceKey"),
            [$"{Xaml}.Data.TemplateBinding"] = new("TemplateBinding", "Property"),
            [$"{Xaml}.Data.Binding"] = new("Binding", "Path"),
            [$"{Xaml}.Data.RelativeSource"] = new("RelativeSource", "Mode"),
            ["x.Null"] = new("x:Null", null),
            ["x.NullExtension"] = new("x:Null", null),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Every type named in the tables above, by its simple name, where the simple name is unique. The Windows Phone
    /// 8.1 compiler files some types under a coarser namespace than their real one, e.g. <c>Binding</c> and
    /// <c>ScrollViewer</c> as <c>Windows.UI.Xaml.Binding</c> and <c>Windows.UI.Xaml.ScrollViewer</c>, and
    /// <c>x:Null</c> as <c>Windows.UI.Xaml.Null</c>.
    /// </summary>
    private static readonly FrozenDictionary<string, string> TypesBySimpleName = BuildSimpleNames();

    /// <summary>
    /// Whether an attached property is also an ordinary property of its owner, so that on the owner itself it is
    /// written unqualified: <c>&lt;ScrollViewer ZoomMode="Disabled"&gt;</c>. Only ScrollViewer's are; Grid.Row on a
    /// Grid stays <c>Grid.Row</c>.
    /// </summary>
    public static bool IsAlsoInstanceProperty(string declaringTypeFullName, string propertyName) =>
        Canonical(declaringTypeFullName) == $"{Controls}.ScrollViewer"
        && AttachedProperties.Contains($"{Controls}.ScrollViewer.{propertyName}");

    public static bool IsAttached(string declaringTypeFullName, string propertyName)
    {
        string type = Canonical(declaringTypeFullName);
        return AttachedOnlyOwners.Contains(type) || AttachedProperties.Contains($"{type}.{propertyName}");
    }

    public static bool IsContentProperty(string declaringTypeFullName, string propertyName) =>
        ContentProperties.Contains($"{Canonical(declaringTypeFullName)}.{propertyName}");

    public static bool TryGetMarkupExtension(string typeFullName, [NotNullWhen(true)] out MarkupExtensionInfo? info) =>
        MarkupExtensions.TryGetValue(Canonical(typeFullName), out info);

    /// <summary>The name the tables use for a type, allowing for the coarse namespaces described above.</summary>
    private static string Canonical(string typeFullName)
    {
        if (!typeFullName.StartsWith(Xaml, StringComparison.Ordinal))
        {
            return typeFullName;
        }

        string simpleName = typeFullName[(typeFullName.LastIndexOf('.') + 1)..];
        return TypesBySimpleName.TryGetValue(simpleName, out string? canonical) ? canonical : typeFullName;
    }

    private static FrozenDictionary<string, string> BuildSimpleNames()
    {
        IEnumerable<string> types = AttachedOnlyOwners
            .Concat(AttachedProperties.Select(OwnerOf))
            .Concat(ContentProperties.Select(OwnerOf))
            .Concat(MarkupExtensions.Keys);

        return types
            .Distinct(StringComparer.Ordinal)
            .GroupBy(t => t[(t.LastIndexOf('.') + 1)..], StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToFrozenDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        static string OwnerOf(string property) => property[..property.LastIndexOf('.')];
    }
}
