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
import uuid
from pathlib import Path
import xml.etree.ElementTree as ET

import dhcb_agent


YEARS = tuple(range(2022, 2028))
AUTOCAD_SERIES = {"R24.1": 2022, "R24.2": 2023, "R24.3": 2024,
                  "R25.0": 2025, "R25.1": 2026, "R26.0": 2027}


def expected_runtime(app: str, year: int) -> str:
    # Profiles describe the DHCB build, not proof that a host loaded the add-in.
    if year <= 2024:
        return "net48"
    return "net10.0" if year == 2027 or (app == "autocad" and year == 2026) else "net8.0"


def compatibility_profiles() -> list:
    profiles = [{"app": app, "year": year, "targetRuntime": expected_runtime(app, year),
                 "verification": "requires_host_acceptance"}
                for app in ("revit", "autocad") for year in YEARS]
    profiles.extend({"app": "autocad", "year": year, "targetRuntime": runtime,
                     "verification": "requires_host_acceptance"}
                    for year, runtime in ((2025, "net10.0"), (2026, "net8.0")))
    return profiles


def _inspect_profile(contents: Path, app: str, year: int) -> tuple:
    """Profile do bước đóng gói tạo; không suy runtime của DLL chỉ từ tên thư mục."""
    name = ("AutoCAD" if app == "autocad" else "Revit") + " " + str(year) + " profile"
    path = contents / "dhcb-host-profile.json"
    if not path.is_file():
        return expected_runtime(app, year), _check(name, "warning",
            "Gói cũ thiếu profile runtime. Đang dùng mục tiêu build mặc định; chưa xác minh runtime của DLL đã cài.")
    try:
        profile = json.loads(path.read_text(encoding="utf-8-sig"))
        allowed = {expected_runtime(app, year)}
        if app == "autocad" and year in (2025, 2026):
            allowed = {"net8.0", "net10.0"}
        if not isinstance(profile, dict) or profile.get("product") != app \
                or type(profile.get("year")) is not int or profile["year"] != year \
                or profile.get("runtime") not in allowed:
            raise ValueError("Invalid profile")
        runtime = profile["runtime"]
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError, TypeError):
        return expected_runtime(app, year), _check(name, "error",
            "Profile không đọc được hoặc product/year/runtime sai. Cài lại đúng gói; không dùng profile để sửa nhãn DLL.")
    return runtime, _check(name, "ok", "Profile gói khai báo " + runtime + "; cần nghiệm thu add-in trong host.")


def _runtime_check(app: str, year: int, host: Path, target_runtime=None) -> dict:
    name = ("AutoCAD" if app == "autocad" else "Revit") + " " + str(year)
    expected = target_runtime or expected_runtime(app, year)
    if expected == "net48":
        return _check(name + " host", "ok",
                      "Đã thấy executable; bản này dùng .NET Framework 4.8. Chưa xác nhận runtime hay add-in đã nạp.")
    filename = "acdbmgd.runtimeconfig.json" if app == "autocad" else "Revit.runtimeconfig.json"
    try:
        runtime = json.loads((host / filename).read_text(encoding="utf-8-sig"))
        options = runtime.get("runtimeOptions") if isinstance(runtime, dict) else None
        tfm = options.get("tfm") if isinstance(options, dict) else None
    except (OSError, UnicodeError, json.JSONDecodeError):
        tfm = None
    matches = tfm == expected
    if app == "autocad" and year == 2025 and expected == "net10.0":
        update_advice = " AutoCAD 2025 cần Update 1.4 trở lên cho build .NET 10 này."
    else:
        update_advice = ""
    if app == "revit" and year in (2025, 2026) and expected == "net8.0" and tfm == "net10.0":
        return _check(name + " runtime", "warning",
                      "Host đã chuyển sang .NET 10; gói Revit này build .NET 8. Cần kiểm nạp add-in và nghiệm thu trong host cập nhật, chưa xác nhận tương thích.")
    advice = " AutoCAD 2026 cần Update 1.2 trở lên cho build .NET 10 này." if app == "autocad" and year == 2026 and expected == "net10.0" else ""
    return _check(name + " runtime", "ok" if matches else "error",
                  "Runtime khớp " + expected + "; chưa xác nhận add-in đã nạp." if matches
                  else "Không xác minh được runtime " + expected + ". Kiểm đúng thư mục và phiên bản host." + advice + update_advice)


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


def inspect_autocad_installation(acad_dir=None, year=None) -> list:
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
    requested_year = year
    custom_year = requested_year if requested_year is not None else 2026
    seen = set()
    required = ("DhcbTools.AutoCAD.dll", "DhcbTools.AutoCAD.Core.dll", "DhcbTools.Core.AutoCAD.dll",
                "DhcbTools.Shared.Hosting.dll", "DhcbTools.Shared.Logic.dll", "Newtonsoft.Json.dll")
    components = manifest.findall("Components")
    if not components:
        checks.append(_check("Module AutoCAD", "error", "Manifest không có component AutoCAD."))
    for component in components:
        requirements = component.find("RuntimeRequirements")
        entry = component.find("ComponentEntry")
        year = AUTOCAD_SERIES.get(requirements.get("SeriesMin")) if requirements is not None else None
        if year is None or requirements.get("SeriesMax") != requirements.get("SeriesMin"):
            checks.append(_check("Module AutoCAD", "warning", "Component dùng dải phiên bản chưa được doctor kiểm chứng."))
            continue
        if requested_year is not None and year != requested_year:
            continue
        name = "AutoCAD " + str(year)
        expected = f"Contents/{year}/DhcbTools.AutoCAD.dll"
        module = entry.get("ModuleName", "").replace("\\", "/") if entry is not None else ""
        if year in seen or len(component.findall("ComponentEntry")) != 1 or module not in (expected, "./" + expected):
            checks.append(_check(name + " module", "error", "Component trùng hoặc đường module không đúng cấu trúc gói DHCB."))
            continue
        seen.add(year)
        contents = bundle / "Contents" / str(year)
        missing = [filename for filename in required if not (contents / filename).is_file()]
        checks.append(_check(name + " module", "error" if missing else "ok",
                             "Thiếu assembly: " + ", ".join(missing) if missing else "Đủ các assembly bắt buộc; chưa xác nhận host đã nạp."))
        target_runtime, profile_check = _inspect_profile(contents, "autocad", year)
        checks.append(profile_check)
        custom = acad_dir is not None and year == custom_year
        host = Path(acad_dir) if custom else Path(
            os.environ.get("ProgramFiles") or "C:/Program Files") / "Autodesk" / ("AutoCAD " + str(year))
        if not (host / "acad.exe").is_file():
            checks.append(_check(name + " host", "error" if custom else "warning",
                                 "Không thấy acad.exe ở thư mục đã chỉ định." if custom
                                 else "Không thấy host tại thư mục chuẩn. Nếu cài nơi khác, dùng --acad-dir cùng --year."))
            continue
        checks.append(_runtime_check("autocad", year, host, target_runtime))
    if requested_year is not None and requested_year not in seen:
        checks.append(_check("AutoCAD " + str(requested_year) + " module", "error",
                             "Manifest chưa có component hợp lệ cho năm đã chọn. Cài đúng gói của phiên bản host."))
    return checks


def inspect_revit_installation(revit_dir=None, year=None) -> list:
    """Kiểm manifest, dependencies và host; không chạy Revit hoặc tải DLL."""
    user_base = Path(os.environ.get("APPDATA") or str(Path.home() / ".config")) / "Autodesk" / "Revit" / "Addins"
    machine_base = Path(os.environ.get("PROGRAMDATA") or "C:/ProgramData") / "Autodesk" / "Revit" / "Addins"
    custom_year = year if year is not None else 2026
    years = (year,) if year is not None else YEARS
    required = ("DhcbTools.Revit.dll", "DhcbTools.Core.dll", "DhcbTools.Shared.Hosting.dll",
                "DhcbTools.Shared.Logic.dll", "Newtonsoft.Json.dll")
    checks = []
    found = False
    for selected in years:
        name = "Revit " + str(selected)
        manifests = [base / str(selected) / "DhcbTools.Revit.addin" for base in (user_base, machine_base)]
        manifests = [path for path in manifests if path.is_file()]
        custom = revit_dir is not None and selected == custom_year
        host = Path(revit_dir) if custom else Path(
            os.environ.get("ProgramFiles") or "C:/Program Files") / "Autodesk" / name
        if not manifests:
            if year is not None or custom or (host / "Revit.exe").is_file():
                checks.append(_check(name + " add-in", "warning", "Chưa thấy manifest DHCB ở thư mục Addins người dùng hoặc toàn máy."))
            if custom and not (host / "Revit.exe").is_file():
                checks.append(_check(name + " host", "error", "Không thấy Revit.exe ở thư mục đã chỉ định."))
            continue
        found = True
        if len(manifests) > 1:
            checks.append(_check(name + " add-in", "warning",
                                 "Manifest người dùng che manifest cùng tên của toàn máy; Revit chỉ đọc bản người dùng. Kiểm đúng bản cần dùng."))
            # Autodesk registration precedence: the per-user manifest shadows the same filename globally.
            manifests = manifests[:1]
        target_runtime = expected_runtime("revit", selected)
        for path in manifests:
            try:
                manifest = ET.parse(path).getroot()
                entries = manifest.findall("AddIn")
                entry = entries[0] if len(entries) == 1 else None
                if manifest.tag != "RevitAddIns" or entry is None or entry.get("Type") != "Application" \
                        or entry.findtext("FullClassName") != "DhcbTools.Revit.App" \
                        or entry.findtext("VendorId") != "DHCB":
                    raise ValueError("Invalid manifest")
                uuid.UUID((entry.findtext("AddInId") or "").strip())
                assembly = (entry.findtext("Assembly") or "").strip().replace("\\", "/")
                if not assembly or Path(assembly).name != "DhcbTools.Revit.dll":
                    raise ValueError("Invalid assembly")
                module = Path(assembly)
                module = module if module.is_absolute() else path.parent / module
            except (OSError, ET.ParseError, ValueError):
                checks.append(_check(name + " add-in", "error",
                                     "Manifest không đọc được hoặc không đúng add-in DHCB. Cài lại từ gói đã kiểm chứng."))
                continue
            missing = [filename for filename in required if not (module.parent / filename).is_file()]
            checks.append(_check(name + " module", "error" if missing else "ok",
                                 "Thiếu assembly: " + ", ".join(missing) if missing
                                 else "Đủ manifest và assembly bắt buộc; chưa xác nhận host đã nạp."))
            target_runtime, profile_check = _inspect_profile(module.parent, "revit", selected)
            checks.append(profile_check)
        if not (host / "Revit.exe").is_file():
            checks.append(_check(name + " host", "error" if custom else "warning",
                                 "Không thấy Revit.exe ở thư mục đã chỉ định." if custom
                                 else "Không thấy host tại thư mục chuẩn. Nếu cài nơi khác, dùng --revit-dir cùng --year."))
        else:
            checks.append(_runtime_check("revit", selected, host, target_runtime))
    if not found and not checks:
        checks.append(_check("Add-in Revit", "warning", "Chưa thấy manifest DHCB cho Revit 2022–2027. Cài đúng gói của phiên bản host."))
    return checks


def diagnose(app: str = "all", offline: bool = False, config_dir=None, acad_dir=None, *, year=None, revit_dir=None) -> dict:
    if app not in ("all", "revit", "autocad") or (year is not None and year not in YEARS):
        raise ValueError("Chọn app hợp lệ và năm từ 2022 đến 2027.")
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
    token_ready = len(token) >= 32 and token.isascii() and all(33 <= ord(char) <= 126 for char in token)
    checks.append(_check("Token Bridge", "ok" if token_ready else "warning",
                         "Đã thấy token đủ độ dài; không hiển thị giá trị." if token_ready
                         else "Token chưa có, ngắn hơn 32 ký tự hoặc định dạng lỗi. Mở host có add-in để sinh token; không gửi thử token sai."))

    enabled = settings is not None and settings.get("bridge", {}).get("enabled", True)
    apps = ("revit", "autocad") if app == "all" else (app,)
    if "autocad" in apps and platform.system() == "Windows":
        checks.extend(inspect_autocad_installation(acad_dir) if year is None
                      else inspect_autocad_installation(acad_dir, year))
    if "revit" in apps and platform.system() == "Windows":
        checks.extend(inspect_revit_installation(revit_dir, year))
    for target in apps:
        if offline or not enabled or not token_ready:
            reason = "Chế độ offline." if offline else "Bridge đã tắt/cấu hình cần sửa hoặc token chưa sẵn sàng."
            checks.append(_check("Bridge " + target, "warning", reason + " Không gửi request."))
            continue
        health = dhcb_agent.request(target, "GET", "/health", timeout=5)
        host = health.get("app")
        if health.get("success") is False or not isinstance(host, str) or host.lower() != target.lower() or not isinstance(health.get("version"), str) or not health["version"].strip():
            checks.append(_check("Bridge " + target, "error",
                                 "Không xác minh được đúng host và phiên bản tại cổng chuẩn. Mở host, kiểm add-in và log."))
            continue
        checks.append(_check("Bridge " + target, "ok", "Đúng ứng dụng; health phản hồi có phiên bản add-in. Endpoint này chưa xác nhận năm host hay nghiệm thu lệnh."))
        catalog = dhcb_agent.request(target, "GET", "/tools", timeout=5)
        tools = catalog.get("tools")
        valid = catalog.get("success") is not False and isinstance(tools, list) and bool(tools) and all(
            isinstance(t, dict) and isinstance(t.get("name"), str) and bool(t["name"].strip()) for t in tools)
        if valid:
            valid = len({t["name"].strip().casefold() for t in tools}) == len(tools)
        checks.append(_check("Danh mục " + target, "ok" if valid else "error",
                             f"Đọc được {len(tools)} lệnh." if valid
                             else "Danh mục không đọc được hoặc sai định dạng. Kiểm token, cập nhật add-in và client cùng phiên bản."))
    return {"checks": checks, "errors": sum(c["status"] == "error" for c in checks),
            "warnings": sum(c["status"] == "warning" for c in checks),
            "compatibilityProfiles": compatibility_profiles(),
            "scope": "Chẩn đoán môi trường/cấu hình và Bridge; profile 2022–2027 là mục tiêu build. Cần nghiệm thu trong từng host; không xác nhận chất lượng model hay kết quả nghiệp vụ."}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Chẩn đoán DHCB chỉ đọc, không in bí mật.")
    parser.add_argument("--app", choices=("revit", "autocad", "all"), default="all")
    parser.add_argument("--offline", action="store_true", help="Chỉ kiểm môi trường/file, không kết nối Bridge")
    parser.add_argument("--json", action="store_true", help="In JSON để gửi kèm báo lỗi")
    parser.add_argument("--year", type=int, choices=YEARS, help="Chỉ kiểm gói của phiên bản host đã chọn (2022–2027)")
    parser.add_argument("--acad-dir", help="Thư mục chứa acad.exe cho --year; mặc định AutoCAD 2026 nếu không chọn năm")
    parser.add_argument("--revit-dir", help="Thư mục chứa Revit.exe cho --year; mặc định Revit 2026 nếu không chọn năm")
    args = parser.parse_args(argv)
    options = {"acad_dir": args.acad_dir}
    if args.year is not None:
        options["year"] = args.year
    if args.revit_dir is not None:
        options["revit_dir"] = args.revit_dir
    report = diagnose(args.app, args.offline, **options)
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
