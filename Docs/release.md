# Release Build

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Target

Build a Windows player from Unity after the runtime port has a playable main scene.

Target scene:

- `Assets/Scenes/Main.unity`

Current starter scene:

- `Assets/Scenes/SampleScene.unity`

## Prerequisites

- Unity `6000.4.8f1`
- Windows build support module installed
- URP 2D renderer assigned in project settings
- `Assets/Scenes/Main.unity` added to Build Profiles or Build Settings

## Editor Build

Use Unity editor:

1. open project
2. create or verify `Assets/Scenes/Main.unity`
3. open Build Profiles or Build Settings
4. choose Windows
5. build to `Builds/Windows/play-ground.exe`

## Command-Line Build

Add a build script later under:

- `Assets/Editor/Build/WindowsBuild.cs`

Expected command shape:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Unity.exe' -batchmode -projectPath . -executeMethod PlayGround.EditorBuild.WindowsBuild.BuildRelease -quit
```

Do not add this script until runtime scene and build profile exist.
