#!/bin/sh
set -e

PUBLISH_DIR="publish"

mkdir -p "$PUBLISH_DIR"

dotnet publish \
  -c Release \
  -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:IncludeAllContentForSelfExtract=true \
  -o "$PUBLISH_DIR"

# Remove PDBs from publish output
find "$PUBLISH_DIR" -type f -name "*.pdb" -delete

# Copy appsettings files explicitly
cp appsettings*.json "$PUBLISH_DIR"/
