using System.Security.Cryptography;
using OpenTaan.ServerSwitcher.Core.Model;

namespace OpenTaan.ServerSwitcher.Core.Tests;

internal static class Fixtures
{
    /// <summary>The stock taan_server.0m from the game client (not committed; see README).</summary>
    public static string TaanServerPath => Path.Combine(AppContext.BaseDirectory, "fixtures", "taan_server.0m");

    public static byte[] TaanServer => File.ReadAllBytes(TaanServerPath);

    // Reference values of the stock file.
    public const string TaanServerSha256 = "6e08b360fe7f6bf2960590c866923ffa0005aa43dbfda76ef2c15e524c6594ac";
    public const string TaanServerPlaintextSha256 = "2a9d9ac9ace3eef09a44e42a70548a10d0c3b8acb35861c1c613368623d8b6f9";
    public const uint TaanServerSeed = 0x83EF8226;
    public const uint TaanServerChecksum = 0x35AA77DC;
    public const int TaanServerPlaintextLength = 1048;
    /// <summary>Each stock channel points at its own server: ch-1 → .2 … ch-6 → .7.</summary>
    public static readonly string[] OriginalIps =
        ["180.148.129.2", "180.148.129.3", "180.148.129.4", "180.148.129.5", "180.148.129.6", "180.148.129.7"];

    public const string OriginalIp = "180.148.129.2"; // ch-1
    public const string OriginalPort = "2180";

    public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>A small tree resembling the TAAN config, built in code.</summary>
    public static ConfigNode SampleTree()
    {
        var root = new ConfigNode("content");
        var server = root.AddChild(new ConfigNode("server"));
        server.AddAttribute("name", "Test-Service");
        for (var i = 1; i <= 3; i++)
        {
            var channel = server.AddChild(new ConfigNode("channel"));
            channel.AddAttribute("name", $"ch-{i}");
            channel.AddAttribute("ip", "10.0.0.1");
            channel.AddAttribute("port", "2180");
        }

        root.AddChild(new ConfigNode("EnableRanking"));
        return root;
    }

    public sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opentaan-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
