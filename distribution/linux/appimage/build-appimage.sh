#!/usr/bin/env sh
set -eu

ROOTDIR="$(readlink -f "$(dirname "$0")")"/../../../
cd "$ROOTDIR"

BUILDDIR=${BUILDDIR:-publish}
OUTDIR=${OUTDIR:-publish_appimage}

# AppStream

rm -rf AppDir
mkdir -p AppDir/usr/lib AppDir/usr/bin
mkdir -p AppDir/usr/share/metainfo AppDir/usr/share/applications
mkdir -p AppDir/usr/share/icons/hicolor/256x256/apps/

cp -r "$BUILDDIR"/* AppDir/usr/lib/

cp distribution/linux/appimage/app.ryujinx.Ryujinx.appdata.xml AppDir/usr/share/metainfo/app.ryujinx.Ryujinx.appdata.xml
cp distribution/linux/app.ryujinx.Ryujinx.desktop AppDir/usr/share/applications/app.ryujinx.Ryujinx.desktop
cp distribution/misc/Logo.png AppDir/usr/share/icons/hicolor/256x256/apps/app.ryujinx.Ryujinx.png
ln -s ../lib/Ryujinx AppDir/usr/bin/Ryujinx

# AppImage Root

ln -s ./usr/share/applications/app.ryujinx.Ryujinx.desktop AppDir/app.ryujinx.Ryujinx.desktop
ln -s ./usr/share/icons/hicolor/256x256/apps/app.ryujinx.Ryujinx.png AppDir/.DirIcon
ln -s ./usr/share/icons/hicolor/256x256/apps/app.ryujinx.Ryujinx.png AppDir/app.ryujinx.Ryujinx.png
ln -s ./usr/lib/Ryujinx.sh AppDir/AppRun

# Ensure necessary bins are set as executable
chmod +x AppDir/AppRun AppDir/usr/bin/Ryujinx*

mkdir -p "$OUTDIR"

# The "-n" flag removes the appstream checks during build, in case the main website is down.
# Run "appstreamcli validate --explain AppDir/usr/share/metainfo/app.ryujinx.Ryujinx.appdata.xml" to check manually
appimagetool --appimage-extract-and-run -n --comp zstd --mksquashfs-opt -Xcompression-level --mksquashfs-opt 21 \
    AppDir "$OUTDIR"/Ryujinx.AppImage
