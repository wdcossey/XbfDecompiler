namespace LibXbf.Records.Nodes;

/// <summary>How the node stream of an XBF v1 file encodes node tags.</summary>
public enum XbfNodeEncoding
{
    /// <summary>
    /// Each node tag is one byte and <see cref="XbfNodeType.LineInfo"/> holds two 16-bit deltas. Used by
    /// almost every Windows 8.1 / Windows Phone 8.1 file.
    /// </summary>
    Byte,

    /// <summary>
    /// Each node tag is a 32-bit word and <see cref="XbfNodeType.LineInfo"/> holds an absolute 32-bit line and
    /// column. The other fields are unchanged. Seen in <c>LISTVIEWBASEITEM.GENERIC.XBF</c> from the Windows Phone
    /// 8.1 emulator's <c>Windows.UI.Xaml.Phone.dll</c>, the only sample whose header carries a hash.
    /// </summary>
    Word,
}
