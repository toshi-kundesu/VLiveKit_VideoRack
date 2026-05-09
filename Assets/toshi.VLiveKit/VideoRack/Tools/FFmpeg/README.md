# FFmpeg

This package can include FFmpeg as a third-party command line tool for the
HAP Converter editor window.

FFmpeg is executed as a separate process. It is not linked into the VLiveKit
VideoRack assemblies.

## Platform Paths

```text
Tools/FFmpeg/Windows/ffmpeg.exe
Tools/FFmpeg/macOS/ffmpeg
Tools/FFmpeg/Linux/ffmpeg
```

The HAP Converter uses the platform-specific executable path automatically.
If a package-local FFmpeg build is unavailable, the converter falls back to the
legacy project-side path under `Assets/toshi.VLiveKit/VideoRack/Tools/FFmpeg`.

## Windows Build

The Windows binary currently bundled here is a gyan.dev FFmpeg GPLv3 full build.
Keep these files together when distributing the package:

```text
Tools/FFmpeg/Windows/ffmpeg.exe
Tools/FFmpeg/Windows/GPL-3.0.txt
Tools/FFmpeg/Windows/README.txt
ThirdPartyNotices/FFmpeg.md
```

In npm releases, the Windows binary may be stored as split payload files named
`ffmpeg.exe.part*.bytes`. VideoRack reconstructs them into a cache executable
before running FFmpeg. Keep every part file together with the GPL text, upstream
README, and third-party notice.

`README.txt` is the upstream gyan.dev build readme and includes the exact
FFmpeg version, build configuration, source commit, enabled components, and
external library versions.

Before distributing a release, make sure the corresponding source remains
available for the exact binary you ship. See `ThirdPartyNotices/FFmpeg.md`.
