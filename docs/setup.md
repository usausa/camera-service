# 🚀 導入

## 💻 動作環境

- Windows 11、ASP.NET Core 10 のランタイム(ビルドには .NET 10 SDK)
- USB カメラ(MediaFrameReader で使えるもの)。形式は YUY2 か NV12
- ⚠️ カメラの使用を許可しておく(設定 > プライバシーとセキュリティ > カメラ)

## ▶️ 試しに動かす

```
dotnet run --project src/CameraService.Host
```

- ブラウザーで `http://localhost:5180` を開くと、カメラの一覧が出る(Live でライブ、Snapshot で静止画)
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
- ログは発行先の 1 つ上の `Log\CameraService_yyyyMMdd.log`(発行先が `C:\Services\CameraService` なら `C:\Services\Log`)
- 保存画像は既定で発行先の `Images\{カメラの ID}`。別のドライブにするときは `Storage:Path` か、カメラの `Snapshot:Path` に絶対パスを書く([⚙️ 設定](settings.md))
- ✅ サービス(LocalSystem、セッション 0)でもカメラを使えることを確かめた
