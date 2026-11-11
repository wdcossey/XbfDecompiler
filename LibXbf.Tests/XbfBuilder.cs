using System.Text;
using LibXbf.Records;
using LibXbf.Records.Nodes;
using LibXbf.Records.Types;

namespace LibXbf.Tests;

/// <summary>Builds minimal XBF v1 files in memory, following the layout observed in real Windows 8.1 files.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "Only owns in-memory streams.")]
internal sealed class XbfBuilder
{
    private readonly List<string> _strings = [];
    private readonly List<uint> _assemblies = [];
    private readonly List<(uint Assembly, uint Name)> _typeNamespaces = [];
    private readonly List<(uint Namespace, uint Name, TypeFlags Flags)> _types = [];
    private readonly List<(PropertyFlags Flags, uint Type, uint Name)> _properties = [];
    private readonly List<uint> _xmlNamespaces = [];
    private readonly MemoryStream _nodes = new();
    private readonly BinaryWriter _nodeWriter;
    private readonly XbfNodeEncoding _encoding;

    public XbfBuilder(XbfNodeEncoding encoding = XbfNodeEncoding.Byte)
    {
        _encoding = encoding;
        _nodeWriter = new BinaryWriter(_nodes, Encoding.Unicode);
        _assemblies.Add(String("System.Windows"));
    }

    /// <summary>The 32 characters of the header hash; blank in all but one known Windows 8.1 file.</summary>
    public string Hash { get; set; } = new(' ', 32);

    public uint String(string value)
    {
        int index = _strings.IndexOf(value);
        if (index < 0)
        {
            _strings.Add(value);
            index = _strings.Count - 1;
        }

        return (uint)index;
    }

    public uint Type(string ns, string name, TypeFlags flags = TypeFlags.None)
    {
        uint nsName = String(ns);
        int nsIndex = _typeNamespaces.FindIndex(n => n.Name == nsName);
        if (nsIndex < 0)
        {
            _typeNamespaces.Add((0, nsName));
            nsIndex = _typeNamespaces.Count - 1;
        }

        _types.Add(((uint)nsIndex, String(name), flags));
        return (uint)(_types.Count - 1);
    }

    public uint Property(uint declaringType, string name, PropertyFlags flags = PropertyFlags.None)
    {
        _properties.Add((flags, declaringType, String(name)));
        return (uint)(_properties.Count - 1);
    }

    public uint XmlNamespace(string uri)
    {
        _xmlNamespaces.Add(String(uri));
        return (uint)(_xmlNamespaces.Count - 1);
    }

    public XbfBuilder Namespace(uint xmlNamespaceId, string prefix)
    {
        Node(XbfNodeType.Namespace);
        _nodeWriter.Write(xmlNamespaceId);
        _nodeWriter.Write(0u);
        WriteString(_nodeWriter, prefix, padded: false);
        return this;
    }

    public XbfBuilder StartObject(uint typeId, bool retrieved = false)
    {
        Node(XbfNodeType.StartObject);
        _nodeWriter.Write(typeId);
        _nodeWriter.Write(retrieved ? 1u : 0u);
        return this;
    }

    public XbfBuilder EndObject() => Node(XbfNodeType.EndObject);

    public XbfBuilder StartProperty(uint propertyId)
    {
        Node(XbfNodeType.StartProperty);
        _nodeWriter.Write(propertyId);
        _nodeWriter.Write(0u);
        return this;
    }

    public XbfBuilder EndProperty() => Node(XbfNodeType.EndProperty);

    public XbfBuilder Text(string value)
    {
        Node(XbfNodeType.Text);
        _nodeWriter.Write(String(value));
        _nodeWriter.Write(0u);
        return this;
    }

    /// <summary>A property whose single value is a text node.</summary>
    public XbfBuilder TextProperty(uint propertyId, string value) => StartProperty(propertyId).Text(value).EndProperty();

    public XbfBuilder Value(XbfValueType type, Action<BinaryWriter>? payload = null)
    {
        Node(XbfNodeType.Value);
        Tag((byte)type);
        payload?.Invoke(_nodeWriter);
        return this;
    }

    public XbfBuilder ValueProperty(uint propertyId, XbfValueType type, Action<BinaryWriter>? payload = null) =>
        StartProperty(propertyId).Value(type, payload).EndProperty();

    public XbfBuilder Raw(params byte[] bytes)
    {
        _nodeWriter.Write(bytes);
        return this;
    }

    public byte[] Build(uint majorVersion = 1, uint minorVersion = 0)
    {
        var tables = new MemoryStream();
        var tw = new BinaryWriter(tables, Encoding.Unicode);
        long Here() => XbfHeader.Size + tables.Position - XbfHeader.OffsetBase;

        long stringOffset = Here();
        tw.Write(_strings.Count);
        foreach (string s in _strings)
        {
            WriteString(tw, s, padded: majorVersion >= 2);
        }

        long assemblyOffset = Here();
        tw.Write(_assemblies.Count);
        foreach (uint a in _assemblies)
        {
            tw.Write((uint)AssemblyEntryType.Native);
            tw.Write(a);
        }

        long typeNamespaceOffset = Here();
        tw.Write(_typeNamespaces.Count);
        foreach (var (assembly, name) in _typeNamespaces)
        {
            tw.Write(assembly);
            tw.Write(name);
        }

        long typeOffset = Here();
        tw.Write(_types.Count);
        foreach (var (ns, name, flags) in _types)
        {
            tw.Write((uint)flags);
            tw.Write(ns);
            tw.Write(name);
        }

        long propertyOffset = Here();
        tw.Write(_properties.Count);
        foreach (var (flags, type, name) in _properties)
        {
            tw.Write((uint)flags);
            tw.Write(type);
            tw.Write(name);
        }

        long xmlNamespaceOffset = Here();
        tw.Write(_xmlNamespaces.Count);
        foreach (uint x in _xmlNamespaces)
        {
            tw.Write(x);
        }

        tw.Flush();
        _nodeWriter.Flush();

        var file = new MemoryStream();
        var w = new BinaryWriter(file, Encoding.Unicode);
        w.Write(XbfHeader.Magic);
        w.Write((uint)(XbfHeader.Size - XbfHeader.OffsetBase + tables.Length));
        w.Write((uint)_nodes.Length);
        w.Write(majorVersion);
        w.Write(minorVersion);
        foreach (long offset in new[] { stringOffset, assemblyOffset, typeNamespaceOffset, typeOffset, propertyOffset, xmlNamespaceOffset })
        {
            w.Write((ulong)offset);
        }

        w.Write(Encoding.Unicode.GetBytes(Hash.PadRight(32)[..32]));
        w.Write(tables.ToArray());
        w.Write(_nodes.ToArray());
        w.Flush();
        return file.ToArray();
    }

    public XbfFile Load(uint majorVersion = 1) => XbfFile.Load(new MemoryStream(Build(majorVersion)));

    /// <summary>A line info node as the encoding writes it: deltas (byte) or absolute values (word).</summary>
    public XbfBuilder LineInfo(int line, int column)
    {
        Node(XbfNodeType.LineInfo);
        if (_encoding == XbfNodeEncoding.Word)
        {
            _nodeWriter.Write((uint)line);
            _nodeWriter.Write((uint)column);
        }
        else
        {
            _nodeWriter.Write((short)line);
            _nodeWriter.Write((short)column);
        }

        return this;
    }

    private XbfBuilder Node(XbfNodeType type)
    {
        Tag((byte)type);
        return this;
    }

    private void Tag(byte tag)
    {
        if (_encoding == XbfNodeEncoding.Word)
        {
            _nodeWriter.Write((uint)tag);
        }
        else
        {
            _nodeWriter.Write(tag);
        }
    }

    private static void WriteString(BinaryWriter w, string value, bool padded)
    {
        w.Write((uint)value.Length);
        w.Write(Encoding.Unicode.GetBytes(value));
        if (padded)
        {
            w.Write((ushort)0);
        }
    }
}
