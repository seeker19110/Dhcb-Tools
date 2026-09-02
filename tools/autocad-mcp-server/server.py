"""
AutoCAD Tools MCP Server
Cung cấp tools để điều khiển AutoCAD qua HTTP bridge (localhost:8766).
Cài: hermes mcp add autocad-tools --command python --args <path>/server.py
"""

from __future__ import annotations
import json
import os
import subprocess
import sys
from pathlib import Path
from typing import Any

from fastmcp import FastMCP

BRIDGE_URL = "http://localhost:8766"
PANEL_HTML = str(Path(__file__).parent / "panel.html")

mcp = FastMCP(
    name="autocad-tools",
    instructions=(
        "Công cụ điều khiển AutoCAD qua HTTP Bridge (port 8766). "
        "Trước khi dùng, dùng autocad_health để kiểm tra bridge có sống không. "
        "Tool autocad_open_panel mở bảng điều khiển ngay trong chat Hermes."
    ),
)


def _fetch(path: str, body: dict | None = None) -> dict:
    """Gọi HTTP bridge."""
    try:
        import urllib.request
        import urllib.error

        if body is None:
            req = urllib.request.Request(BRIDGE_URL + path)
        else:
            data = json.dumps(body).encode()
            req = urllib.request.Request(
                BRIDGE_URL + path,
                data=data,
                headers={"Content-Type": "application/json"},
                method="POST",
            )
        with urllib.request.urlopen(req, timeout=10) as resp:
            return json.loads(resp.read().decode())
    except Exception as e:
        return {"error": str(e), "connected": False}


# ── Tool 1: Health ────────────────────────────────────────────────────────────

@mcp.tool()
def autocad_health() -> str:
    """
    Kiểm tra AutoCAD Bridge có đang chạy không (localhost:8766).
    Trả về status, app name, port.
    """
    result = _fetch("/health")
    if result.get("status") == "ok":
        return f"✅ Bridge đang chạy — {result.get('app','AutoCAD')} port {result.get('port',8766)}"
    elif "error" in result:
        return (
            f"❌ Không kết nối được: {result['error']}\n"
            "→ Hãy mở AutoCAD và load add-in bằng lệnh NETLOAD:\n"
            r"  C:\Users\liend\Dhcb Tools\build_v2\DhcbTools.AutoCAD.dll"
        )
    return f"⚠️ Phản hồi lạ: {result}"


# ── Tool 2: Open Panel ────────────────────────────────────────────────────────

@mcp.tool()
def autocad_open_panel() -> str:
    """
    Mở bảng điều khiển AutoCAD ngay trong chat Hermes.
    Bảng có đầy đủ: Query, Layers, AutoNumbering, Cleanup, Raw JSON.
    Trả về directive ::preview{} để Hermes nhúng widget vào chat.
    """
    # Đảm bảo panel.html tồn tại (copy từ embedded hoặc đọc từ file)
    panel_path = Path(PANEL_HTML)
    if not panel_path.exists():
        return (
            f"❌ Không tìm thấy panel.html tại {PANEL_HTML}\n"
            "→ Chạy: autocad_install_panel để cài lại."
        )

    # Trả về marker mà Hermes sẽ render thành widget nhúng
    abs_path = str(panel_path).replace("\\", "/")
    return f'::preview{{file="{abs_path}"}}'


# ── Tool 3: Query ─────────────────────────────────────────────────────────────

@mcp.tool()
def autocad_query(
    query_type: str,
    limit: int = 50,
) -> str:
    """
    Truy vấn thông tin từ bản vẽ AutoCAD đang mở.

    query_type: một trong: drawing_info, layers, blocks, inserts, entities,
                text, xrefs, layouts, stats
    limit: số lượng kết quả tối đa (áp dụng cho entities/inserts/text/blocks)
    """
    valid = {"drawing_info", "layers", "blocks", "inserts", "entities", "text", "xrefs", "layouts", "stats"}
    if query_type not in valid:
        return f"❌ query_type không hợp lệ. Chọn một trong: {', '.join(sorted(valid))}"

    config = {"limit": limit} if query_type in {"entities", "inserts", "text", "blocks"} else None
    body: dict[str, Any] = {"query": query_type}
    if config:
        body["config"] = config

    result = _fetch("/query", body)
    if "error" in result and not result.get("layers") and not result.get("count") and not result.get("filename"):
        return f"❌ Lỗi: {result['error']}"

    # Format gọn
    if query_type == "stats":
        total = result.get("totalEntities", 0)
        by_type = result.get("byType", [])
        lines = [f"📊 Tổng: {total:,} entities", ""]
        for t in by_type[:10]:
            lines.append(f"  {t['type']:30s} {t['count']:>6,}")
        return "\n".join(lines)

    if query_type == "drawing_info":
        r = result
        return (
            f"📄 File: {r.get('filename','?')}\n"
            f"   Version: {r.get('dwgVersion','?')}\n"
            f"   Units: {r.get('unitsName','?')}\n"
            f"   Layers: {r.get('layerCount','?')}\n"
            f"   Entities: {r.get('entityCount','?')}"
        )

    if query_type == "layers":
        layers = result.get("layers", [])
        count = result.get("count", len(layers))
        sample = layers[:5]
        lines = [f"🗂 {count} layers:"]
        for l in sample:
            status = ("OFF " if l.get("isOff") else "") + ("FRZ " if l.get("isFrozen") else "") + ("LCK" if l.get("isLocked") else "")
            lines.append(f"  {l['name']:<35} color={l.get('colorIndex'):>3}  {status}")
        if count > 5:
            lines.append(f"  ... và {count-5} layer khác")
        return "\n".join(lines)

    if query_type == "layouts":
        layouts = result.get("layouts", [])
        count = result.get("count", len(layouts))
        names = [l["name"] for l in layouts]
        return f"📋 {count} layouts: {', '.join(names)}"

    # Generic fallback — trả JSON
    return json.dumps(result, ensure_ascii=False, indent=2)[:2000]


# ── Tool 4: Execute ───────────────────────────────────────────────────────────

@mcp.tool()
def autocad_execute(
    command: str,
    block_name: str = "",
    attribute_tag: str = "",
    prefix: str = "",
    start_number: int = 1,
    step: int = 1,
    pad_width: int = 0,
    purge_unused: bool = True,
    audit_errors: bool = True,
    output_path: str = "",
    dry_run: bool = True,
) -> str:
    """
    Thực thi lệnh vào AutoCAD.

    command: AutoNumbering | DrawingCleanup | LayerExport | LayerImport
    dry_run: True = xem trước không ghi thật (mặc định True để an toàn)

    AutoNumbering params: block_name, attribute_tag, prefix, start_number, step, pad_width
    DrawingCleanup params: purge_unused, audit_errors
    LayerExport params: output_path (đường dẫn CSV đầu ra)
    """
    valid_cmds = {"AutoNumbering", "DrawingCleanup", "LayerExport", "LayerImport"}
    if command not in valid_cmds:
        return f"❌ command không hợp lệ. Chọn: {', '.join(sorted(valid_cmds))}"

    # Build config theo lệnh
    if command == "AutoNumbering":
        config: dict[str, Any] = {
            "blockName": block_name,
            "attributeTag": attribute_tag,
            "prefix": prefix,
            "startNumber": start_number,
            "step": step,
            "padWidth": pad_width,
            "dryRun": dry_run,
        }
    elif command == "DrawingCleanup":
        config = {
            "purgeUnused": purge_unused,
            "auditErrors": audit_errors,
            "dryRun": dry_run,
        }
    elif command == "LayerExport":
        if not output_path:
            output_path = "C:/Users/liend/AppData/Local/Temp/dhcb_layers_export.csv"
        config = {"outputPath": output_path}
    else:
        config = {}

    result = _fetch("/execute", {"command": command, "config": config})

    success = result.get("success", False)
    summary = result.get("summary", "")
    count = result.get("affectedCount", 0)
    messages = result.get("messages", [])
    errors = result.get("errors", [])

    icon = "✅" if success else "❌"
    dry_note = " [DRY RUN — chưa ghi thật]" if dry_run and command != "LayerExport" else ""

    lines = [f"{icon} {summary}{dry_note}", f"   Affected: {count}"]
    if messages:
        lines.append("   Chi tiết:")
        for m in messages[:10]:
            lines.append(f"     • {m}")
        if len(messages) > 10:
            lines.append(f"     ... và {len(messages)-10} dòng khác")
    if errors:
        lines.append("   Lỗi:")
        for e in errors[:5]:
            lines.append(f"     ✗ {e}")

    return "\n".join(lines)


# ── Tool 5: Layer Export nhanh ────────────────────────────────────────────────

@mcp.tool()
def autocad_export_layers(output_path: str = "") -> str:
    """
    Xuất toàn bộ layer ra file CSV.
    output_path: đường dẫn file CSV (mặc định: Temp/dhcb_layers_export.csv)
    """
    if not output_path:
        output_path = "C:/Users/liend/AppData/Local/Temp/dhcb_layers_export.csv"

    result = _fetch("/execute", {"command": "LayerExport", "config": {"outputPath": output_path}})
    if result.get("success"):
        count = result.get("affectedCount", 0)
        return f"✅ Xuất {count} layers → {output_path}"
    return f"❌ {result.get('summary', str(result))}"


if __name__ == "__main__":
    mcp.run()
