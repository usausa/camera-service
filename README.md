# 📷 camera-service

🌐 English | [日本語](README.ja.md)

A simple surveillance camera service for Windows. It streams cameras to the browser and saves still images on a schedule. While nobody watches, the cameras and the encoders are closed.

## ✨ Features

| Feature | Description |
|:--|:--|
| 🖥️ Web UI | Camera list, camera (live / still), saved images, status |
| 🎥 Live view | H.264 (WebSocket + WebCodecs, hardware encoder), MJPEG where the browser cannot use it |
| 🖼️ Still image | One JPEG of the current moment |
| 🗂️ Image browser | Date → hour → thumbnails → full size. ←/→ to move, time-lapse playback, download |
| 💾 Scheduled saving | Still images on a cron schedule (local time) for each camera, as `yyyyMMdd\HHmmss.jpg` |
| 🕒 Timestamp | The capture time is burned into the saved images |
| 🧹 Automatic deletion | Old dates, and the oldest dates while the free space is low |
| 💤 Closed when unused | A camera closes shortly after its last user leaves, and its encoders and buffers are released. For a save it opens just before the time |
| 🔌 Reconnection | A camera opens again after an error or when its frames stop, and the live view connects again after a lost connection |
| 📷 Multiple cameras | Listed in the settings, each with an ID |
| ❤️ Health check | `/health` (cameras and storage) |

## 💻 Requirements

- Windows 11 with the ASP.NET Core 10 runtime (the .NET 10 SDK to build)
- A USB camera that works with MediaFrameReader, in YUY2 or NV12
- ⚠️ Allow access to the camera (Settings > Privacy & security > Camera)

## ▶️ Try it

```
dotnet run --project src/CameraService.Host
```

- Open `http://localhost:5180` to see the camera list
- `/api/devices` lists the cameras of the PC and their formats (for `Device` / `DeviceId` in the settings)
- `/health` returns `Healthy`
- 💡 The sample `appsettings.json` saves an image of `camera1` every minute. Set `Snapshot:Enable` to `false` if you only want the live view

## 📦 Install as a Windows service

1. Publish
   ```
   dotnet publish src/CameraService.Host -c Release -o C:\Services\CameraService
   ```
2. Edit `Cameras` in `appsettings.json` of the published folder (see Settings below)
3. Register and start it from an administrator PowerShell
   ```
   pwsh tools\Install-Service.ps1 -Path C:\Services\CameraService
   ```
4. To remove it
   ```
   pwsh tools\Uninstall-Service.ps1
   ```

- It starts automatically and runs as LocalSystem. `-StartupType Manual` registers it without the automatic start
- After a failure it is restarted one minute later
- To view it from other PCs, add `-FirewallPort 5180` to open the port in Windows Firewall
- ⚠️ Use the published folder: the build output has no `wwwroot`, so the pages would have no CSS and scripts
- **Logs:** `Log\CameraService_yyyyMMdd.log` one level above the published folder (`C:\Services\Log` for `C:\Services\CameraService`). Set `Serilog:MinimumLevel:Default` to `Debug` for a line per saved image
- **Images:** `Images\{camera ID}` in the published folder by default (see Saved images below)
- ⚠️ When the service does not start, read the log file: wrong settings and JSON errors are written there (a service has no console)

## ⚙️ Settings

Set in `appsettings.json`, or on the command line (for example `--Cameras:0:Device="ELECOM 2MP Webcam"`). The settings are validated at the start, and the service does not start when they are wrong.

### 🌐 Port

| Key | Description | Default |
|:--|:--|:--|
| `http_ports` | HTTP port (on all addresses) | `5180` |

### 📷 Camera behavior

| Key | Description | Default |
|:--|:--|:--|
| `Camera:IdleTime` | Seconds until a camera closes after its last user left | `10` |
| `Camera:WarmupTime` | Milliseconds after opening whose frames are not used (until the brightness settles) | `1000` |
| `Camera:FrameTimeout` | Seconds without frames until a camera is opened again | `5` |

### 📡 Streams

| Key | Description | Default |
|:--|:--|:--|
| `Stream:Fps` | Maximum fps of MJPEG and still images (camera frames are skipped) | `10` |
| `Stream:Quality` | JPEG quality (1-100) | `80` |
| `Stream:H264:Enable` | H.264 live view (WebSocket) | `true` |
| `Stream:H264:Bitrate` | Constant bitrate in kbps. 0 chooses by resolution (up to 640x480: 4000, up to 1280x720: 8000, above: 16000) | `0` |
| `Stream:H264:KeyFrameInterval` | Seconds between key frames | `2` |
| `Stream:H264:PreferHardware` | Tries a hardware encoder first. `false` uses only software | `true` |

- A 640x480 JPEG is about 64 KB, so about 5 Mbps at 10 fps
- H.264 sends every camera frame, about 4 Mbps at 640x480
- ⚠️ Browsers decode H.264 only on localhost or HTTPS. Over plain HTTP from another PC the live view uses MJPEG

### 💾 Storage and deletion

| Key | Description | Default |
|:--|:--|:--|
| `Storage:Path` | Base folder of the saved images (a relative path starts at the service folder) | `Images` |
| `Retention:Schedule` | When old images are deleted (cron, local time), also at the start | `0 3 * * *` |
| `Retention:Days` | Days to keep, today included. 0 keeps images regardless of their date | `30` |
| `Retention:MinFreeSpace` | Free space to keep in GB. Below it the oldest dates are deleted. 0 does not check | `10` |

### 🎥 Cameras (`Cameras`)

| Key | Description | Default |
|:--|:--|:--|
| `Id` | Used in URLs and folder names: letters, digits, `-` and `_`, unique regardless of case | Required |
| `Name` | Display name, `Id` when empty | Empty |
| `Device` | Device name. When `Device` and `DeviceId` are empty, the first device is used | Empty |
| `DeviceId` | Tells apart devices with the same name (`id` in `/api/devices`) | Empty |
| `Format` | `Yuy2` / `Nv12` | `Yuy2` |
| `Width` / `Height` / `Fps` | Resolution and frame rate (a format of the camera) | `640` / `480` / `30` |
| `Snapshot:Enable` | Scheduled saving (`true` in the sample `appsettings.json`) | `false` |
| `Snapshot:Schedule` | When to save (cron, local time) | `* * * * *` |
| `Snapshot:Path` | Folder of the camera (a relative path starts at `Storage:Path`), the `Id` when empty | Empty |
| `Snapshot:Quality` | JPEG quality of the saved images (1-100) | `90` |
| `Snapshot:Timestamp:Enable` | Burns in the time | `true` |
| `Snapshot:Timestamp:Format` | Format of the time (see Timestamp below) | `yyyy/MM/dd HH:mm:ss` |
| `Snapshot:Timestamp:Position` | `TopLeft` / `TopRight` / `BottomLeft` / `BottomRight` | `BottomRight` |
| `Snapshot:Timestamp:Scale` | Size of the characters (see Timestamp below) | `0` |

- ⚠️ The first device can be a virtual camera (for example the virtual camera of a phone). Set `Device` to the name
- 💡 For several cameras on one USB controller use NV12: uncompressed YUY2 needs much bandwidth (the NV12 of many cameras is MJPG from the camera decoded by Windows)

### 🔒 HTTPS

Kestrel settings are enough. With `Kestrel:Endpoints` the value of `http_ports` is not used, so list HTTP too.

```json
"Kestrel": {
  "Endpoints": {
    "Http": { "Url": "http://*:5180" },
    "Https": {
      "Url": "https://*:5181",
      "Certificate": { "Subject": "camera.local", "Store": "My", "Location": "LocalMachine" }
    }
  }
}
```

- Keep the certificate in the certificate store of the computer (no password in the settings file). The viewing devices must trust it
- With HTTPS, browsers on other PCs can use H.264 (WebCodecs needs a secure connection)
- HTTPS uses HTTP/2, so one page can open several live views (HTTP/1.1 allows 6 connections to a host)

## 🕒 Schedules (cron)

`Snapshot:Schedule` and `Retention:Schedule` take 5 fields (minute, hour, day, month, day of week), or 6 fields with the seconds first.

- Allowed forms: `*`, `5`, `1,15`, `9-17`, `*/10`, `0-30/5` (numbers only, no names such as `MON`). The day of week is 0-7 (0 and 7 are Sunday)
- Local time. ⚠️ The first run after a daylight saving change can be off

| Example | Meaning |
|:--|:--|
| `* * * * *` | Every minute |
| `*/10 * * * * *` | Every 10 seconds |
| `* 22-23,0-5 * * *` | Every minute at night (22:00 to 05:59) |
| `*/5 9-17 * * 1-5` | Every 5 minutes from 9:00 to 17:59 on weekdays |
| `0 3 * * *` | Every day at 3:00 (the default of the deletion) |

- A run that was missed is skipped: after a delay such as sleep, one image is taken, not all of the missed ones
- While the previous save still waits for the camera, that run is not taken

## 💾 Saved images

Saved as `{camera folder}\yyyyMMdd\HHmmss.jpg`. The name is the scheduled time, the burned-in time is the time of the frame. An image of the same name is replaced.

| Setting | A relative path starts at |
|:--|:--|
| `Storage:Path` (default `Images`) | The service folder (where the exe is) |
| `Snapshot:Path` of the camera (the `Id` when empty) | `Storage:Path` |

| `Storage:Path` | `Snapshot:Path` | Folder of the camera `front` |
|:--|:--|:--|
| `Images` | Empty | `{service}\Images\front` |
| `D:\CameraImages` | Empty | `D:\CameraImages\front` |
| `Images` | `outdoor\front` | `{service}\Images\outdoor\front` |
| `Images` | `E:\Front` | `E:\Front` |

- The folder is created at the start and checked for writing, the result is in the log with the free space
- Two cameras cannot share a folder: the start stops, also when the paths are written differently (`front`, `.\front`, an absolute path)
- A 640x480 image at quality 90 is up to about 100 KB: saving every minute takes about 140 MB a day, about 4.3 GB for 30 days
- The camera opens about 3 seconds before the time (`WarmupTime` + 2 seconds) and closes after `IdleTime`. Saving every minute keeps it open for about 13 seconds of each minute
- A failed save is logged once as a warning, the next successful save logs the number of failures

### 🔤 Timestamp

- **Characters:** digits, space, `+` `-` `.` `/` `:`. The service does not start when the format gives other characters (names of days, AM/PM)
- **Size:** `Scale` is the size in steps of 7 dots of height. 0 chooses by the image height (480 lines: 2, 720: 3, 1080: 4)
- **Position:** one of the corners, with a margin of `Scale` × 4 dots. What does not fit in the image is cut

### 🧹 Deletion

- Runs at `Retention:Schedule` and at the start (what became old while the service was stopped is deleted too)
- Only date folders (a valid date of 8 digits) are deleted, other files and folders stay
- Today is never deleted
- `Days`: dates older than this number of days, today included (30 keeps 30 days)
- `MinFreeSpace`: below it, the dates of all the cameras on that drive are deleted, oldest first
- The folders of cameras that do not save now are included (images saved earlier are deleted)
- ⚠️ The free space of a network path (UNC) is not checked

## 🖥️ Pages

Open `http://{PC}:5180/`.

| Page | Path | Content |
|:--|:--|:--|
| 📷 Cameras | `/` | The latest saved image, state, last save and errors of each camera. Updated every 10 seconds |
| 🎥 Camera | `/cameras/{id}` | Live view (H.264 or MJPEG) or still image |
| 🗂️ Images | `/cameras/{id}/images/{yyyyMMdd}` | Date → hour → thumbnails → full size |
| 📊 Status | `/status` | State, capture fps, fps, bitrate and clients of MJPEG and H.264, H.264 encoder, last image, errors, free space. Updated every second |

The camera list does not open live views (HTTP/1.1 allows 6 connections to a server).

### 🎥 Camera

- **Live:** H.264 in a browser with WebCodecs on localhost or HTTPS, otherwise MJPEG. The top right of the picture shows which. It also changes to MJPEG when H.264 cannot be decoded or the server has no encoder
- **Lost connection:** the last picture stays, the label at the top right turns into a red `Reconnecting`, and the view connects again by itself
- **Errors:** failures of the camera and the saving, and the reason when H.264 has no encoder, are shown above the picture
- **Still:** refresh button and automatic refresh (5 / 10 / 30 / 60 seconds). A closed camera opens first, so the first image takes about 2 seconds
- Leaving the page or changing to the still image closes the connection

### 🗂️ Images

- **Date:** newest first
- **Hour:** number of images of each hour from 0 to 23, hours without images cannot be selected. It starts at the hour of the latest image
- **Thumbnails:** those of the selected hour, loaded as they come into view
- **Refresh button:** reads the images saved since the page was opened
- **Full size:** click a thumbnail. You can go through all the images of the day
- **Time-lapse button:** plays from the start of the selected hour

| Key / button | Action |
|:--|:--|
| ← / → | Previous / next |
| Home / End | First / last |
| Space | Play / stop the time-lapse (5 / 10 / 20 / 30 fps, stops at the end) |
| Esc | Close |
| ⬇ | Download (`{id}_{yyyyMMdd}_{HHmmss}.jpg`) |

## 🔗 Endpoints

| Path | Content |
|:--|:--|
| `/cameras/{id}/stream.mjpg` | MJPEG (can be shown with `img`) |
| `/cameras/{id}/h264` | H.264 over WebSocket, see the [technical notes](docs/technical.md) |
| `/cameras/{id}/snapshot.jpg` | A still image. A closed camera opens for it |
| `/api/images/{id}` | Dates with saved images (`yyyyMMdd`, newest first) |
| `/api/images/{id}/{yyyyMMdd}` | Times of that date (`HHmmss`, oldest first) |
| `/images/{id}/{yyyyMMdd}/{HHmmss}.jpg` | A saved image |
| `/images/{id}/{yyyyMMdd}/{HHmmss}/thumbnail.jpg` | Its thumbnail |
| `/api/devices` | Cameras of the PC and their formats |
| `/health` | Health check |

An unknown `id`, a date that is not 8 digits of a valid date, a time that is not 6 digits of a valid time and a missing image give 404.

### ❤️ Health check

| Result | HTTP | When |
|:--|:--|:--|
| `Healthy` | 200 | None of the below |
| `Degraded` | 200 | The free space of a storage drive is below `Retention:MinFreeSpace`, or H.264 has no encoder (the live view works with MJPEG) |
| `Unhealthy` | 503 | A camera failed (it does not open or close, or its frames stopped) until it sends frames again, a save failed, or a storage folder cannot be written |

A camera is opened only when it is used, so a camera nobody uses keeps its last state.

## ⚠️ Good to know

- **A camera has one user:** the service opens a camera exclusively. While another application uses it, the camera cannot be opened and saves fail (in the log and `/health`) until a later save works. While the service has it open, other applications cannot open it
- **Dark rooms:** a camera lengthens its exposure in the dark, so a 30 fps format can drop to about 10 fps and the file of a save comes a little later

## 📚 Technical notes

How it works inside, protocols, measurements and checks: [docs/technical.md](docs/technical.md)

## 📄 License

[MIT](LICENSE)
