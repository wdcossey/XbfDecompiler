using System.Text;

namespace LibXbf;

internal static class BinaryReaderExtensions
{
    extension(BinaryReader br)
    {
        public long Remaining => br.BaseStream.Length - br.BaseStream.Position;

        /// <summary>
        /// Reads <paramref name="charCount"/> UTF-16LE code units.
        /// </summary>
        /// <remarks>
        /// <see cref="BinaryReader.ReadChars"/> is deliberately avoided: its decoder may consume more bytes than
        /// requested when the data ends in a lone surrogate, which would desynchronise the rest of the stream.
        /// </remarks>
        public string ReadUtf16String(uint charCount)
        {
            long byteCount = (long)charCount * 2;
            if (byteCount > br.Remaining)
            {
                throw new InvalidXbfException(
                    $"String of {charCount} characters at offset 0x{br.BaseStream.Position:X} runs past the end of the file.");
            }

            return Encoding.Unicode.GetString(br.ReadBytes((int)byteCount));
        }

        /// <summary>
        /// Reads a 32-bit element count and checks that the stream could plausibly hold that many elements.
        /// </summary>
        public int ReadCount(int minimumElementSize)
        {
            uint count = br.ReadUInt32();
            if (count > br.Remaining / minimumElementSize)
            {
                throw new InvalidXbfException(
                    $"Table at offset 0x{br.BaseStream.Position - 4:X} claims {count} entries, which exceeds the remaining file size.");
            }

            return (int)count;
        }
    }
}
