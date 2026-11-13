using OpenTaan.ServerSwitcher.Core.Editing;
using OpenTaan.ServerSwitcher.Core.Format;

namespace OpenTaan.ServerSwitcher.Core.Tests;

public class SaveTests
{
    [StockFileFact]
    public void Save_SwitchedServer_KeepsOriginalSeed_AndReloadsWithNewIp()
    {
        using var temp = new Fixtures.TempDirectory();
        var path = temp.File("taan_server.0m");
        File.Copy(Fixtures.TaanServerPath, path);

        var doc = ConfigDocument.Open(path);
        doc.Execute(ServerEndpoints.CreateSwitchCommand(doc.Root, "172.20.10.9")!);
        var result = doc.Save();

        Assert.Equal(path + ".bak", result.BackupPath);
        Assert.Equal(Fixtures.TaanServerSha256, Fixtures.Sha256(File.ReadAllBytes(result.BackupPath!)));
        Assert.False(doc.IsModified);
        Assert.Equal(Fixtures.TaanServerSeed, doc.Container.Seed);

        var reloaded = OmFile.Load(path);
        Assert.Equal(Fixtures.TaanServerSeed, reloaded.Container.Seed);
        Assert.Equal(0x03, reloaded.Container.InnerFlags);
        Assert.Equal(OmTree.DefaultHeader, reloaded.Tree.Header);
        Assert.Equal(["172.20.10.9:2180"], ServerEndpoints.DistinctEndpoints(reloaded.Tree.Root));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp", SearchOption.AllDirectories));
    }

    [StockFileFact]
    public void Save_Twice_KeepsTheFirstBackup()
    {
        using var temp = new Fixtures.TempDirectory();
        var path = temp.File("taan_server.0m");
        File.Copy(Fixtures.TaanServerPath, path);

        var doc = ConfigDocument.Open(path);
        doc.Execute(ServerEndpoints.CreateSwitchCommand(doc.Root, "10.0.0.1")!);
        doc.Save();
        doc.Execute(ServerEndpoints.CreateSwitchCommand(doc.Root, "10.0.0.2")!);
        var second = doc.Save();

        Assert.Null(second.BackupPath);
        Assert.Equal(Fixtures.TaanServerSha256, Fixtures.Sha256(File.ReadAllBytes(path + ".bak")));
    }

    [StockFileFact]
    public void SaveAs_NewFile_CreatesNoBackup()
    {
        using var temp = new Fixtures.TempDirectory();
        var doc = new ConfigDocument(OmFile.Decode(Fixtures.TaanServer));
        var result = doc.Save(temp.File("out/modified.0m"));
        Assert.Null(result.BackupPath);
        Assert.Equal(temp.File("out/modified.0m"), doc.FilePath);
        Assert.True(OmFile.Load(result.Path).Tree.Root.StructurallyEquals(doc.Root));
    }

    [StockFileFact]
    public void Save_UnchangedStockFile_ProducesEquivalentFile()
    {
        using var temp = new Fixtures.TempDirectory();
        var doc = new ConfigDocument(OmFile.Decode(Fixtures.TaanServer));
        var result = doc.Save(temp.File("same.0m"));

        var original = OmContainer.Decode(Fixtures.TaanServer);
        var saved = OmContainer.Decode(File.ReadAllBytes(result.Path));
        Assert.Equal(original.Plaintext, saved.Plaintext);
        Assert.Equal(original.Info.Checksum, saved.Info.Checksum);
        Assert.Equal(original.Info.Seed, saved.Info.Seed);
    }
}
