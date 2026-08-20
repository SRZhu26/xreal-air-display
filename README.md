# XREAL Air Viewer

XREAL Air Viewer is a Windows desktop viewer for using XREAL Air glasses as a stationary 3DoF display. It captures a selected desktop monitor and presents it on a configurable flat or curved panel while head rotation keeps the panel stable in the scene. This viewer is a separate application from the legacy PhoenixHeadTracker tracker below; it renders the desktop locally instead of sending pose data to OpenTrack or controlling the mouse.

## Quickstart

The viewer is an x64 .NET 8 developer build. Use Windows 10/11 with the original XREAL Air connected directly over USB-C, Windows configured to **Extend these displays**, and two distinct displays available: one for the captured desktop and one for the glasses output. Copy the x64 `AirAPI_Windows.dll` and `hidapi.dll` dependencies from the legacy application’s release folder before running the viewer. Their provenance and redistribution terms still need review before packaging a release.

1. Restore and build `XrealAirViewer\XrealAirViewer.sln` for **Release | x64**.
2. Launch `PhoenixAirViewer.App.dll` from the x64 Release output folder.
3. Choose different source and output displays. The viewer refuses to start if both selections are the same or only one display is available.
4. Click **Connect Air**, look straight ahead, and click **Recenter**. Live-desktop startup automatically recenters once per second for three seconds; a short `Ctrl+Alt+Space` press recenters while holding it enters alignment preview.
5. Choose the panel size, distance, horizontal `X` curvature, vertical `Y` curvature, axis sensitivity, roll lock, and horizon lock, then start **live desktop**. Fresh settings start with a larger `2.4 m x 1.35 m` curved monitor. Existing settings can use **Wide monitor** to apply the same preset.
6. Close the viewer normally so the panel, tracking, and display settings are saved.

The normal Release build writes diagnostic JSONL files under `%LOCALAPPDATA%\PhoenixAirViewer\logs\`. Use the `NoLogging | x64` configuration when file logging is not appropriate. Normal mode does not record captured desktop pixels or typed text. The explicit `--diagnostic` mode is the exception described below.

## XrealAirViewer (implementation in progress)

The `XrealAirViewer` solution is a separate application foundation for a stationary 3DoF desktop viewer. The existing `PhoenixHeadTracker` WinForms/OpenTrack application remains unchanged as the legacy tracker.

Implemented so far:

- Quaternion-first pose contracts, normalization, shortest-path SLERP, recentering, optional smoothing, optional angular-velocity limiting, roll lock, and tested horizon lock in `PhoenixAirViewer.Core`.
- Signed pitch, yaw, and roll sensitivity controls from `-200%` to `+200%`. `+100%` keeps the configured direction, `-100%` reverses it, and `0%` disables that axis. The current starting values are pitch `+100%`, yaw `-100%`, and roll `-100%`.
- Editable `Near`, `Mid`, `Far`, and `Furthest` distance profiles. Each profile stores its own distance, pitch/yaw/roll gains, and signed translation-assist gain. The defaults are `0.7 m`, `0.85 m`, `1.0 m`, and `1.2 m`; old `Middle` and `Medium` settings migrate to `Mid` and `Far`.
- Signed yaw and pitch drift-rate controls from `-10.00` to `+10.00 deg/s` with `0.01 deg/s` resolution. Both default to `0`; negative and positive values continuously counter-rotate the room-locked panel in opposite directions.
- Optional bounded 2.5D translation assist derived from pitch and yaw. It is a visual comfort heuristic, not physical 6DoF position tracking.
- A resizable and maximizable control window with an explicit label and value for every slider, including yaw/pitch translation and signed yaw/pitch drift rates.
- The default AirAPI/Fusion WXYZ mapping is the evidence-supported signed permutation decoded `(X,Y,Z)` -> renderer `(-X,-Z,-Y)`, so the observed physical pitch, yaw, and roll axes reach the renderer's pitch, yaw, and roll axes respectively. Guided calibration can replace it for a different physical mounting or sign convention.
- The default live path disables artificial smoothing and the optional user angular-velocity limit to reduce visual lag during fast head rotation, while retaining a conservative `900 deg/s` pose-stability ceiling for impossible one-sample jumps. Existing single-radius panel settings migrate their radius to both independent axes.
- An `AirPoseSource` adapter that uses the native `GetQuaternion` export and reports missing DLLs, wrong bitness, missing exports, null pointers, and invalid pose data.
- Windows monitor enumeration by device name and monitor bounds.
- A borderless output window with `Ctrl+Alt+Space` recenter support, distinct source/output display validation, and persisted monitor selection.
- A primary-desktop fallback: the app uses the Windows primary monitor as the source and the XREAL display as output when no saved source monitor is available. It does not create a new Windows virtual display.
- A background-thread live desktop panel using Desktop Duplication, a D3D11 swap chain, runtime-compiled shaders, quaternion camera counter-rotation, synchronized presentation, configurable size/distance, and independently adjustable horizontal and vertical curvature.
- A `Wide monitor` preset that enlarges the panel to `2.4 m x 1.35 m` with `4 m` horizontal and vertical radii while leaving translation assist disabled.
- A source-coordinate mouse cursor overlay that stays attached to the captured desktop panel while it rotates or translates.
- The cursor overlay uses the current Windows cursor bitmap, alpha, dimensions, and hotspot so it follows the configured Windows pointer appearance instead of drawing a generic arrow.
- A temporary hold-to-align preview: hold the Recenter button or configured hotkey to make the panel head-following for comparison with a real monitor; release restores room lock without changing the saved neutral.
- Settings persistence under `%APPDATA%\PhoenixAirViewer\settings.json`, including panel geometry, signed axis sensitivity, smoothing/lock settings, and display identities.
- Capture/render resource recreation with bounded retry after access loss, device removal, display loss, or renderer exceptions.
- Diagnostic JSON-lines logging with native, capture, session, renderer, and settings errors, plus a no-op logger for the quiet build.
- A dependency-free test executable covering pose math, horizon lock, panel settings, persistence, logging, and latest-sample storage.

The remaining release gates are physical validation of the corrected compensation during rapid rotation, cross-adapter/output testing, richer telemetry, and native DLL provenance/redistribution review. The viewer targets .NET 8 Windows and x64; the legacy tracker remains .NET Framework 4.7.2.

## XrealAirViewer quick start

The current implementation is a developer-build foundation. It requires Windows 10/11 x64, the .NET 8 SDK, an x64 graphics adapter, and the native `AirAPI_Windows.dll` plus `hidapi.dll` files copied from the legacy x64 Release folder. The latest evidence supports the WXYZ layout and signed axis mapping; repeat the controlled pose sequence through the glasses to certify the physical compensation signs.

1. Connect the glasses directly over USB-C and set Windows to **Extend these displays**.
2. Build the solution in x64.
3. Launch the diagnostic build first so a reproducible log and structured pose evidence are available.
4. Select different source and output displays. The current viewer intentionally refuses to start when only one display is available or both selections are the same.
5. Click **Connect Air**, look straight ahead, and click **Recenter** or briefly press `Ctrl+Alt+Space`. Hold either input to enter alignment preview after the hold threshold. Live-desktop startup repeats recentering once per second for three seconds while re-priming the yaw drift counter.
6. Select the `Near`, `Mid`, `Far`, or `Furthest` distance profile. Adjust panel width, height, horizontal `X` curvature, vertical `Y` curvature, pitch/yaw/roll sensitivity, signed pitch/yaw drift rates, translation assist, roll lock, and horizon lock. Use **Wide monitor** for the large curved preset, then start **live desktop**.
7. Stop the viewer before unplugging the glasses. Settings are saved when the control window closes.

The control window remains on the selected normal monitor. The output is a borderless window positioned on the selected output monitor. A probe that presents to a hidden or normal monitor is not proof that the image is visible through physical glasses.

### Axis sensitivity tuning

The control window exposes one signed slider for pitch, yaw, and roll. The value is a multiplier applied to the recentered renderer-space rotation before the world-locked camera transform:

- `+100%` is the configured axis direction at one-to-one scale.
- `-100%` reverses the axis direction at one-to-one scale.
- `0%` suppresses that axis.
- Values above `100%` increase the movement, and values between `0%` and `100%` reduce it.

For the first physical check, leave **Roll lock** and **Horizon lock** unchecked. Start at pitch `+100%`, yaw `-100%`, and roll `-100%`, then recenter after Air Fusion warm-up. Test one axis at a time: verify pitch direction first, reduce its magnitude if necessary, then verify that yaw moves the finite panel opposite the gaze and that roll rotates the panel in-plane in the opposite direction. The selected values are saved with the viewer settings and recorded in diagnostic manifests and pose-evidence records.

The curved mesh is symmetric about the panel center and opens toward the viewer. Horizontal `X` curvature bends the left and right edges, while vertical `Y` curvature bends the top and bottom edges; either axis can be flat independently. The `Wide monitor` preset uses a `2.4 m x 1.35 m` panel and `4 m` radii; reducing either radius increases that axis's bend, subject to the minimum-radius validation. Curvature changes the panel geometry only. It does not correct pose drift.

### Profiles, drift, and alignment preview

The distance profile selector keeps separate tuning values for `Near`, `Mid`, `Far`, and `Furthest`. Switching profiles applies the profile's distance and four tuning gains together. The selected profile and values are saved under `%APPDATA%\PhoenixAirViewer\settings.json`.

`Yaw drift rate` and `Pitch drift rate` are direct continuous counters. They range from `-10.00` to `+10.00 deg/s` in `0.01 deg/s` steps and default to `0`. A negative value moves the room-locked panel in the negative renderer direction; a positive value moves it in the positive direction. Use a very small value such as `-0.01 deg/s` for a slow counter, and adjust the sign while the glasses remain stationary. These controls do not infer drift or wait for stillness; they apply the selected rate continuously using elapsed pose time.

The shipped yaw counter starts at `-0.11 deg/s`; pitch drift starts at `0 deg/s`. At live-desktop startup, the viewer recenters once per second for three seconds while re-priming yaw drift at `-0.25`, `+0.25`, and finally `-0.11 deg/s`. Set either rate to `0` when you want no continuous counter.

When **Start live desktop** is pressed, the viewer captures the selected source display without changing its brightness, covering it, or changing its input behavior. Adjust the source monitor brightness yourself if you want a darker physical screen; the source framebuffer remains available to Desktop Duplication. The viewer then automatically recenters once per second for three seconds while the yaw drift counter is re-primed. Keep your head in the desired neutral position during that startup interval; a manual recenter remains available afterward.

`Translation (yaw + pitch)` adds a bounded panel-plane offset derived from the current viewing angle and selected panel distance. It intentionally ignores roll, and the horizontal yaw offset moves opposite the turn so the panel center does not simply orbit with the head. It can make the display feel less like a rotating sheet, but it is not measured head translation or true 6DoF parallax. Start at `0%`, then increase it gradually during physical testing.

The viewer does not install, create, or rearrange Windows monitors. It captures a monitor already exposed by Windows and renders it into the connected XREAL output. **Wide monitor** changes only the rendered panel geometry, not the Windows desktop resolution.

The mouse pointer is composited into the source desktop coordinates, so it should move with the desktop panel rather than remain fixed to the output display. Hold **Recenter** or `Ctrl+Alt+Space` for approximately a quarter second to enter alignment preview. While held, the panel follows the head and a cyan border marks the XREAL output surface for alignment against a real monitor. Releasing the input restores room lock and immediately recenters at the current pose, committing that monitor-facing direction as the new neutral. A short click still performs a normal recenter.

While live desktop is running, `Ctrl+Alt+Q` and `Ctrl+Alt+C` stop it. The source display is never changed by the viewer; its current brightness, windows, and normal mouse interaction remain under Windows control.

### Build and run

Use an installed `dotnet` command or the user-local SDK path shown below:

```powershell
$dotnet = 'C:\Users\shuairon\.dotnet\dotnet.exe'
& $dotnet restore XrealAirViewer\XrealAirViewer.sln
& $dotnet build XrealAirViewer\XrealAirViewer.sln --configuration Release -p:Platform=x64
& $dotnet 'XrealAirViewer\PhoenixAirViewer.App\bin\x64\Release\net8.0-windows\PhoenixAirViewer.App.dll'
```

For a quiet build with no file logging:

```powershell
& $dotnet build XrealAirViewer\XrealAirViewer.sln --configuration NoLogging -p:Platform=x64
& $dotnet 'XrealAirViewer\PhoenixAirViewer.App\bin\x64\NoLogging\net8.0-windows\PhoenixAirViewer.App.dll'
```

The normal Release build writes logs to `%LOCALAPPDATA%\PhoenixAirViewer\logs\` when file logging is enabled. The `NoLogging` configuration always uses the no-op logger and must not create log files. Normal mode does not record desktop pixels or typed text.

### Tests and probes

Run the x64 test DLL directly so the native dependency path and architecture are unambiguous:

```powershell
& $dotnet build XrealAirViewer\XrealAirViewer.sln --configuration Debug -p:Platform=x64
$tests = 'XrealAirViewer\PhoenixAirViewer.Tests\bin\x64\Debug\net8.0-windows\PhoenixAirViewer.Tests.dll'
& $dotnet $tests
& $dotnet $tests --capture-probe
& $dotnet $tests --renderer-probe
& $dotnet $tests --camera-convention-probe
& $dotnet $tests --renderer-screen-probe
& $dotnet $tests --session-probe
```

The default test path is hardware-independent. `--capture-probe` validates Desktop Duplication, `--renderer-probe` validates one D3D11 present, `--camera-convention-probe` validates the inverse world-lock transform, finite-panel perspective behavior, and mixed-axis relative pose composition, `--renderer-screen-probe` presents one captured frame to a real monitor and saves a screenshot without Air hardware, and `--session-probe` requires at least two Windows displays so source and output can be distinct. These probes do not validate quaternion axes or visible output inside the glasses.

For a physical test that proves the viewer path is active before taking any images, run:

```powershell
& $dotnet build XrealAirViewer\PhoenixAirViewer.Tests\PhoenixAirViewer.Tests.csproj --configuration Release -p:Platform=x64
& $dotnet 'XrealAirViewer\PhoenixAirViewer.Tests\bin\x64\Release\net8.0-windows\PhoenixAirViewer.Tests.dll' --live-hardware-probe
```

`--live-hardware-probe` connects the Air sensor first, waits for a fresh quaternion, reads monitor EDID identities, selects the XREAL Air display (`MRG`/XREAL/Nreal identity when available), selects a different desktop source, starts the real `DesktopViewerSession`, and waits for its first `presenting` status. It runs for 15 seconds or until Escape. Only after that status does it save separate source and output captures under `%TEMP%`; the JSONL log records the selected displays, source/copy pixel signatures, pose age, and render metrics. If several unidentified non-primary displays exist, it refuses to guess.

For a clean stationary hardware check, leave the glasses face down and still, then run:

```powershell
& $dotnet build XrealAirViewer\PhoenixAirViewer.Tests\PhoenixAirViewer.Tests.csproj --configuration Release -p:Platform=x64
& $dotnet 'XrealAirViewer\PhoenixAirViewer.Tests\bin\x64\Release\net8.0-windows\PhoenixAirViewer.Tests.dll' --stationary-pose-probe
```

`--stationary-pose-probe` records 30 seconds of native quaternion data, with compensation disabled, to a CSV under `%TEMP%`. A large relative change with fresh samples is evidence that the Air/Fusion pose itself is changing; stable native samples with moving processed output would instead indicate viewer correction or presentation logic. Normal diagnostic logs also emit one `air.pose.sample` record per second with native and decoded quaternion values.

### Diagnostic workflow

Use the normal Release build for hardware troubleshooting. Reproduce the issue, note the selected source/output displays and the exact motion or connection sequence, close the viewer cleanly, and collect the newest JSONL file from `%LOCALAPPDATA%\PhoenixAirViewer\logs\`. It contains startup/runtime information, display selection, native connection failures, pose validity errors, session transitions, recovery attempts, settings errors, and exception stacks. It does not contain captured screen images.

For an instrumented interactive run, launch the application with the explicit diagnostic switch:

```powershell
& $dotnet 'XrealAirViewer\PhoenixAirViewer.App\bin\x64\Release\net8.0-windows\PhoenixAirViewer.App.dll' --diagnostic
```

Diagnostic mode forces the normal file logger on, records viewer-local mouse/focus/action events, settings/display changes, and the native loader context before `StartConnection`. Key values are redacted while a text-entry control has focus. It does not install a global keyboard hook or record typed text.

For labeled pose evidence, connect the Air, start **live desktop**, and click **Pose evidence**. Hold each instructed position, press its button once, and keep holding that exact position through the visible three-second countdown: Neutral `0 deg`, Yaw Left/Right `30 deg`, Pitch Up/Down `20 deg`, and Roll Left/Right `20 deg`. Each press captures the native quaternion components, decoded and mapped quaternions, pose age/status, the last pose presented by the render session, and the selected display metadata. After three seconds it saves a full virtual-desktop image plus `source` and `output` framebuffer images. Evidence IDs are unique within one open Pose Evidence window. For a second complete calibration run, start a fresh diagnostic process so its filenames cannot collide with the first run. The pose captured at button press is the primary measurement; the delayed screenshot pose is retained separately to show whether the head moved during the countdown.

Each diagnostic session is stored under `%LOCALAPPDATA%\PhoenixAirViewer\diagnostics\<session>\` and contains `manifest.json`, `evidence.jsonl`, and evidence PNGs. The manifest records the native quaternion layout, sensor basis, active distance profile, signed pitch/yaw/roll and translation gains, pitch/yaw drift rates, `world-locked` camera mode, capture delay, panel settings, process details, and display bounds. Each evidence record snapshots the profile and gains active when its button was pressed. Alignment preview adds rate-limited `alignment.preview.sample` events containing native/mapped/relative/presented pose, neutral, camera mode, translation offset, active drift rates, and timestamps. The PNG for the XREAL output is a Windows framebuffer capture; it is not a camera image of what is seen through the lenses. These files can contain anything visible on the displays, so use this mode only for a controlled reproduction and share the files carefully. Close the application normally after the reproduction so both JSONL logs and pending evidence records are flushed or explicitly marked incomplete.

For the requested object-persistent behavior, `world-locked` means the desktop is a finite virtual panel fixed in room orientation. After recentering, turning your head should reveal a corresponding portion or corner of that panel while the rest moves out of the field of view, just as with a real monitor viewed from an angle. The panel should not remain full-size, centered, and face-on while following your gaze. Start with a flat panel distance of about `2 m` for physical validation; the default `1 m` distance makes a `1.6 m` panel become strongly oblique during a modest yaw. Large yaw can make a flat panel narrow or edge-on, which is expected for room locking. The renderer uses the inverse camera orientation for this behavior; using the direct orientation would create a head-facing display instead.

#### Reporting a problem

After reproducing an issue, close the viewer normally, then run this PowerShell block. It creates a metadata-only feedback bundle containing the newest runtime JSONL log, diagnostic manifest, evidence JSONL, and settings snapshot. Desktop screenshots are not copied by default because they may contain private content.

```powershell
$root = Join-Path $env:LOCALAPPDATA 'PhoenixAirViewer'
$latestLog = Get-ChildItem (Join-Path $root 'logs\*.jsonl') | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$latestSession = Get-ChildItem (Join-Path $root 'diagnostics') -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$bundle = Join-Path $env:TEMP ('PhoenixAirViewer-feedback-' + $stamp)
$diagnostics = Join-Path $bundle 'diagnostics'
New-Item -ItemType Directory -Path $diagnostics -Force | Out-Null
if ($latestLog) { Copy-Item $latestLog.FullName (Join-Path $bundle 'runtime.jsonl') }
if ($latestSession) {
	Copy-Item (Join-Path $latestSession.FullName 'manifest.json') $diagnostics -ErrorAction SilentlyContinue
	Copy-Item (Join-Path $latestSession.FullName 'evidence.jsonl') $diagnostics -ErrorAction SilentlyContinue
}
$settings = Join-Path $env:APPDATA 'PhoenixAirViewer\settings.json'
if (Test-Path $settings) { Copy-Item $settings (Join-Path $bundle 'settings.json') }
Compress-Archive -Path $bundle -DestinationPath ($bundle + '.zip') -Force
Write-Output ('Feedback bundle: ' + $bundle + '.zip')
```

If visual evidence is needed, copy only the matching evidence PNGs from the same diagnostic session into `$diagnostics` before creating the archive. The `output` PNG is a Windows framebuffer capture, not a camera image through the glasses. Include a short note with the build command, selected source/output displays, exact movement, expected behavior, observed behavior, and whether the issue was present while the head was moving or only after it stopped.

The most useful log events are `air.connect.failed`, `air.pose.invalid`, `pose.recentered`, `alignment.preview.entered`, `alignment.preview.sample`, `alignment.preview.exited`, `session.attempt.failed`, `session.metrics`, `evidence.press`, `evidence.capture.completed`, and `evidence.capture.failed`. `session.metrics` includes `presentSkips`, average/maximum `presentIntervalMs`, and `poseMaxVelocityDegPerSec`: a high pose velocity with normal present intervals points to sensor/Fusion motion, while present skips or a large maximum interval points to render pacing. The `pose.recentered` event reports the pre-recenter offset in degrees, the previous relative quaternion, the gain-adjusted estimate, and sample age. In `evidence.jsonl`, compare `PoseAtPress`, `PoseAtScreenshot`, and `PoseUsedForLastPresentation`, along with the active profile, signed sensitivity values, translation gain, and pitch/yaw drift rates. For the corrected mapping, pitch should primarily change mapped X, yaw mapped Y, and roll mapped Z. A large-yaw edge-on view is expected for the default room-fixed panel; pitch-induced roll, stale pose status, missing presentations, or a large difference between press and screenshot pose are issues to report.

Use the `NoLogging` build when diagnostic files are not acceptable. It keeps the same UI-visible errors and viewer behavior but deliberately does not create file logs.

### Current limitations

- The latest physical evidence supports the WXYZ layout and the default signed axis remap, but repeat the controlled pose matrix through the glasses after updating the viewer and confirm the compensation signs during rapid rotation before distributing a release.
- The first release path requires separate source and output displays on a compatible adapter. Cross-adapter capture-to-present transfer is not implemented.
- This is rotational 3DoF: it can stabilize angular head rotation and optionally apply a bounded angle-derived translation assist, but it cannot measure physical translation or leaning, provide true 6DoF parallax, track eyes, or provide an absolute yaw reference. Some physical movement will therefore make the panel follow.
- The Air pose is rotational 3DoF and has no absolute yaw reference. If the glasses return physically to neutral but Fusion reports a different quaternion, the fixed neutral reference produces a residual screen angle; this is sensor yaw drift, not capture or presentation lag. The signed pitch/yaw drift rates are deliberately manual counters rather than an automatic drift classifier; use Recenter as the primary neutral correction, and use the rate controls only when a stationary counter is desired.
- Protected content, exclusive-fullscreen sources, GPU resets, output hotplug, and display resolution changes have recovery code but still need physical/manual validation.

The bundled native DLL exports `StartConnection`, `StopConnection`, `GetEuler`, `GetQuaternion`, and `GetBrightness`. Its dependency and redistribution terms must still be verified before creating a release package.

# Original PhoenixHeadTracker README

The following section is preserved from the original [PhoenixHeadTracker repository](https://github.com/iVideoGameBoss/PhoenixHeadTracker) for attribution. It documents the PhoenixHeadTracker application and its OpenTrack and mouse-tracking workflows; it does not describe the separate XREAL Air Viewer above.

# PhoenixHeadTracker
The Phoenix Head Tracker is a program that interfaces with Xreal Air glasses to capture and analyze sensor data using custom version of [AirAPI_Windows.dll](https://github.com/MSmithDev/AirAPI_Windows) to support roll data. By detecting changes in the user's head yaw and pitch and roll, this program can send this data to opentrack over UDP or you can even control the movement of the computer mouse on screen which can be used to play video games that use mouse look feature. You can also use this feature with Nreal (Xreal) Air 3D SBS mode

## Support
Hey, I created PhoenixHeadTracker for Xreal Air and would really appreciate your support. I work on this software on my own time for you guys. Thank You!

[![Buy Me A Coffee](https://img.buymeacoffee.com/button-api/?text=Buy%20me%20a%20coffee&button_colour=BD5FFF&font_colour=ffffff&font_family=Cookie&outline_colour=000000&coffee_colour=FFDD00)](https://buymeacoffee.com/ivideogameboss)

https://user-images.githubusercontent.com/129109589/229800261-125fdc69-845c-4815-9231-f5b6f53a43fa.mp4

I worked all day and night on this thing and it was worth it for you all. You will love your Xreal Air glasses with this new tool. It even works with 3D SBS mode. Play your games, Skyrim, DCS, Microsoft Flight Simulator, Cyberpunk 2077.

https://user-images.githubusercontent.com/129109589/230780822-298d8527-4deb-4d49-a18e-28d2e3c5ec9b.mp4

# How to use PhoenixHeadTracker
To connect your Xreal Air glasses to your PC, there are two options available. Firstly, you can use the USB-Type C connector. Alternatively, a goFanco adapter can also be used, which can be obtained from the following Amazon link: [goFanco adapter](https://www.amazon.com/gp/product/B08Y5PBWLQ/ref=ppx_yo_dt_b_asin_title_o03_s00?ie=UTF8&psc=1)

It is important to ensure that your glasses have a direct connection to the PC. Once connected, launch the PhoenixHeadTracker software and click on the 'Connect Xreal Air' option. Please allow a few seconds for the sensors to adjust.


You now have two options for utilizing the head tracking data. Firstly, you can use opentrack, or alternatively, you can click on 'Start Mouse Track'. This will allow you to control the mouse on your screen, enabling you to look around in video games. Here is an example of playing Cyberpunk 2077 with mouse look feature and using a controller at same time. On PC you can play games using a controller, mouse, keyboard. PhoenixHeadTracker and Xreal Air are the perfect match.

https://user-images.githubusercontent.com/129109589/231939591-a10d483a-73e6-49ed-bf91-9a9bea4aa893.mp4

Should you choose to use opentrack, you can do so by clicking on the 'start opentrack UDP' option. Within opentrack, you will need to select UDP over network in order to receive the data.

![Screenshot 2023-04-14 211257](https://user-images.githubusercontent.com/129109589/232178275-0cf625e5-ec33-4693-a267-54263bb61514.png)

Opentrack settings for UDP

![Screenshot 2023-04-08 210432](https://user-images.githubusercontent.com/129109589/230751023-7cad672a-8384-430a-80d7-90aa4ea986ce.png)

You can also adjust how to use the Yaw and Pitch values. So if you want the in game camera to turn 90 degrees like in real life you can adjust it in mappings.

![Screenshot 2023-04-12 073500](https://user-images.githubusercontent.com/129109589/231459880-3880c7c7-425a-4139-8880-e4882242ed39.png)

Here you can see I wanted the in game camera to turn faster on Yaw so I don't have to turn 90 degrees is real life. It makes it eaiser on the neck when playing games. So now when I turn my head it will turn faster to left and right using the data from PhoenixHeadTracker.

![yaw](https://user-images.githubusercontent.com/129109589/231812388-13638e1f-8a0d-4ab1-92d3-9df32284643e.png)

I did the same think for Pitch so it just feels right.

![pitch](https://user-images.githubusercontent.com/129109589/231812662-f7456c5b-ff64-4778-b579-c5f7ca037648.png)


# Setup your Center Key in Opentrack

Due to how gyro data works and drifts, it is a good idea to have a center camera key setup. Just click on bind and pick a key. In this example you can see I picked the '-' key on my numpad.

![Screenshot 2023-04-13 103644](https://user-images.githubusercontent.com/129109589/231813488-767b9d61-0373-4315-b4c1-ae6a2a4d24f9.png)


# Fight Drift

When you are looking around in a game the Yaw, Pitch and Roll values can drift overtime. You can fight this type of drift more by adding a negative or positive value. You want to try to keep the ‘Track’ value to where; when you can turn your head and bring it back, and it shows you the same view more or less or close enough. Don't drive yourself crazy over this cause you can always center the view in opentrack with the shortcut key I told you about above. Remember we are dealing with math and the physical world and earth's gravity. Below example shows I added a value of -3 to help fight drift on my Xreal Air glasses Yaw value when playing Elite Dangerous.

![fightdrift](https://github.com/iVideoGameBoss/PhoenixHeadTracker/assets/129109589/dbd6ff27-c79a-43ec-984f-d59dbe586da4)



# Microsoft Flight Simulator working with opentrack

https://user-images.githubusercontent.com/129109589/230751056-9ac0df97-939f-4e08-b3d2-690d606b58e5.mp4


# Download Latest Release

Phoenixheadtracker https://github.com/iVideoGameBoss/PhoenixHeadTracker/releases

Opentrack https://github.com/opentrack/opentrack/releases

# How to build using Visual Studio 22
PhoenixHeadTracker is based on the AirAPI_Windows.dll :https://github.com/MSmithDev/AirAPI_Windows: You will find the custom version of AirAPI_Windows.dll that supports roll data and also hidapi.dll in the PhoenixHeadTracker/bin/x64/Release/ and or debug folder. These two files are required in order to connect to Xreal Air glasses. The version of AirAPI_Windows.dll included with PhoenixHeadTracker supports roll data. 


Once you clone the project, open in Visual Studio 22 by clicking on PhoenixHeadTracker.sln. Make sure you set to build on x64 and debug or release. Then simply click on start.

![visualstudio22](https://user-images.githubusercontent.com/129109589/228050319-965458a1-af36-466a-8aa7-c45364bc91dd.png)


Make sure that both AirAPI_Windows.dll and hidapi.dll are in the debug and release folder. I have included them with project.

![Screenshot 2023-03-27 145335](https://user-images.githubusercontent.com/129109589/228051761-b6afc531-5881-4ea3-b935-c2c07860951e.png)

# You Can Use Phoenix Head Tracker with Gyro Data from Other Devices 

PhoenixHeadTracker can be made to work with other devices that can supply gyro data using a dll. You can also add code if you want to work with 6dof data. Its already setup to work with 3dof data in degrees. Create your own dll and import it like I did here with AirAPI_Windows.dll. Add two functions to get started. 

![Screenshot 2023-04-13 105555](https://user-images.githubusercontent.com/129109589/231817088-a0858efd-4658-409c-86d4-4a896ee8b6a9.png)

The GetEuler function returns an array for Yaw, Pitch and Roll. 

![Screenshot 2023-04-13 104815](https://user-images.githubusercontent.com/129109589/231816062-8c449833-fc7f-4a5b-9395-3fad939c88ea.png)



# DCS (Digital Combat Simulator)


https://user-images.githubusercontent.com/129109589/230140740-2248b626-169c-4f85-bb17-baec839264f3.mp4

# Star History

[![Star History Chart](https://api.star-history.com/svg?repos=iVideoGameBoss/PhoenixHeadTracker&type=Date)](https://star-history.com/#iVideoGameBoss/PhoenixHeadTracker&Date)

