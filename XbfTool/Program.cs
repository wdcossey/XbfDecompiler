using System.Text.Json.Nodes;
using System.Xml.Linq;
using LibXbf;
using LibXbf.Output;
using LibXbf.Pe;

namespace XbfTool;

internal static class Program
{
    private const string Usage = """
        xbf - extract XBF resources from PE files, and convert XBF v1 (Windows 8.1 / Windows Phone 8.1) to XAML or JSON

        Usage:
          xbf extract <pe-file>... -o <dir> [--type <type>]
              Writes the resources of each DLL/EXE to <dir>, named after the resource
              (e.g. BUTTON.GENERIC.XBF). --type defaults to 256, where Windows stores XBF.
          xbf xaml <file-or-dir>... -o <dir>
              Decompiles .xbf files to XAML. BUTTON.GENERIC.XBF is written as button.xaml.
          xbf json <file-or-dir>... -o <dir>
              Writes the resources, styles, setters, template trees, parts, visual states and
              animations of each .xbf or .xaml file as JSON (format "xbf-json/1"), plus an
              index.json listing every input and its result. A .xaml input, such as an SDK's
              generic.xaml, gives the same shape, so runtime and design-time files compare.
          xbf info <file>...
              Prints an XBF file's header, encoding and table sizes.

        A directory input means every .xbf (and, for json, .xaml) file directly inside it.
        Exit code: 0 when every input succeeded, 1 when any failed, 2 on a usage error.
        """;

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            var options = Options.Parse(args.AsSpan(1));
            return args[0] switch
            {
                "extract" => Extract(options),
                "xaml" => Convert(options, json: false),
                "json" => Convert(options, json: true),
                "info" => Info(options),
                _ => throw new UsageException($"Unknown command '{args[0]}'."),
            };
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Run 'xbf --help' for usage.");
            return 2;
        }
    }

    private static int Extract(Options options)
    {
        string output = options.RequireOutput();
        Directory.CreateDirectory(output);
        int failures = 0;

        foreach (string path in options.RequireInputs())
        {
            try
            {
                IReadOnlyList<PeResource> resources = PeResource.Read(path, options.Type ?? "256");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (PeResource resource in resources)
                {
                    string name = SafeFileName(resource.Name);
                    if (!seen.Add(name))
                    {
                        name = $"{name}.{resource.Language}";
                    }

                    File.WriteAllBytes(Path.Combine(output, name), resource.Data);
                }

                int xbf = resources.Count(r => r.IsXbf);
                Console.WriteLine($"{Path.GetFileName(path)}: {resources.Count} resources of type {options.Type ?? "256"} ({xbf} XBF)");
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException or ArgumentOutOfRangeException)
            {
                Console.Error.WriteLine($"FAILED {path}: {ex.Message}");
                failures++;
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static int Convert(Options options, bool json)
    {
        string output = options.RequireOutput();
        Directory.CreateDirectory(output);
        var index = new JsonArray();
        int failures = 0;
        int count = 0;

        foreach (string path in ExpandInputs(options.RequireInputs(), includeXaml: json))
        {
            count++;
            string outputName = OutputStem(path) + (json ? ".json" : ".xaml");
            var entry = new JsonObject { ["input"] = Path.GetFileName(path), ["output"] = outputName };
            try
            {
                bool isXaml = path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
                if (json)
                {
                    JsonObject document = isXaml
                        ? JsonOutput.Convert(XDocument.Load(path))
                        : new JsonOutput().GetOutput(XbfFile.Load(path));
                    File.WriteAllText(Path.Combine(output, outputName), JsonOutput.ToJsonString(document));
                    entry["source"] = document["source"]?.DeepClone();
                }
                else
                {
                    XbfFile file = XbfFile.Load(path);
                    using FileStream stream = File.Create(Path.Combine(output, outputName));
                    XamlOutput.Save(new XamlOutput().GetOutput(file), stream);
                    entry["source"] = new JsonObject { ["kind"] = "xbf", ["nodeEncoding"] = file.NodeEncoding.ToString() };
                }

                entry["ok"] = true;
            }
            catch (Exception ex) when (ex is InvalidXbfException or IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                entry["ok"] = false;
                entry["error"] = ex.Message;
                Console.Error.WriteLine($"FAILED {path}: {ex.Message}");
                failures++;
            }

            index.Add(entry);
        }

        if (json)
        {
            var summary = new JsonObject { ["format"] = JsonOutput.FormatId, ["files"] = index };
            File.WriteAllText(Path.Combine(output, "index.json"), JsonOutput.ToJsonString(summary));
        }

        Console.WriteLine($"{count - failures} of {count} converted to {(json ? "JSON" : "XAML")} in {output}");
        return failures == 0 && count > 0 ? 0 : 1;
    }

    private static int Info(Options options)
    {
        int failures = 0;
        foreach (string path in options.RequireInputs())
        {
            try
            {
                XbfFile file = XbfFile.Load(path);
                Console.WriteLine($"""
                    {Path.GetFileName(path)}
                      version        {file.FileVersion}
                      node encoding  {file.NodeEncoding}
                      metadata size  {file.Header.MetadataSize}
                      node size      {file.Header.NodeSize}
                      hash           {file.Header.Hash.Trim(' ', '\0')}
                      strings        {file.StringTable.Count}
                      assemblies     {file.AssemblyTable.Count}
                      type ns        {file.TypeNamespaceTable.Count}
                      types          {file.TypeTable.Count}
                      properties     {file.PropertyTable.Count}
                      xml ns         {file.XmlNamespaceTable.Count}
                      root           {file.GetTypeInfo(file.RootNode.Id).FullName}
                    """);
            }
            catch (Exception ex) when (ex is InvalidXbfException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"FAILED {path}: {ex.Message}");
                failures++;
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static IEnumerable<string> ExpandInputs(IEnumerable<string> inputs, bool includeXaml)
    {
        foreach (string input in inputs)
        {
            if (Directory.Exists(input))
            {
                foreach (string file in Directory.EnumerateFiles(input).Order(StringComparer.OrdinalIgnoreCase))
                {
                    if (file.EndsWith(".xbf", StringComparison.OrdinalIgnoreCase)
                        || (includeXaml && file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)))
                    {
                        yield return file;
                    }
                }
            }
            else
            {
                yield return input;
            }
        }
    }

    /// <summary><c>BUTTON.GENERIC.XBF</c> becomes <c>button</c>; <c>generic.xaml</c> becomes <c>generic</c>.</summary>
    private static string OutputStem(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        if (stem.EndsWith(".GENERIC", StringComparison.OrdinalIgnoreCase))
        {
            stem = stem[..^".GENERIC".Length];
        }

        return stem.ToLowerInvariant();
    }

    private static string SafeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private sealed class Options
    {
        private readonly List<string> _inputs = [];

        public string? Output { get; private set; }

        public string? Type { get; private set; }

        public static Options Parse(ReadOnlySpan<string> args)
        {
            var options = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-o" or "--output":
                        options.Output = ++i < args.Length ? args[i] : throw new UsageException("-o needs a directory.");
                        break;
                    case "--type":
                        options.Type = ++i < args.Length ? args[i] : throw new UsageException("--type needs a value.");
                        break;
                    case { } flag when flag.StartsWith('-'):
                        throw new UsageException($"Unknown option '{flag}'.");
                    default:
                        options._inputs.Add(args[i]);
                        break;
                }
            }

            return options;
        }

        public string RequireOutput() => Output ?? throw new UsageException("This command needs -o <dir>.");

        public List<string> RequireInputs() =>
            _inputs.Count > 0 ? _inputs : throw new UsageException("This command needs at least one input.");
    }

    private sealed class UsageException(string message) : Exception(message);
}
