namespace OpenTaan.ServerSwitcher.Core.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> for tests that need the stock client file <c>fixtures/taan_server.0m</c>.
/// The file belongs to the game, so it is not committed; without it these tests are skipped, not failed.
/// </summary>
public sealed class StockFileFactAttribute : FactAttribute
{
    public StockFileFactAttribute()
    {
        if (!File.Exists(Fixtures.TaanServerPath))
        {
            Skip = "Needs the stock taan_server.0m from the game client: copy it to tests/OpenTaan.ServerSwitcher.Core.Tests/fixtures/ (see README).";
        }
    }
}
