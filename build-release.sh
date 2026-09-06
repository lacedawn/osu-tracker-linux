#!/bin/bash
set -e

VERSION="${1:-1.0.0}"
OUTPUT_DIR="./releases/v${VERSION}"

echo "Building Circle-Tracker v${VERSION}"
echo "=================================="

rm -rf ./releases
mkdir -p "$OUTPUT_DIR"

echo "Building Linux x64..."
dotnet publish -c Release -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:Version="${VERSION}" \
    -o "${OUTPUT_DIR}/circle-tracker-linux-x64"

echo "Building Windows x64..."
dotnet publish -c Release -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:Version="${VERSION}" \
    -o "${OUTPUT_DIR}/circle-tracker-win-x64"

echo "Building macOS x64..."
dotnet publish -c Release -r osx-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:Version="${VERSION}" \
    -o "${OUTPUT_DIR}/circle-tracker-osx-x64"

echo "Building macOS ARM64..."
dotnet publish -c Release -r osx-arm64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:Version="${VERSION}" \
    -o "${OUTPUT_DIR}/circle-tracker-osx-arm64"

echo ""
echo "Creating release archives..."
cd "$OUTPUT_DIR"

tar -czf circle-tracker-${VERSION}-linux-x64.tar.gz circle-tracker-linux-x64/
zip -r circle-tracker-${VERSION}-win-x64.zip circle-tracker-win-x64/
tar -czf circle-tracker-${VERSION}-osx-x64.tar.gz circle-tracker-osx-x64/
tar -czf circle-tracker-${VERSION}-osx-arm64.tar.gz circle-tracker-osx-arm64/

cd ../..

echo ""
echo "✓ Build complete!"
echo "Release artifacts in: ${OUTPUT_DIR}"
echo ""
echo "Archives created:"
ls -lh "${OUTPUT_DIR}"/*.{tar.gz,zip} 2>/dev/null || true
echo ""
echo "Next steps:"
echo "1. git tag v${VERSION}"
echo "2. git push origin v${VERSION}"
echo "3. Create GitHub release and upload archives from ${OUTPUT_DIR}"
