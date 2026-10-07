"""Chẩn đoán DHCB bằng thao tác chỉ đọc; không gửi config hay lệnh vào mô hình.

    python dhcb_doctor.py --app revit
    python dhcb_doctor.py --offline --json

Không ghi file, không cài dependency và không in token/nội dung cấu hình.
"""

from __future__ import annotations

import argparse
import json
import os
import platform
import sys
from pathlib import Path
import xml.etree.ElementTree as ET

import dhcb_agent


def _check(name: str, status: str, detail: str) -> dict:
    return {"name": name, "status": status, "detail": detail}


def inspect_config(path: Path) -> tuple:
    """Trả cấu hình cho kiểm tra nội bộ; báo cáo chỉ chứa tên file và tình trạng."""
    if not path.exists():
        return {}, _check(path.name, "warning", "Chưa có cấu hình riêng; dùng mặc định. Mẫu nằm trong configs/.")
    try:
        config = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, UnicodeError, json.JSONDecodeError):
        return None, _check(path.name, "error", "Không đọc được file hoặc JSON chưa hợp lệ. Sửa file rồi chạy lại.")
    if not isinstance(config, dict):
        return None, _check(path.name, "error", "Cấu hình phải là JSON object, không phải danh sách/chuỗi.")
    if path.name == "settings.json":
        bridge = config.get("bridge", {})
        if not isinstance(bridge, dict) or not isinstance(bridge.get("enabled", True), bool):
            return None, _check(path.name, "error", "bridge phải là object; bridge.enabled phải là true hoặc false.")
    return config, _check(path.name, "ok", "JSON đọc được; không hiển thị nội dung để bảo vệ dữ liệu dự án.")


def inspect_autocad_installation(acad_dir=None) -> list:
    """Kiểm bundle và runtime đã cài; không tải assembly hoặc chạy AutoCAD."""
    bundle = Path(os.environ.get("APPDATA") or str(Path.home() / ".config")) / "Autodesk" / "ApplicationPlugins" / "DhcbTools.bundle"
    manifest_path = bundle / "PackageContents.xml"
    if not manifest_path.exists():
        return [_check("Bundle AutoCAD", "warning", "Chưa thấy bundle trong APPDATA. Cài plugin hoặc kiểm đường NETLOAD đang dùng.")]
    try:
        manifest = ET.parse(manifest_path).getroot()
        if manifest.tag != "ApplicationPackage" or not manifest.get("AppVersion"):
            raise ValueError("Invalid package")
    except (OSError, ET.ParseError, ValueError):
        return [_check("Bundle AutoCAD", "error", "Manifest bundle không đọc được hoặc sai định dạng. Cài lại từ gói đã kiểm chứng.")]

    checks = [_check("Bundle AutoCAD", "ok", "Manifest đọc được; sẽ kiểm module và runtime riêng.")]
    series = {"R24.3": 2024, "R25.0": 2025, "R25.1": 2026}
    seen = set()
    required = ("DhcbTools.AutoCAD.dll", "DhcbTools.AutoCAD.Core.dll", "DhcbTools.Core.AutoCAD.dll",
                "DhcbTools.Shared.Hosting.dll", "DhcbTools.Shared.Logic.dll", "Newtonsoft.Json.dll")
    components = manifest.findall("Components")
    if not components:
        checks.append(_check("Module AutoCAD", "error", "Manifest không có component AutoCAD."))
    for component in components:
        requirements = component.find("RuntimeRequirements")
        entry = component.find("ComponentEntry")
        year = series.get(requirements.get("SeriesMin")) if requirements is not None else None
        if year is None or requirements.get("SeriesMax") != requirements.get("SeriesMin"):
            checks.append(_check("Module AutoCAD", "warning", "Component dùng dải phiên bản chưa được doctor kiểm chứng."))
            continue
        name = "AutoCAD " + str(year)
        expected = f"Contents/{year}/DhcbTools.AutoCAD.dll"
        module = entry.get("ModuleName", "").replace("\\", "/") if entry is not None else ""
        if year in seen or module not in (expected, "./" + expected):
            checks.append(_check(name + " module", "error", "Component trùng hoặc đường module không đúng cấu trúc gói DHCB."))
            continue
        seen.add(year)
        contents = bundle / "Contents" / str(year)
        missing = [filename for filename in required if not (contents / filename).is_file()]
        checks.append(_check(name + " module", "error" if missing else "ok",
                             "Thiếu assembly: " + ", ".join(missing) if missing else "Đủ các assembly bắt buộc; chưa xác nhận host đã nạp."))
        host = Path(acad_dir) if year == 2026 and acad_dir is not None else Path(
            os.environ.get("ProgramFiles") or "C:/Program Files") / "Autodesk" / ("AutoCAD " + str(year))
        if not (host / "acad.exe").is_file():
            checks.append(_check(name + " host", "error" if year == 2026 and acad_dir is not None else "warning",
                                 "Không thấy acad.exe ở thư mục đã chỉ định." if year == 2026 and acad_dir is not None
                                 else "Không thấy host tại thư mục chuẩn. Nếu AutoCAD 2026 cài nơi khác, dùng --acad-dir."))
            continue
        if year == 2024:
            checks.append(_check(name + " host", "ok", "Đã thấy acad.exe; bản 2024 dùng .NET Framework, chưa kiểm runtime bằng JSON."))
            continue
        expected_tfm = "net10.0" if year == 2026 else "net8.0"
        try:
            runtime = json.loads((host / "acdbmgd.runtimeconfig.json").read_text(encoding="utf-8-sig"))
            options = runtime.get("runtimeOptions") if isinstance(runtime, dict) else None
            tfm = options.get("tfm") if isinstance(options, dict) else None
        except (OSError, UnicodeError, json.JSONDecodeError):
            tfm = None
        matches = tfm == expected_tfm
        checks.append(_check(name + " runtime", "ok" if matches else "error",
                             "Runtime khớp " + expected_tfm + "." if matches
                             else "Không xác minh được runtime " + expected_tfm + ". Cập nhật host; AutoCAD 2026 cần Update 1.2 trở lên."))
    return checks


def diagnose(app: str = "all", offline: bool = False, config_dir=None, acad_dir=None) -> dict:
    base = Path(config_dir) if config_dir is not None else Path(
        os.environ.get("APPDATA") or str(Path.home() / ".config")) / "DHCB"
    checks = [
        _check("Python", "ok" if sys.version_info >= (3, 9) else "error", "Cần Python 3.9 trở lên."),
        _check("Nền tảng", "ok" if platform.system() == "Windows" else "warning",
               "Add-in chạy trên Windows; kiểm logic/script có thể chạy trên hệ điều hành khác."),
    ]
    settings = None
    for name in ("settings.json", "dictionary.json", "ai.json"):
        config, check = inspect_config(base / name)
        checks.append(check)
        if name == "settings.json":
            settings = config
    token = dhcb_agent.load_token()
    checks.append(_check("Token Bridge", "ok" if len(token) >= 32 else "warning",
                         "Đã thấy token đủ độ dài; không hiển thị giá trị." if len(token) >= 32
                         else "Token chưa có hoặc ngắn hơn 32 ký tự. Mở host có add-in để sinh token; không gửi thử token sai."))

    enabled = settings is not None and settings.get("bridge", {}).get("enabled", True)
    apps = ("revit", "autocad") if app == "all" else (app,)
    if "autocad" in apps and platform.system() == "Windows":
        checks.extend(inspect_autocad_installation(acad_dir))
    for target in apps:
        if offline or not enabled or len(token) < 32:
            reason = "Chế độ offline." if offline else "Bridge đã tắt/cấu hình cần sửa hoặc token chưa sẵn sàng."
            checks.append(_check("Bridge " + target, "warning", reason + " Không gửi request."))
            continue
        health = dhcb_agent.request(target, "GET", "/health", timeout=5)
        host = health.get("app")
        if not isinstance(host, str) or host.lower() != target.lower() or not health.get("version"):
            checks.append(_check("Bridge " + target, "error",
                                 "Không xác minh được đúng host và phiên bản tại cổng chuẩn. Mở host, kiểm add-in và log."))
            continue
        checks.append(_check("Bridge " + target, "ok", "Đúng host; endpoint health phản hồi có phiên bản."))
        catalog = dhcb_agent.request(target, "GET", "/tools", timeout=5)
        tools = catalog.get("tools")
        valid = isinstance(tools, list) and bool(tools) and all(
            isinstance(t, dict) and isinstance(t.get("name"), str) and bool(t["name"]) for t in tools)
        checks.append(_check("Danh mục " + target, "ok" if valid else "error",
                             f"Đọc được {len(tools)} lệnh." if valid
                             else "Danh mục không đọc được hoặc sai định dạng. Kiểm token, cập nhật add-in và client cùng phiên bản."))
    return {"checks": checks, "errors": sum(c["status"] == "error" for c in checks),
            "warnings": sum(c["status"] == "warning" for c in checks),
            "scope": "Chẩn đoán môi trường/cấu hình và Bridge; không xác nhận chất lượng model hay kết quả nghiệp vụ."}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Chẩn đoán DHCB chỉ đọc, không in bí mật.")
    parser.add_argument("--app", choices=("revit", "autocad", "all"), default="all")
    parser.add_argument("--offline", action="store_true", help="Chỉ kiểm môi trường/file, không kết nối Bridge")
    parser.add_argument("--json", action="store_true", help="In JSON để gửi kèm báo lỗi")
    parser.add_argument("--acad-dir", help="Thư mục chứa acad.exe của AutoCAD 2026 nếu cài ngoài đường chuẩn")
    args = parser.parse_args(argv)
    report = diagnose(args.app, args.offline, acad_dir=args.acad_dir)
    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2))
    else:
        for check in report["checks"]:
            print(f"[{check['status'].upper()}] {check['name']}: {check['detail']}")
        print(f"Kết quả: {report['errors']} lỗi, {report['warnings']} cảnh báo.")
        print(report["scope"])
    return 1 if report["errors"] else 0


if __name__ == "__main__":
    sys.exit(main())
