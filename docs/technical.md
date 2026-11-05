# 🔧 Technical notes

🌐 English | [日本語](technical.ja.md)

The specifications of the cameras first, then those of the application. For installation and settings, see the [README](../README.md).

## 📷 Cameras

### 🔎 Devices and formats

- A device counts as a camera when all its sources are color sources. It can have more than one (preview and record), the first is used. Infrared cameras and sensor groups do not count
- The device of a camera: `DeviceId`, then `Device`, otherwise the first device with the format. A configured device is never replaced
- A format matches by pixel format, size and frame rate (within 0.5 fps). Pixel formats: YUY2 and NV12
- Device and format are resolved when the camera opens first and after a failure
- Access is exclusive: while another application holds the camera, the open succeeds and the capture then fails

### 🎨 Colors

- The range comes from the format of the camera, video range when it has none. The matrix is BT.601
- JPEG: YUY2 and video range NV12 become full range NV12, full range NV12 is encoded as it is
- H.264: the range of the camera is passed through and sent in the header. The browser sets the range and the matrix on the decoder, so the colors match JPEG with any encoder

### 🔄 Open and close

- Users (clients, still requests, saves) lease the camera. The first user opens it at once, and it closes `IdleTime` after the last returns it
- Frames of the first `WarmupTime` are not used (the exposure settles)
- A save leases the camera `WarmupTime` + 2 seconds before its time
- A still image or a save waits up to `WarmupTime` + 10 seconds for a frame, then fails (still image: 503)
- The steps (open, close, retry, frame watchdog) run one at a time on a 0.5-second timer
- Device search, open and close are cut off after 15 seconds. A close always releases the camera

### ⚠️ Failures

- Failure: the open or the close fails, the camera reports an error, no frame for `FrameTimeout`, or an unexpected exception in a step
- The camera closes and opens again after 5 → 10 → 30 → 60 seconds, only while it is leased
- It counts as failed until its next frame: if it opens but sends nothing, the delays keep growing and `/health` stays 503
- A save fails at once while the camera waits for the retry

### 🌙 Frame rate

- A camera can deliver fewer frames than its format says, for example in the dark (longer exposure)
- A frame is skipped while the previous one is still being processed
- MJPEG and still images: thinned to `Stream:Fps`
- H.264: all frames. The key frame interval and the bitrate count the frames of the format, so with fewer frames key frames come less often and the bitrate is lower
- A save takes the first frame captured at the scheduled time or later

## 🧩 Application

### 🧱 Projects

| Project | Content |
|:--|:--|
| `src/CameraService.Core` | Video types and interfaces, YUV conversion, frame sharing, timestamp, image names, deletion targets (no OS dependency) |
| `src/CameraService.Platform.Windows` | Cameras (MediaFrameReader), JPEG and thumbnails (WIC), H.264 (Media Foundation) |
| `src/CameraService.Host` | Service (ASP.NET Core, Blazor Server) |
| `tests/CameraService.UnitTests` | Core and Platform.Windows |
| `tests/CameraService.IntegrationTests` | Server with test cameras |
| `tools` | Service registration and removal |

- Windows APIs are only in Platform.Windows. `AddWindowsPlatform()` registers them for the Core interfaces

### 🖼️ JPEG and MJPEG

- Encoded only while clients wait. One encode is shared by all clients (latest frame only)
- An encoder error is logged, a new encoder is tried after 10 seconds
- Closing the camera releases the encoder and the buffers
- The next boundary follows each frame at once
- `?raw`: same body, `Content-Type: application/octet-stream`, for `fetch`
- Live view: `fetch` + canvas, only the latest image is decoded when decoding lags. Leaving the page stops the request
- On stop: MJPEG ends and new requests get 503

### 🎞️ H.264

- **Encoder:** hardware first, then Windows software. Low latency, CBR, no B frames, Main profile
- **Lifetime:** per camera. Created in the background for the first client, released after 10 seconds without clients
- **Failure:** clients closed with `InternalServerError`, the view falls back to MJPEG, `/health` is `Degraded`. Retried after 10 seconds or for a new client
- **Frames:** YUY2 is reordered to NV12, range unchanged. One encode is shared
- **Key frames:** every `Stream:H264:KeyFrameInterval`. After connecting or skipping frames, sending waits for a key frame requested from the encoder (at most every 500 ms). The browser requests one by a text message on a decode error or a full queue
- **Viewer:** `VideoDecoder` on localhost or HTTPS, otherwise MJPEG. `NotSupportedError` also falls back to MJPEG
- **Stop:** connections close when the stop begins (`EndpointUnavailable`)

### 📡 WebSocket protocol

- Server: one binary message per frame (header + Annex B access unit), starting with a key frame
- Client: any text message requests a key frame
- Close status: `InternalServerError` (1011, no encoder, the view uses MJPEG), `EndpointUnavailable` (1001, stop, the view reconnects)
- `Stream:H264:Enable` = `false`: 404. Not a WebSocket request: 400

Header (little endian, 32 bytes):

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

### 🔌 Lost connections

MJPEG and H.264 behave the same, without a page reload.

- **Closed:** reconnects after 1 second, keeps the last frame, shows `Reconnecting`
- **No close (network down):** `Reconnecting` after 5 seconds without data, reconnects after 15 seconds
- A camera sending nothing behaves the same

### 💾 Saving

Schedules are cron expressions in the local time. A missed run is skipped.

1. Lease the camera
2. Copy the first frame captured at the scheduled time or later as full range NV12
3. Burn in the capture time, encode with `Snapshot:Quality`
4. Write a `.tmp` file, replace `{folder}\yyyyMMdd\HHmmss.jpg`
5. Return the lease

- Timestamp: own 7-dot bitmap font, white with a black outline, neutral chroma under both, margin of `Scale` × 4 dots, cut at the image edge
- Deletion stops with a warning if the free space becomes unknown

### 🖼️ Thumbnails and cache

- **Cache:** `Cache-Control: private, max-age=86400`, `ETag` from file time and size, 304 on a match
- **Thumbnails:** decoded scaled as YCbCr planes, encoded without color conversion. About 320 wide, at a scale of the decoder (1/2, 1/4, 1/8). Quality 80, up to 64 MB in memory (least used removed)
- **Full size view:** loads the next 3 images ahead
- **Deleted image:** 404

| Image | Thumbnail |
|:--|:--|
| 640x480 | 320x240 |
| 1280x720 | 320x180 |
| 1920x1080 | 480x270 |
