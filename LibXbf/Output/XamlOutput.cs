using System.Text;
using System.Xml;
using System.Xml.Linq;
using LibXbf.Records.Nodes;

namespace LibXbf.Output;

/// <summary>Reconstructs XAML markup from an <see cref="XbfFile"/>.</summary>
public sealed class XamlOutput : IXbfOutput<XDocument>
{
    public static readonly XNamespace PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    public static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    public XDocument GetOutput(XbfFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new Writer(file).Write();
    }

    /// <summary>Formats a document produced by <see cref="GetOutput"/> as indented markup.</summary>
    public static string ToXamlString(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Root?.ToString(SaveOptions.OmitDuplicateNamespaces) ?? "";
    }

    /// <summary>Saves a document produced by <see cref="GetOutput"/> as UTF-8.</summary>
    public static void Save(XDocument document, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(document);
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            NamespaceHandling = NamespaceHandling.OmitDuplicates,
        };
        using var writer = XmlWriter.Create(stream, settings);
        document.Save(writer);
    }

    private sealed class Writer(XbfFile file)
    {
        private static readonly System.Buffers.SearchValues<char> CharsNeedingQuotes =
            System.Buffers.SearchValues.Create(",={}'\"\\");

        public XDocument Write()
        {
            var declarations = new List<XAttribute>();
            var prefixes = new HashSet<string>(StringComparer.Ordinal);

            foreach (XbfNamespace ns in file.Namespaces)
            {
                // The same prefix can be declared on several elements; XML only allows one per element.
                if (!prefixes.Add(ns.Prefix))
                {
                    continue;
                }

                declarations.Add(new XAttribute(
                    ns.Prefix.Length == 0 ? "xmlns" : XNamespace.Xmlns + ns.Prefix, file.GetXmlNamespace(ns)));
            }

            return new XDocument(WriteObject(file.RootNode, declarations));
        }

        private XElement WriteObject(XbfObject obj, IEnumerable<XAttribute>? namespaceDeclarations = null)
        {
            XbfTypeInfo type = file.GetTypeInfo(obj.Id);
            var element = new XElement(TypeName(type), namespaceDeclarations);

            foreach (XbfProperty property in obj.Properties)
            {
                WriteProperty(element, type, property);
            }

            return element;
        }

        private void WriteProperty(XElement element, XbfTypeInfo objectType, XbfProperty xp)
        {
            XbfPropertyInfo property = file.GetPropertyInfo(xp.Id);

            // __implicit_items (collection/dictionary items) and __implicit_initialization (e.g. the text of
            // <x:String>) become direct content of the element.
            if (property.IsImplicit)
            {
                element.Add(Content(xp));
                return;
            }

            string name = PropertyName(property.Name);

            if (property.IsDirective)
            {
                if (TryGetAttributeValue(xp, out string? directiveValue))
                {
                    AddAttribute(element, XamlNamespace + name, directiveValue, xp);
                }
                else
                {
                    element.Add(new XElement(XamlNamespace + name, Content(xp)));
                }

                return;
            }

            // ScrollViewer.ZoomMode on a ScrollViewer is written like any other property, as the SDK's markup does.
            bool attached = KnownProperties.IsAttached(property.DeclaringType.FullName, name)
                && !(KnownProperties.IsAlsoInstanceProperty(property.DeclaringType.FullName, name)
                     && TypeLocalName(property.DeclaringType) == TypeLocalName(objectType));
            XbfTypeInfo owner = attached ? property.DeclaringType : objectType;

            if (TryGetAttributeValue(xp, out string? value))
            {
                // Unprefixed attributes are in no namespace (the default xmlns does not apply to attributes),
                // so only qualify attached properties that come from a non-default namespace.
                XName attributeName = attached
                    ? XmlNamespaceFor(owner) == PresentationNamespace
                        ? XName.Get(QualifiedName(owner, name))
                        : XmlNamespaceFor(owner) + QualifiedName(owner, name)
                    : XName.Get(EncodeName(name));

                AddAttribute(element, attributeName, value, xp);
                return;
            }

            if (!attached && KnownProperties.IsContentProperty(property.DeclaringType.FullName, name))
            {
                element.Add(Content(xp));
                return;
            }

            element.Add(new XElement(XmlNamespaceFor(owner) + QualifiedName(owner, name), Content(xp)));
        }

        private static void AddAttribute(XElement element, XName name, string value, XbfProperty xp)
        {
            if (element.Attribute(name) is null)
            {
                element.SetAttributeValue(name, value);
            }
            else
            {
                // The same property set twice; keep both rather than silently dropping one.
                // "--" is not allowed inside an XML comment.
                string safeValue = value.Replace("--", "- -", StringComparison.Ordinal);
                element.Add(new XComment($" duplicate {name.LocalName}=\"{safeValue}\" (line {xp.LineInfo.LineNumber}) "));
            }
        }

        /// <summary>The nodes for the values of a property written in element (not attribute) form.</summary>
        private IEnumerable<XNode> Content(XbfProperty xp)
        {
            foreach (XbfNode node in xp.Values)
            {
                switch (node)
                {
                    case XbfObject obj when obj.IsRetrieved && CanFlatten(obj):
                        // A collection retrieved from the property (e.g. UIElementCollection): only its items
                        // appear in markup.
                        foreach (XbfProperty items in obj.Properties)
                        {
                            foreach (XNode child in Content(items))
                            {
                                yield return child;
                            }
                        }

                        break;

                    case XbfObject obj:
                        yield return WriteObject(obj);
                        break;

                    case XbfText text:
                        yield return new XText(file.GetString(text.Id));
                        break;

                    case XbfValue val:
                        yield return new XText(val.Value);
                        break;
                }
            }
        }

        private bool CanFlatten(XbfObject obj) =>
            obj.Properties.TrueForAll(p => file.GetPropertyInfo(p.Id).IsImplicit);

        /// <summary>
        /// Gets the value of a property if it can be written as an attribute: a single literal, or a single
        /// markup extension such as <c>{ThemeResource Key}</c>.
        /// </summary>
        private bool TryGetAttributeValue(XbfProperty xp, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
        {
            value = null;
            if (xp.Values.Count != 1)
            {
                return false;
            }

            switch (xp.Values[0])
            {
                case XbfText text:
                    value = EscapeLiteral(file.GetString(text.Id));
                    return true;
                case XbfValue val:
                    value = EscapeLiteral(val.Value);
                    return true;
                case XbfObject obj:
                    return TryFormatMarkupExtension(obj, out value);
                default:
                    return false;
            }
        }

        private bool TryFormatMarkupExtension(XbfObject obj, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
        {
            value = null;
            if (obj.IsRetrieved)
            {
                return false;
            }

            XbfTypeInfo type = file.GetTypeInfo(obj.Id);
            if (!KnownProperties.TryGetMarkupExtension(type.FullName, out MarkupExtensionInfo? extension))
            {
                return false;
            }

            string? positional = null;
            var named = new List<string>();

            foreach (XbfProperty xp in obj.Properties)
            {
                XbfPropertyInfo property = file.GetPropertyInfo(xp.Id);
                if (!TryGetMarkupExtensionArgument(xp, out string? argument))
                {
                    return false;
                }

                if (positional is null && property.Name == extension.PositionalProperty)
                {
                    positional = argument;
                }
                else
                {
                    named.Add($"{property.Name}={argument}");
                }
            }

            var builder = new StringBuilder("{").Append(extension.Name);
            IEnumerable<string> arguments = positional is null ? named : named.Prepend(positional);
            string joined = string.Join(", ", arguments);
            if (joined.Length > 0)
            {
                builder.Append(' ').Append(joined);
            }

            value = builder.Append('}').ToString();
            return true;
        }

        private bool TryGetMarkupExtensionArgument(XbfProperty xp, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? argument)
        {
            argument = null;
            if (xp.Values.Count != 1)
            {
                return false;
            }

            switch (xp.Values[0])
            {
                case XbfText text:
                    argument = QuoteArgument(file.GetString(text.Id));
                    return true;
                case XbfValue val:
                    argument = QuoteArgument(val.Value);
                    return true;
                case XbfObject obj:
                    return TryFormatMarkupExtension(obj, out argument);
                default:
                    return false;
            }
        }

        private static XName TypeName(XbfTypeInfo type) => XmlNamespaceFor(type) + EncodeName(TypeLocalName(type));

        // Any "using:" namespace declared by the original markup is reused automatically, as LINQ to XML picks
        // up the prefix declared on the root element.
        private static XNamespace XmlNamespaceFor(XbfTypeInfo type) => type.Namespace switch
        {
            // Language types such as x:Null are flagged as directives, whatever namespace the compiler filed them in.
            _ when type.Flags.HasFlag(Records.Types.TypeFlags.IsMarkupDirective) => XamlNamespace,
            "x" or "Windows.Foundation" => XamlNamespace,
            // The Maps controls are not part of the default XAML namespace.
            "Windows.UI.Xaml.Controls.Maps" => "using:Windows.UI.Xaml.Controls.Maps",
            string ns when ns.StartsWith("Windows.UI", StringComparison.Ordinal) => PresentationNamespace,
            string ns => "using:" + ns,
        };

        private static string QualifiedName(XbfTypeInfo owner, string property) =>
            EncodeName(TypeLocalName(owner)) + "." + EncodeName(property);

        // Internal framework types are prefixed with '!' (e.g. "!VisualStateGroupCollection").
        private static string TypeLocalName(XbfTypeInfo type) => type.Name.TrimStart('!');

        // Deferred properties such as VisualState.__DeferredStoryboard are written as the real property.
        private static string PropertyName(string name) =>
            name.StartsWith("__Deferred", StringComparison.Ordinal) ? name["__Deferred".Length..] : name;

        private static string EncodeName(string name) => XmlConvert.EncodeLocalName(name) ?? name;

        // A literal attribute value starting with '{' would be parsed as a markup extension.
        private static string EscapeLiteral(string value) =>
            value.StartsWith('{') ? "{}" + value : value;

        private static string QuoteArgument(string value)
        {
            bool needsQuotes = value.Length == 0
                || value.AsSpan().ContainsAny(CharsNeedingQuotes)
                || char.IsWhiteSpace(value[0])
                || char.IsWhiteSpace(value[^1]);

            return needsQuotes
                ? "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'"
                : value;
        }
    }
}
