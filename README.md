# 📷 camera-service

カメラの映像をブラウザーへ配信する、簡易的な監視カメラの Windows サービス。見ている人がいないときはカメラを閉じる。カメラの制御には MediaFrameReader(Windows Runtime)を使う。

## ✨ できること

| 機能 | 内容 |
|:--|:--|
| 🖥️ 画面 | カメラ一覧、カメラ(ライブ / 静止画)、保存画像、状態 |
| 🎥 ライブ | MJPEG で配信(`img` で表示できる) |
| 🖼️ 静止画 | その時点の 1 枚(JPEG) |
| 🗂️ 保存画像の閲覧 | 日付 → 時間 → サムネイル → 拡大。←/→ で前後、タイムラプス再生、ダウンロード |
| 💾 定期保存 | カメラごとに Cron の時刻(現地時刻)で静止画を保存(`yyyyMMdd\HHmmss.jpg`) |
| 🕒 日時の焼き込み | 保存画像に撮った日時を入れる(自前のフォント) |
| 🧹 自動削除 | 古い日付と、空き容量が足りないときは古い順に消す |
| 💤 使っていないときは閉じる | 最後の人が離れてから一定時間でカメラを閉じ、エンコーダーとバッファも手放す。保存のときは少し前に開く |
| 🔌 再接続 | エラーやフレームが止まったときに開き直す |
| 📷 複数カメラ | 設定に並べたカメラを ID で使い分ける |
| ❤️ ヘルスチェック | `/health`(カメラと保存先) |

## 🧩 構成

| プロジェクト | 内容 |
|:--|:--|
| `src/CameraService.Core` | 映像の型とインターフェース、YUV の変換、フレームの共有、日時の焼き込み、保存の名前と削除の対象(OS に依存しない) |
| `src/CameraService.Platform.Windows` | Windows のカメラ(MediaFrameReader)と JPEG・サムネイル(WIC) |
| `src/CameraService.Host` | サービス本体(ASP.NET Core、Blazor Server) |
| `tests/CameraService.UnitTests` | Core と Platform.Windows のテスト |
| `tests/CameraService.IntegrationTests` | テスト用のカメラでサーバーを動かすテスト |
| `tools` | Windows サービスの登録と削除 |

## 📚 ドキュメント

- [🚀 導入](docs/setup.md)
- [⚙️ 設定](docs/settings.md)
- [🖥️ 画面](docs/ui.md)
- [📡 配信](docs/streaming.md)
- [💾 保存](docs/snapshot.md)
- [🔗 エンドポイント](docs/endpoints.md)
