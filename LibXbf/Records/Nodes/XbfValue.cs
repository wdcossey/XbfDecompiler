using System.Globalization;

namespace LibXbf.Records.Nodes;

/// <summary>
/// A typed, pre-parsed property value. Unlike other nodes it has no id/flags prefix in the stream.
/// </summary>
public sealed class XbfValue : XbfNode
{
    internal XbfValue(BinaryReader br, XbfNodeEncoding encoding = XbfNodeEncoding.Byte)
    {
        long typeOffset = br.BaseStream.Position;

        // No word-encoded sample contains a Value node, so a 32-bit value tag (like the node tag) is an assumption.
        uint tag = encoding == XbfNodeEncoding.Word ? br.ReadUInt32() : br.ReadByte();
        Type = tag <= byte.MaxValue ? (XbfValueType)tag : XbfValueType.None;
        Value = Type switch
        {
            XbfValueType.IsBoolFalse => "False",
            XbfValueType.IsBoolTrue => "True",
            XbfValueType.IsFloat => Format(br.ReadSingle()),
            // Stored as seconds; checked against the SDK's generic.xaml (ProgressBar: 0.167 s is "0:0:0.167").
            XbfValueType.IsKeyTime or XbfValueType.IsDuration => FormatTimeSpan(br.ReadSingle()),
            XbfValueType.IsLengthConverter => FormatLength(br.ReadSingle()),
            XbfValueType.IsSigned => br.ReadInt32().ToString(CultureInfo.InvariantCulture),
            XbfValueType.IsCString => br.ReadUtf16String(br.ReadUInt32()),
            XbfValueType.IsThickness => FormatThickness(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
            XbfValueType.IsGridLength => FormatGridLength(br.ReadUInt32(), br.ReadSingle()),
            XbfValueType.IsColor => FormatColor(br.ReadUInt32()),
            _ => throw new InvalidXbfException($"Unknown value type {tag} at offset 0x{typeOffset:X}."),
        };
    }

    public XbfValueType Type { get; }

    /// <summary>The value formatted as it would appear in XAML markup.</summary>
    public string Value { get; }

    private static string Format(float value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Writes seconds the way XAML writes a TimeSpan: "0:0:0.167", or "0" for zero. A bare "0.167" would be
    /// parsed as a TimeSpan string, not as seconds.
    /// </summary>
    private static string FormatTimeSpan(float seconds)
    {
        if (seconds == 0)
        {
            return "0";
        }

        if (!float.IsFinite(seconds))
        {
            return Format(seconds);
        }

        // Go through the shortest round-trip decimal so 0.167f stays 0.167 rather than 0.16699999570846558.
        decimal total = decimal.Parse(Format(seconds), NumberStyles.Float, CultureInfo.InvariantCulture);
        string sign = total < 0 ? "-" : "";
        total = Math.Abs(total);
        decimal hours = Math.Floor(total / 3600);
        decimal minutes = Math.Floor(total % 3600 / 60);
        decimal rest = total % 60;
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{hours}:{minutes}:{rest}");
    }

    // Double.NaN is how "Auto" is stored for Width/Height and similar length properties.
    private static string FormatLength(float value) => float.IsNaN(value) ? "Auto" : Format(value);

    private static string FormatThickness(float left, float top, float right, float bottom) =>
        left == right && top == bottom
            ? left == top ? Format(left) : $"{Format(left)},{Format(top)}"
            : $"{Format(left)},{Format(top)},{Format(right)},{Format(bottom)}";

    private static string FormatGridLength(uint unitType, float value) => unitType switch
    {
        0 => "Auto",
        2 => value == 1 ? "*" : $"{Format(value)}*",
        // 1 is pixels; XAML pixel lengths are plain numbers (a "px" suffix is not valid in Windows XAML).
        _ => Format(value),
    };

    // Colours are stored as packed 0xAARRGGBB.
    private static string FormatColor(uint argb) => $"#{argb:X8}";
}
