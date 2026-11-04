# 🔧 Technical notes

🌐 English | [日本語](technical.ja.md)

Internals, protocol, measurements and checks. For installation and settings, see the [README](../README.md).

## 🧩 Projects

| Project | Content |
|:--|:--|
| `src/CameraService.Core` | Video types and interfaces, YUV conversion, frame sharing, timestamp, image names, deletion targets (no OS dependency) |
| `src/CameraService.Platform.Windows` | Cameras (MediaFrameReader), JPEG and thumbnails (WIC), H.264 (Media Foundation) |
| `src/CameraService.Host` | Service (ASP.NET Core, Blazor Server, own CSS, Bootstrap Icons) |
| `tests/CameraService.UnitTests` | Core and Platform.Windows |
| `tests/CameraService.IntegrationTests` | Server with test cameras, real executable for startup |
| `tools` | Service registration and removal |

- Windows APIs are only in Platform.Windows. `AddWindowsPlatform()` registers them for the Core interfaces
- The test server skips `Program.cs` and the pages. `ProgramTests` starts the real executable
- Tests are executables (Microsoft Testing Platform): `tests\CameraService.UnitTests\bin\Debug\net10.0-windows10.0.26100.0\CameraService.UnitTests.exe`

## 🔄 Camera lifecycle

- Users (clients, still requests, saves) lease the camera. The first opens it, and it closes `IdleTime` after the last returns it
- Frames of the first `WarmupTime` are dropped
- Failure (open, close, error, `FrameTimeout`): close and reopen after 5 → 10 → 30 → 60 seconds, only while leased
- Failed until the next frame: if a camera opens but sends nothing, delays keep growing and `/health` stays 503
- Device search, open and close time out after 15 seconds and count as failures. Close releases the reader and the capture even on an exception
- Device and format are resolved on the first open and after a failure
- Steps run one at a time on a 0.5-second timer (at once for the first user)
- Exclusive access: while another application holds the camera, the open succeeds and the capture then fails (`0xC00D3704`)

## 🖼️ JPEG and MJPEG

- Encoded on the camera thread, thinned to `Stream:Fps`, only while clients wait. One encode is shared (latest frame only)
- YUY2 and video-range NV12 become full-range NV12, then WIC encodes
- A work buffer holds the JPEG, which is copied to a right-sized frame (no maximum-size buffer per frame, about 0.9 MB at 640x480)
- Closing the camera releases the encoder and buffers
- The next boundary follows each frame at once (browsers show a part when the next boundary arrives)
- `?raw`: same body, `Content-Type: application/octet-stream`, for `fetch` (browsers may refuse multipart for non-images)
- Live view: `fetch` + canvas (`img` cannot detect a lost connection). Latest image only when decoding lags. Leaving the page aborts `fetch`
- On stop: MJPEG ends and new requests get 503. Otherwise reconnecting pages delay the stop by 30 seconds

## 🎞️ H.264

Ported from tool-camera.

- **Encoder:** hardware first (NVENC etc.), then Windows software. Low latency, CBR, no B frames, Main profile
- **Lifetime:** per camera. Created in the background for the first client (NVENC 0.3-0.6 seconds), released after 10 seconds without clients (0.5-second timer, also while the camera is closed)
- **Failure:** clients closed with `InternalServerError`, view falls back to MJPEG, `/health` is `Degraded`. Retried after 10 seconds or for a new client
- **Frames:** all camera frames. YUY2 is reordered to NV12, range unchanged. One encode is shared
- **Key frames:** every `Stream:H264:KeyFrameInterval`. After connecting or skipping frames, sending waits for a key frame requested from the encoder (at most every 500 ms). The browser requests one by text message on a decode error or full queue
- **Colors:** camera range passed through and sent in the header. The browser sets the range and BT.601 matrix on the decoder, matching JPEG with any encoder
- **Viewer:** `VideoDecoder` on localhost or HTTPS, otherwise MJPEG. `NotSupportedError` also falls back to MJPEG
- **Stop:** connections close when the stop begins (`EndpointUnavailable`)

### WebSocket protocol

- Server: one binary message per frame (header + Annex B access unit), starting with a key frame
- Client: any text message requests a key frame
- Close status: `InternalServerError` (1011, no encoder, view uses MJPEG), `EndpointUnavailable` (1001, stop, view reconnects)
- `Stream:H264:Enable` = `false`: 404. Not a WebSocket request: 400

Header (little endian, 32 bytes, tool-camera's header without detection):

| Offset | Type | Content |
|--:|:--|:--|
| 0 | uint16 | Version (1) |
| 2 | uint16 | Header length (data position) |
| 4 | uint16 | Width |
| 6 | uint16 | Height |
| 8 | uint32 | Camera frame number |
| 12 | uint32 | Flags (bit 3: key frame, bit 4: full range with BT.601 matrix, otherwise video range) |
| 16 | float64 | Capture time (Unix ms) |
| 24 | float64 | Encoded time (Unix ms) |

## 🔌 Lost connections

MJPEG and H.264 behave the same, without a page reload.

- **Closed:** reconnects after 1 second, keeps the last frame, shows `Reconnecting`
- **No close (network down):** `Reconnecting` after 5 seconds without data, reconnects after 15 seconds
- A camera sending nothing behaves the same

## 🕒 Schedules

- Mofucat.JobScheduler evaluates cron in the offset of the current time, so it gets a TimeProvider returning local time (with UTC, `0 3 * * *` is 12:00 in Japan)
- That TimeProvider is not registered app-wide (other components expect UTC)
- `MisfirePolicy.Skip`

## 💾 Saving

1. Lease the camera `WarmupTime` + 2 seconds before the time (3 seconds by default)
2. Copy the first frame captured after the time as full-range NV12 (on the camera thread). Chosen by capture time: a frame arriving just after the time may have been captured before it
3. Burn in the time, encode with `Snapshot:Quality`
4. Write a `.tmp` file, replace `{folder}\yyyyMMdd\HHmmss.jpg`
5. Return the lease

- While the camera is failed, the run fails at once. No frame for 10 seconds also fails it
- One `Debug` line per save
- Timestamp: own 7-dot bitmap font, white with a black outline, neutral chroma under both, margin of `Scale` × 4 dots, cut at the image edge
- Deletion stops with a warning if the free space becomes unknown

## 🖼️ Thumbnails and cache

- **Cache:** `Cache-Control: private, max-age=86400`, `ETag` from file time and size, 304 on a match
- **Thumbnails:** WIC decodes scaled YCbCr planes, which are encoded directly (no color conversion)
  - About 320 wide, at a decoder scale (1/2, 1/4, 1/8). Scaled chroma comes at luma size, so 2x2 is averaged to 4:2:0
  - Quality 80, up to 64 MB in memory (least used removed)
- **Full size view:** loads the next 3 images ahead

| Image | Thumbnail |
|:--|:--|
| 640x480 | 320x240 (about 17 KB) |
| 1280x720 | 320x180 |
| 1920x1080 | 480x270 (1/4, chosen by the decoder) |

- About 2-4 ms each (640x480, with HTTP), about 2 ms from memory

## ❤️ Logs

- Health results are not logged (`Microsoft.Extensions.Diagnostics.HealthChecks` = `Fatal`), so calls add no lines. Failures have their own lines
- A failed start is logged as `Fatal` by `Program.cs`, exit code 1. Until the settings are read, a temporary logger writes to `..\Log\CameraService_.log`, so JSON errors reach the file

## 📊 Measurements

### Camera and MJPEG

ELECOM 2MP Webcam, YUY2 640x480 30fps, `Stream:Fps` 10, Release.

| Item | Value |
|:--|:--|
| Open | 0.81-0.83 seconds (device search about 0.7 seconds), 0.09 seconds right after closing |
| Still, camera closed | 1.8-1.9 seconds |
| Still, camera open | 71 ms |
| MJPEG | 10 fps, about 64 KB per frame |
| Close | About 10.7 seconds after the last client |
| Live view stopped until disconnect | 3-7 ms |
| CPU (one core) | Idle 0.57 %, one MJPEG client 3.6 % |

- Brightness after opening: near the settled value, up to about 8 % variation at 0.3-0.6 seconds
- As a service, idle with saving every minute: 0.33 % CPU
- Stop with a page open: 1.5 seconds (31.5 seconds before refusing requests during the stop)

### MJPEG and H.264

640x480, one client, Release, as a service.

| Method | fps | Bitrate | Per frame | Server CPU (one core) |
|:--|--:|--:|--:|--:|
| MJPEG | 10 | 4.6 Mbps | About 58 KB | 2.4-2.6 % |
| H.264 (NVIDIA H.264 Encoder MFT) | 29 | 3.9-4.0 Mbps | About 17 KB | 2.1-2.3 % |

- Browser (localhost): `avc1.4D401E`, 149 frames in 5 seconds, all drawn, key frames about every 2 seconds
- Encoder released about 10 seconds after the last client
- Plain HTTP on a LAN IP has no `VideoDecoder`: MJPEG. `NotSupportedError` raised: MJPEG after the first key frame
- Stop with an H.264 page: 1.6-2.6 seconds
- 2 MJPEG, 2 H.264 and a still every 5 seconds: save in 49 ms, H.264 at 28 fps, one encoder
- Camera held by another process: one warning, `/health` 503, 200 after the next save
- Same without saving, client connected: delays 5 → 10 → 30 seconds, `/health` 503 in all 444 calls (every 0.1 seconds), 200 on the first frame
- MJPEG on a LAN IP for about 50 minutes: about 30,000 frames, 1.05 GB, no reconnect, browser heap 6 MB
- Relay program breaking connections: closed, back in about 2 seconds. Refused, `Reconnecting` stays. Traffic stopped, `Reconnecting` after 5 seconds and reconnect after 15. No page reload (MJPEG and H.264)

### Saving

ELECOM 2MP Webcam, YUY2 640x480 30fps, default settings, quality 90.

| Item | Value |
|:--|:--|
| Open | About 2.8 seconds before the time (0.87-0.88 seconds first, then 0.12) |
| Save | File 43-68 ms after the time |
| Close | About 10.6 seconds after the save (open about 13 seconds per minute) |
| Per image | About 97-102 KB (about 140 MB a day, 4.3 GB for 30 days at one per minute) |
| Deletion at start | 11 ms for one date folder |

## ✅ Checks

### Windows service

Published folder as a LocalSystem service (session 0), twice end to end (the second after code review fixes). ELECOM 2MP Webcam, YUY2 640x480, saving every minute.

| Item | Result |
|:--|:--|
| Saving | 43-93 ms after the time (108-678 ms in the dark). Names match burned-in times to the second in 12 images |
| Long run | About 1 hour with a page open: no warnings or errors, no reconnect, 165 MB |
| Pages | All pages, static files, thumbnails, full size |
| H.264 | NVENC works in session 0 |
| Wrong settings | No start, reason in the log (wrong value, JSON error) |
| Deletion | At the start and at a fixed local time (10:30:00) |
| Forced exit | Restarted after about 60 seconds, saving resumes, open pages reload and go live |
| Stop | 1.55 seconds with clients, 0.27 seconds without. Next start answers in 0.7 seconds |
| Removal | No service, process or port left |

Not checked: automatic start at boot (registered as manual).

### Pages

Built-in browser, real camera, saving every 5 seconds (images hidden by CSS).

- All pages, thumbnails (320x240), date change, viewer keys, time-lapse, preload, download name, status, missing camera, no console errors
- Leaving a live view returns the server's clients to 0
- Live ↔ still 40 times and open ↔ close of the viewer 20 times right away: no errors
- Going back through the history to another camera shows that camera

### Other

- Saved images: every minute 5 times, every 10 seconds 7 times, names and burned-in times match. At the start only old date folders were deleted
- HTTPS: `/health` and a still image over HTTP/2 with a development certificate

## 💡 Findings

- Long exposure in the dark: 16-17 fps in a room at 6:00, about 10 fps at dusk. H.264 follows, MJPEG thins further (8.3 fps at 16.4 fps). Saves wait for the next frame, so they come later
- Choosing the frame by arrival burned in a second too early (`:59`), so it is chosen by capture time
- `TimeProvider.GetLocalNow()` adds the offset of `LocalTimeZone` to `GetUtcNow()` and is not virtual. `LocalTimeProvider` returned local time and was 9 hours ahead, so its `LocalTimeZone` is UTC
- The NV12 of cameras such as the ELECOM is MJPG decoded by Windows

## 🐧 Linux

Not built. The projects are split for it.

- `CameraService.Platform.Linux`: LinuxDotNet.Video4Linux2 (YUYV / MJPEG). MJPEG can stream as is, the timestamp needs decoding. JPEG and H.264 encoders are needed
- Host targets `net10.0` and `net10.0-windows`, with the Platform reference and registration per OS
