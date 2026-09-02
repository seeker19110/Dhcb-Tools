#!/usr/bin/env python3
"""
dhcb_mcp_server.py — MCP server (stdio, JSON-RPC 2.0) bọc HTTP Bridge của Revit/AutoCAD (mục 6.2).
Không dependency ngoài, chạy offline. Cho Claude Desktop / Claude Code / bất kỳ MCP client nào gọi lệnh DHCB.

  tools/list  — sinh từ GET /tools của Bridge (chính bảng CommandCatalog) + tool `query` và `chat`
  tools/call  — POST /execute (luôn ép dryRun:true trừ khi tham số confirm=true), /query, /chat

Cấu hình Claude Desktop (claude_desktop_config.json):
  "mcpServers": { "dhcb-revit": { "command": "python", "args": ["C:/Dhcb-Tools/scripts/dhcb_mcp_server.py", "revit"] } }
"""

import json
import sys

sys.path.insert(0, __file__.rsplit("/", 1)[0] if "/" in __file__ else ".")
try:
    import dhcb_agent  # cùng thư mục
except ImportError:  # pragma: no cover
    from scripts import dhcb_agent  # type: ignore

APP = (sys.argv[1] if len(sys.argv) > 1 else "revit").lower()
if APP not in ("revit", "autocad"):
    sys.stderr.write("Dùng: dhcb_mcp_server.py revit|autocad [--read-only] [--group NHÓM]\n")
    sys.exit(2)

# --read-only: chỉ lộ tool đọc (giống Revit 2027 MCP Server tech preview). --group: chỉ lộ một nhóm tool
# (mô hình local 7–14B chọn kém khi > ~8 tool). Nhóm suy từ mô tả/tên lệnh trong catalog.
READ_ONLY = "--read-only" in sys.argv
GROUP = None
if "--group" in sys.argv:
    i = sys.argv.index("--group")
    GROUP = sys.argv[i + 1].lower() if i + 1 < len(sys.argv) else None

GROUPS = {
    "query": ("query", "chat"),
    "data": ("ParameterExport", "ParameterImport", "LayerExport", "LayerImport", "AttributeExport", "AttributeImport", "WarningsExport", "BlockQuantity"),
    "sheets": ("SheetRename", "RevisionOnSheets", "SheetBatchCreate", "BatchExport"),
    "cleanup": ("RemoveUnusedViews", "StylePurge", "DrawingCleanup", "FamilyAudit", "LayerTranslate"),
    "check": ("HealthReport", "ParameterRuleCheck", "ClashDetection", "ConnectorChecker", "LayerStandardCheck", "XrefAudit", "DrawingCompare"),
    "mep": ("SleeveAuto", "ElevationTag", "HangerAuto", "PipeSplitter", "RouteFromLines", "DevicePlacement", "SizingProposal", "ApplySizing", "SystemColor", "SystemName", "FlowNumbering"),
    "project": ("ProjectInfo", "LevelSetup", "GridSetup", "FamilyLoader", "ProjectFromTemplate", "TransferStandards", "GridFromCsv", "GridExtract"),
    "ai": ("CadLayerMap", "SpecToConfig", "ColorByParameter", "AutoNumbering", "AttributeIncrement", "TextReplace"),
}

PROTOCOL_VERSION = "2024-11-05"


def tool_list() -> list:
    tools = []
    catalog = dhcb_agent.request(APP, "GET", "/tools")
    allowed = set(n.lower() for n in GROUPS.get(GROUP, ())) if GROUP else None
    for t in catalog.get("tools", []):
        if READ_ONLY and t.get("writesModel"):
            continue
        if allowed is not None and t["name"].lower() not in allowed:
            continue
        props = dict(t.get("inputSchema", {}).get("properties", {}))
        if t.get("writesModel"):
            props["confirm"] = {"type": "boolean", "description": "true = chạy THẬT (mặc định chỉ xem trước dryRun)"}
        tools.append({
            "name": t["name"],
            "description": t.get("description", "") + (" (sửa mô hình — mặc định xem trước)" if t.get("writesModel") else ""),
            "inputSchema": {"type": "object", "properties": props},
        })
    tools.append({
        "name": "query",
        "description": (
            f"Đọc ngữ cảnh {APP} (không ghi). Revit: document_info, levels, views, sheets, rooms, elements, "
            "families, warnings, links, stats; element_geometry (hộp bao/đường tâm/connector, params: elementIds "
            "hoặc categories), parameters_of (tham số của category — dùng trước khi dựng config), schedule_rows "
            "(bảng thống kê dạng hàng), snapshot (ảnh PNG base64 của view — để NHÌN kết quả), selection (đang chọn "
            "gì; truyền elementIds để ĐẶT lựa chọn), show_elements (zoom tới phần tử cho kỹ sư nhìn), active_view. "
            "AutoCAD: drawing_info, layers, blocks, inserts…"
        ),
        "inputSchema": {"type": "object", "properties": {"query": {"type": "string"}, "params": {"type": "object"}}, "required": ["query"]},
    })
    if allowed is not None and "chat" not in allowed:
        return tools
    tools.append({
        "name": "chat",
        "description": "Dịch câu tiếng Việt sang lệnh + config đề xuất (không chạy).",
        "inputSchema": {"type": "object", "properties": {"text": {"type": "string"}}, "required": ["text"]},
    })
    return tools


def call_tool(name: str, arguments: dict) -> dict:
    if name == "query":
        return dhcb_agent.request(APP, "POST", "/query", {"query": arguments.get("query", ""), "params": arguments.get("params", {})})
    if name == "chat":
        return dhcb_agent.request(APP, "POST", "/chat", {"text": arguments.get("text", "")})
    if READ_ONLY:
        # Chỉ chấp nhận lệnh đọc; lệnh ghi bị từ chối ngay tại server (không phụ thuộc client).
        catalog = dhcb_agent.request(APP, "GET", "/tools")
        writes = {t["name"].lower() for t in catalog.get("tools", []) if t.get("writesModel")}
        if name.lower() in writes:
            return {"success": False, "summary": f"Server đang chạy --read-only; lệnh ghi '{name}' bị chặn."}
    config = dict(arguments)
    confirm = bool(config.pop("confirm", False))
    # Nguyên tắc: AI chỉ đề xuất, kỹ sư xác nhận — không confirm thì luôn xem trước.
    config["dryRun"] = not confirm
    return dhcb_agent.send(APP, name, config)


def respond(msg_id, result=None, error=None):
    payload = {"jsonrpc": "2.0", "id": msg_id}
    if error is not None:
        payload["error"] = error
    else:
        payload["result"] = result
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def main():
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            msg = json.loads(line)
        except json.JSONDecodeError:
            continue
        method = msg.get("method")
        msg_id = msg.get("id")
        params = msg.get("params") or {}

        if method == "initialize":
            respond(msg_id, {
                "protocolVersion": PROTOCOL_VERSION,
                "capabilities": {"tools": {}},
                "serverInfo": {"name": f"dhcb-{APP}" + ("-readonly" if READ_ONLY else "") + (f"-{GROUP}" if GROUP else ""), "version": "1.1.0"},
            })
        elif method == "notifications/initialized":
            continue
        elif method == "tools/list":
            try:
                respond(msg_id, {"tools": tool_list()})
            except Exception as ex:  # noqa: BLE001
                respond(msg_id, error={"code": -32000, "message": str(ex)})
        elif method == "tools/call":
            try:
                result = call_tool(params.get("name", ""), params.get("arguments") or {})
                is_error = ("success" in result and not result["success"]) or "error" in result
                respond(msg_id, {"content": [{"type": "text", "text": json.dumps(result, ensure_ascii=False, indent=2)}], "isError": is_error})
            except Exception as ex:  # noqa: BLE001
                respond(msg_id, error={"code": -32000, "message": str(ex)})
        elif method == "ping":
            respond(msg_id, {})
        elif msg_id is not None:
            respond(msg_id, error={"code": -32601, "message": f"Method không hỗ trợ: {method}"})


if __name__ == "__main__":
    main()
