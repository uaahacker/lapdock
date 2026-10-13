"""Packs a clean copy of the project (no build output, no local junk) into release/LapDock-<version>-source.zip.

    python tools/make_source_zip.py 1.0.0
"""
import fnmatch
import os
import pathlib
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
SKIP_DIRS = {".git", "bin", "obj", ".vs", "artifacts", "release", ".tools", "TestResults", "__pycache__", ".idea"}
SKIP_FILES = ["*.user", "*.suo", "LapDock-Icons.zip", "Thumbs.db", ".DS_Store", "*.swp"]


def main():
    version = sys.argv[1] if len(sys.argv) > 1 else "1.0.0"
    out = ROOT / "release" / f"LapDock-{version}-source.zip"
    out.parent.mkdir(exist_ok=True)
    count = 0
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for folder, dirs, files in os.walk(ROOT):
            dirs[:] = sorted(d for d in dirs if d not in SKIP_DIRS)
            for name in sorted(files):
                if any(fnmatch.fnmatch(name, p) for p in SKIP_FILES):
                    continue
                path = pathlib.Path(folder) / name
                z.write(path, ("lapdock" / path.relative_to(ROOT)).as_posix())
                count += 1
    print(f"{count} files -> {out.relative_to(ROOT)} ({out.stat().st_size // 1024} KB)")


if __name__ == "__main__":
    main()
