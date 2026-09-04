#!/usr/bin/env bash
# Build kiểm tra trên Linux/CI (không cần Revit/AutoCAD, không có WindowsDesktop SDK):
#   - Shared.Logic + Shared.Hosting + BatchRunner: build thật, test xUnit.
#   - Core Revit / Core AutoCAD / hai vỏ: biên dịch với API package NuGet (RevitVersion=2025 → net8.0-windows),
#     UseWPF=false để bỏ thư mục UI (WPF) của vỏ Revit. Đây là lưới bắt lỗi biên dịch trước khi lên máy Windows;
#     kiểm thử chức năng thật vẫn theo docs/dac-ta-kiem-thu.md §4.
#   Phiên bản chọn bằng biến môi trường; TFM do Directory.Build.props quyết định:
#     REVIT_VERSION=2024            → net48          ACAD_VERSION=2024 → net48
#     REVIT_VERSION=2025|2026       → net8.0-windows ACAD_VERSION=2025 → net8.0-windows
#     REVIT_VERSION=2027            → net10.0-windows ACAD_VERSION=2026 → net10.0-windows (cần SDK 10)
#   Ví dụ đường .NET 10 của AutoCAD:  ACAD_VERSION=2026 ./scripts/check-build.sh
set -euo pipefail
cd "$(dirname "$0")/.."

REVIT=${REVIT_VERSION:-2025}
ACAD=${ACAD_VERSION:-2025}
COMMON=(-p:EnableWindowsTargeting=true -p:RevitVersion=$REVIT -p:AcadVersion=$ACAD -p:UseWPF=false -nologo -v:q -clp:ErrorsOnly)

echo "== test Shared.Logic"
dotnet test tests/DhcbTools.Shared.Logic.Tests/DhcbTools.Shared.Logic.Tests.csproj -nologo -v:q

echo "== build BatchRunner"
dotnet build src/DhcbTools.BatchRunner/DhcbTools.BatchRunner.csproj -nologo -v:q -clp:ErrorsOnly

for proj in src/DhcbTools.Core/DhcbTools.Core.csproj src/DhcbTools.Core.AutoCAD/DhcbTools.Core.AutoCAD.csproj \
            src/DhcbTools.Revit/DhcbTools.Revit.csproj src/DhcbTools.AutoCAD/DhcbTools.AutoCAD.csproj \
            src/DhcbTools.AutoCAD.Core/DhcbTools.AutoCAD.Core.csproj; do
  echo "== check-build $proj (Revit $REVIT / AutoCAD $ACAD)"
  dotnet build "$proj" "${COMMON[@]}"
done
echo "OK"
