# VLiveKit VideoRack

ライブイベント用の video asset を Unity Editor 上で準備するための tool package です。

## Package

- Package name: `com.toshi.vlivekit.videorack`
- Version: `0.1.17`
- Unity: 2022.3
- Repository: https://github.com/toshi-kundesu/VLiveKit_VideoRack
- Package root: `Assets/toshi.VLiveKit/VideoRack`

## 主な内容

- HAP Converter editor window
- ffmpeg を使った HAP .mov 変換
- Audio Converter editor window
- ffmpeg を使った MP3 / WAV / OGG 音声変換
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

## Audio Converter

Open `toshi/VLiveKit/VideoRack/Audio Converter` to convert audio files between
MP3, WAV, and OGG from the Unity Editor. The window uses the package/project
FFmpeg executable, supports overwrite/reveal options, optional trim, sample-rate
and channel conversion, and advanced ffmpeg arguments when needed.

## NDI Test Sender

Open `toshi/VLiveKit/VideoRack/NDI Test Sender` to send a moving test-pattern
from the editor window without entering Play Mode. The window can also create a
scene rig with a generated RenderTexture and `Klak.Ndi.NdiSender` configured for
Texture capture. KlakNDI is treated as an optional project dependency; this
package does not reference it directly.

## NDI Test Receiver

Open `toshi/VLiveKit/VideoRack/NDI Test Receiver` to receive and preview an NDI
source directly in the editor window without entering Play Mode. The window can
refresh available KlakNDI source names, or create a scene rig with a target
RenderTexture, `Klak.Ndi.NdiReceiver`, and optional preview quad.

## License

この package 独自のコードと asset は repository の `LICENSE` に従います。third-party asset を含む場合は、それぞれの license / README を確認してください。
