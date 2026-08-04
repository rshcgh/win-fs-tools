# win fs tools

native windows 10 or later (missing) file operations ported from posix

## operations

- delete duplicates by file size & sha-256 content hash, with an optional bit-perfect backup & duplicate-only parent-folder deletion
- copy, move, or delete multiple extensions (in one operation)
- compress a complete input tree into one zip archive
- un-compress one or more zip archives into separate output folders
- sort files by extension, modified month, or size band, with copy or move behavior
- create a `sha256` checksum manifest

(all operations share input path, output path, and include subfolders settings)

## how to build

install the .net 8 windows desktop runtime (for self-contained distribution, publish with a locally available windows runtime pack), then double-click `scripts\build.cmd`, or run it from a developer command prompt ... the executable will be in `bin\Release\net8.0-windows\win-fs-tools.exe`
