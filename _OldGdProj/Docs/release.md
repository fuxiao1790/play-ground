# Release Build

This project exports the current main scene, `res://scenes/level/main.tscn`, through the
`Windows Desktop` preset in [`export_presets.cfg`](../export_presets.cfg).

## Prerequisites

- Godot 4.6.2 Mono is installed at:
  `E:\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe`
- Godot 4.6.2 Mono export templates are installed under:
  `%APPDATA%\Godot\export_templates\4.6.2.stable.mono`
- The `Windows Desktop` export preset exists in
  [`export_presets.cfg`](../export_presets.cfg).

## Build

Run from the project root:

```powershell
New-Item -ItemType Directory -Force builds\windows | Out-Null
& 'E:\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe' --headless --path . --export-release 'Windows Desktop' 'builds/windows/play-ground.exe'
```

The release executable is written to:

```text
builds/windows/play-ground.exe
```

## Smoke Test

After exporting, run a quick headless launch check:

```powershell
& 'E:\godot\projects\play-ground\builds\windows\play-ground.exe' --headless --quit-after 2
```

An exit code of `0` means the build launched and shut down cleanly.

Godot may print non-fatal editor settings or Windows root certificate warnings
after export. The build is still valid if the export command exits with code
`0` and the executable exists.
