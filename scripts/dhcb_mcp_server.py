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
    sys.stderr.write("Dùng: dhcb_mcp_server.py revit|autocad\n")
    sys.exit(2)

PROTOCOL_VERSION = "2024-11-05"


def tool_list() -> list:
    tools = []
    catalog = dhcb_agent.request(APP, "GET", "/tools")
    for t in catalog.get("tools", []):
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
        "description": f"Đọc ngữ cảnh {APP} (không ghi): document_info/levels/views/sheets/rooms/elements… hoặc drawing_info/layers/blocks/inserts…",
        "inputSchema": {"type": "object", "properties": {"query": {"type": "string"}, "params": {"type": "object"}}, "required": ["query"]},
    })
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
                "serverInfo": {"name": f"dhcb-{APP}", "version": "1.0.0"},
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
