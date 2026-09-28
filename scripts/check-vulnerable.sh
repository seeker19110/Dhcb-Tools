#!/usr/bin/env bash
# Quét dependency có lỗ hổng đã biết — thay cho dependency-review-action (repo chưa bật Dependency graph,
# xem PR #159). Chạy được ở máy lẫn CI (.github/workflows/dependency-audit.yml), không cần Revit/AutoCAD:
#   - NuGet: `dotnet list package --vulnerable --include-transitive` trên cả solution, cho ba TFM mà
#     Directory.Build.props sinh ra (2023 → net48, 2025 → net8.0-windows, 2027 → net10.0-windows) — gói
#     phụ thuộc điều kiện theo TFM (System.Drawing.Common…) chỉ hiện ra ở đúng TFM của nó.
#   - Python: pip-audit trên requirements-dev.txt (kéo theo tools/autocad-mcp-server/requirements.txt).
# Thoát 1 nếu có ít nhất một package dính lỗ hổng; in rõ package, phiên bản và mã advisory.
set -euo pipefail
cd "$(dirname "$0")/.."

report=$(mktemp)
trap 'rm -f "$report"' EXIT
status=0

for v in 2023 2025 2027; do
  echo "== NuGet (Revit/AutoCAD $v)"
  dotnet restore Dhcb-Tools.sln -p:EnableWindowsTargeting=true -p:RevitVersion=$v -p:AcadVersion=$v -nologo -v:q
  dotnet list Dhcb-Tools.sln package --vulnerable --include-transitive --format json > "$report"
  python3 - "$report" <<'PY' || status=1
import json, sys
data = json.load(open(sys.argv[1], encoding="utf-8"))
hits = []
for project in data.get("projects", []):
    for framework in project.get("frameworks", []):
        for kind in ("topLevelPackages", "transitivePackages"):
            for package in framework.get(kind, []):
                advisories = ", ".join(v.get("advisoryurl", "?") for v in package.get("vulnerabilities", []))
                hits.append(f"  {project['path'].split('/')[-1]} [{framework['framework']}] "
                            f"{package['id']} {package.get('resolvedVersion', '?')}: {advisories}")
print("\n".join(hits) if hits else "  không có package nào dính lỗ hổng đã biết")
sys.exit(1 if hits else 0)
PY
done

echo "== Python (pip-audit)"
python3 -m pip_audit -r requirements-dev.txt --progress-spinner off || status=1

exit $status
