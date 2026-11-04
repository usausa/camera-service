# 🔧 Technical notes

🌐 English | [日本語](technical.ja.md)

How the service works inside, its protocols, measurements and checks. To install, configure and use it, see the [README](../README.md).

## 🧩 Projects

| Project | Content |
|:--|:--|
| `src/CameraService.Core` | Video types and interfaces, YUV conversion, frame sharing, timestamp, names of the saved images and what to delete (no OS dependency) |
| `src/CameraService.Platform.Windows` | Windows cameras (MediaFrameReader), JPEG and thumbnails (WIC), H.264 (Media Foundation) |
| `src/CameraService.Host` | The service (ASP.NET Core, Blazor Server), with its own CSS and Bootstrap Icons |
| `tests/CameraService.UnitTests` | Tests of Core and Platform.Windows |
| `tests/CameraService.IntegrationTests` | Tests that run the server with test cameras, and start the real executable for the settings |
| `tools` | Registering and removing the Windows service |

- Windows APIs (WinRT, WIC, Media Foundation) are only in Platform.Windows. Host uses the interfaces of Core, and `AddWindowsPlatform()` registers the implementations at the start
- The test server does not go through `Program.cs` and does not map the pages. `ProgramTests` starts the real executable for what happens at the start
- The tests are executables (Microsoft Testing Platform), for example `tests\CameraService.UnitTests\bin\Debug\net10.0-windows10.0.26100.0\CameraService.UnitTests.exe`

## 🔄 Opening and closing a camera

- The users (MJPEG and H.264 clients, still image requests, scheduled saves) lease the camera. The first one opens it, and it closes `IdleTime` after the last one returned it
- The frames of the first `WarmupTime` after opening are not used
- On a failure (does not open, does not close, an error, no frames for `FrameTimeout`) it is closed and opened again. The delays are 5 → 10 → 30 → 60 seconds. It is not opened again while nobody leases it
- It stays failed until the next frame: when it opens and sends no frames, the delays go on growing and `/health` does not recover
- Searching the devices, opening and closing are cut off after 15 seconds and count as failures, so a driver that does not return does not stop the steps of the camera or the stop of the service. An exception while closing still releases the reader and the capture
- The device and the format are searched when it first opens, and again after a failure
- Opening, closing and retrying advance one at a time on a timer of 0.5 seconds (at once for the first user)
- The camera is opened exclusively. While another application uses it, the capture opens and then fails (`0xC00D3704`). A scheduled save fails for that run, is reported in the log and `/health`, and recovers with a save after the camera is free

## 🖼️ JPEG and MJPEG

- On the camera thread, thinned to `Stream:Fps` and encoded only while a client waits. One encode is shared by all the clients (each gets only the latest frame)
- YUY2 and video range NV12 become full range NV12, then WIC encodes it
- The JPEG is written to a work buffer and copied to a frame of the real size, so a frame does not keep a buffer of the largest size (about 0.9 MB at 640x480)
- Closing the camera also releases the encoder and the buffers
- MJPEG sends the next boundary right after each frame (a browser shows a part when the next boundary arrives)
- `?raw` on `stream.mjpg` gives the same body with `Content-Type: application/octet-stream`. The live view uses it with `fetch`: a browser may refuse a multipart response for anything other than an image
- The live view draws on a canvas from `fetch` (with `img`, a lost connection cannot be noticed). When decoding is late only the latest image is drawn. Leaving the page aborts the `fetch`, which closes the connection
- On a stop MJPEG ends, and requests after the stop began get 503. The pages try to connect again, and without this a new connection stays and the stop waits for 30 seconds

## 🎞️ H.264

Ported from the H.264 streaming of tool-camera (Media Foundation encoder, WebSocket, WebCodecs).

- **Encoder:** tries hardware (NVENC and others) first, then the software encoder of Windows. Low latency mode, CBR, no B frames, Main profile. Bitrate by resolution (up to 640x480: 4 Mbps, up to 1280x720: 8 Mbps, above: 16 Mbps)
- **Creation and release:** for each camera, created in the background when the first client comes (NVENC takes 0.3-0.6 seconds), and released after 10 seconds without clients (checked by the timer of 0.5 seconds, so also while the camera is closed)
- **Failure:** when it cannot be created or stops working, the connected clients are closed with `InternalServerError` and the page changes to MJPEG. The reason is on the camera page and the status page, and `/health` is `Degraded`. Created again after 10 seconds or for a new client
- **Frames:** every camera frame is encoded (no thinning). YUY2 is reordered to NV12 without changing the range. One encode is shared by all the clients
- **Key frames:** every 2 seconds (`Stream:H264:KeyFrameInterval`). After connecting and after skipping frames because of a slow send, nothing is sent until the next key frame, which is requested from the encoder (once per 500 ms at most). The browser also requests one by a text message on a decode error or a full queue
- **Colors:** the range of the camera is passed as it is and sent in the header. The browser gives the decoder the range and the BT.601 matrix, so every encoder gives the same colors as the JPEG
- **Page:** H.264 when `VideoDecoder` exists on localhost or HTTPS, otherwise MJPEG. A configuration that cannot be decoded (`NotSupportedError`) also changes to MJPEG
- **Stop:** the connection is closed when the stop begins (`EndpointUnavailable`)

### WebSocket protocol

- From the server: a binary message for each frame (header + Annex B access unit). The first message is a key frame
- From the client: any text message requests a key frame
- Close status from the server: `InternalServerError` (1011, no encoder, the page changes to MJPEG), `EndpointUnavailable` (1001, stop, the page connects again)
- `Stream:H264:Enable` `false` gives 404, a request that is not a WebSocket gives 400

The header (little endian, 32 bytes, the header of tool-camera without its detection part):

| Offset | Type | Content |
|--:|:--|:--|
| 0 | uint16 | Version (1) |
| 2 | uint16 | Header length (position of the data) |
| 4 | uint16 | Width |
| 6 | uint16 | Height |
| 8 | uint32 | Frame number of the camera |
| 12 | uint32 | Flags (bit 3: key frame, bit 4: full range with the BT.601 matrix, otherwise video range) |
| 16 | float64 | Capture time (Unix ms) |
| 24 | float64 | Encoded time (Unix ms) |

## 🔌 Lost connections

The same for MJPEG and H.264. The page comes back without a reload.

- **Closed connection:** connects again after 1 second. The last picture stays and the label becomes a red `Reconnecting`
- **No close (for example the network went down):** `Reconnecting` after 5 seconds without data, and after 15 seconds the connection is closed and opened again
- A camera that failed and sends no frames behaves the same way (the reason is shown above the picture)

## 🕒 Schedules

- The scheduler (Mofucat.JobScheduler) evaluates cron in the offset of the current time, so it gets a TimeProvider that returns local time (with UTC, `0 3 * * *` would be 12:00 in Japan)
- That TimeProvider is not registered for the whole application (other components expect UTC)
- Misfires are skipped (`MisfirePolicy.Skip`)

## 💾 Saving

1. The camera is leased `WarmupTime` + 2 seconds before the scheduled time (3 seconds by default), and opens if it is closed
2. The first frame captured after the scheduled time is copied and made full range NV12 (on the camera thread). It is chosen by the capture time: a frame that arrives right after the scheduled time may have been captured before it
3. The time is burned in, then it becomes a JPEG of `Snapshot:Quality`
4. It is written to a temporary file (`.tmp`), which replaces `{folder}\yyyyMMdd\HHmmss.jpg`
5. The camera is returned, and closes after `IdleTime` if nobody else uses it

- While the camera is failed (waiting to open again) that run fails without waiting. No frame for 10 seconds also fails it
- A line per save is `Debug` (too many at `Information` every minute)

### Timestamp rendering

- An own bitmap font of digits and symbols (7 dots high), so no OS fonts are needed
- White characters with a black outline. The chroma under the characters and the outline becomes neutral, so the colors of the background do not bleed into them

### Deletion

- Stops with a warning when the free space becomes unknown while deleting
- The folders to delete come from all the cameras, and the free space check is per drive

## 🖼️ Thumbnails and cache

- **Cache:** `Cache-Control: private, max-age=86400` and an `ETag` from the time and size of the file. A request with the same `ETag` gets 304
- **Thumbnails:** the JPEG decoder of WIC scales while decoding to YCbCr planes, which become a JPEG directly (no color conversion)
  - About 320 wide, at a scale the decoder offers (1/2, 1/4, 1/8). The scaled chroma comes at the size of the luma, so 2x2 is averaged to 4:2:0
  - Quality 80. Up to 64 MB in the memory of the server (the least used are removed)
- **Image pages:** the full size view loads the next 3 images ahead

| Image | Thumbnail |
|:--|:--|
| 640x480 | 320x240 (about 17 KB) |
| 1280x720 | 320x180 |
| 1920x1080 | 480x270 (1/4, chosen by the decoder) |

- Time: about 2-4 ms each (640x480, HTTP included), about 2 ms from the memory

## ❤️ Health check and logs

- The results are not logged (`Microsoft.Extensions.Diagnostics.HealthChecks` is `Fatal`), so that a call does not add a line. Failures of the camera and the saving and low free space have their own log lines
- A failure of the start is logged as `Fatal` by a catch in `Program.cs`, which ends with exit code 1. Until the settings are read, a temporary logger writes to `..\Log\CameraService_.log`, so JSON errors in the settings reach the file too

## 📊 Measurements

### Camera and MJPEG

ELECOM 2MP Webcam, YUY2 640x480 30fps, `Stream:Fps` 10, Release.

| Item | Value |
|:--|:--|
| Opening the camera | 0.81-0.83 seconds (with about 0.7 seconds of device search, 0.09 seconds right after closing) |
| Still image, camera closed | 1.8-1.9 seconds (opening + `WarmupTime` of 1 second) |
| Still image, camera open | 71 ms |
| MJPEG | 10 fps, about 64 KB per frame |
| Until closing | About 10.7 seconds after the last client (`IdleTime` of 10 seconds + the timer) |
| From stopping the live view until the connection ends | 3-7 ms |
| CPU (of one core) | 0.57 % with nobody watching, 3.6 % with one MJPEG client |

- Brightness after opening (average of the frame): near the settled value from the start, varying up to about 8 % at 0.3-0.6 seconds. `WarmupTime` stays at 1 second by default
- As a service, with nobody watching and saving every minute: 0.33 % CPU
- Until stopped: 1.5 seconds with a page open (31.5 seconds before requests were refused after the stop began)

### MJPEG and H.264

The same 640x480, one client each, Release, as a service.

| Method | fps | Bitrate | Per frame | Server CPU (of one core) |
|:--|--:|--:|--:|--:|
| MJPEG | 10 | 4.6 Mbps | About 58 KB | 2.4-2.6 % |
| H.264 (NVIDIA H.264 Encoder MFT) | 29 | 3.9-4.0 Mbps | About 17 KB | 2.1-2.3 % |

- H.264 sends 3 times the frames at a lower bitrate and CPU than MJPEG
- In the built-in browser (localhost): codec `avc1.4D401E`, 640x480, 149 frames in 5 seconds, all drawn. Key frames about every 2 seconds
- Leaving the page brings the H.264 clients to 0 and stops the encoding, and the encoder is released about 10 seconds later
- On plain HTTP to a LAN IP (not secure) there is no `VideoDecoder`, so MJPEG is used. With `NotSupportedError` raised, it changed to MJPEG after the first key frame and the H.264 connection closed
- Stopping with an H.264 page open took 1.6-2.6 seconds
- With 2 MJPEG clients, 2 H.264 clients and a still image every 5 seconds, a scheduled save took 49 ms. H.264 ran at 28 fps with one encoder
- While another process used the camera, one warning of a failed save appeared and `/health` was 503. It returned to 200 with a save after the camera was free
- In a process without saving and with a client while another process used the camera: the delays grew 5 → 10 → 30 seconds and `/health` stayed 503 (444 calls, one every 0.1 seconds), and it returned to 200 with the first frame after the camera was free
- MJPEG on a LAN IP (not secure) open for about 50 minutes: about 30,000 frames, 1.05 GB, no reconnection, browser heap 6 MB
- With a relay program that breaks connections: a closed connection came back in about 2 seconds. While connections were refused the label stayed `Reconnecting`. With the traffic stopped, `Reconnecting` after 5 seconds and a new connection after 15 seconds. No page reload in any case (MJPEG and H.264, built-in browser)

### Saving

ELECOM 2MP Webcam, YUY2 640x480 30fps, default settings (`WarmupTime` of 1 second, `IdleTime` of 10 seconds, quality 90).

| Item | Value |
|:--|:--|
| Opening the camera | About 2.8 seconds before the time (first 0.87-0.88 seconds with the device search, later 0.12 seconds) |
| Saving | The file exists 43-68 ms after the time |
| Closing | About 10.6 seconds after the save (open for about 13 seconds of each minute when saving every minute) |
| Per image | About 97-102 KB (about 140 MB a day and 4.3 GB for 30 days when saving every minute) |
| Deleting at the start | 11 ms for one old date folder |

- ✅ Saving every minute 5 times and every 10 seconds 7 times: the name is the scheduled time, the burned-in time is the capture time, equal to the second
- ✅ At the start only old date folders were deleted. A date 29 days ago, today and folders not named by a date stayed

## ✅ Checks

### As a Windows service

The published folder registered as a LocalSystem service (session 0), checked twice from end to end (the second time after the fixes of a code review). ELECOM 2MP Webcam, YUY2 640x480, saving every minute.

| Item | Result |
|:--|:--|
| Saving | 43-93 ms after the time (108-678 ms when the frame interval grows in the dark). In 12 images the name and the burned-in time are equal to the second |
| Long run | About 1 hour with a page open (about 30,000 frames of MJPEG on a LAN IP). No warnings or errors, no reconnection, 165 MB of memory |
| Pages | All pages, CSS, JS, fonts, thumbnails, full size view |
| H.264 | NVENC works in session 0 (NVIDIA H.264 Encoder MFT). Every camera frame is sent (about 10 fps in the dark) |
| Wrong settings | The service does not start. The reason of a wrong value or a JSON error is in the log file |
| Deletion | At the start, and by a job at a fixed local time (ran at 10:30:00) |
| Forced exit | Restarted about 60 seconds later, saving goes on from the next time. An open page reloads itself and the live view returns |
| Stop | 1.55 seconds with H.264, MJPEG and a page connected, 0.27 seconds with nobody. The next start answers in 0.7 seconds |
| Removal | No service, process or port left |

Registered with a manual start. The automatic start when the PC starts has not been checked.

### Pages

In the built-in browser with the real camera saving every 5 seconds (the images hidden by CSS, only the layout and values were read).

- **Camera list:** latest thumbnail (320x240), state, time, links
- **Camera:** live 640x480. Leaving the page brings the clients of the server back to 0. The still image reloads by the switch, the button and the automatic refresh of 5 seconds
- **Images:** changing the date changes the URL. Number of images per hour, thumbnails
- **Full size:** ←/→/Home/End, Space plays (stops at the end), Esc closes, images loaded ahead, download name
- **Others:** a missing camera and a date without images, the status page. No errors in the console
- **Quick actions:** changing to the still image right after the live view (40 times) and closing the full size view right after opening it (20 times) give no errors. Going back through the history to another camera shows that camera

### HTTPS

✅ With a development certificate, `/health` and a still image over HTTPS and HTTP/2.

## 💡 Findings

- Dark rooms: the camera lengthens its exposure, so a 30 fps format gave 16-17 fps in a room at 6:00, and about 10 fps at dusk. H.264 follows, MJPEG thins further (8.3 fps at 16.4 fps of the camera)
- A save waits for a frame captured after the scheduled time, so a longer frame interval delays the file (108-678 ms in a room at dusk)
- Choosing the frame by arrival order burned in a second before the schedule (`:59`, because of the delay until a frame arrives), so it is chosen by the capture time
- `TimeProvider.GetLocalNow()` adds the offset of `LocalTimeZone` to the clock value of `GetUtcNow()` and is not virtual. `LocalTimeProvider`, whose `GetUtcNow()` returns local time, added it twice and went 9 hours ahead, so its `LocalTimeZone` is UTC
- The NV12 of cameras such as the ELECOM is MJPG from the camera decoded by Windows

## 🐧 Linux

Not built yet, the projects are split so that it can be added.

- `CameraService.Platform.Linux` with LinuxDotNet.Video4Linux2 (YUYV / MJPEG). An MJPEG camera can be streamed as it is, but the timestamp needs decoding. The JPEG and H.264 encoders have to be provided separately
- Host gets more targets (`net10.0` and `net10.0-windows`), with the Platform reference and registration per OS
