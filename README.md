# Lumidlet

A J2ME emulator for Windows 10 Mobile. It runs Java ME games and apps (CLDC 1.1 / MIDP 2.0) natively on the phone, developed and tested on a Lumia 950 running build 15254.

There's no Java on the phone. Lumidlet carries its own bytecode interpreter and implements the Java ME class libraries in C#, so a `.jar` goes straight from your downloads into the app.

## What it supports

- **VM:** all JVM bytecodes, including `jsr`/`ret` from old compilers. Exceptions, real threads with `synchronized`/`wait`/`notify`, and lazy class loading, so `Class.forName` checks for optional APIs behave the way games expect.
- **CLDC 1.1:** `java.lang` (String, StringBuffer, boxed types, Math, Thread, Runtime, System), `java.util` (Vector, Stack, Hashtable, Random with Java's exact sequence, Date, Calendar, TimeZone, Timer) and `java.io` (byte-array, data, reader and writer streams, with UTF-8, ISO-8859-1 and windows-1251).
- **LCDUI:**
  - Canvas and GameCanvas, plus the full Graphics API: arcs, round rects, triangles, `drawRegion` with all 8 transforms, `drawRGB`, `copyArea` and clipping.
  - Images in PNG (built-in decoder) and JPEG/GIF/BMP, fonts, Commands with soft keys and an options menu.
  - Form with all its items, List, TextBox, Alert and Ticker. Text fields open the phone keyboard.
- **Game API:** Sprite (frames, reference pixel, transforms, pixel-level collisions), TiledLayer with animated tiles, LayerManager.
- **RMS:** record stores are saved per game, with enumerations, filters, comparators and listeners.
- **Sound (MMAPI):** MIDI goes through a built-in software synthesizer, so no extra packages are needed. WAV, MP3 and AMR play through the phone's media player. Tone sequences and `Manager.playTone` work too.
- **Nokia UI API:** FullCanvas, DirectGraphics (pixel formats, flips and rotations, polygons), DeviceControl vibration, and `com.nokia.mid.sound.Sound`, including OTA ringtones.
- **Networking:** HTTP and HTTPS through `Connector`/`HttpConnection`.

## Using it

1. **Add games.**
   - Tap **+** and pick a `.jar` (and its `.jad`, if you have one).
   - Or open a `.jar` from File Explorer.
   - Or drop files into the app's `LocalState\inbox` folder; they're imported the next time Lumidlet starts.
2. **Play.** Tap a game to start it. The on-screen keypad has the soft keys, the D-pad and the number keys, and you can slide your finger between keys. Touching the game screen sends pointer events.
3. **Change settings.** Press and hold a game to set its screen size, MIDlet, text encoding, sound, keypad, smoothing and landscape mode. You can also **Pin to Start** from there, so the tile opens the game directly.
4. **Use the in-game menu.** **Back** (or the ☰ key) opens it, with resume, keypad, scaling, sound, restart and exit.

Most games are made for one screen size and draw only that area. If a game only fills the top-left corner, change its screen size in the settings (common sizes are 128×160, 176×208, 176×220, 240×320 and 320×240).

A Bluetooth keyboard works as well:

| Key | Does |
| --- | --- |
| Arrows | D-pad |
| Enter | Fire |
| F1 / F2 | Soft keys |
| Digits | Number keys |
| E / R | `*` / `#` |
| Esc | Menu |

## Building

You'll need Visual Studio 2019 with the UWP workload, the Windows 10 SDK 19041 and the Windows Mobile Extension SDK.

```bash
powershell -ExecutionPolicy Bypass -File app/build.ps1
```

This builds a Release|ARM `.appx` (no bundle) and bumps the package version first. Add `-Install -Phone <ip>` to install the package on the phone through Device Portal.

`harness/` is a desktop test runner for the same core. It runs a jar without a window, presses keys from a script and saves screenshots plus a WAV of the synthesizer output:

```bash
powershell -ExecutionPolicy Bypass -File harness/build.ps1
```

```bash
harness/bin/harness.exe game.jar --size 176x208 --time 20000 --every 2000 --keys "2000:-5,4000:-2+500" --shots out
```

Key codes follow Nokia's: −1…−4 are up, down, left and right, −5 is fire, −6 and −7 are the soft keys, and 48–57 are the digits. Set `LUMIDLET_TRACE=1` to log every exception the game throws.

## Layout

| Path | What's in it |
| --- | --- |
| `core/Vm` | Class file parser, interpreter, threads, class loading |
| `core/Lib` | CLDC: `java.lang`, `java.util`, `java.io` |
| `core/Midp` | MIDP: display and event loop, rasterizer, LCDUI screens, game API, RMS, media and synth, Nokia APIs, networking |
| `app` | UWP shell: library, game page, keypad, host (Win2D text, WIC images, AudioGraph, MediaPlayer) |
| `harness` | Desktop test runner |
| `test` | Test MIDlets, compile-only MIDP stubs, screenshot montage script |
| `tools` | Phone helpers: push jars to the inbox, launch with a key script, take screenshots |

## Not supported yet

M3G 3D (JSR-184), Bluetooth (JSR-82), file access (JSR-75), SMS (JSR-120), raw sockets, and vendor APIs other than Nokia's.
