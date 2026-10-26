#!/usr/bin/env bash
# Builds bin/FileDateChanger.exe on Linux/macOS with Mono (the result runs on Windows)
set -e
cd "$(dirname "$0")"
mkdir -p bin
mcs -sdk:4.5 -langversion:5 -codepage:65001 -target:winexe -optimize+ \
  -out:bin/FileDateChanger.exe \
  -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll \
  -r:System.Xml.Linq.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll \
  src/FileDateChanger.cs
echo "OK: bin/FileDateChanger.exe"
