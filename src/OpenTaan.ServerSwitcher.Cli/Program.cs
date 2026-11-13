using System.Text;
using OpenTaan.ServerSwitcher.Core.Editing;
using OpenTaan.ServerSwitcher.Core.Format;
using OpenTaan.ServerSwitcher.Core.Interop;
using OpenTaan.ServerSwitcher.Core.Model;
using OpenTaan.ServerSwitcher.Core.Validation;

namespace OpenTaan.ServerSwitcher.Cli;

/// <summary>
/// Command-line companion to the editor: inspect, switch, export, import, decrypt and encrypt .0m files.
/// </summary>
public static class Program
{
    private const string Usage = """
        OpenTaan Server Switcher (command line)

        Usage:
          opentaan-switcher info     <file.0m>                     Show headers, seed and channels
          opentaan-switcher show     <file.0m>                     Print the config tree
          opentaan-switcher switch   <file.0m> <ip> [--port N] [-o out.0m] [--no-backup]
                                                                   Point every channel at <ip>[:port]
          opentaan-switcher validate <file.0m>                     Check ip/port values
          opentaan-switcher export   <file.0m> <out.xml|out.json>  Export the tree as XML or JSON
          opentaan-switcher import   <in.xml|in.json> <out.0m> [--template original.0m] [--no-backup]
                                                                   Build a .0m from XML/JSON
          opentaan-switcher decrypt  <file.0m> [out.bin]           Write the decrypted plaintext
          opentaan-switcher encrypt  <in.bin> <out.0m> [--template original.0m] [--no-backup]
                                                                   Encrypt plaintext into a .0m

        Re-encryption always reuses an existing seed: the one of --template, otherwise the one of the
        file being overwritten. Only a brand-new file gets the stock TAAN seed 0x83EF8226 and tree header.
        Overwriting a file keeps the first version as <file>.bak (unless --no-backup).
        """;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            var options = Options.Parse(args.Skip(1));
            return args[0].ToLowerInvariant() switch
            {
                "info" => Info(options),
                "show" => Show(options),
                "switch" or "set-ip" => Switch(options),
                "validate" => Validate(options),
                "export" => Export(options),
                "import" => Import(options),
                "decrypt" => Decrypt(options),
                "encrypt" => Encrypt(options),
                _ => throw new UsageException($"Unknown command '{args[0]}'."),
            };
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine("Run 'opentaan-switcher --help' for usage.");
            return 2;
        }
        catch (Exception ex) when (ex is OmFormatException or FormatException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Info(Options o)
    {
        var path = o.Positional(0, "file.0m");
        var content = OmFile.Load(path);
        var c = content.Container;
        Console.WriteLine($"File:              {Path.GetFullPath(path)}");
        Console.WriteLine($"Size:              {new FileInfo(path).Length} bytes");
        Console.WriteLine($"Outer flags:       0x{c.OuterFlags:X4}");
        Console.WriteLine($"Inner flags:       0x{c.InnerFlags:X2} ({(c.IsCompressed ? "zlib" : "raw")}, {(c.IsEncrypted ? "XOR" : "plain")})");
        Console.WriteLine($"Seed:              {(c.Seed is { } s ? $"0x{s:X8}" : "(none)")}");
        Console.WriteLine($"Checksum:          0x{c.Checksum:X8} (valid)");
        Console.WriteLine($"Plaintext:         {content.Plaintext.Length} bytes");
        Console.WriteLine($"Tree header:       {Convert.ToHexString(content.Tree.Header)}");
        Console.WriteLine($"Header bytes:      {Convert.ToHexString(c.HeaderBytes)}");
        Console.WriteLine($"Lossless rewrite:  {(content.RoundTripsExactly ? "yes" : "NO")}");

        var channels = ServerEndpoints.FindChannels(content.Tree.Root);
        Console.WriteLine();
        Console.WriteLine($"Channels ({channels.Count}):");
        foreach (var ch in channels)
        {
            Console.WriteLine($"  {ch.GetValue("name") ?? ch.Path,-8} {ch.GetValue("ip")}:{ch.GetValue("port")}");
        }

        PrintWarnings(content.Warnings);
        return 0;
    }

    private static int Show(Options o)
    {
        var content = OmFile.Load(o.Positional(0, "file.0m"));
        Print(content.Tree.Root, 0);
        return 0;

        static void Print(ConfigNode node, int depth)
        {
            var indent = new string(' ', depth * 2);
            var meta = node.Metadata.Length > 0 ? $" [{node.Metadata}]" : string.Empty;
            Console.WriteLine($"{indent}{node.Name}{meta}");
            foreach (var a in node.Attributes)
            {
                Console.WriteLine($"{indent}  {a.Key} = {a.Value}");
            }

            foreach (var child in node.Children)
            {
                Print(child, depth + 1);
            }
        }
    }

    private static int Switch(Options o)
    {
        var path = o.Positional(0, "file.0m");
        var ip = o.Positional(1, "ip");
        var port = o.Value("--port");
        var output = o.Value("-o") ?? o.Value("--output") ?? path;

        var document = ConfigDocument.Open(path);
        var before = ServerEndpoints.DistinctEndpoints(document.Root);
        var command = ServerEndpoints.CreateSwitchCommand(document.Root, ip, port);
        if (command is null)
        {
            Console.WriteLine($"All channels already point at {ip}{(port is null ? "" : ":" + port)}. Nothing to do.");
            if (!string.Equals(Path.GetFullPath(output), Path.GetFullPath(path), StringComparison.Ordinal))
            {
                Save(document, output, o.Flag("--no-backup"));
            }

            return 0;
        }

        document.Execute(command);
        if (!CheckProblems(document.Root))
        {
            return 1;
        }

        Save(document, output, o.Flag("--no-backup"));
        Console.WriteLine($"Switched {ServerEndpoints.FindChannels(document.Root).Count} channel(s):");
        Console.WriteLine($"  before: {string.Join(", ", before)}");
        Console.WriteLine($"  after:  {string.Join(", ", ServerEndpoints.DistinctEndpoints(document.Root))}");
        return 0;
    }

    private static int Validate(Options o)
    {
        var content = OmFile.Load(o.Positional(0, "file.0m"));
        PrintWarnings(content.Warnings);
        var ok = CheckProblems(content.Tree.Root);
        if (ok)
        {
            Console.WriteLine("No problems found.");
        }

        return ok ? 0 : 1;
    }

    private static int Export(Options o)
    {
        var content = OmFile.Load(o.Positional(0, "file.0m"));
        var output = o.Positional(1, "out.xml|out.json");
        var text = Path.GetExtension(output).ToLowerInvariant() switch
        {
            ".xml" => XmlProjection.ToXml(content.Tree.Root),
            ".json" => JsonProjection.ToJson(content.Tree.Root),
            _ => throw new UsageException("Output must end with .xml or .json."),
        };
        File.WriteAllText(output, text, new UTF8Encoding(false));
        Console.WriteLine($"Exported {output}");
        return 0;
    }

    private static int Import(Options o)
    {
        var input = o.Positional(0, "in.xml|in.json");
        var output = o.Positional(1, "out.0m");
        var text = File.ReadAllText(input);
        var root = Path.GetExtension(input).ToLowerInvariant() switch
        {
            ".xml" => XmlProjection.FromXml(text),
            ".json" => JsonProjection.FromJson(text),
            _ => throw new UsageException("Input must end with .xml or .json."),
        };

        if (!CheckProblems(root))
        {
            return 1;
        }

        var (container, header) = Template(o, output);
        var result = OmFile.Save(output, container, header, root, createBackup: !o.Flag("--no-backup"));
        Report(result);
        return 0;
    }

    private static int Decrypt(Options o)
    {
        var input = o.Positional(0, "file.0m");
        var output = o.PositionalOrNull(1) ?? Path.ChangeExtension(input, ".decrypted.bin");
        var decoded = OmContainer.Decode(File.ReadAllBytes(input));
        File.WriteAllBytes(output, decoded.Plaintext);
        Console.WriteLine($"Decrypted {decoded.Plaintext.Length} bytes → {output} (seed 0x{decoded.Info.Seed:X8}, checksum valid)");
        PrintWarnings(decoded.Warnings);
        return 0;
    }

    private static int Encrypt(Options o)
    {
        var input = o.Positional(0, "in.bin");
        var output = o.Positional(1, "out.0m");
        var plaintext = File.ReadAllBytes(input);
        _ = OmTree.Read(plaintext); // refuse to encrypt something that is not a valid config tree
        var (container, _) = Template(o, output);
        var result = OmFile.SavePlaintext(output, container, plaintext, createBackup: !o.Flag("--no-backup"));
        Console.WriteLine($"Encrypted {plaintext.Length} bytes");
        Report(result);
        return 0;
    }

    /// <summary>
    /// Container settings (seed, flags) and tree header to encode with: from <c>--template</c>, otherwise from the
    /// existing <paramref name="output"/> file so overwriting never changes its seed; stock values only for a new file.
    /// </summary>
    private static (OmContainerInfo Container, byte[] Header) Template(Options o, string output)
    {
        if (o.Value("--template") is { } template)
        {
            var content = OmFile.Load(template);
            return (content.Container, content.Tree.Header);
        }

        return (OmContainerInfo.TaanDefault, OmTree.DefaultHeader);
    }

    private static void Save(ConfigDocument document, string output, bool noBackup)
    {
        if (File.Exists(output))
        {
            OmFileContent existing;
            try
            {
                existing = OmFile.Load(output);
            }
            catch (OmFormatException ex)
            {
                throw new UsageException(
                    $"{output} already exists but is not a readable .0m file ({ex.Message}), so its seed cannot be reused. Pass --template original.0m or choose a new output path.");
            }

            Console.WriteLine($"Reusing seed 0x{existing.Container.Seed:X8} and tree header of the existing {output}");
            return (existing.Container, existing.Tree.Header);
        }

        var result = document.Save(output, createBackup: !noBackup);
        Report(result);
    }

    private static void Report(OmSaveResult result)
    {
        Console.WriteLine($"Saved {result.Path} ({result.Size} bytes, seed 0x{result.Container.Seed:X8})");
        if (result.BackupPath is not null)
        {
            Console.WriteLine($"Backup of the previous file: {result.BackupPath}");
        }
    }

    private static bool CheckProblems(ConfigNode root)
    {
        var problems = Validator.Default.Validate(root);
        foreach (var p in problems)
        {
            Console.Error.WriteLine($"{(p.Severity == ProblemSeverity.Error ? "error" : "warning")}: {p.Message} — {p.Location}");
        }

        return problems.All(p => p.Severity != ProblemSeverity.Error);
    }

    private static void PrintWarnings(IEnumerable<string> warnings)
    {
        foreach (var w in warnings)
        {
            Console.Error.WriteLine($"warning: {w}");
        }
    }

    private sealed class UsageException(string message) : Exception(message);

    private sealed class Options
    {
        private static readonly HashSet<string> ValueOptions = ["--port", "-o", "--output", "--template"];
        private static readonly HashSet<string> FlagOptions = ["--no-backup"];

        private readonly List<string> _positional = new();
        private readonly Dictionary<string, string> _values = new();
        private readonly HashSet<string> _flags = new();

        public static Options Parse(IEnumerable<string> args)
        {
            var options = new Options();
            using var e = args.GetEnumerator();
            while (e.MoveNext())
            {
                var arg = e.Current;
                if (arg is "--seed")
                {
                    throw new UsageException("Custom seeds are not supported: a different seed can crash the client. The original seed is always reused (use --template to take it from a file).");
                }

                if (ValueOptions.Contains(arg))
                {
                    if (!e.MoveNext())
                    {
                        throw new UsageException($"{arg} needs a value.");
                    }

                    options._values[arg] = e.Current;
                }
                else if (FlagOptions.Contains(arg))
                {
                    options._flags.Add(arg);
                }
                else if (arg.StartsWith('-') && arg.Length > 1)
                {
                    throw new UsageException($"Unknown option '{arg}'.");
                }
                else
                {
                    options._positional.Add(arg);
                }
            }

            return options;
        }

        public string Positional(int index, string name) =>
            PositionalOrNull(index) ?? throw new UsageException($"Missing <{name}>.");

        public string? PositionalOrNull(int index) => index < _positional.Count ? _positional[index] : null;

        public string? Value(string name) => _values.GetValueOrDefault(name);

        public bool Flag(string name) => _flags.Contains(name);
    }
}
