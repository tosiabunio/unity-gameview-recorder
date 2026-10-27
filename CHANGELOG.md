# Changelog

## 0.5.5 - 2026-10-27

- README: gizmos are recorded while the Game view's Gizmos toggle is on (tested with URP's 2D renderer), and recording is verified on macOS / Metal as well as Direct3D. No code changes.

## 0.5.4 - 2026-10-27

- No more CS0618 warning on Unity 6000.5 and newer: the AudioListener lookup uses `FindObjectsByType<T>()`, which replaces the obsolete overload with a sort mode there. Older versions keep the old call.
- Author in `package.json`: Maciej Miasik.

## 0.5.3 - 2026-10-27

- README: installing from GitHub is the only setup described. No code changes.

## 0.5.2 - 2026-10-27

- Published on GitHub (`https://github.com/tosiabunio/unity-gameview-recorder`) under the 0BSD license (`LICENSE.md`). Install it from the git URL in Package Manager.
- README: installing from GitHub, keeping recordings out of version control. No code changes.

## 0.5.1 - 2026-10-18

- The recording shortcut is **Ctrl+F7** (was Ctrl+F9). Ctrl+F9 is also Multiplayer Play Mode's "Focus Player One", and F9 alone is Fullscreen Editor's Game View and the Profiler's record toggle. A binding changed in Edit > Shortcuts stays as it is.

## 0.5.0 - 2026-10-13

- **Format**: MKV (default) or MP4. MP4 gets its index at the front (`-movflags +faststart`, a second pass when the recording stops), so browsers start playing before the whole file has loaded; it's readable only once ffmpeg has finished it. Lossless always records to MKV. The file extension follows the format.

## 0.4.0 - 2026-10-12

- Optional clicks (**Clicks**, off by default): a ring grows from where a mouse button goes down and fades out over 0.4 s, one for the left button, two in a row for the right. Drawn under the cursor, sized by **Cursor size**. Presses are picked up every Update and timed on the recording clock.

## 0.3.0 - 2026-10-12

- Optional cursor (**Cursor**, off by default): draws an arrow into the video at the mouse position, since the OS cursor isn't part of the Game view image. Hidden while the game hides or locks the cursor. **Cursor size** sets its scale (Auto: by the video height).
- The runtime assembly references the Input System when the project has it (`GAMEVIEWRECORDER_INPUT_SYSTEM`), for the mouse position; otherwise it uses the old Input Manager.

## 0.2.0 - 2026-10-11

- Wwise audio: records the Wwise sound engine's main output through the integration's device capture, bound by reflection (no reference to Wwise). For projects with Unity audio disabled.
- "Record audio" became **Audio**: Auto (Wwise when running, else Unity), Unity, Wwise, Off.
- With Unity audio disabled, the recorder no longer queries AudioSettings, which made Unity log "Audio system is disabled, so AudioSettings.outputSampleRate cannot be queried".
- Starting a recording gives the focus back to the Game view (Wwise suspends while it's unfocused).

## 0.1.0 - 2026-10-11

- First version: records the Game view in Play Mode, with audio, to MKV through ffmpeg at the Game view's resolution.
- Presets Shareable (H.264), Lossless (FFV1), Hardware (NVENC) and Custom.
- Recorder window, Ctrl+F9 menu shortcut, main toolbar toggle (Unity 6.3+), Record on Play.
- Real-time constant frame rate with repeated/dropped frame stats; audio kept on the same clock; Editor pause excluded.
