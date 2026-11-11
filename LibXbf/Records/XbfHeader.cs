using System.Text;

namespace LibXbf.Records;

public sealed class XbfHeader
{
    /// <summary>Size of the header in bytes.</summary>
    public const int Size = 0x84;

    /// <summary>
    /// Table offsets, and <see cref="MetadataSize"/>, are measured from the end of the
    /// magic/metadata-size/node-size fields rather than from the start of the file.
    /// </summary>
    public const int OffsetBase = 0x0C;

    private const int HashLength = 0x40;

    public static ReadOnlySpan<byte> Magic => "XBF\0"u8;

    internal XbfHeader(BinaryReader br)
    {
        if (br.Remaining < Size)
        {
            throw new InvalidXbfException("The file is too small to be an XBF file.");
        }

        MagicNumber = br.ReadBytes(4);
        if (!HasValidMagic)
        {
            throw new InvalidXbfException("The file does not start with the XBF signature.");
        }

        MetadataSize = br.ReadUInt32();
        NodeSize = br.ReadUInt32();
        MajorFileVersion = br.ReadUInt32();
        MinorFileVersion = br.ReadUInt32();
        StringTableOffset = br.ReadUInt64();
        AssemblyTableOffset = br.ReadUInt64();
        TypeNamespaceTableOffset = br.ReadUInt64();
        TypeTableOffset = br.ReadUInt64();
        PropertyTableOffset = br.ReadUInt64();
        XmlNamespaceTableOffset = br.ReadUInt64();
        Hash = Encoding.Unicode.GetString(br.ReadBytes(HashLength));
    }

    public byte[] MagicNumber { get; }

    public uint MetadataSize { get; }

    public uint NodeSize { get; }

    public uint MajorFileVersion { get; }

    public uint MinorFileVersion { get; }

    public ulong StringTableOffset { get; }

    public ulong AssemblyTableOffset { get; }

    public ulong TypeNamespaceTableOffset { get; }

    public ulong TypeTableOffset { get; }

    public ulong PropertyTableOffset { get; }

    public ulong XmlNamespaceTableOffset { get; }

    public string Hash { get; }

    public Version FileVersion => new((int)MajorFileVersion, (int)MinorFileVersion);

    public bool HasValidMagic => MagicNumber.AsSpan().SequenceEqual(Magic);

    /// <summary>Absolute file offset of the first node.</summary>
    public long NodeStreamOffset => OffsetBase + (long)MetadataSize;
}
