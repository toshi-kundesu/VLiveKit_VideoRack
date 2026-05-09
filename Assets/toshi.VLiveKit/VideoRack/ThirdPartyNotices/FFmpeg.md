# FFmpeg Third-Party Notice

VLiveKit VideoRack bundles FFmpeg for the HAP Converter editor tool on Windows.
FFmpeg is executed as a separate command line program and is not linked into the
VLiveKit VideoRack assemblies.

## Component

- Component: FFmpeg
- Bundled binary: `Tools/FFmpeg/Windows/ffmpeg.exe`
- Published payload: `Tools/FFmpeg/Windows/ffmpeg.exe.part*.bytes` may be used
  when npm package size handling requires split files; VideoRack reconstructs
  them into a cache executable before use.
- Build provider: gyan.dev
- Build name: `2025-06-17-git-ee1f79b0fa-full_build-www.gyan.dev`
- License: GNU General Public License version 3
- License text: `Tools/FFmpeg/Windows/GPL-3.0.txt`
- Upstream build readme: `Tools/FFmpeg/Windows/README.txt`
- FFmpeg source commit: https://github.com/FFmpeg/FFmpeg/commit/ee1f79b0fa
- gyan.dev builds page: https://www.gyan.dev/ffmpeg/builds/

The upstream `README.txt` included next to the binary contains the exact build
configuration, enabled libraries, enabled codecs, and external library versions
for this binary.

## Corresponding Source

When distributing this package with the bundled FFmpeg binary, make sure users
can obtain the Corresponding Source for the exact FFmpeg binary being shipped.
At minimum, release materials should provide clear source instructions next to
the binary download and keep them available for as long as the binary is
distributed.

The source reference recorded by the upstream build is:

```text
https://github.com/FFmpeg/FFmpeg/commit/ee1f79b0fa
```

Because this is a static `full_build`, also preserve the external library list
and versions from `Tools/FFmpeg/Windows/README.txt`. For public releases, prefer
hosting a source archive or a source manifest alongside the package release so
the source remains available independently of third-party web pages.

## License Separation

The bundled FFmpeg binary is licensed separately from VLiveKit VideoRack. Do not
apply VLiveKit license terms, EULA terms, or redistribution restrictions in a
way that limits the rights granted to FFmpeg recipients under GPLv3.

## Patent Notice

FFmpeg supports many multimedia formats and codecs. Patent rules vary by
jurisdiction and by use case. This notice does not grant patent rights.
