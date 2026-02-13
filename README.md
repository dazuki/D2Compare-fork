> This is a fork of [locbones/D2Compare](https://github.com/locbones/D2Compare).
>
> The original WinForms application has been migrated to [Avalonia UI](https://avaloniaui.net/) for cross-platform support (Windows and Linux) and changed target framework from .NET 7 to .NET 8 (LTS).
 
## Requirements

### Windows

.NET 8 SDK or Runtime (LTS)

Download: https://dotnet.microsoft.com/en-us/download/dotnet/8.0

### Linux

**Ubuntu/Debian:**
```bash
sudo apt install dotnet-runtime-8.0
```

**Fedora:**
```bash
sudo dnf install dotnet-runtime-8.0
```

**Arch Linux:**
```bash
sudo pacman -S dotnet-runtime-8.0
```

### Screenshot

<img src="https://i.imgur.com/9kYoo5Y.png" />

> **Note:** This fork was developed by dazuki with the assistance of Claude Code.
> 
> This is more of a personal project to make some tools i like to use when modding D2R more compatible with Linux natively.
> 
> Feel free to try it out, otherwise use the original version by locbones!

---

# D2Compare (README.md from locbones/D2Compare)
This program was made to assist modders in comparing (and eventually converting) .TXT files between varying datasets.
It will allow you to compare between different retail versions of both legacy and resurrected; or use your own files.
At this time, the program makes no changes to any files and should be considered completely safe to use in your workflow.

I built this program using Visual Studio 2022 with C# Winforms and with no external dependencies needed. However, .NET Core 7.0+ is required to run this program.
My coding skills leave much to be desired; just trying to help how I can. I'm always open to submissions/improvements in time.
The future plans for this program will be to enable more filetypes to compare as well as allow converting between version structures. As with everything; it will happen as time allows.

Please join our Discord community for more info and support:
https://www.discord.gg/pqUWcDcjWF