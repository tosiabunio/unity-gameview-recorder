# Game View Recorder

Records the Game view in Play Mode, with audio (Unity or Wwise) and optionally the mouse cursor and clicks, to an `.mkv` or `.mp4` file. The video has the Game view's resolution (what `Screen.width × Screen.height` is in Play Mode, letterbox bars and overlay UI included) and a constant frame rate in real time. Gizmos are in the video while the Game view's **Gizmos** toggle is on (tested with URP); turn it off for a clean recording. Editor only. ffmpeg does the encoding.

## Add it to a project

You need:

- Unity 6000.0 or newer. The toolbar button needs 6000.3 or newer.
- ffmpeg on every machine that records ([Install ffmpeg](#install-ffmpeg-once-per-machine)).

Install it from [OpenUPM](https://openupm.com/packages/com.airon.gameview-recorder/) or straight from GitHub. Either way the package is read-only in the project. To change it, clone the repository into `Packages/com.airon.gameview-recorder` instead; Unity loads it as an embedded package. To remove it, select it in the Package Manager and press **Remove**.

### Install from OpenUPM

With the [OpenUPM CLI](https://openupm.com/docs/getting-started-cli.html), in the project folder:

```
openupm add com.airon.gameview-recorder
```

Or add the OpenUPM registry and the package to `Packages/manifest.json` yourself, next to what's already there:

```json
{
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": ["com.airon.gameview-recorder"]
    }
  ],
  "dependencies": {
    "com.airon.gameview-recorder": "0.5.6"
  }
}
```

The Package Manager then lists new versions, and **Update** installs them.

### Install from GitHub

Unity needs git, installed and on PATH, to fetch the package.

1. **Window > Package Manager**, the **+** button > **Install package from git URL...**
2. Enter:

   ```
   https://github.com/tosiabunio/unity-gameview-recorder.git#0.5.6
   ```

Or add the line to `Packages/manifest.json` yourself:

```json
"com.airon.gameview-recorder": "https://github.com/tosiabunio/unity-gameview-recorder.git#0.5.6"
```

`#0.5.6` picks a release; the [tags](https://github.com/tosiabunio/unity-gameview-recorder/tags) list them all. To update, change the tag. Without one, Unity takes the latest commit on `main` and keeps it (in `packages-lock.json`) until you add the URL again.

### Keep recordings out of version control

Recordings go to `<project>/Recordings/` by default. Have your version control ignore that folder (with git, add `/Recordings/` to the project's `.gitignore`), or set an output folder outside the project in the recorder window.

### Install ffmpeg (once per machine)

ffmpeg isn't bundled.

- Windows: `winget install Gyan.FFmpeg` (or `choco install ffmpeg`)
- macOS: `brew install ffmpeg`

You don't need to restart Unity. Open **Tools > Game View Recorder > Open Window** and press **Detect**; the ffmpeg section should show its version.

The recorder looks for ffmpeg on PATH. On Windows it re-reads the user and machine PATH, which is why a fresh install works without a restart. It also looks in the usual winget, Chocolatey, Scoop and Homebrew folders. Anywhere else, set the path in the window. That path is saved per machine and applies to all projects.

### Check that it works

1. Choose a fixed resolution in the Game view's resolution menu, e.g. 1920×1080. The video uses that size.
2. Enter Play Mode, press **Ctrl+F7**, play for a few seconds, press **Ctrl+F7** again.
3. The Console shows `[GameViewRecorder] Saved <file>.mkv (…)`. The window's **Show in Explorer** and **Play** buttons open it.

To get the record button on the main toolbar, right-click an empty part of the toolbar and turn on *Game View Recorder/Record*. **Show All** shows every hidden toolbar element.

## Use

- **Tools > Game View Recorder > Open Window**: Start/Stop, live stats, settings, ffmpeg status.
- **Ctrl+F7** (Cmd+F7 on macOS; Tools > Game View Recorder > Start or Stop Recording): works while the Game view has focus in Play Mode. Rebind it in Edit > Shortcuts under Main Menu.
- **Toolbar toggle** (Unity 6.3+): a record button with the elapsed time. If it isn't on the main toolbar, right-click an empty part of the toolbar and turn on *Game View Recorder/Record*.
- **Record on Play**: starts a recording whenever Play Mode starts.

Leaving Play Mode, a script reload or quitting the Editor stops the recording and finishes the file. The Editor's Pause button pauses the recording. The Console gets a `[GameViewRecorder]` line with the saved file and its stats, or the error with ffmpeg's last log lines.

Settings are per user and per project, in `UserSettings/GameViewRecorder.asset`:

| Setting | Default | |
|---|---|---|
| Frame rate | 60 fps | Constant frame rate of the video. |
| Encoding | Shareable | See below. |
| Format | MKV | MKV or MP4; see [Formats](#formats). |
| Audio | Auto | Where the sound comes from; see [Audio](#audio). |
| Cursor | off | Draws an arrow cursor into the video; see [Cursor and clicks](#cursor-and-clicks). |
| Clicks | off | Draws a growing ring where a mouse button goes down. |
| Cursor size | Auto | Video pixels per cursor pixel, for the arrow and the click rings. Auto is 1× up to 1440p and 2× at 4K. |
| Output folder | `Recordings` | Absolute, or relative to the project folder. |
| File name | `{product} {date}` | Tokens `{product}`, `{scene}`, `{date}`; the format's extension (`.mkv`, `.mp4`) is added, existing files are never overwritten. |
| Record on Play | off | |

### Encodings

| Preset | ffmpeg | Use |
|---|---|---|
| Shareable | `libx264` 4:2:0, CRF 18, veryfast, BT.709 + AAC 192k | Plays everywhere. |
| Lossless | `ffv1` RGB + FLAC | Exact pixels, for editing or archiving. Larger files; plays in VLC/mpv. Always MKV. |
| Hardware | `h264_nvenc` 4:2:0 + AAC | NVIDIA GPUs; almost no CPU cost at 1080p and above. |
| Custom | your options | Replace the codec options, e.g. `-c:v libx264rgb -crf 0 -c:a flac`. Don't add `-vf`; the recorder sets it. |

### Formats

| Format | ffmpeg | |
|---|---|---|
| MKV | `-f matroska` | Keeps what was written if ffmpeg is stopped before it finishes the file. Not every player or site takes it; QuickTime doesn't. |
| MP4 | `-f mp4 -movflags +faststart` | Plays everywhere, browsers included. Readable only once ffmpeg has finished it: a recording cut off before then is lost. |

- **MP4 finishes in two passes.** When the recording stops, ffmpeg moves the file's index to the front, so a browser can start playing before the whole file has loaded. That rereads the file, which takes a moment for long recordings.
- **Lossless always records to MKV**: few players read FFV1 from an MP4. The window shows MKV while Lossless is picked and keeps your choice for the other encodings.
- **Custom** options must use codecs MP4 can hold (H.264, HEVC, AV1, AAC, FLAC, ...). Otherwise ffmpeg stops at the start and the Console shows its error. Your options come after `-movflags +faststart`, so they can replace it.
- An existing MKV can be remuxed to MP4 without re-encoding: `ffmpeg -i in.mkv -c copy -movflags +faststart out.mp4` (not for Lossless).

### Audio

| Audio | Records |
|---|---|
| Auto | Wwise while its sound engine is running, else Unity audio (when it's enabled). |
| Unity | What the active AudioListener hears. Needs Unity audio: Project Settings > Audio > Disable Unity Audio must be off. |
| Wwise | The Wwise sound engine's main output: the final mix, as it goes to the speakers. |
| Off | No sound. |

**Wwise projects** usually have Unity audio disabled, so Auto picks Wwise. The recorder finds the Wwise integration (`AkUnitySoundEngine`, or `AkSoundEngine` in older integrations) by reflection, so the package needs no reference to it. It uses the integration's device capture, tested with Wwise 2025.1.

By default Wwise **suspends its audio while the Game view isn't focused** (Project Settings > Wwise Initialization > Suspend Audio During Focus Loss). The recording then gets silence for as long as you're clicking around the Editor. Starting a recording from the window or the toolbar gives the focus back to the Game view, and Ctrl+F7 doesn't take it away. To keep the sound while the Game view isn't focused, turn on **Render During Focus Loss** in the same settings. Wwise then keeps rendering, so the recording keeps its sound even though you don't hear it.

Other middleware (FMOD) isn't supported. Its output bypasses the AudioListener.

### Cursor and clicks

The OS draws the mouse cursor over the Game view after Unity has rendered it, so it isn't in the screen copy. With **Cursor** on, the recorder draws a standard arrow (black outline, white fill, 12×21 pixels at 1×) where the mouse is, in every frame:

- The position is the one the game sees in that frame: the Input System's `Mouse.current`, or `Input.mousePosition` in projects that use the old Input Manager. The arrow follows the game's frames, so it can look a frame or two behind the cursor you see on screen.
- Nothing is drawn while the game hides the cursor (`Cursor.visible = false`, or `CursorLockMode.Locked`) or the mouse is outside the Game view.
- It's always the arrow. A custom cursor (`Cursor.SetCursor`) or the OS's text and resize cursors aren't drawn as such.
- By default the Input System gives the game mouse input only while the Game view has focus, so the arrow stays where it was while you work in other Editor windows.

With **Clicks** on, a ring grows from where a mouse button goes down and fades out over 0.4 s: one ring for the left button, two in a row for the right. The rings are white with darker edges, 3 to 18 pixels in radius at 1×, and the arrow is drawn over them.

- Presses are picked up in every Update, so a click in a frame the video skips still shows. They are timed on the recording clock, so the rings grow smoothly whatever the game's frame rate.
- Clicks show while the cursor is hidden too (a game usually shows it again on a click), but not while it's locked or outside the Game view. They work with or without **Cursor**.

## How it works

1. A hidden object waits for the end of each frame (`WaitForEndOfFrame`, after the UI is drawn) and copies the screen with `ScreenCapture.CaptureScreenshotIntoRenderTexture`.
2. `AsyncGPUReadback` reads it without stalling the GPU, and a writer thread pipes the raw RGBA frame to ffmpeg's stdin.
3. The video runs on a real-time clock. Each frame lands in the slot of the moment it was captured. A slot the game didn't render in time repeats the previous frame (*repeated*). When the game renders faster than the video's rate, the extra frames aren't captured. Frames that would overflow the encoder queue (about one second, at most 512 MB) are skipped and also show as repeats (*dropped*). The game never waits for the encoder.
4. With **Cursor** on, the mouse position is read when the frame is captured; with **Clicks** on, the rings' size and fade at that moment. They are drawn into the frame after the readback, before it goes to ffmpeg.
5. Audio is streamed to ffmpeg as raw float samples over a loopback TCP connection. Unity audio is tapped with `OnAudioFilterRead` on the active AudioListener's GameObject, so it's the final mix. Wwise audio is read every frame from the integration's capture of the main output (`StartDeviceCapture`, `GetCaptureSamples`), interleaved floats in Wwise's sample rate and channel layout. The audio follows the same clock as the video. Silence is padded while there is no sound source (no listener, Wwise suspended), and drift beyond about 0.15 s (sound card clock vs. system clock) is corrected (*corrections*).
6. Paused time (the Editor's Pause button) is left out of both. Wwise keeps rendering while the Editor is paused; that audio is dropped.

The video size is fixed at the first frame. If the Game view changes size mid-recording (Free Aspect + resizing), frames are centered in the original size, unscaled, and a warning is logged. Pick a fixed resolution in the Game view to avoid this.

## Limitations

- Editor and Play Mode only. The runtime assembly has a `UNITY_EDITOR` define constraint, so nothing ships in builds.
- The Game view must be visible: Unity doesn't render a Game view tab hidden behind another tab, so the video freezes.
- Sound comes from Unity audio or Wwise, one of them. FMOD isn't supported, and in a project that plays sound through both Unity and Wwise only one is recorded.
- Needs a graphics API with async GPU readback (all desktop APIs). Tested on Windows / Direct3D 12 and macOS / Metal. The vertical orientation of the screen copy depends on the API: it's flipped for OpenGL and not for Direct3D, Metal or Vulkan. Direct3D and Metal are verified.

## Layout

- `Runtime/` (`Airon.GameViewRecorder`): the recording engine. `RecordingSession` (capture, lifecycle), `VideoWriter` and `AudioWriter` (writer threads), `FrameTimeline` and `AudioAligner` (timing), `FfmpegArguments` (presets and formats), `FrameGrabber` and `AudioTap` (hidden components), `WwiseCapture` (Wwise output, bound by reflection), `CursorOverlay` and `ClickRings` (the drawn cursor and clicks), `MouseInput` (the mouse, from the Input System or the Input Manager).
- `Editor/` (`Airon.GameViewRecorder.Editor`): `GameRecorder` (Editor entry point: `StartRecording`, `StopRecording`, `ToggleRecording`, `Session`, `Changed`), `RecorderSettings`, `FfmpegLocator`, window, menu and toolbar.
- `Tests/Editor/`: NUnit edit-mode tests (timing, audio alignment, ffmpeg arguments and formats, writers, cursor and click drawing, Wwise binding against a fake engine, file names, ffmpeg lookup).
