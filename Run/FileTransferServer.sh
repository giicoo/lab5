#!/usr/bin/env bash
set -e
HERE="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$HERE/../SampleFile"
dotnet run --project "$HERE/../Lab5/FileTransferServer/FileTransferServer.csproj" --no-launch-profile
