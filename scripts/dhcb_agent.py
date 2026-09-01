#!/usr/bin/env python3
"""
dhcb_agent.py — Client để agent AI (hoặc bạn từ terminal) gửi lệnh vào Revit (8765) / AutoCAD (8766) đang chạy.
Không cần dependency ngoài. Không cần internet.

Token (mục 0.1): đọc từ %APPDATA%\\DHCB\\bridge-token.txt (do add-in sinh lần đầu), hoặc biến môi trường
DHCB_BRIDGE_TOKEN. Bridge chỉ nhận 127.0.0.1.

Cách dùng:
    python dhcb_agent.py revit tools                          # danh sách lệnh + schema (GET /tools)
    python dhcb_agent.py revit chat "đánh số cửa tầng 3 tiền tố D-"   # đề xuất lệnh, KHÔNG chạy
    python dhcb_agent.py revit query document_info
    python dhcb_agent.py revit Cleanup --dry-run
    python dhcb_agent.py revit AutoNumbering --category Doors --param Mark --prefix D- --pad 3
    python dhcb_agent.py revit raw '{"command":"HangerAuto","config":{"hangerFamilyName":"HGR","spacingMm":2500}}'
    python dhcb_agent.py revit exec RouteFromLines --config-file route.json --no-dry-run

    python dhcb_agent.py autocad LayerExport --output C:/tmp/layers.csv
    python dhcb_agent.py autocad exec GridExtract --config '{"gridLayer":"AXIS","outputPath":"C:/tmp/grids.csv"}'
"""

import argparse
import json
import os
import sys
import urllib.error
import urllib.request

PORTS = {"revit": 8765, "autocad": 8766}


def base_url(app: str) -> str:
    return f"http://127.0.0.1:{PORTS[app]}"


def load_token() -> str:
    env = os.environ.get("DHCB_BRIDGE_TOKEN")
    if env:
        return env.strip()
    appdata = os.environ.get("APPDATA") or os.path.join(os.path.expanduser("~"), ".config")
    path = os.path.join(appdata, "DHCB", "bridge-token.txt")
    try:
        with open(path, "r", encoding="utf-8") as f:
            return f.read().strip()
    except OSError:
        return ""


def request(app: str, method: str, path: str, payload=None, timeout: int = 35) -> dict:
    url = base_url(app) + path
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    headers = {"Authorization": "Bearer " + load_token()}
    if data is not None:
        headers["Content-Type"] = "application/json; charset=utf-8"
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="replace")
        try:
            parsed = json.loads(body)
        except Exception:
            parsed = {"error": body}
        if e.code == 401:
            parsed.setdefault("summary", "401 — sai token. Kiểm tra %APPDATA%\\DHCB\\bridge-token.txt hoặc DHCB_BRIDGE_TOKEN.")
        elif e.code == 429:
            parsed.setdefault("summary", "429 — Bridge đang khoá 5 phút vì sai token nhiều lần.")
        elif e.code == 504:
            parsed.setdefault("summary", "504 — hết thời gian chờ; lệnh đã bị huỷ, không chạy.")
        parsed.setdefault("success", False)
        parsed.setdefault("summary", f"HTTP {e.code}: {body}")
        return parsed
    except urllib.error.URLError as e:
        return {"success": False, "summary": f"Không kết nối được ({e.reason}). {app.capitalize()} có đang mở và plugin đã load chưa?"}


def send(app: str, command: str, config: dict) -> dict:
    return request(app, "POST", "/execute", {"command": command, "config": config})


def print_result(result: dict):
    if "success" in result:
        icon = "✓" if result.get("success") else "✗"
        print(f"\n{icon} {result.get('summary', '')}")
        for msg in result.get("messages", []):
            print(f"  • {msg}")
        for err in result.get("errors", []):
            print(f"  ! {err}")
        affected = result.get("affectedCount")
        if affected is not None:
            print(f"\n  Số phần tử/object bị ảnh hưởng: {affected}")
    else:
        print(json.dumps(result, ensure_ascii=False, indent=2))


def build_config(args, app: str, dry_run: bool) -> dict:
    cmd_upper = args.command.upper()
    if cmd_upper in ("PARAMETEREXPORT", "LAYEREXPORT"):
        return {
            "outputPath": args.output or f"C:/Users/Public/dhcb_{app}_export.csv",
            **({"categories": args.categories} if args.categories else {}),
            **({"parameterNames": args.params} if args.params else {}),
            **({"filterNameContains": args.filter} if args.filter else {}),
        }
    if cmd_upper in ("PARAMETERIMPORT", "LAYERIMPORT"):
        return {"inputPath": args.input or "", "dryRun": dry_run, **({"createMissing": True} if args.create_missing else {})}
    if cmd_upper in ("CLEANUP", "REMOVEUNUSEDVIEWS", "DRAWINGCLEANUP"):
        return {"dryRun": dry_run}
    if cmd_upper in ("AUTONUMBERING", "AUTONUMBER"):
        if app == "revit":
            return {"category": args.category or "", "parameterName": args.param or "Mark", "prefix": args.prefix,
                    "padWidth": args.pad, "startNumber": args.start, "dryRun": dry_run, **({"levelName": args.level} if args.level else {})}
        return {"blockName": args.block or args.category or "", "attributeTag": args.attr or "MARK", "prefix": args.prefix,
                "padWidth": args.pad, "startNumber": args.start, "dryRun": dry_run}
    return {"dryRun": dry_run} if dry_run is not None else {}


def main():
    parser = argparse.ArgumentParser(description="DHCB Agent Client — gửi lệnh vào Revit/AutoCAD qua HTTP Bridge (offline)")
    parser.add_argument("app", choices=["revit", "autocad"])
    parser.add_argument("command", help="Tên lệnh, hoặc: tools | chat | query | raw | exec")
    parser.add_argument("arg", nargs="?", help="chat: câu tiếng Việt · query: tên query · raw: JSON · exec: tên lệnh")
    parser.add_argument("--config", help="(exec) JSON config inline")
    parser.add_argument("--config-file", help="(exec) file JSON config")
    parser.add_argument("--params", nargs="+", help="(ParameterExport) tham số; (query) key=value")
    parser.add_argument("--categories", nargs="+")
    parser.add_argument("--output")
    parser.add_argument("--input")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--no-dry-run", action="store_true", help="Ghi thật (mặc định luôn dry-run)")
    parser.add_argument("--category")
    parser.add_argument("--param")
    parser.add_argument("--attr")
    parser.add_argument("--block")
    parser.add_argument("--prefix", default="")
    parser.add_argument("--pad", type=int, default=0)
    parser.add_argument("--start", type=int, default=1)
    parser.add_argument("--level")
    parser.add_argument("--create-missing", action="store_true")
    parser.add_argument("--filter")
    args = parser.parse_args()

    app = args.app
    dry_run = not args.no_dry_run
    cmd = args.command.lower()

    if cmd == "tools":
        result = request(app, "GET", "/tools")
        if "tools" in result:
            for t in result["tools"]:
                flag = "✎" if t.get("writesModel") else "○"
                print(f"{flag} {t['name']:<20} {t.get('description','')}   [{', '.join(t.get('inputSchema',{}).get('properties',{}).keys())}]")
            sys.exit(0)
        print_result(result)
        sys.exit(1)

    if cmd == "chat":
        if not args.arg:
            print("Cần câu lệnh tiếng Việt sau 'chat'.", file=sys.stderr)
            sys.exit(1)
        result = request(app, "POST", "/chat", {"text": args.arg})
        print(json.dumps(result, ensure_ascii=False, indent=2))
        sys.exit(0 if result.get("command") else 1)

    if cmd == "query":
        params = {}
        for kv in args.params or []:
            k, _, v = kv.partition("=")
            params[k] = v
        result = request(app, "POST", "/query", {"query": args.arg or "document_info", "params": params})
        print(json.dumps(result, ensure_ascii=False, indent=2))
        sys.exit(0 if "error" not in result else 1)

    if cmd == "raw":
        if not args.arg:
            print("Cần JSON thô sau 'raw'.", file=sys.stderr)
            sys.exit(1)
        data = json.loads(args.arg)
        result = send(app, data["command"], data.get("config", {}))
        print_result(result)
        sys.exit(0 if result.get("success") else 1)

    if cmd == "exec":
        if not args.arg:
            print("Cần tên lệnh sau 'exec'.", file=sys.stderr)
            sys.exit(1)
        config = {}
        if args.config_file:
            with open(args.config_file, "r", encoding="utf-8-sig") as f:
                config = json.load(f)
        if args.config:
            config.update(json.loads(args.config))
        if args.no_dry_run:
            config["dryRun"] = False
        elif "dryRun" not in config:
            config["dryRun"] = True
        result = send(app, args.arg, config)
        print_result(result)
        sys.exit(0 if result.get("success") else 1)

    result = send(app, args.command, build_config(args, app, dry_run))
    print_result(result)
    sys.exit(0 if result.get("success") else 1)


if __name__ == "__main__":
    main()
