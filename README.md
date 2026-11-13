# OpenTaan Server Switcher

Cross-platform editor (Windows, macOS, Linux) for the TAAN Online client's encrypted `.0m` config files,
built for the OpenTaan project: point the client at your own server, or inspect and edit any `.0m` tree.

## Projects

| Path | What it is |
|---|---|
| `src/OpenTaan.ServerSwitcher.Core` | UI-free library: `.0m` container + tree format, config model, undo/redo, change tracking, validation, XML/JSON import/export. |
| `src/OpenTaan.ServerSwitcher.Editor` | Avalonia 12 desktop editor (Tree / Table / Raw XML / Changes views, inspector, problems panel, "Switch server"). |
| `src/OpenTaan.ServerSwitcher.Cli` | `opentaan-switcher` command-line tool. |
| `tests/OpenTaan.ServerSwitcher.Core.Tests` | xUnit tests, most of them against the stock `taan_server.0m` (see [Tests](#tests)). |

All logic lives in Core so a future .NET MAUI front end only needs a new UI project.

## Build and run

Requires the .NET 10 SDK.

```sh
dotnet build OpenTaan.ServerSwitcher.slnx
dotnet test
dotnet run --project src/OpenTaan.ServerSwitcher.Editor            # editor
dotnet run --project src/OpenTaan.ServerSwitcher.Editor -- path/to/taan_server.0m
```

### Tests

The stock `taan_server.0m` is a game file, so it is not in this repository (`.0m` files are git-ignored).
Copy it from the TAAN Online client's data folder to:

```
tests/OpenTaan.ServerSwitcher.Core.Tests/fixtures/taan_server.0m
```

Without it, the tests that need it are reported as skipped; the rest still run. The tests expect the
unmodified client file (SHA-256 `6e08b360…6594ac`, channels `180.148.129.2`–`.7`).

### Publishing

Self-contained builds:

```sh
dotnet publish src/OpenTaan.ServerSwitcher.Editor -c Release -r win-x64     --self-contained
dotnet publish src/OpenTaan.ServerSwitcher.Editor -c Release -r osx-arm64   --self-contained
dotnet publish src/OpenTaan.ServerSwitcher.Editor -c Release -r linux-x64   --self-contained
```

## Command line

```sh
opentaan-switcher info     taan_server.0m                 # headers, seed, channels
opentaan-switcher switch   taan_server.0m 203.0.113.10 [--port 2180] [-o out.0m]
opentaan-switcher export   taan_server.0m config.xml      # or .json
opentaan-switcher import   config.xml out.0m --template taan_server.0m
opentaan-switcher decrypt  taan_server.0m plain.bin
opentaan-switcher encrypt  plain.bin out.0m --template taan_server.0m
opentaan-switcher validate taan_server.0m
```

(`dotnet run --project src/OpenTaan.ServerSwitcher.Cli -- <command> …` during development.)

## Safety

- **The original seed is always reused** when re-encrypting (stock files use `0x83EF8226`). A different seed
  can crash the client, so neither the editor nor the CLI offers one. Without `--template`, the CLI's `import`
  and `encrypt` reuse the seed of the file they overwrite; only a brand-new file gets the stock seed.
- Saving checks the result by decrypting it again before anything is written, writes through a temporary
  file, and keeps the first version of an overwritten file as `<file>.bak`.
- IP addresses and ports are validated; errors block saving.

## File format

```
.0m file
  u16 0x27AA · u16 flags · u32 size of inner block
  'S' · u8 flags (1 = zlib, 2 = XOR) · u32 checksum · u32 seed · u32 plaintext size
  payload = XOR(zlib(plaintext)), 64-byte key: w0 = seed ^ 0x8473FBC1, wN = wN-1 − 0x7B8C043F
  checksum = Adler-32 of plaintext, but starting from 0 instead of 1

plaintext
  14-byte header (kept verbatim)
  node = string name · string metadata · u32 n · (string key · string value)×n · u32 m · node×m
  string = u32 character count · UTF-16LE characters
```

The tree is shown as XML for viewing and export. Things XML cannot express (repeated attribute keys, empty
names, metadata) use reserved `_om.*` markers so XML export → import is lossless.
