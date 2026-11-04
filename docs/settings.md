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
| `Stream:H264:Enable` | H.264 のライブ(WebSocket) | `true` |
| `Stream:H264:Bitrate` | kbps(固定のビットレート)。0 なら解像度から決める(640x480 まで 4000、1280x720 まで 8000、それより上 16000) | `0` |
| `Stream:H264:KeyFrameInterval` | キーフレームの間隔(秒) | `2` |
| `Stream:H264:PreferHardware` | ハードウェアのエンコーダーを先に試す。`false` ならソフトウェアだけ | `true` |

- 640x480 の JPEG は 1 枚約 64 KB。10 fps で約 5 Mbps
- H.264 はカメラのフレームを間引かない。640x480 で約 4 Mbps
- ⚠️ ブラウザーが H.264 をデコードできるのは localhost か HTTPS だけ。ほかの PC から http で見ると MJPEG になる

## 💾 保存先と自動削除

| キー | 内容 | 既定値 |
|:--|:--|:--|
| `Storage:Path` | 保存画像の基準のフォルダー(相対パスならサービスのフォルダーから) | `Images` |
| `Retention:Schedule` | 古い画像を消す時刻(Cron、現地時刻)。起動時にも動く | `0 3 * * *` |
| `Retention:Days` | 残す日数(今日を含む)。0 なら日付では消さない | `30` |
| `Retention:MinFreeSpace` | 残す空き容量(GB)。下回ったら古い日付から消す。0 なら調べない | `10` |

## 🎥 カメラの一覧(`Cameras`)

| キー | 内容 | 既定値 |
|:--|:--|:--|
| `Id` | URL とフォルダー名に使う。英数字と `-` `_`、重複なし(大文字小文字は区別しない) | 必須 |
| `Name` | 表示名。空なら `Id` | 空 |
| `Device` | デバイス名。`Device` と `DeviceId` が空なら先頭のデバイス | 空 |
| `DeviceId` | 同じ名前のデバイスが複数あるときの区別(`/api/devices` の `id`) | 空 |
| `Format` | `Yuy2` / `Nv12` | `Yuy2` |
| `Width` / `Height` / `Fps` | 解像度とフレームレート(カメラが持つ形式) | `640` / `480` / `30` |
| `Snapshot:Enable` | 定期保存(`appsettings.json` の例では `true`) | `false` |
| `Snapshot:Schedule` | 保存の時刻(Cron、現地時刻。5 項目か、秒を先頭に足した 6 項目) | `* * * * *` |
| `Snapshot:Path` | 保存先(相対パスなら `Storage:Path` から)。空ならカメラの `Id` | 空 |
| `Snapshot:Quality` | 保存する JPEG の品質(1〜100) | `90` |
| `Snapshot:Timestamp:Enable` | 日時の焼き込み | `true` |
| `Snapshot:Timestamp:Format` | 日時の書式(数字、空白、`+` `-` `.` `/` `:` だけになるもの) | `yyyy/MM/dd HH:mm:ss` |
| `Snapshot:Timestamp:Position` | `TopLeft` / `TopRight` / `BottomLeft` / `BottomRight` | `BottomRight` |
| `Snapshot:Timestamp:Scale` | 文字の大きさ(1 段で 7 ドットの高さ)。0 なら画像の高さから決める | `0` |

- ⚠️ 先頭のデバイスが仮想カメラのことがある(この PC では Pixel 9a の仮想カメラ)。`Device` で名前を指定する
- 💡 複数台を同じ USB につなぐときは NV12 を使う。非圧縮の YUY2 は帯域が大きい(ELECOM などの NV12 は、カメラが MJPG で送って Windows がデコードしたもの)
- Cron の書き方と例、保存先の決まり方、焼き込み、自動削除は [💾 保存](snapshot.md)
- 2 台のカメラで同じ `Snapshot:Path` は使えない(画像が置き換わってしまうので、起動時の検証で止まる)

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
- HTTPS なら、ほかの PC のブラウザーでも H.264 で見られる(WebCodecs は安全な接続でだけ使える)
- ✅ 開発用の証明書で、HTTPS と HTTP/2 で `/health` と静止画が取れることを確かめた
