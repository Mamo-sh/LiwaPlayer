# LiwaPlayer

🇹🇷 [Türkçe için tıklayın / Turkish version](README.tr.md)

A lightweight music player for Windows, designed for low-spec machines such as POS terminals. Streams audio-only from YouTube — no video decoding, minimal CPU and RAM usage.

![.NET 8](https://img.shields.io/badge/.NET-8.0-blueviolet) ![WPF](https://img.shields.io/badge/UI-WPF-blue) ![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-lightgrey)

## Features

- **YouTube search built in** — search videos or YouTube Music songs and add them to playlists without opening a browser. Only the audio stream is played (no video decoding).
- **User-defined playlists** — create, open, and delete as many playlists as you like from the playlist manager window. Playlists are stored as JSON.
- **Import from anywhere:**
  - **Spotify** playlists and albums (public links) — each track is automatically matched to its YouTube equivalent using duration-based matching.
  - **YouTube / YouTube Music** playlists and albums via share links.
  - **Local files** — MP3, M4A, FLAC, WAV, OGG and more, with automatic tag reading.
- **YouTube account sign-in** — optional login through an embedded browser window. With a Premium account, playback is ad-free and your private playlists become importable. Session cookies are stored encrypted with Windows DPAPI.
- **Birthday song auto-detection** — songs whose titles reference birthdays (Turkish verb forms included) are automatically routed to a playlist whose name contains "doğum", keeping celebration songs out of the regular rotation.
- **Background playback** — closing the window minimizes to the system tray; music keeps playing. Tray menu offers play/pause, next/previous, update check, and exit.
- **Auto-update** — checks GitHub Releases on startup and installs new versions with one click.
- **POS friendly** — single instance enforcement, remembered window position/volume/last playlist, data stored safely in `%LocalAppData%\LiwaPlayer`, optional start-with-Windows via the installer.

## Installation

Download the latest `LiwaPlayer-Setup-<version>.exe` from [Releases](../../releases) and run it. The installer:

- installs the self-contained app (no .NET runtime required),
- optionally creates a desktop shortcut and start-with-Windows entry,
- installs the Microsoft Edge WebView2 Runtime if missing (needed only for account sign-in).

## Building from source

Requirements: .NET 8 SDK, Windows 10+. For the installer package: [Inno Setup 6](https://jrsoftware.org/isdl.php).

```powershell
# Run in development
dotnet run --project LiwaPlayer

# Build the installer (self-contained publish + setup package)
.\Installer\build-setup.ps1 -Version 1.0.0
```

## Releasing an update

1. Bump the version: `.\Installer\build-setup.ps1 -Version 1.1.0`
2. Create a GitHub release tagged `v1.1.0` and attach `Installer\Output\LiwaPlayer-Setup-1.1.0.exe`.
3. Running installations detect the new release on startup (or via tray menu → *Check for Updates*), download the setup, and reinstall silently, restarting the app when done.

## Tech stack

| Component | Purpose |
|---|---|
| WPF (.NET 8) | UI, custom dark theme |
| LibVLC / LibVLCSharp | Audio playback (`--no-video`) |
| YoutubeExplode | YouTube search, playlists, audio stream resolution |
| YouTube Music InnerTube API | Music-only search results |
| Spotify embed endpoint | Public playlist/album track listing (no API key) |
| WebView2 | Google account sign-in window |
| Inno Setup 6 | Installer and silent auto-update |

## Notes

- Ad-free playback depends on the signed-in account actually having YouTube Premium; the app does not block ads itself.
- Spotify import reads public content only, up to ~100 tracks per list (embed endpoint limit). Tracks with no YouTube match are reported so you can add them manually from local files.
- All user data (playlists, settings, session, log) lives in `%LocalAppData%\LiwaPlayer\Data` and survives app updates.
