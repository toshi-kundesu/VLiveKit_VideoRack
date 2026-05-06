# VLiveKit VideoRack

Unity Editor tools for preparing live-event video assets.

## HAP Converter

Open from Unity:

`Tools > VLiveKit > VideoRack > HAP Converter`

This tool runs the ffmpeg executable inside this Unity project and converts source movies to HAP `.mov` files without blocking the Unity Editor.

Place your ffmpeg build here:

```text
Assets/toshi.VLiveKit/VideoRack/Tools/FFmpeg/Windows/ffmpeg.exe
```

Optional platform paths are prepared for later:

```text
Assets/toshi.VLiveKit/VideoRack/Tools/FFmpeg/macOS/ffmpeg
Assets/toshi.VLiveKit/VideoRack/Tools/FFmpeg/Linux/ffmpeg
```

ffmpeg binaries are not included in this repository. If you distribute a build with ffmpeg included, confirm the relevant LGPL/GPL license requirements for that build.
