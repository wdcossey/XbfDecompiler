namespace LibXbf;

/// <summary>
/// Thrown when a file is not a well-formed XBF file.
/// </summary>
public class InvalidXbfException : Exception
{
    public InvalidXbfException()
        : base("The file is not a valid XBF file.")
    {
    }

    public InvalidXbfException(string message)
        : base(message)
    {
    }

    public InvalidXbfException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when a file is a recognisable XBF file, but of a format version this library cannot decode.
/// </summary>
public sealed class UnsupportedXbfVersionException : InvalidXbfException
{
    public UnsupportedXbfVersionException()
    {
    }

    public UnsupportedXbfVersionException(string message)
        : base(message)
    {
    }

    public UnsupportedXbfVersionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public UnsupportedXbfVersionException(Version version)
        : base($"XBF version {version} is not supported. Only XBF v1 files (Windows 8.1 / Windows Phone 8.1) can be decompiled.")
    {
        Version = version;
    }

    public Version? Version { get; }
}
