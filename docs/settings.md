# ⚙️ 設定

`appsettings.json` で指定する。コマンドライン引数でも上書きできる(例: `--Cameras:0:Device="ELECOM 2MP Webcam"`)。設定は起動時に検証し、正しくなければ起動しない(ログに理由が出る)。

## 🌐 待ち受け

| キー | 内容 | 既定値 |
|:--|:--|:--|
| `http_ports` | HTTP のポート(すべてのアドレスで待ち受ける) | `5180` |

## 📷 カメラの動き

| キー | 内容 | 既定値 |
|:--|:--|:--|
| `Camera:IdleTime` | 使う人がいなくなってから閉じるまで(秒) | `10` |
| `Camera:WarmupTime` | 開いた直後に使わない時間(ミリ秒。明るさが落ち着くまで) | `1000` |
| `Camera:FrameTimeout` | フレームが来ないときに開き直すまで(秒) | `5` |

## 📡 配信

| キー | 内容 | 既定値 |
|:--|:--|:--|
| `Stream:Fps` | MJPEG と静止画の最大 fps(カメラのフレームを間引く) | `10` |
| `Stream:Quality` | JPEG の品質(1〜100) | `80` |

- 640x480 の JPEG は 1 枚約 64 KB。10 fps で約 5 Mbps

## 🎥 カメラの一覧(`Cameras`)

| キー | 内容 | 既定値 |
|:--|:--|:--|
| `Id` | URL とフォルダー名に使う。英数字と `-` `_`、重複なし(大文字小文字は区別しない) | 必須 |
| `Name` | 表示名。空なら `Id` | 空 |
| `Device` | デバイス名。`Device` と `DeviceId` が空なら先頭のデバイス | 空 |
| `DeviceId` | 同じ名前のデバイスが複数あるときの区別(`/api/devices` の `id`) | 空 |
| `Format` | `Yuy2` / `Nv12` | `Yuy2` |
| `Width` / `Height` / `Fps` | 解像度とフレームレート(カメラが持つ形式) | `640` / `480` / `30` |

- ⚠️ 先頭のデバイスが仮想カメラのことがある(この PC では Pixel 9a の仮想カメラ)。`Device` で名前を指定する
- 💡 複数台を同じ USB につなぐときは NV12 を使う。非圧縮の YUY2 は帯域が大きい(ELECOM などの NV12 は、カメラが MJPG で送って Windows がデコードしたもの)

## 🔒 HTTPS

Kestrel の設定だけで有効にできる。`Kestrel:Endpoints` を書くと `http_ports` は使われないので、HTTP も並べる。

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

- 証明書はコンピューターの証明書ストアに置く(パスワードを設定ファイルに書かずに済む)。見る端末で信頼されている必要がある
- HTTPS では HTTP/2 になり、1 画面で複数のライブを開ける(HTTP/1.1 は 1 つのホストに同時 6 接続まで)
- ✅ 開発用の証明書で、HTTPS と HTTP/2 で `/health` と静止画が取れることを確かめた
