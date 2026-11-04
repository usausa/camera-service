# 🚀 導入

## 💻 動作環境

- Windows 11、ASP.NET Core 10 のランタイム(ビルドには .NET 10 SDK)
- USB カメラ(MediaFrameReader で使えるもの)。形式は YUY2 か NV12
- ⚠️ カメラの使用を許可しておく(設定 > プライバシーとセキュリティ > カメラ)

## ▶️ 試しに動かす

```
dotnet run --project src/CameraService.Host
```

- ブラウザーで `http://localhost:5180` を開くと、カメラの一覧が出る([🖥️ 画面](ui.md))
- `/api/devices` で PC のカメラと形式が分かる(設定の `Device` / `DeviceId` に使う)
- `/health` が `Healthy` を返す

## 📦 Windows サービスとして登録

1. 発行する
   ```
   dotnet publish src/CameraService.Host -c Release -o C:\Services\CameraService
   ```
2. 発行先の `appsettings.json` の `Cameras` を直す([⚙️ 設定](settings.md))
3. 管理者の PowerShell で登録する
   ```
   pwsh tools\Install-Service.ps1 -Path C:\Services\CameraService
   ```
4. 削除するとき
   ```
   pwsh tools\Uninstall-Service.ps1
   ```

- 自動起動、LocalSystem で動く。失敗したら 1 分後に再起動する
- 他の PC から見るときは `-FirewallPort 5180` を付けて、ファイアウォールを開ける
- ⚠️ 発行したフォルダーを使う(ビルドの出力には `wwwroot` がなく、画面の CSS と JS が出ない)
- ログは発行先の 1 つ上の `Log\CameraService_yyyyMMdd.log`(発行先が `C:\Services\CameraService` なら `C:\Services\Log`)。起動しないときの理由(設定の誤りなど)もここに出る
- 保存画像は既定で発行先の `Images\{カメラの ID}`。別のドライブにするときは `Storage:Path` か、カメラの `Snapshot:Path` に絶対パスを書く([⚙️ 設定](settings.md))

## ✅ サービスでの確認

発行したフォルダーを LocalSystem のサービス(セッション 0)に登録して、2 回通しで確かめた(2 回目はレビューの指摘を直したあと)。ELECOM 2MP Webcam、YUY2 640x480、毎分の保存。

| 項目 | 結果 |
|:--|:--|
| 保存 | 毎分の保存が予定の時刻から 43〜93 ms(暗くてフレームの間隔が伸びると 108〜678 ms)。12 枚で名前と焼き込みの時刻が秒まで一致 |
| 連続運転 | 約 1 時間、ページを開いたまま(LAN の IP の MJPEG で約 3 万枚)。警告とエラーなし、つなぎ直しなし、メモリー 165 MB |
| 画面 | 全ページ、CSS・JS・フォント、サムネイル、拡大 |
| H.264 | セッション 0 でも NVENC を使える(NVIDIA H.264 Encoder MFT)。カメラのフレームを全部送る(暗いと約 10 fps) |
| 設定の誤り | サービスは起動しない。値の誤りも JSON の書き間違いも、理由がログファイルに出る |
| 自動削除 | 起動時と、時刻を指定したジョブ(現地時刻の 10:30:00 に実行) |
| 強制終了 | 約 60 秒後に再起動して、次の時刻から保存を続ける。開いていたページは自分で読み直し、ライブも戻る |
| 止める | H.264、MJPEG、画面をつないだまま 1.55 秒。誰もいなければ 0.27 秒。次の起動は 0.7 秒で応答 |
| 削除 | サービス、プロセス、ポートが残らない |

- ほかのアプリがカメラを使っているときの動きは [📡 配信](streaming.md)
- 手動起動で確かめた。PC の起動時の自動起動は未確認
