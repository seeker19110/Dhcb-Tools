#!/usr/bin/env bash
# Quét dependency có lỗ hổng đã biết — thay cho dependency-review-action (repo chưa bật Dependency graph,
# xem PR #159). Chạy được ở máy lẫn CI (.github/workflows/dependency-audit.yml), không cần Revit/AutoCAD:
#   - NuGet: quét solution cho mọi năm/runtime (2022–2027 và hai profile AutoCAD 2025/2026).
#     Restore và list dùng CÙNG property MSBuild qua environment; list package không nhận -p.
#   - Python: pip-audit trên requirements-dev.txt (kéo theo tools/autocad-mcp-server/requirements.txt).
# Thoát 1 nếu có ít nhất một package dính lỗ hổng; in rõ package, phiên bản và mã advisory.
set -euo pipefail
cd "$(dirname "$0")/.."

report=$(mktemp)
trap 'rm -f "$report"' EXIT
status=0

for profile in 2022:net48 2023:net48 2024:net48 2025:net8 2025:net10 2026:net8 2026:net10 2027:net10; do
  version=${profile%%:*}
  runtime=${profile#*:}
  echo "== NuGet (Revit/AutoCAD $version; AutoCAD $runtime)"
  export RevitVersion=$version AcadVersion=$version AcadRuntime=$runtime EnableWindowsTargeting=true
  dotnet restore Dhcb-Tools.sln -nologo -v:q
  dotnet list Dhcb-Tools.sln package --no-restore --vulnerable --include-transitive --format json > "$report"
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
