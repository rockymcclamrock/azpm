# Building azpm

## Prerequisites

- **.NET 10 SDK** (`dotnet --version` → `10.x`)
- For a **Native AOT publish** only, a C/C++ toolchain:
  - **Windows:** Visual Studio 2022 with "Desktop development with C++" (provides `link.exe` + the
    Windows SDK).
  - **Linux:** `clang` and `zlib1g-dev`.
  - **macOS:** the Xcode command-line tools (`xcode-select --install`).

Plain `dotnet build` / `dotnet test` need none of the C++ bits.

## Common commands

```sh
dotnet build                       # build everything
dotnet test                        # run the xUnit suite
dotnet run --project src/Azpm -- ls --home ./scratch/home    # run the CLI locally
dotnet publish src/Azpm -c Release -r win-x64   # single-file native binary -> …/publish/azpm(.exe)
```

RIDs used by CI: `win-x64`, `osx-arm64`, `linux-x64`.

## Windows AOT gotcha

The AOT compiler shells out to `vswhere.exe` to locate the MSVC linker. If it isn't on `PATH`
you get:

```
'vswhere.exe' is not recognized … Microsoft.NETCore.Native.targets … error MSB3073
```

Fix — either publish from a **Developer PowerShell/Command Prompt for VS 2022**, or add the
installer directory to `PATH` for the session:

```powershell
$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;$env:PATH"
dotnet publish src/Azpm -c Release -r win-x64
```

GitHub's `windows-latest` runners already have it set up.

## Layout

```
src/Azpm/            the CLI (System.CommandLine + Native AOT)
  Handlers/          one class per command, dependency-injected + unit-tested
tests/Azpm.Tests/    xUnit v3; FakeAzRunner stands in for the Azure CLI
docs/spikes/         Phase 0 spike findings
```
