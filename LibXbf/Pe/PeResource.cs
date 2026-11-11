using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;

namespace LibXbf.Pe;

/// <summary>A resource in a PE file (DLL or EXE).</summary>
/// <param name="Type">The resource type: a number such as <c>256</c>, or a name.</param>
/// <param name="Name">The resource name, such as <c>BUTTON.GENERIC.XBF</c>, or a number.</param>
/// <param name="Language">The language id, such as 1033.</param>
public sealed record PeResource(string Type, string Name, int Language, byte[] Data)
{
    /// <summary>Whether the data starts with the XBF magic number.</summary>
    public bool IsXbf => Data.AsSpan().StartsWith("XBF\0"u8);

    /// <summary>
    /// Reads the resources of a PE file. Windows 8.1 and Windows Phone 8.1 store compiled XAML as resources of type
    /// 256 named after the file, e.g. <c>Windows.UI.Xaml.Phone.dll</c>'s <c>BUTTON.GENERIC.XBF</c>.
    /// </summary>
    /// <param name="type">Only read resources of this type (a number or a name); all types when null.</param>
    /// <exception cref="BadImageFormatException">The file is not a valid PE file.</exception>
    public static IReadOnlyList<PeResource> Read(Stream stream, string? type = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen | PEStreamOptions.PrefetchEntireImage);
        DirectoryEntry directory = pe.PEHeaders.PEHeader?.ResourceTableDirectory ?? default;
        if (directory.Size == 0)
        {
            return [];
        }

        byte[] section = [.. pe.GetSectionData(directory.RelativeVirtualAddress).GetContent()];
        var result = new List<PeResource>();

        foreach ((string typeName, int typeOffset) in Entries(section, 0))
        {
            if (type is not null && !string.Equals(typeName, type, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach ((string name, int nameOffset) in Entries(section, typeOffset))
            {
                foreach ((string language, int dataEntry) in Entries(section, nameOffset))
                {
                    int rva = BinaryPrimitives.ReadInt32LittleEndian(section.AsSpan(dataEntry));
                    int size = BinaryPrimitives.ReadInt32LittleEndian(section.AsSpan(dataEntry + 4));
                    byte[] data = [.. pe.GetSectionData(rva).GetContent(0, size)];
                    result.Add(new PeResource(typeName, name, int.Parse(language, System.Globalization.CultureInfo.InvariantCulture), data));
                }
            }
        }

        return result;
    }

    /// <inheritdoc cref="Read(Stream, string?)"/>
    public static IReadOnlyList<PeResource> Read(string path, string? type = null)
    {
        using var stream = File.OpenRead(path);
        return Read(stream, type);
    }

    /// <summary>The entries of a resource directory: each name or id, and the offset it points to.</summary>
    private static IEnumerable<(string Name, int Offset)> Entries(byte[] section, int directory)
    {
        int named = BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(directory + 12));
        int ids = BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(directory + 14));

        for (int i = 0; i < named + ids; i++)
        {
            int entry = directory + 16 + (i * 8);
            uint name = BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(entry));
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(entry + 4));

            string text;
            if ((name & 0x8000_0000) != 0)
            {
                int at = (int)(name & 0x7FFF_FFFF);
                int length = BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(at));
                text = Encoding.Unicode.GetString(section, at + 2, length * 2);
            }
            else
            {
                text = name.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            yield return (text, (int)(offset & 0x7FFF_FFFF));
        }
    }
}
