#!/usr/bin/env bash
# Builds the plugin in Docker and zips it with meta.json for manual install.
set -euo pipefail
cd "$(dirname "$0")"
docker run --rm -v "$PWD":/w -w /w/src/Jellyfin.Plugin.Organizer mcr.microsoft.com/dotnet/sdk:9.0 \
  dotnet build -c Release -o /w/out
cat > out/meta.json <<JSON
{"guid":"6f1c2a54-3b7e-4d0a-9a51-2c8e7d4b1f90","name":"Library Organizer","description":"Rule-based collections and ordering","overview":"Auto collections from genres, tags, countries, studios","owner":"local","category":"General","version":"0.5.0.0","targetAbi":"10.11.0.0","timestamp":"$(date -u +%Y-%m-%dT%H:%M:%SZ)"}
JSON
rm -f LibraryOrganizer_0.5.0.0.zip
(cd out && zip -q ../LibraryOrganizer_0.5.0.0.zip Jellyfin.Plugin.Organizer.dll meta.json)
sha256sum LibraryOrganizer_0.5.0.0.zip
