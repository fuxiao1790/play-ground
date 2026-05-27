# Testing

## Summary

This project uses headless Godot smoke tests from [`tests/`](../tests/).

Most tests are standalone GDScript entry scripts that:

- boot a tiny runtime or scene
- run a small gameplay scenario
- call `quit(0)` on pass
- call `quit(1)` on failure

Tests are used to check correctness of behaviour.

Rule:

- if behavior is incorrect, test must fail

Tests are not just for "something ran."
Tests should prove expected gameplay or runtime behavior happened, and reject wrong behavior clearly.

## Keep Tests From Polluting Runtime Code

Tests should observe behavior through public gameplay/runtime contracts. Do not
ask runtime code to write extra metadata, counters, flags, or debug breadcrumbs
only so a test can inspect them.

Preferred test observability:

- create a test-only C# or GDScript listener/recorder under [`tests/`](../tests/)
- use an existing public runtime method such as a count, state query, or
  renderer/debug query
- assert on real gameplay effects such as damage, despawn, target state, or
  callback payloads

Avoid:

- `SetMeta(...)` markers in production code that only tests read
- exported test toggles on gameplay nodes
- fake runtime arrays or bookkeeping that are not useful to gameplay,
  diagnostics, or tooling

If a new observer is needed, keep it test-owned unless it is a real public
diagnostic feature with non-test value.

Test-only Godot scene fixtures must also live under [`tests/`](../tests/).
If a `.tscn` file is used only by smoke tests, keep it in `tests/scenes/`
instead of the runtime `scenes/` tree.

## Important Local Rule

On this machine, headless Godot should be run with an explicit `--log-file` path.

Reason:

- default `user://logs` has been unstable here
- missing log setup can cause noisy failures or crashes before the real test result

## Build C# Before Running Tests

Run this from the project root:

```powershell
& 'E:\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe' --headless --path . --build-solutions --quit
```

Use this whenever C# files changed.

## Run One Smoke Test

Example:

```powershell
if (Test-Path tests/_projectile_root_loop_combine_smoke.log) { Remove-Item -LiteralPath tests/_projectile_root_loop_combine_smoke.log -Force }
& 'E:\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe' --headless --path . --log-file tests/_projectile_root_loop_combine_smoke.log --script res://tests/projectile_root_loop_combine_smoke.gd
if (Test-Path tests/_projectile_root_loop_combine_smoke.log) { Get-Content tests/_projectile_root_loop_combine_smoke.log }
```

Replace:

- log path
- `--script` path

to match the test you want.

## Run Another Example

```powershell
if (Test-Path tests/_projectile_rendering_multimesh_smoke.log) { Remove-Item -LiteralPath tests/_projectile_rendering_multimesh_smoke.log -Force }
& 'E:\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe' --headless --path . --log-file tests/_projectile_rendering_multimesh_smoke.log --script res://tests/projectile_rendering_multimesh_smoke.gd
if (Test-Path tests/_projectile_rendering_multimesh_smoke.log) { Get-Content tests/_projectile_rendering_multimesh_smoke.log }
```

## Run All Smoke Tests

This simple loop runs every `*_smoke.gd` file in `tests/`:

```powershell
$godot = 'E:\godot\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe'
$failures = @()

Get-ChildItem tests -Filter *_smoke.gd | Sort-Object Name | ForEach-Object {
	$testFile = $_
	$logFile = Join-Path $testFile.DirectoryName ("_" + [System.IO.Path]::GetFileNameWithoutExtension($testFile.Name) + ".log")

	if (Test-Path $logFile) {
		Remove-Item -LiteralPath $logFile -Force
	}

	& $godot --headless --path . --log-file $logFile --script ("res://tests/" + $testFile.Name)
	$exitCode = $LASTEXITCODE

	if (Test-Path $logFile) {
		Get-Content $logFile
	}

	if ($exitCode -ne 0) {
		$failures += $testFile.Name
	}
}

if ($failures.Count -gt 0) {
	Write-Host "Failed tests:"
	$failures | ForEach-Object { Write-Host $_ }
	exit 1
}

Write-Host "All smoke tests passed."
```

## How To Read Results

Pass usually looks like:

- printed summary line from the test
- process exit code `0`

Fail usually looks like:

- `push_error(...)` output in terminal or log
- GDScript backtrace
- process exit code `1`

## Current Test Location

Smoke tests live in [`tests/`](../tests/).

Projectile-focused examples:

- [`tests/projectile_root_loop_combine_smoke.gd`](../tests/projectile_root_loop_combine_smoke.gd)
- [`tests/projectile_rendering_multimesh_smoke.gd`](../tests/projectile_rendering_multimesh_smoke.gd)
- [`tests/projectile_main_scene_player_to_mob_smoke.gd`](../tests/projectile_main_scene_player_to_mob_smoke.gd)
- [`tests/projectile_tracking_smoke.gd`](../tests/projectile_tracking_smoke.gd)
- [`tests/projectile_stress_smoke.gd`](../tests/projectile_stress_smoke.gd)
