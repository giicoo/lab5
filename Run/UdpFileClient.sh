#!/usr/bin/env bash
set -e
HERE="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$HERE/../SampleFile"
dotnet run --project "$HERE/../Lab5/UdpFileClient/UdpFileClient.csproj" --no-launch-profile
