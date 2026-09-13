# CopyToast

A lightweight, standalone Windows background listener that displays an **Android-style pill toast notification** at the bottom center of the screen whenever text is copied or pasted.

Built in pure C# with native WPF (.NET Framework) — zero external runtime dependencies, 0 CPU when idle, ~20 KB executable.

---

## Features

- **Android-Style Pill Toast**: Floating dark rounded capsule (`#E81E1E1E`, 20px radius, drop shadow) with smooth fade-in/fade-out animations.
- **Copy & Paste Detection**:
  - **Copy / Cut**: Detects clipboard updates and displays `[Copied] <snippet>`.
  - **Paste**: Monitors <kbd>Ctrl</kbd>+<kbd>V</kbd> and <kbd>Shift</kbd>+<kbd>Insert</kbd> and displays `[Pasted] <snippet>`.
- **Focus Safe**: Uses `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` so it **never steals focus** from your typing, active games, or applications.
- **All-In-One Executable**: `CopyToast.exe` serves as the GUI Control Panel, background daemon, installer, and uninstaller.
- **Ultra-Lightweight**: Single 20 KB binary with 0 external dependencies.

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
├── CopyToast.exe       # Precompiled all-in-one binary (~20 KB)
├── build-exe.bat       # One-click compiler script
├── README.md           # Documentation
└── src/
    └── CopyToast.cs    # Full C# source code
```

---

## License

MIT License.
