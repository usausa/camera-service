# 📷 camera-service

カメラの映像をブラウザーへ配信する、簡易的な監視カメラの Windows サービス。見ている人がいないときはカメラを閉じる。カメラの制御には MediaFrameReader(Windows Runtime)を使う。

## ✨ できること

| 機能 | 内容 |
|:--|:--|
| 🎥 ライブ | MJPEG で配信(`img` で表示できる) |
| 🖼️ 静止画 | その時点の 1 枚(JPEG) |
| 💤 使っていないときは閉じる | 最後の人が離れてから一定時間でカメラを閉じ、エンコーダーとバッファも手放す |
| 🔌 再接続 | エラーやフレームが止まったときに開き直す |
| 📷 複数カメラ | 設定に並べたカメラを ID で使い分ける |
| ❤️ ヘルスチェック | `/health` |

## 🧩 構成

| プロジェクト | 内容 |
|:--|:--|
| `src/CameraService.Core` | 映像の型とインターフェース、YUV の変換、フレームの共有(OS に依存しない) |
| `src/CameraService.Platform.Windows` | Windows のカメラ(MediaFrameReader)と JPEG(WIC) |
| `src/CameraService.Host` | サービス本体(ASP.NET Core、Blazor Server) |
| `tests/CameraService.UnitTests` | Core と Platform.Windows のテスト |
| `tests/CameraService.IntegrationTests` | テスト用のカメラでサーバーを動かすテスト |
| `tools` | Windows サービスの登録と削除 |

## 📚 ドキュメント

- [🚀 導入](docs/setup.md)
- [⚙️ 設定](docs/settings.md)
- [📡 配信](docs/streaming.md)
