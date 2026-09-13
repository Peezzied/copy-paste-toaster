# CopyToast

A lightweight, standalone Windows background listener that displays an **Android-style pill toast notification** at the bottom center of the screen whenever anything is copied or pasted.

Built in pure C# with native WPF (.NET Framework) — zero external runtime dependencies, 0% CPU when idle, ~24 KB executable.

---

## Features

- **Android-Style Pill Toast**: Floating dark rounded capsule (`#E81E1E1E`, 20px radius, drop shadow) with smooth fade-in/fade-out animations.
- **Universal Copy Interception**:
  - Uses native Windows OS clipboard push notifications (`AddClipboardFormatListener` / `WM_CLIPBOARDUPDATE`).
  - **Text & Code**: `[Copied] 📝 <snippet>` or `[Copied] 💻 <code snippet>`.
  - **URLs**: `[Copied] 🔗 https://...`.
  - **Files & Folders**: `[Copied] 📁 3 files (report.pdf, photo.png, +1 more)`.
  - **Images & Screenshots**: `[Copied] 🖼️ Image (1920 × 1080)` (Snipping Tool `Win+Shift+S`, PrtScn, paint, browser copy).
  - **Audio & Media**: `[Copied] 🎵 Audio Stream`.
- **System-Wide Paste Interception**:
  - Global low-level hook (`WH_KEYBOARD_LL`) capturing <kbd>Ctrl</kbd>+<kbd>V</kbd>, <kbd>Shift</kbd>+<kbd>Insert</kbd>, <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>V</kbd> (terminals/plain text paste), and <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>V</kbd>.
  - Context menu paste event hooks (`SetWinEventHook`).
- **Focus Safe**: Uses `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` so it **never steals focus** from your typing, active games, or applications.
- **All-In-One Executable**: `CopyToast.exe` serves as the GUI Control Panel, background daemon, installer, and uninstaller.
- **Ultra-Lightweight**: Single self-contained binary with 0 external dependencies.

---

## Usage

### 1. Control Panel GUI
Simply double-click **`CopyToast.exe`** to open the Control Panel:
- View live status (**Running** / **Stopped**, **Enabled** / **Disabled** in Windows Startup).
- Toggle the listener on/off with one click.
- Install to or Uninstall from Windows Startup with one click.
- Test toast animations with the **Show Test Toast** button.

### 2. Command-Line (CLI)
You can also control `CopyToast.exe` via terminal or scripts:

| Command | Description |
| :--- | :--- |
| `CopyToast.exe` | Opens the GUI Control Panel. |
| `CopyToast.exe --install` | Installs to `%LOCALAPPDATA%\CopyToast`, adds to Windows Startup, and launches the daemon. |
| `CopyToast.exe --uninstall` | Stops running instances and removes the Windows Startup shortcut. |
| `CopyToast.exe --start` | Runs the silent background listener directly. |
| `CopyToast.exe --stop` | Gracefully terminates any running background instances. |

---

## Building from Source

To compile `CopyToast.exe` using Windows' built-in C# compiler (`csc.exe`):

```cmd
build-exe.bat
```

No Visual Studio, SDK, or extra package installation is required.

---

## Project Structure

```
copy-paste-toaster/
├── CopyToast.exe       # Precompiled all-in-one binary (~24 KB)
├── build-exe.bat       # One-click compiler script
├── README.md           # Documentation
└── src/
    └── CopyToast.cs    # Full C# source code
```

---

## License

MIT License.
