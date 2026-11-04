# 📷 camera-service

🌐 English | [日本語](README.ja.md)

A Windows service that streams cameras to the browser and saves still images on a schedule. Cameras and encoders stay closed while unused.

## ✨ Features

| Feature | Description |
|:--|:--|
| 🎥 Live view | H.264 (hardware encoder, WebCodecs) or MJPEG |
| 🖼️ Still image | JPEG of the current frame |
| 💾 Scheduled saving | Cron schedule per camera, capture time burned in |
| 🗂️ Image browser | Date → hour → thumbnail → full size, time-lapse, download |
| 🧹 Deletion | By age and by free space |
| 💤 On demand | Cameras open when used and close when idle |
| 🔌 Recovery | Failed cameras reopen, lost live views reconnect |
| 📷 Multiple cameras | Listed in the settings |
| ❤️ Health check | `/health` |

## 💻 Requirements

- Windows 11, ASP.NET Core 10 runtime (.NET 10 SDK to build)
- USB camera supported by MediaFrameReader (YUY2 or NV12)
- ⚠️ Camera access allowed (Settings > Privacy & security > Camera)

## ▶️ Try it

```
dotnet run --project src/CameraService.Host
```

- `http://localhost:5180`: camera list
- `/api/devices`: cameras and formats (for `Device` / `DeviceId`)
- 💡 The sample `appsettings.json` saves `camera1` every minute (`Snapshot:Enable` turns it off)

## 📦 Install as a service

1. Publish
   ```
   dotnet publish src/CameraService.Host -c Release -o C:\Services\CameraService
   ```
2. Edit `Cameras` in the published `appsettings.json`
3. Register and start (administrator PowerShell)
   ```
   pwsh tools\Install-Service.ps1 -Path C:\Services\CameraService
   ```
4. Remove
   ```
   pwsh tools\Uninstall-Service.ps1
   ```

- Runs as LocalSystem, starts automatically (`-StartupType Manual` to disable), restarts 1 minute after a failure
- `-FirewallPort 5180` opens the port for other PCs
- ⚠️ Use the published folder: the build output has no `wwwroot`
- **Log:** `Log\CameraService_yyyyMMdd.log` one level above the published folder. `Serilog:MinimumLevel:Default` = `Debug` adds a line per save
- ⚠️ If the service does not start, read the log: wrong settings and JSON errors are written there

## ⚙️ Settings

In `appsettings.json` or on the command line (`--Cameras:0:Device="ELECOM 2MP Webcam"`). Invalid settings stop the start.

### 🌐 Port

| Key | Description | Default |
|:--|:--|:--|
| `http_ports` | HTTP port | `5180` |

### 📷 Camera behavior

| Key | Description | Default |
|:--|:--|:--|
| `Camera:IdleTime` | Seconds until closing after the last user | `10` |
| `Camera:WarmupTime` | Milliseconds of frames ignored after opening | `1000` |
| `Camera:FrameTimeout` | Seconds without frames until reopening | `5` |

### 📡 Streams

| Key | Description | Default |
|:--|:--|:--|
| `Stream:Fps` | Maximum fps of MJPEG and still images | `10` |
| `Stream:Quality` | JPEG quality (1-100) | `80` |
| `Stream:H264:Enable` | H.264 live view | `true` |
| `Stream:H264:Bitrate` | kbps. 0: by resolution (up to 640x480: 4000, up to 1280x720: 8000, above: 16000) | `0` |
| `Stream:H264:KeyFrameInterval` | Seconds between key frames | `2` |
| `Stream:H264:PreferHardware` | Try hardware encoders first | `true` |

- At 640x480: MJPEG about 5 Mbps (10 fps), H.264 about 4 Mbps (all frames)
- ⚠️ H.264 needs localhost or HTTPS. Plain HTTP from other PCs uses MJPEG

### 💾 Storage and deletion

| Key | Description | Default |
|:--|:--|:--|
| `Storage:Path` | Base folder of the images (relative to the service folder) | `Images` |
| `Retention:Schedule` | Deletion time (cron), also runs at the start | `0 3 * * *` |
| `Retention:Days` | Days to keep, including today. 0: no limit | `30` |
| `Retention:MinFreeSpace` | Free space to keep (GB). 0: no check | `10` |

### 🎥 Cameras (`Cameras`)

| Key | Description | Default |
|:--|:--|:--|
| `Id` | For URLs and folders: letters, digits, `-`, `_`. Unique, ignoring case | Required |
| `Name` | Display name | `Id` |
| `Device` | Device name. Without `Device` and `DeviceId`: first device | Empty |
| `DeviceId` | `id` in `/api/devices`, for devices with the same name | Empty |
| `Format` | `Yuy2` / `Nv12` | `Yuy2` |
| `Width` / `Height` / `Fps` | A format of the camera | `640` / `480` / `30` |
| `Snapshot:Enable` | Scheduled saving | `false` |
| `Snapshot:Schedule` | Cron | `* * * * *` |
| `Snapshot:Path` | Camera folder (relative to `Storage:Path`) | `Id` |
| `Snapshot:Quality` | JPEG quality (1-100) | `90` |
| `Snapshot:Timestamp:Enable` | Burn in the time | `true` |
| `Snapshot:Timestamp:Format` | Time format giving only digits, space, `+` `-` `.` `/` `:` | `yyyy/MM/dd HH:mm:ss` |
| `Snapshot:Timestamp:Position` | `TopLeft` / `TopRight` / `BottomLeft` / `BottomRight` | `BottomRight` |
| `Snapshot:Timestamp:Scale` | Character size (1 = 7 dots). 0: by image height (480: 2, 720: 3, 1080: 4) | `0` |

- ⚠️ The first device may be virtual (for example a phone). Set `Device`
- 💡 Use NV12 for several cameras on one USB controller: YUY2 needs much bandwidth

### 🔒 HTTPS

Set `Kestrel:Endpoints`. It replaces `http_ports`, so list HTTP too.

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

- Certificate in the computer's store, trusted by the viewing devices
- Gives other PCs H.264, and HTTP/2 (HTTP/1.1 allows 6 connections per host)

## 🕒 Schedules (cron)

5 fields (minute, hour, day, month, weekday), or 6 with seconds first. Local time.

- Forms: `*`, `5`, `1,15`, `9-17`, `*/10`, `0-30/5`. Numbers only. Weekday 0-7 (0 and 7: Sunday)

| Example | Meaning |
|:--|:--|
| `* * * * *` | Every minute |
| `*/10 * * * * *` | Every 10 seconds |
| `* 22-23,0-5 * * *` | Every minute at night (22:00-05:59) |
| `*/5 9-17 * * 1-5` | Every 5 minutes, 9:00-17:59 on weekdays |
| `0 3 * * *` | Daily at 3:00 |

- Missed runs are not caught up
- A run is skipped while the previous save waits for the camera
- ⚠️ The first run after a daylight saving change can be off

## 💾 Saved images

`{camera folder}\yyyyMMdd\HHmmss.jpg`. The name is the scheduled time, the burned-in time is the capture time.

| `Storage:Path` | `Snapshot:Path` | Folder of camera `front` |
|:--|:--|:--|
| `Images` | Empty | `{service}\Images\front` |
| `D:\CameraImages` | Empty | `D:\CameraImages\front` |
| `Images` | `outdoor\front` | `{service}\Images\outdoor\front` |
| `Images` | `E:\Front` | `E:\Front` |

- Two cameras cannot share a folder (checked on the resolved paths)
- 640x480, quality 90: up to about 100 KB per image, about 140 MB a day when saving every minute
- The camera opens about 3 seconds before the time and closes after `IdleTime`
- A failed save logs one warning until the next success

### 🧹 Deletion

- Runs at `Retention:Schedule` and at the start
- Deletes only date folders (valid 8-digit names), never today
- `MinFreeSpace`: oldest first, across the cameras on the drive
- Includes folders of cameras that no longer save
- ⚠️ Free space of network paths (UNC) is not checked

## 🖥️ Pages

`http://{PC}:5180/`

| Page | Path | Content |
|:--|:--|:--|
| 📷 Cameras | `/` | Latest image, state, last save, errors. Updated every 10 seconds |
| 🎥 Camera | `/cameras/{id}` | Live or still image |
| 🗂️ Images | `/cameras/{id}/images/{yyyyMMdd}` | Date → hour → thumbnail → full size |
| 📊 Status | `/status` | State, fps, bitrate, clients, encoder, last image, errors, free space. Updated every second |

### 🎥 Camera

- **Live:** the top right shows H.264 or MJPEG
- **Red `Reconnecting`:** connection lost, the last frame stays, reconnects automatically
- **Still:** refresh button or every 5 / 10 / 30 / 60 seconds. About 2 seconds when the camera is closed

### 🗂️ Images

- **Refresh button:** loads images saved after opening the page
- **Time-lapse button:** plays from the selected hour

| Key / button | Action |
|:--|:--|
| ← / → | Previous / next |
| Home / End | First / last |
| Space | Play / stop the time-lapse (5 / 10 / 20 / 30 fps) |
| Esc | Close |
| ⬇ | Download (`{id}_{yyyyMMdd}_{HHmmss}.jpg`) |

## 🔗 Endpoints

| Path | Content |
|:--|:--|
| `/cameras/{id}/stream.mjpg` | MJPEG |
| `/cameras/{id}/h264` | H.264 over WebSocket ([protocol](docs/technical.md)) |
| `/cameras/{id}/snapshot.jpg` | Still image |
| `/api/images/{id}` | Dates (`yyyyMMdd`, newest first) |
| `/api/images/{id}/{yyyyMMdd}` | Times (`HHmmss`, oldest first) |
| `/images/{id}/{yyyyMMdd}/{HHmmss}.jpg` | Saved image |
| `/images/{id}/{yyyyMMdd}/{HHmmss}/thumbnail.jpg` | Thumbnail |
| `/api/devices` | Cameras and formats |
| `/health` | Health check |

An unknown `id`, an invalid date or time, and a missing image give 404.

### ❤️ Health check

| Result | HTTP | Condition |
|:--|:--|:--|
| `Healthy` | 200 | None of the below |
| `Degraded` | 200 | Free space below `Retention:MinFreeSpace`, or no H.264 encoder |
| `Unhealthy` | 503 | Camera failed (until its next frame), save failed, or folder not writable |

An unused camera keeps its last state.

## ⚠️ Good to know

- **One user per camera:** the service and other applications cannot use a camera at the same time. Saves fail while another application holds it
- **Dark rooms:** longer exposure can lower the camera to about 10 fps and delay saved files slightly

## 📚 Technical notes

[docs/technical.md](docs/technical.md)
