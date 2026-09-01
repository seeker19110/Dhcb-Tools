#!/usr/bin/env python3
"""
dhcb_agent.py — Client để agent AI (hoặc bạn từ terminal) gửi lệnh vào
Revit (port 8765) hoặc AutoCAD (port 8766) đang chạy.

Cách dùng:
    python dhcb_agent.py revit ParameterExport --categories Doors Walls --params Mark Level --output C:/tmp/params.csv
    python dhcb_agent.py revit Cleanup --dry-run
    python dhcb_agent.py revit AutoNumbering --category Doors --param Mark --prefix D- --pad 3

    python dhcb_agent.py autocad LayerExport --output C:/tmp/layers.csv
    python dhcb_agent.py autocad LayerImport --input C:/tmp/layers.csv --create-missing
    python dhcb_agent.py autocad Cleanup --dry-run
    python dhcb_agent.py autocad AutoNumbering --block DOOR --attr MARK --prefix D- --pad 3

Hoặc gửi JSON thô:
    python dhcb_agent.py revit raw '{"command":"Cleanup","config":{"dryRun":false}}'
"""

import argparse
import json
import sys
import urllib.request
import urllib.error

REVIT_URL = "http://localhost:8765/execute"
ACAD_URL  = "http://localhost:8766/execute"


def send(url: str, command: str, config: dict) -> dict:
    payload = json.dumps({"command": command, "config": config}).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=payload,
        headers={"Content-Type": "application/json; charset=utf-8"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=35) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="replace")
        try:
            return json.loads(body)
        except Exception:
            return {"success": False, "summary": f"HTTP {e.code}: {body}"}
    except urllib.error.URLError as e:
        app = "Revit" if "8765" in url else "AutoCAD"
        return {
            "success": False,
            "summary": f"Không kết nối được ({e.reason}). "
                       f"{app} có đang mở và plugin đã được load chưa?",
        }


def print_result(result: dict):
    icon = "✓" if result.get("success") else "✗"
    print(f"\n{icon} {result.get('summary', '')}")
    for msg in result.get("messages", []):
        print(f"  • {msg}")
    for err in result.get("errors", []):
        print(f"  ! {err}")
    affected = result.get("affectedElementCount") or result.get("affectedCount")
    if affected is not None:
        print(f"\n  Số phần tử/object bị ảnh hưởng: {affected}")


def main():
    parser = argparse.ArgumentParser(description="DHCB Agent Client — gửi lệnh vào Revit/AutoCAD")
    parser.add_argument("app", choices=["revit", "autocad"], help="Ứng dụng mục tiêu")
    parser.add_argument("command", help="Tên lệnh hoặc 'raw' để gửi JSON thô")
    parser.add_argument("raw_json", nargs="?", help="JSON thô khi command='raw'")

    # Revit options
    parser.add_argument("--categories", nargs="+", help="(ParameterExport) Danh sách category")
    parser.add_argument("--params",     nargs="+", help="(ParameterExport) Danh sách tham số")
    parser.add_argument("--output",     help="Đường dẫn file output CSV")
    parser.add_argument("--input",      help="Đường dẫn file input CSV")
    parser.add_argument("--dry-run",    action="store_true", help="Chỉ xem trước, không ghi")
    parser.add_argument("--no-dry-run", action="store_true", help="Ghi thật (tắt dry-run)")
    parser.add_argument("--category",   help="(AutoNumbering) Category / Block name")
    parser.add_argument("--param",      help="(AutoNumbering Revit) Tên tham số")
    parser.add_argument("--attr",       help="(AutoNumbering AutoCAD) Attribute tag")
    parser.add_argument("--block",      help="(AutoNumbering AutoCAD) Block name")
    parser.add_argument("--prefix",     default="", help="Tiền tố đánh số")
    parser.add_argument("--pad",        type=int, default=0, help="Số chữ số tối thiểu")
    parser.add_argument("--start",      type=int, default=1, help="Số bắt đầu")
    parser.add_argument("--level",      help="(AutoNumbering Revit) Tên Level lọc")
    parser.add_argument("--create-missing", action="store_true", help="(LayerImport) Tạo layer mới nếu thiếu")
    parser.add_argument("--filter",     help="(LayerExport) Lọc tên layer chứa chuỗi này")

    args = parser.parse_args()
    url = REVIT_URL if args.app == "revit" else ACAD_URL
    dry_run = not args.no_dry_run  # mặc định dry-run=True để an toàn

    # Raw mode
    if args.command.lower() == "raw":
        if not args.raw_json:
            print("Lỗi: cần truyền JSON thô sau 'raw'", file=sys.stderr)
            sys.exit(1)
        data = json.loads(args.raw_json)
        result = send(url, data["command"], data.get("config", {}))
        print_result(result)
        sys.exit(0 if result.get("success") else 1)

    # Build config theo command
    cmd = args.command
    config: dict = {}

    cmd_upper = cmd.upper()

    if cmd_upper in ("PARAMETEREXPORT", "LAYEREXPORT"):
        config = {
            "outputPath": args.output or f"C:/Users/Public/dhcb_{args.app}_export.csv",
            **({"categories": args.categories} if args.categories else {}),
            **({"parameterNames": args.params} if args.params else {}),
            **({"filterNameContains": args.filter} if args.filter else {}),
        }

    elif cmd_upper in ("PARAMETERIMPORT", "LAYERIMPORT"):
        config = {
            "inputPath": args.input or "",
            "dryRun": dry_run,
            **({"createMissing": args.create_missing} if args.create_missing else {}),
        }

    elif cmd_upper in ("CLEANUP", "REMOVEUNUSEDVIEWS", "DRAWINGCLEANUP"):
        config = {"dryRun": dry_run}

    elif cmd_upper in ("AUTONUMBERING", "AUTONUMBER"):
        if args.app == "revit":
            config = {
                "category": args.category or "",
                "parameterName": args.param or "Mark",
                "prefix": args.prefix,
                "padWidth": args.pad,
                "startNumber": args.start,
                "dryRun": dry_run,
                **({"levelName": args.level} if args.level else {}),
            }
        else:
            config = {
                "blockName": args.block or args.category or "",
                "attributeTag": args.attr or "MARK",
                "prefix": args.prefix,
                "padWidth": args.pad,
                "startNumber": args.start,
                "dryRun": dry_run,
            }
    else:
        # Gửi nguyên command, config rỗng
        config = {}

    result = send(url, cmd, config)
    print_result(result)
    sys.exit(0 if result.get("success") else 1)


if __name__ == "__main__":
    main()
