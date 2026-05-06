# VLiveKit VideoRack

ライブイベント用の video asset を Unity Editor 上で準備するための tool package です。

## Package

- Package name: `com.toshi.vlivekit.videorack`
- Version: `0.0.1`
- Unity: 2022.3
- Repository: https://github.com/toshi-kundesu/VLiveKit_VideoRack
- Package root: `Assets/toshi.VLiveKit/VideoRack`

## 主な内容

- HAP Converter editor window
- ffmpeg を使った HAP .mov 変換
- Windows / macOS / Linux 向け ffmpeg 配置パスの用意

## 依存・同梱 asset

- ffmpeg binary は repository に含めません。必要に応じて project 側に配置してください。

## インストール

Unity の `Packages/manifest.json` の `dependencies` に追加します。

```json
{
  "dependencies": {
    "com.toshi.vlivekit.videorack": "https://github.com/toshi-kundesu/VLiveKit_VideoRack.git?path=/Assets/toshi.VLiveKit/VideoRack#main"
  }
}
```

VLiveKit sandbox では submodule として `Packages/VLiveKit_VideoRack` に配置し、`file:` 参照で読み込んでいます。

## 注意

- ffmpeg を同梱して配布する場合は、その build の LGPL/GPL 条件を確認してください。

## License

この package 独自のコードと asset は repository の `LICENSE` に従います。third-party asset を含む場合は、それぞれの license / README を確認してください。
