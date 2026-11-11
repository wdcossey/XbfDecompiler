using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace LibXbf.Output;

/// <summary>
/// Extracts what a theme author looks up from a XAML resource dictionary as JSON: every resource (by key and theme
/// dictionary), every style with its setters, and every control template's element tree, named parts, visual states,
/// transitions and animations (targets, key frames, easing).
/// </summary>
/// <remarks>
/// <para>
/// The conversion runs on XAML, so <see cref="Convert(XDocument)"/> gives the same shape for markup that never was
/// XBF, such as an SDK's <c>generic.xaml</c>. That makes a runtime template and its design-time source comparable.
/// </para>
/// <para>
/// Values are strings as they appear in XAML (<c>"0,9.5"</c>, <c>"{ThemeResource PhoneAccentBrush}"</c>,
/// <c>"0:0:0.167"</c>). Fields without a value are left out. Type and attribute names in the XAML language
/// namespace are prefixed <c>x:</c>; all others are bare.
/// </para>
/// </remarks>
public sealed class JsonOutput : IXbfOutput<JsonObject>
{
    /// <summary>Identifies the shape of the document; bumped on incompatible changes.</summary>
    public const string FormatId = "xbf-json/1";

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        // Keep markup readable: "{ThemeResource X}" rather than "{ThemeResource X}".
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public JsonObject GetOutput(XbfFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var source = new JsonObject
        {
            ["kind"] = "xbf",
            ["xbfVersion"] = file.FileVersion.ToString(),
            ["nodeEncoding"] = file.NodeEncoding.ToString(),
        };
        return new Converter().Convert(new XamlOutput().GetOutput(file), source);
    }

    /// <summary>Converts XAML markup, such as an SDK's <c>generic.xaml</c>, to the same JSON shape.</summary>
    public static JsonObject Convert(XDocument xaml)
    {
        ArgumentNullException.ThrowIfNull(xaml);
        return new Converter().Convert(xaml, new JsonObject { ["kind"] = "xaml" });
    }

    public static string ToJsonString(JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return json.ToJsonString(Indented);
    }

    private sealed class Converter
    {
        private readonly JsonArray _resources = [];
        private readonly JsonArray _styles = [];

        public JsonObject Convert(XDocument xaml, JsonObject source)
        {
            XElement root = xaml.Root ?? throw new ArgumentException("The document has no root element.", nameof(xaml));
            var result = new JsonObject
            {
                ["format"] = FormatId,
                ["source"] = source,
                ["root"] = TypeName(root),
            };

            if (root.Name.LocalName == "ResourceDictionary")
            {
                CollectResources(root, theme: null);
                result["resources"] = _resources;
                result["styles"] = _styles;
            }
            else
            {
                result["tree"] = Node(root, path: "", parts: null, groups: null);
            }

            return result;
        }

        private void CollectResources(XElement dictionary, string? theme)
        {
            foreach (XElement child in dictionary.Elements())
            {
                if (IsPropertyElement(child))
                {
                    string property = child.Name.LocalName;
                    if (property.EndsWith(".ThemeDictionaries", StringComparison.Ordinal))
                    {
                        foreach (XElement themeDictionary in child.Elements())
                        {
                            CollectResources(themeDictionary, Key(themeDictionary));
                        }
                    }
                    else if (property.EndsWith(".MergedDictionaries", StringComparison.Ordinal))
                    {
                        foreach (XElement merged in child.Elements())
                        {
                            CollectResources(merged, theme);
                        }
                    }

                    continue;
                }

                if (child.Name.LocalName == "Style")
                {
                    _styles.Add(Style(child, theme));
                }
                else
                {
                    JsonObject resource = Node(child, path: "", parts: null, groups: null);
                    var entry = new JsonObject();
                    Set(entry, "key", Key(child));
                    Set(entry, "theme", theme);
                    foreach (KeyValuePair<string, JsonNode?> field in resource.ToList())
                    {
                        resource.Remove(field.Key);
                        entry[field.Key] = field.Value;
                    }

                    _resources.Add(entry);
                }
            }
        }

        private JsonObject Style(XElement style, string? theme)
        {
            var result = new JsonObject();
            Set(result, "key", Key(style));
            Set(result, "theme", theme);
            Set(result, "targetType", (string?)style.Attribute("TargetType"));
            Set(result, "basedOn", (string?)style.Attribute("BasedOn"));

            var setters = new JsonArray();
            JsonObject? template = null;
            foreach (XElement setter in ContentAndProperty(style, "Setters").Where(e => e.Name.LocalName == "Setter"))
            {
                string? property = (string?)setter.Attribute("Property");
                var entry = new JsonObject();
                Set(entry, "property", property);

                if (setter.Attribute("Value") is { } value)
                {
                    entry["value"] = value.Value;
                }
                else if (setter.Elements().FirstOrDefault(e => e.Name.LocalName == "Setter.Value") is { } valueElement)
                {
                    XElement[] content = valueElement.Elements().ToArray();
                    if (property == "Template" && content is [{ Name.LocalName: "ControlTemplate" } controlTemplate])
                    {
                        template = Template(controlTemplate);
                        continue;
                    }

                    entry["valueTree"] = Nodes(valueElement, path: "", parts: null, groups: null);
                }

                setters.Add(entry);
            }

            result["setters"] = setters;
            if (template is not null)
            {
                result["template"] = template;
            }

            return result;
        }

        private JsonObject Template(XElement controlTemplate)
        {
            var parts = new JsonArray();
            var groups = new JsonArray();
            var result = new JsonObject();
            Set(result, "targetType", (string?)controlTemplate.Attribute("TargetType"));

            XElement[] content = controlTemplate.Elements().Where(e => !IsPropertyElement(e)).ToArray();
            if (content.Length == 1)
            {
                result["tree"] = Node(content[0], path: "", parts, groups);
            }
            else if (content.Length > 1)
            {
                result["tree"] = new JsonArray(content.Select(e => (JsonNode)Node(e, path: "", parts, groups)).ToArray());
            }

            result["parts"] = parts;
            result["visualStateGroups"] = groups;
            return result;
        }

        /// <summary>
        /// A generic element: its type, name, attributes, text, property elements and children. Inside a template,
        /// named elements are also listed in <paramref name="parts"/> and visual state groups are moved to
        /// <paramref name="groups"/>.
        /// </summary>
        private JsonObject Node(XElement element, string path, JsonArray? parts, JsonArray? groups)
        {
            string type = TypeName(element);
            string? name = Name(element);
            string here = path.Length == 0 ? type : $"{path}/{type}";
            if (name is not null)
            {
                here += "#" + name;
            }

            var result = new JsonObject { ["type"] = type };
            Set(result, "name", name);

            if (Attributes(element) is { Count: > 0 } attributes)
            {
                result["attributes"] = attributes;
            }

            if (!element.HasElements && element.Value.Trim() is { Length: > 0 } text)
            {
                result["text"] = text;
            }

            if (name is not null && parts is not null)
            {
                parts.Add(new JsonObject { ["name"] = name, ["type"] = type, ["path"] = here });
            }

            JsonObject? properties = null;
            JsonArray? children = null;
            foreach (XElement child in element.Elements())
            {
                if (!IsPropertyElement(child))
                {
                    (children ??= []).Add(Node(child, here, parts, groups));
                    continue;
                }

                string property = child.Name.LocalName;
                if (groups is not null && property == "VisualStateManager.VisualStateGroups")
                {
                    foreach (XElement group in child.Elements())
                    {
                        groups.Add(VisualStateGroup(group, here));
                    }

                    continue;
                }

                properties ??= [];
                properties[property] = Nodes(child, here, parts, groups);
            }

            if (properties is not null)
            {
                result["properties"] = properties;
            }

            if (children is not null)
            {
                result["children"] = children;
            }

            return result;
        }

        /// <summary>The content of a property element: nodes, or the text if it has no elements.</summary>
        private JsonArray Nodes(XElement propertyElement, string path, JsonArray? parts, JsonArray? groups)
        {
            if (!propertyElement.HasElements)
            {
                return [propertyElement.Value];
            }

            var result = new JsonArray();
            foreach (XElement child in propertyElement.Elements())
            {
                result.Add(Node(child, path, parts, groups));
            }

            return result;
        }

        private JsonObject VisualStateGroup(XElement group, string ownerPath)
        {
            var states = new JsonArray();
            var transitions = new JsonArray();

            foreach (XElement state in ContentAndProperty(group, "States").Where(e => e.Name.LocalName == "VisualState"))
            {
                var entry = new JsonObject();
                Set(entry, "name", Name(state));
                if (Storyboard(state) is { } storyboard)
                {
                    entry["storyboard"] = storyboard;
                }

                states.Add(entry);
            }

            foreach (XElement transition in ContentAndProperty(group, "Transitions")
                         .Where(e => e.Name.LocalName == "VisualTransition"))
            {
                var entry = new JsonObject();
                Set(entry, "from", (string?)transition.Attribute("From"));
                Set(entry, "to", (string?)transition.Attribute("To"));
                Set(entry, "generatedDuration", (string?)transition.Attribute("GeneratedDuration"));
                if (transition.Elements().FirstOrDefault(e => e.Name.LocalName == "VisualTransition.GeneratedEasingFunction")
                    is { } easing && easing.Elements().FirstOrDefault() is { } easingFunction)
                {
                    entry["generatedEasing"] = Node(easingFunction, "", null, null);
                }

                if (Storyboard(transition) is { } storyboard)
                {
                    entry["storyboard"] = storyboard;
                }

                transitions.Add(entry);
            }

            var result = new JsonObject();
            Set(result, "name", Name(group));
            result["declaredOn"] = ownerPath;
            result["states"] = states;
            if (transitions.Count > 0)
            {
                result["transitions"] = transitions;
            }

            return result;
        }

        private JsonObject? Storyboard(XElement owner)
        {
            XElement? storyboard = ContentAndProperty(owner, "Storyboard")
                .FirstOrDefault(e => e.Name.LocalName == "Storyboard");
            if (storyboard is null)
            {
                return null;
            }

            var result = new JsonObject();
            if (Attributes(storyboard) is { Count: > 0 } attributes)
            {
                result["attributes"] = attributes;
            }

            result["animations"] = new JsonArray(ContentAndProperty(storyboard, "Children")
                .Select(a => (JsonNode)Animation(a)).ToArray());
            return result;
        }

        private JsonObject Animation(XElement animation)
        {
            var result = new JsonObject { ["type"] = TypeName(animation) };
            Set(result, "target", (string?)animation.Attribute("Storyboard.TargetName"));
            Set(result, "property", (string?)animation.Attribute("Storyboard.TargetProperty"));

            JsonObject attributes = Attributes(animation);
            attributes.Remove("Storyboard.TargetName");
            attributes.Remove("Storyboard.TargetProperty");
            if (attributes.Count > 0)
            {
                result["attributes"] = attributes;
            }

            JsonArray? keyFrames = null;
            foreach (XElement child in animation.Elements())
            {
                if (!IsPropertyElement(child))
                {
                    (keyFrames ??= []).Add(KeyFrame(child));
                }
                else if (child.Name.LocalName.EndsWith(".EasingFunction", StringComparison.Ordinal)
                         && child.Elements().FirstOrDefault() is { } easing)
                {
                    result["easing"] = Node(easing, "", null, null);
                }
                else if (child.Name.LocalName.EndsWith(".KeyFrames", StringComparison.Ordinal))
                {
                    foreach (XElement keyFrame in child.Elements())
                    {
                        (keyFrames ??= []).Add(KeyFrame(keyFrame));
                    }
                }
            }

            if (keyFrames is not null)
            {
                result["keyFrames"] = keyFrames;
            }

            return result;
        }

        private JsonObject KeyFrame(XElement keyFrame)
        {
            var result = new JsonObject { ["type"] = TypeName(keyFrame) };
            if (Attributes(keyFrame) is { Count: > 0 } attributes)
            {
                result["attributes"] = attributes;
            }

            foreach (XElement child in keyFrame.Elements().Where(IsPropertyElement))
            {
                string property = child.Name.LocalName;
                if (property.EndsWith(".EasingFunction", StringComparison.Ordinal) && child.Elements().FirstOrDefault() is { } easing)
                {
                    result["easing"] = Node(easing, "", null, null);
                }
                else if (property.EndsWith(".Value", StringComparison.Ordinal))
                {
                    result["valueTree"] = Nodes(child, "", null, null);
                }
            }

            return result;
        }

        /// <summary>
        /// The elements of a collection property, whether written as content or as a property element
        /// (<c>&lt;Style.Setters&gt;</c>); markup from different sources uses either.
        /// </summary>
        private static IEnumerable<XElement> ContentAndProperty(XElement owner, string property)
        {
            foreach (XElement child in owner.Elements())
            {
                if (!IsPropertyElement(child))
                {
                    yield return child;
                }
                else if (child.Name.LocalName == $"{owner.Name.LocalName}.{property}")
                {
                    foreach (XElement item in child.Elements())
                    {
                        yield return item;
                    }
                }
            }
        }

        private static JsonObject Attributes(XElement element)
        {
            var result = new JsonObject();
            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration
                    || attribute.Name == XamlOutput.XamlNamespace + "Key"
                    || attribute.Name == XamlOutput.XamlNamespace + "Name"
                    || attribute.Name == "Name")
                {
                    continue;
                }

                result[AttributeName(attribute)] = attribute.Value;
            }

            return result;
        }

        private static string? Key(XElement element) => (string?)element.Attribute(XamlOutput.XamlNamespace + "Key");

        private static string? Name(XElement element) =>
            (string?)element.Attribute(XamlOutput.XamlNamespace + "Name") ?? (string?)element.Attribute("Name");

        private static bool IsPropertyElement(XElement element) => element.Name.LocalName.Contains('.', StringComparison.Ordinal);

        private static string TypeName(XElement element) =>
            element.Name.Namespace == XamlOutput.XamlNamespace ? "x:" + element.Name.LocalName : element.Name.LocalName;

        private static string AttributeName(XAttribute attribute) =>
            attribute.Name.Namespace == XamlOutput.XamlNamespace ? "x:" + attribute.Name.LocalName : attribute.Name.LocalName;

        private static void Set(JsonObject target, string name, string? value)
        {
            if (value is not null)
            {
                target[name] = value;
            }
        }
    }
}
