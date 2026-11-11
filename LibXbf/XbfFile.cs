using System.Text;
using LibXbf.Records;
using LibXbf.Records.Nodes;
using LibXbf.Records.Types;

namespace LibXbf;

/// <summary>Resolved information about an entry in the type table.</summary>
/// <param name="Name">The type name, e.g. <c>Grid</c>.</param>
/// <param name="Namespace">The CLR/WinRT namespace, e.g. <c>Windows.UI.Xaml.Controls</c>.</param>
public sealed record XbfTypeInfo(string Name, string Namespace, TypeFlags Flags)
{
    public string FullName => Namespace.Length == 0 ? Name : $"{Namespace}.{Name}";

    public override string ToString() => FullName;
}

/// <summary>Resolved information about an entry in the property table.</summary>
public sealed record XbfPropertyInfo(string Name, XbfTypeInfo DeclaringType, PropertyFlags Flags)
{
    public bool IsDirective => Flags.HasFlag(PropertyFlags.IsMarkupDirective);

    public bool IsImplicit => Flags.HasFlag(PropertyFlags.IsImplicitProperty);

    public override string ToString() => $"{DeclaringType.Name}.{Name}";
}

/// <summary>A parsed XAML Binary Format (XBF) v1 file.</summary>
public sealed class XbfFile
{
    private readonly List<XbfNamespace> _namespaces = [];

    private XbfFile(BinaryReader br)
    {
        Header = new XbfHeader(br);
        FileVersion = Header.FileVersion;

        // XBF v2 (Windows 10+) uses an entirely different, bytecode-style node stream.
        if (Header.MajorFileVersion != 1)
        {
            throw new UnsupportedXbfVersionException(FileVersion);
        }

        long expectedLength = Header.NodeStreamOffset + Header.NodeSize;
        if (br.BaseStream.Length < expectedLength)
        {
            throw new InvalidXbfException(
                $"The file is truncated: the header describes {expectedLength} bytes but the file is only {br.BaseStream.Length} bytes.");
        }

        StringTable = new XbfTable<string>("string", Seek(br, Header.StringTableOffset), 4,
            static r => ReadTableString(r));
        AssemblyTable = new XbfTable<AssemblyEntry>("assembly", Seek(br, Header.AssemblyTableOffset),
            AssemblyEntry.Size, AssemblyEntry.Read);
        TypeNamespaceTable = new XbfTable<TypeNamespaceEntry>("type namespace", Seek(br, Header.TypeNamespaceTableOffset),
            TypeNamespaceEntry.Size, TypeNamespaceEntry.Read);
        TypeTable = new XbfTable<TypeEntry>("type", Seek(br, Header.TypeTableOffset), TypeEntry.Size, TypeEntry.Read);
        PropertyTable = new XbfTable<PropertyEntry>("property", Seek(br, Header.PropertyTableOffset),
            PropertyEntry.Size, PropertyEntry.Read);
        XmlNamespaceTable = new XbfTable<uint>("XML namespace", Seek(br, Header.XmlNamespaceTableOffset), 4,
            static r => r.ReadUInt32());

        (RootNode, NodeEncoding) = ReadNodeStream(br, expectedLength);
    }

    public Version FileVersion { get; }

    /// <summary>How the node stream encodes node tags; see <see cref="XbfNodeEncoding"/>.</summary>
    public XbfNodeEncoding NodeEncoding { get; }

    public XbfHeader Header { get; }

    public XbfTable<string> StringTable { get; }

    public XbfTable<AssemblyEntry> AssemblyTable { get; }

    public XbfTable<TypeNamespaceEntry> TypeNamespaceTable { get; }

    public XbfTable<TypeEntry> TypeTable { get; }

    public XbfTable<PropertyEntry> PropertyTable { get; }

    /// <summary>Maps an XML namespace id to an index in the <see cref="StringTable"/>.</summary>
    public XbfTable<uint> XmlNamespaceTable { get; }

    public XbfObject RootNode { get; }

    /// <summary>Every <c>xmlns</c> declaration in the file, in document order.</summary>
    public IReadOnlyList<XbfNamespace> Namespaces => _namespaces;

    /// <exception cref="InvalidXbfException">The file is not a valid XBF file.</exception>
    /// <exception cref="UnsupportedXbfVersionException">The file is an XBF file of an unsupported version.</exception>
    public static XbfFile Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Load(stream);
    }

    /// <inheritdoc cref="Load(string)"/>
    public static XbfFile Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            buffer.Position = 0;
            stream = buffer;
        }

        using var br = new BinaryReader(stream, Encoding.Unicode, leaveOpen: true);
        try
        {
            return new XbfFile(br);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidXbfException("The file ended unexpectedly.", ex);
        }
    }

    /// <summary>Reads the file asynchronously, then parses it.</summary>
    /// <inheritdoc cref="Load(string)"/>
    public static async Task<XbfFile> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Load(new MemoryStream(bytes, writable: false));
    }

    public string GetString(uint id) => StringTable[id];

    public XbfTypeInfo GetTypeInfo(uint typeId)
    {
        TypeEntry type = TypeTable[typeId];
        string ns = GetString(TypeNamespaceTable[type.NamespaceId].StringId);
        return new XbfTypeInfo(GetString(type.StringId), ns, type.Flags);
    }

    public XbfPropertyInfo GetPropertyInfo(uint propertyId)
    {
        PropertyEntry property = PropertyTable[propertyId];

        // Directives (x:Name, x:Key, ...) do not belong to a real type, so don't try to resolve one.
        XbfTypeInfo declaringType = property.Flags.HasFlag(PropertyFlags.IsMarkupDirective)
            ? new XbfTypeInfo("", "x", TypeFlags.IsMarkupDirective)
            : GetTypeInfo(property.TypeId);

        return new XbfPropertyInfo(GetString(property.StringId), declaringType, property.Flags);
    }

    /// <summary>Returns the XML namespace URI of an <c>xmlns</c> declaration.</summary>
    public string GetXmlNamespace(XbfNamespace ns)
    {
        ArgumentNullException.ThrowIfNull(ns);
        return GetString(XmlNamespaceTable[ns.Id]);
    }

    private static BinaryReader Seek(BinaryReader br, ulong tableOffset)
    {
        ulong position = XbfHeader.OffsetBase + tableOffset;
        if (position > (ulong)br.BaseStream.Length)
        {
            throw new InvalidXbfException($"Table offset 0x{tableOffset:X} lies outside the file.");
        }

        br.BaseStream.Position = (long)position;
        return br;
    }

    private static string ReadTableString(BinaryReader br) => br.ReadUtf16String(br.ReadUInt32());

    /// <summary>
    /// The header doesn't say which encoding the node stream uses, and the first bytes of both can look alike
    /// (a byte-encoded Namespace node followed by id 0 reads as a word-encoded one). So try the likelier encoding
    /// first, the word encoding when the header carries a hash, and fall back to the other.
    /// </summary>
    private (XbfObject Root, XbfNodeEncoding Encoding) ReadNodeStream(BinaryReader br, long end)
    {
        XbfNodeEncoding[] order = HasHash(Header.Hash)
            ? [XbfNodeEncoding.Word, XbfNodeEncoding.Byte]
            : [XbfNodeEncoding.Byte, XbfNodeEncoding.Word];

        InvalidXbfException? firstError = null;
        foreach (XbfNodeEncoding encoding in order)
        {
            _namespaces.Clear();
            br.BaseStream.Position = Header.NodeStreamOffset;
            try
            {
                return (ReadNodes(br, end, encoding), encoding);
            }
            catch (InvalidXbfException ex)
            {
                firstError ??= ex;
            }
            catch (EndOfStreamException ex)
            {
                firstError ??= new InvalidXbfException("The node stream ended unexpectedly.", ex);
            }
        }

        throw firstError!;

        static bool HasHash(string hash) => hash.AsSpan().Trim([' ', '\0']).Length > 0;
    }

    private XbfObject ReadNodes(BinaryReader br, long end, XbfNodeEncoding encoding)
    {
        var objectStack = new Stack<XbfObject>();
        var propertyStack = new Stack<XbfProperty>();
        XbfObject? root = null;
        var lineInfo = new XbfLineInfo(0, 0);

        while (br.BaseStream.Position < end)
        {
            long nodeOffset = br.BaseStream.Position;
            uint tag = encoding == XbfNodeEncoding.Word ? br.ReadUInt32() : br.ReadByte();
            var type = tag <= byte.MaxValue ? (XbfNodeType)tag : XbfNodeType.None;

            switch (type)
            {
                case XbfNodeType.StartObject:
                {
                    var obj = new XbfObject(br) { LineInfo = lineInfo };
                    if (root is null)
                    {
                        root = obj;
                    }
                    else if (propertyStack.Count == 0)
                    {
                        throw Invalid(nodeOffset, "an object outside of any property");
                    }

                    objectStack.Push(obj);
                    break;
                }

                case XbfNodeType.EndObject:
                {
                    if (!objectStack.TryPop(out XbfObject? obj))
                    {
                        throw Invalid(nodeOffset, "an EndObject node without a matching StartObject");
                    }

                    // Objects other than the root are the value of the enclosing property.
                    if (objectStack.Count > 0)
                    {
                        propertyStack.Peek().Values.Add(obj);
                    }

                    break;
                }

                case XbfNodeType.StartProperty:
                {
                    if (objectStack.Count == 0)
                    {
                        throw Invalid(nodeOffset, "a property outside of any object");
                    }

                    propertyStack.Push(new XbfProperty(br) { LineInfo = lineInfo });
                    break;
                }

                case XbfNodeType.EndProperty:
                {
                    if (!propertyStack.TryPop(out XbfProperty? property))
                    {
                        throw Invalid(nodeOffset, "an EndProperty node without a matching StartProperty");
                    }

                    objectStack.Peek().Properties.Add(property);
                    break;
                }

                case XbfNodeType.Text:
                    CurrentProperty(nodeOffset).Values.Add(new XbfText(br) { LineInfo = lineInfo });
                    break;

                case XbfNodeType.Value:
                    CurrentProperty(nodeOffset).Values.Add(new XbfValue(br, encoding) { LineInfo = lineInfo });
                    break;

                case XbfNodeType.Namespace:
                    // These aren't part of the object hierarchy.
                    _namespaces.Add(new XbfNamespace(br) { LineInfo = lineInfo });
                    break;

                case XbfNodeType.LineInfo when encoding == XbfNodeEncoding.Word:
                case XbfNodeType.LineInfoAbsolute:
                    lineInfo = new XbfLineInfo(br.ReadUInt32(), br.ReadUInt32());
                    break;

                case XbfNodeType.LineInfo:
                    // Deltas are signed, so let unsigned arithmetic wrap as intended.
                    lineInfo = new XbfLineInfo(
                        unchecked(lineInfo.LineNumber + (uint)br.ReadInt16()),
                        unchecked(lineInfo.LinePosition + (uint)br.ReadInt16()));
                    break;

                case XbfNodeType.EndOfAttributes:
                    break;

                case XbfNodeType.EndOfStream:
                    br.BaseStream.Position = end;
                    break;

                default:
                    throw new InvalidXbfException(
                        $"Unknown node type {tag} at offset 0x{nodeOffset:X} ({encoding.ToString().ToLowerInvariant()}-encoded node stream).");
            }
        }

        if (root is null)
        {
            throw new InvalidXbfException("The file does not contain any objects.");
        }

        if (objectStack.Count > 0 || propertyStack.Count > 0)
        {
            throw new InvalidXbfException("The node stream ended before all objects and properties were closed.");
        }

        return root;

        XbfProperty CurrentProperty(long offset) =>
            propertyStack.Count > 0 ? propertyStack.Peek() : throw Invalid(offset, "a value outside of any property");

        static InvalidXbfException Invalid(long offset, string what) =>
            new($"Malformed node stream: found {what} at offset 0x{offset:X}.");
    }
}
