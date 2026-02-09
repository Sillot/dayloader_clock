# Dayloader Clock

A Windows desktop app inspired by the **Dayloader Clock** concept by [Matty Benedetto](https://www.youtube.com/@MattBenedetto). It displays a visual progress bar of your workday that fills up segment by segment with a color gradient (green → yellow → orange → red).

## Tech Stack

| Component | Technology |
|---|---|
| Language | **C# 12** |
| Framework | **.NET 8** |
| UI | **WPF** (Windows Presentation Foundation) |
| System tray | **Windows Forms** (`NotifyIcon`) |
| Storage | **JSON** (`System.Text.Json`) — files in `%APPDATA%/DayloaderClock/` |

## Key Features

- 80-segment progress bar with color gradient
- Automatic lunch break handling (configurable)
- Overtime detection and counter
- System tray icon with mini preview
- Always-on-top, draggable floating window
- Auto-reset on day change
- Optional auto-start with Windows

## Getting Started

```bash
dotnet run
```

> Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) + Windows 10/11
