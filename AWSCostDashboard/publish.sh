#!/bin/sh
set -e

PACKAGE_DIR="../"
mkdir -p "$PACKAGE_DIR"

APP_NAME="AWSCostDashboard"
APP_CONTENTS="$PACKAGE_DIR/$APP_NAME.app/Contents"

mkdir -p "$APP_CONTENTS/MacOS"
mkdir -p "$APP_CONTENTS/Resources"

dotnet publish \
  -c Release \
  -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:IncludeAllContentForSelfExtract=true \
  -o "$APP_CONTENTS/MacOS"

# Remove PDBs from publish output
find "$APP_CONTENTS/MacOS" -type f -name "*.pdb" -delete

# Copy appsettings files explicitly
cp appsettings*.json "$APP_CONTENTS/MacOS"/

# Copy Info.plist (note capital I) to Contents folder
cp Info.plist "$APP_CONTENTS/"

# Run the create Icons to update the Icon Library
cd ./Resources
./createicns
cd ..

# Copy icon to Resources folder (must match CFBundleIconFile in Info.plist)
if [ -f "./Resources/AppIcon.icns" ]; then
    cp "./Resources/AppIcon.icns" "$APP_CONTENTS/Resources/"
    echo "Icon copied to Resources"
else
    echo "Warning: AppIcon.icns not found - app will use default icon"
fi

# Copy and make launcher executable
cp launcher "$APP_CONTENTS/MacOS/"
chmod +x "$APP_CONTENTS/MacOS/launcher"

# Ensure main binary is executable
chmod +x "$APP_CONTENTS/MacOS/$APP_NAME"

echo "Build complete: $PACKAGE_DIR$APP_NAME.app"