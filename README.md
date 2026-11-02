# BoomBox

Pick a folder and a Linux Live ISO. The folder becomes a separate Linux system: no installer, no boot screen, no setup questions.

Prefer pictures? Open [README-comic.html](README-comic.html) for the animated comic version.

## Using it

1. Run `dist\BoomBox.exe`.
2. Choose a **Folder** and a **Linux Live ISO**, then click **Create box**. This took about 4 minutes for a 3 GB Ubuntu ISO.
3. Click **Open terminal**, or double-click `Terminal.cmd` in the folder.

| You want to | Do this |
| --- | --- |
| Copy files in or out | Copy them into the folder in Explorer. Linux sees them at `/box` (also `~/Box`). |
| Run a file from the folder | Tick the right-click option in BoomBox, then right-click the file and choose **Open in BoomBox**. On Windows 11 it is under **Show more options**. |
| Install software | Use the OS as normal, for example `sudo apt install nmap`. `sudo` needs no password. |
| Run graphical Linux apps | Start them from the terminal. They display through WSLg, Windows' built-in Linux display. |
| See Linux's own files | Click **System files**, or double-click `System Files.cmd`. |

**Open in BoomBox** handles `.deb`, `.rpm`, `.AppImage`, `.sh`, `.py` and Linux programs. Double-clicking a file still opens it in Windows; only the right-click entry sends it to Linux.

## Which ISO to use

Use a **Live** ISO. Installer-only ISOs contain no ready-made system, and BoomBox refuses them with a message.

- Kali: `kali-linux-...-live-amd64.iso`, not `...-installer-amd64.iso`. Kali offers the Live ISO as a **torrent only** (5.5 GB for 2026.3), so you need a torrent client. Check it against Kali's `SHA256SUMS`. A real Kali ISO has not been tested yet.
- Ubuntu: Desktop or live-server
- Debian: `debian-live-...`

Windows ISOs do not work. 64-bit Intel/AMD images only.

## How isolated is a box?

BoomBox runs on WSL2, Windows' built-in lightweight virtual machine. You never see or boot it, but it is there: Linux needs a Linux kernel.

**Separate for each box:** files, packages, users, services, hostname.

**Blocked from Windows by default:** the `C:` drive, running Windows programs, and the Windows `PATH`.

**Shared or not blocked:**

- All boxes share one Microsoft kernel, so `uname -r` shows a WSL kernel.
- Boxes share one network with each other and can reach your Windows machine and LAN.
- Root inside a box can get back to Windows. `sudo mount -t drvfs C: /mnt/x` works, and `sudo` needs no password.

Boxes protect you from an app making a mess. **They do not contain malware.** Use a full virtual machine for anything you suspect is hostile.

## Good to know

- First start takes about 5 seconds. After that the box starts faster while WSL is running.
- A box's disk is `.boombox\ext4.vhdx`, a hidden file in the folder. Do not delete it by hand.
- **Do not move or rename a box folder.** WSL and `/box` both record its exact path.
- **Delete** erases the Linux system. Files in the folder stay.
- Needs Windows 10/11 with WSL2 and the .NET 9 Desktop Runtime. If WSL is missing, BoomBox tells you what to run.

## Building

```bash
dotnet publish src/BoomBox -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o dist
```

`boombox-cli.exe` does the same work from a terminal: `create <folder> <iso>`, `list`, `open <path>`, `delete <folder>`.

The bundled helper is `alpine-minirootfs-3.24.3-x86_64.tar.gz`. BoomBox imports it once as the `BoomBox-Helper` WSL distro to read ISOs.
