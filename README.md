![MarkMello](assets/cover.png)

# MarkMello

[Website](https://markmello.ru) · [Telegram](https://t.me/mark_mello)

**MarkMello is an application for quickly opening and reading Markdown files, with an additional editing mode.**

## What MarkMello can do

MarkMello allows you to:

- quickly open Markdown files in reading mode;
- adjust the reading experience: theme, font size, line height, and document width;
- switch to editing mode when needed and make changes to the file.

## How it differs from regular Markdown editors

MarkMello opens the file for reading first.

Editing is not the primary startup mode: it is enabled manually when you need to make changes.

## Installation

Download the latest build from [Releases](../../releases/latest).

### Windows

1. Download `MarkMello-setup-win-x64.exe` or `MarkMello-setup-win-arm64.exe`, depending on your computer architecture.
2. Run the installer.
3. Launch MarkMello from the Start menu or open a `.md` file with MarkMello.

### macOS

1. Download `MarkMello-macos-arm64.dmg` for Apple Silicon or `MarkMello-macos-x64.dmg` for Intel Mac.
2. Open the DMG.
3. Drag `MarkMello.app` into `Applications`.
4. Launch the app from `Applications`.

### Linux

1. Download `MarkMello-linux-x86_64.AppImage`.
2. Make it executable and run it:

```bash
chmod +x MarkMello-linux-x86_64.AppImage
./MarkMello-linux-x86_64.AppImage
```

The AppImage needs FUSE to mount itself. Distributions that ship only FUSE 3 need
`libfuse2` installed, or you can run the file with `--appimage-extract-and-run`.

## Temporary unsigned builds

Current public MarkMello builds are temporarily distributed without a developer signature. Because of that, Windows or macOS may show a warning on first launch.

This is a temporary distribution pipeline limitation. Developer signing and the normal notarization/signing chain will be added in the future.

### Windows: bypass SmartScreen

If Windows shows a SmartScreen warning:

1. Click `More info`.
2. Click `Run anyway`.

If Windows marked the downloaded file as blocked:

1. Open the installer file properties.
2. Enable `Unblock`, if the option is available.
3. Apply the changes and run the installer again.

### macOS: bypass Gatekeeper

If macOS says the app is damaged, cannot be verified, or cannot be opened because it is from an unknown developer:

1. Open `System Settings`.
2. Go to `Privacy & Security`.
3. Find the message about blocked `MarkMello`.
4. Click `Open Anyway`.
5. Confirm the launch.

If you need to remove the quarantine flag manually for a one-time test:

```bash
xattr -dr com.apple.quarantine /Applications/MarkMello.app
open /Applications/MarkMello.app
```

## Build from source

.NET SDK 9 is required.

```bash
dotnet restore ./MarkMello.sln
dotnet build ./MarkMello.sln
```

Run the project:

```bash
dotnet run --project ./src/MarkMello.Desktop/MarkMello.Desktop.csproj
```

Open a file from the command line:

```bash
dotnet run --project ./src/MarkMello.Desktop/MarkMello.Desktop.csproj -- ./sample.md
```

Open the showcase with the common Markdown elements, to check how each of them renders:

```bash
dotnet run --project ./src/MarkMello.Desktop/MarkMello.Desktop.csproj -- ./samples/showcase.md
```

## Keyboard shortcuts

| Action | Windows / Linux | macOS |
| --- | --- | --- |
| Open file | `Ctrl+O` | `Cmd+O` |
| Toggle editing mode | `Ctrl+E` | `Cmd+E` |
| Find in document | `Ctrl+F` | `Cmd+F` |
| Save | `Ctrl+S` | `Cmd+S` |
| Save as | `Ctrl+Shift+S` | `Cmd+Shift+S` |
| Larger text | `Ctrl+=` / `Ctrl++` | `Cmd+=` / `Cmd++` |
| Smaller text | `Ctrl+-` | `Cmd+-` |
| Reset text size | `Ctrl+0` | `Cmd+0` |
| Settings | `Ctrl+,` | `Cmd+,` |

## Ideas and suggestions

Have an idea or suggestion? Share it in [GitHub Discussions: Ideas](https://github.com/dartdavros/MarkMello/discussions/categories/ideas).

## License

The project is distributed under the GPL-3.0 license.

See [LICENSE](LICENSE).

## Acknowledgements

Diagram support in MarkMello is built on open-source projects:

- [Naiad](https://github.com/Papyrine/Naiad) — a .NET library that renders Mermaid diagrams to SVG in-process, without a browser or external runtime. MIT License.
- [Mermaid](https://github.com/mermaid-js/mermaid) — diagram syntax and specification.

Syntax highlighting in code blocks is built on:

- [TextMateSharp](https://github.com/danipen/TextMateSharp) — a .NET port of the TextMate grammar engine; its grammar package bundles the language grammars of [Visual Studio Code](https://github.com/microsoft/vscode). MIT License.
- [Onigwrap](https://github.com/aikawayataro/Onigwrap) — a .NET wrapper for the [Oniguruma](https://github.com/kkos/oniguruma) regular expression library. MIT License; Oniguruma is under the BSD 2-Clause License.

Interface icons come from [Lucide](https://lucide.dev) — ISC License (icons inherited from Feather are MIT); the full notice is in [LUCIDE_LICENSE.txt](src/MarkMello.Presentation/Themes/LUCIDE_LICENSE.txt).
