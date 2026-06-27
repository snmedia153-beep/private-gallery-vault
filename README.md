# Private Gallery Vault

Private Gallery Vault is a Windows desktop application for organizing personal images, videos, documents, and archives inside a local encrypted vault. It is built with WPF and .NET, with a dark dashboard-style interface, topic folders, media previews, duplicate detection, and fast lock controls.

The project focuses on local-first privacy. Media files are encrypted before they are stored, and temporary decrypted files are cleaned up after use or when the vault is locked.

## Highlights

- Local encrypted vault for personal media files
- Topic folders with sidebar search, sorting, and custom ordering
- Grid and list views for media and topic browsing
- Image viewer and video viewer with zoom, pan, mute, and thumbnail tools
- Drag-and-drop import with progress feedback
- Duplicate detection based on source fingerprinting
- Bulk topic change for selected files
- Manual ordering by drag-and-drop or exact position input
- Tag management, activity logs, duplicate manager, and backup tools
- Instant lock hotkey with modifier-key combinations such as `Ctrl+Shift+X`
- Safe handling for unusual or broken Unicode filenames

## Tech Stack

| Area | Technology |
| --- | --- |
| Desktop UI | WPF, XAML |
| Runtime | .NET 8 Windows |
| Language | C# |
| Local database | SQLite via `Microsoft.Data.Sqlite` |
| Encryption | AES-GCM for file and data encryption, PBKDF2 for password-derived key wrapping |
| Media handling | WPF imaging, MediaElement, Windows Shell thumbnail fallback |
| Build | PowerShell release script, self-contained win-x64 publish |

## Project Structure

```text
PrivateGalleryVault/
├─ src/
│  └─ PrivateGalleryVault/
│     ├─ Assets/              # App icon and bundled UI artwork
│     ├─ Models/              # Domain models
│     ├─ Services/            # Vault, crypto, database, thumbnail, backup services
│     ├─ ViewModels/          # UI presentation models
│     ├─ Views/               # Management center views
│     ├─ Windows/             # Main WPF windows and dialogs
│     ├─ App.xaml
│     ├─ App.xaml.cs
│     └─ PrivateGalleryVault.csproj
├─ docs/
├─ build_release.ps1
├─ .gitignore
└─ README.md
```

## Build Requirements

- Windows 10 or later
- .NET SDK 8 or later
- PowerShell

The project targets `net8.0-windows` and uses WPF, so it must be built on Windows or in a Windows-capable .NET environment.

## Release Build

Run from the repository root:

```powershell
.\build_release.ps1
```

The release output is created under:

```text
publish\win-x64\
```

Run:

```text
publish\win-x64\PrivateGalleryVault.exe
```

## Security Notes

- The vault password is not stored as plain text.
- Stored media files are encrypted before being written to the vault directory.
- Temporary decrypted files are created only when needed for viewing or external opening.
- Temporary files are cleaned up on lock, exit, and delayed retry paths for media files that may still be held by Windows codecs.
- This is a local desktop application; it does not require a server connection for core vault features.

## GitHub Upload Checklist

Before pushing to a public repository, confirm that the following are not included:

- Real personal images, videos, documents, or archives
- Local vault database files such as `*.db` or `*.sqlite`
- Runtime settings such as `settings.json`
- Backup files such as `*.pgvbackup`
- Build outputs such as `bin/`, `obj/`, and `publish/`

The included `.gitignore` already excludes these files.

## Suggested Repository Topics

```text
wpf, dotnet, csharp, desktop-app, sqlite, encryption, media-gallery, privacy, windows
```
