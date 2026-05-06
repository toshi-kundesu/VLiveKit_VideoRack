# FFmpeg Placement

This repository intentionally does not include ffmpeg binaries.

Place your builds at these paths:

```text
Assets/toshi.VLiveKit/VideoRack/Tools/FFmpeg/Windows/ffmpeg.exe
Assets/toshi.VLiveKit/VideoRack/Tools/FFmpeg/macOS/ffmpeg
Assets/toshi.VLiveKit/VideoRack/Tools/FFmpeg/Linux/ffmpeg
```

The HAP Converter uses the platform-specific executable path automatically.

When distributing ffmpeg with a project or package, check the license terms of the exact build you ship.
