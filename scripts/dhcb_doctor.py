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


def diagnose(app: str = "all", offline: bool = False, config_dir=None) -> dict:
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
    args = parser.parse_args(argv)
    report = diagnose(args.app, args.offline)
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
