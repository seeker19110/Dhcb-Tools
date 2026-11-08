"""
AutoCAD Tools MCP Server
Cung cấp tools để điều khiển AutoCAD qua HTTP bridge (127.0.0.1:8766).
Cài: hermes mcp add autocad-tools --command python --args <repo>/tools/autocad-mcp-server/server.py
"""

from __future__ import annotations
import atexit
import http.client
import json
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Any

from fastmcp import FastMCP

sys.path.insert(0, str(Path(__file__).resolve().parent))
import panel_api  # noqa: E402  — cùng thư mục; dùng chung whitelist + chuỗi xác nhận với gateway

BRIDGE_URL = panel_api.AUTOCAD_URL
PANEL_API_URL = f"http://{panel_api.HOST}:{panel_api.PORT}"
PANEL_HTML = str(Path(__file__).parent / "panel.html")
PANEL_API_SCRIPT = str(Path(__file__).parent / "panel_api.py")
DEFAULT_LAYER_CSV = str(Path(tempfile.gettempdir()) / "dhcb_layers_export.csv")

_gateway_process: subprocess.Popen | None = None


def _probe_panel_api() -> str:
    """'ours' = gateway của mình đang chạy · 'free' = port trống · 'foreign' = port bị thứ khác chiếm."""
    import urllib.error

    try:
        with panel_api.LOOPBACK.open(PANEL_API_URL + "/alive", timeout=2) as resp:
            try:
                data = json.loads(resp.read())
            except (ValueError, OSError, http.client.HTTPException):
                return "foreign"
            return "ours" if isinstance(data, dict) and data.get("panelApi") == "ok" else "foreign"
    except urllib.error.HTTPError:
        return "foreign"  # có server trả lời nhưng không phải /alive của gateway
    except http.client.HTTPException:
        return "foreign"
    except (urllib.error.URLError, OSError) as exc:
        return "foreign" if isinstance(getattr(exc, "reason", exc), (TimeoutError, ConnectionResetError, ConnectionAbortedError)) else "free"


def _stop_gateway() -> None:
    proc = _gateway_process
    if proc is None or proc.poll() is not None:
        return
    proc.terminate()
    try:
        proc.wait(timeout=3)
    except subprocess.TimeoutExpired:
        proc.kill()


def _ensure_panel_api() -> str | None:
    """Khởi động gateway CORS/AI khi cần (lần đầu mở panel), không phải lúc import.

    Trả None nếu gateway sẵn sàng, ngược lại trả thông báo lỗi. Port đang bị chương trình khác
    chiếm thì KHÔNG spawn thêm — spawn nữa chỉ sinh một tiến trình chết vì bind lỗi.
    """
    global _gateway_process
    import time

    state = _probe_panel_api()
    if state == "ours":
        return None
    if state == "foreign":
        return (
            f"Port {panel_api.PORT} đang bị một chương trình khác chiếm (không phải panel gateway). "
            "Tắt chương trình đó hoặc đổi PORT trong panel_api.py rồi thử lại."
        )

    creationflags = subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.CREATE_NO_WINDOW
    _gateway_process = subprocess.Popen(
        [sys.executable, PANEL_API_SCRIPT],
        cwd=str(Path(__file__).parent),
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        creationflags=creationflags,
        close_fds=True,
    )
    atexit.register(_stop_gateway)

    # Chờ sẵn sàng (tối đa 5 s) để lần gọi đầu không đua với gateway.
    for _ in range(25):
        time.sleep(0.2)
        if _probe_panel_api() == "ours":
            return None
    return "Gateway panel không lên được trong 5 giây — chạy tay `python panel_api.py` để xem lỗi."


mcp = FastMCP(
    name="autocad-tools",
    instructions=(
        "Công cụ điều khiển AutoCAD qua HTTP Bridge (port 8766). "
        "Trước khi dùng, dùng autocad_health để kiểm tra bridge có sống không. "
        "Tool autocad_open_panel mở bảng điều khiển ngay trong chat Hermes."
    ),
)


def _fetch(path: str, body: dict | None = None) -> dict:
    """Dùng chung transport: giữ kết quả một phần và id recovery trên mọi đường gọi."""
    return panel_api.fetch_autocad(path, body)


# ── Tool 1: Health ────────────────────────────────────────────────────────────

@mcp.tool()
def autocad_health() -> str:
    """
    Kiểm tra AutoCAD Bridge có đang chạy không (127.0.0.1:8766).
    Trả về status, app name, port.
    """
    result = _fetch("/health")
    if result.get("status") == "ok":
        return f"✅ Bridge đang chạy — {result.get('app','AutoCAD')} port {result.get('port',8766)}"
    elif "error" in result:
        return (
            f"❌ Không kết nối được: {result['error']}\n"
            "→ Hãy mở AutoCAD. Cài bằng installer thì plugin tự nạp; bản build tay thì gõ NETLOAD\n"
            "  và chọn <repo>\\src\\DhcbTools.AutoCAD\\bin\\<Cấu hình>\\<TFM>\\DhcbTools.AutoCAD.dll"
        )
    return f"⚠️ Phản hồi lạ: {result}"


@mcp.tool()
def autocad_progress(job_id: str) -> str:
    """Đọc lại kết quả job từ id của phản hồi timeout. Không gửi lại lệnh ghi."""
    try:
        path = panel_api.job_path("progress", job_id)
    except ValueError as exc:
        return f"❌ {exc}"
    result = _fetch(path)
    return json.dumps(result, ensure_ascii=False, indent=2)


@mcp.tool()
def autocad_cancel(job_id: str) -> str:
    """Yêu cầu hủy job đang chờ hoặc hỗ trợ hủy; tiếp tục hỏi progress tới khi kết thúc.

    Không hủy cưỡng bức transaction đã nhận chạy, không gửi lại execute.
    """
    try:
        path = panel_api.job_path("cancel", job_id)
    except ValueError as exc:
        return f"❌ {exc}"
    return json.dumps(_fetch(path, {}), ensure_ascii=False, indent=2)


# ── Tool 2: Open Panel ────────────────────────────────────────────────────────

@mcp.tool()
def autocad_open_panel() -> str:
    """
    Mở bảng điều khiển AutoCAD ngay trong chat Hermes.
    Bảng có đầy đủ: Query, Layers, AutoNumbering, Cleanup, Raw JSON.
    Trả về directive ::preview{} để Hermes nhúng widget vào chat.
    """
    panel_path = Path(PANEL_HTML)
    if not panel_path.exists():
        return (
            f"❌ Không tìm thấy panel.html tại {PANEL_HTML}\n"
            "→ Chép lại thư mục tools/autocad-mcp-server từ repo (panel.html nằm cạnh server.py)."
        )

    # Gateway chỉ cần cho panel — khởi động ở đây, không phải lúc import server.
    problem = _ensure_panel_api()
    if problem:
        return f"❌ {problem}"

    # Xem trước FILE KHỞI CHẠY (do gateway ghi sau khi bind, mang khoá ?k=), không phải panel.html: /panel đòi khoá
    # để tài khoản Windows khác trên cùng máy không lấy được token phiên — xem panel_api.LAUNCH_KEY.
    launch_file = panel_api.launch_file_path()
    if not launch_file.is_file():
        return (
            f"❌ Gateway panel đang chạy nhưng không có file khởi chạy {launch_file}.\n"
            "→ Có thể đó là gateway bản cũ (chưa có khoá khởi chạy) hoặc của tài khoản Windows khác: tắt tiến trình "
            f"`python panel_api.py` đang giữ port {panel_api.PORT} rồi gọi lại tool này."
        )

    # Trả về marker mà Hermes sẽ render thành widget nhúng
    abs_path = str(launch_file).replace("\\", "/")
    return f'::preview{{file="{abs_path}"}}'


# ── Tool 3: Query ─────────────────────────────────────────────────────────────

@mcp.tool()
def autocad_query(
    query_type: str,
    limit: int = 50,
    offset: int = 0,
    params: dict[str, Any] | None = None,
    document_id: str = "",
    as_json: bool = False,
) -> str:
    """
    Truy vấn thông tin từ bản vẽ AutoCAD đang mở.

    query_type: drawing_info, layers, blocks, inserts, entities, text, xrefs, layouts, stats,
                document_context, active_layout, entity_geometry, attributes_of, snapshot
    limit: số lượng kết quả mỗi trang, 0–10000; 0 = mặc định 2000
    offset: số kết quả phù hợp bỏ qua; dùng nextOffset của trang trước
    params: lọc layerContains, entityType, blockName, blockNameContains, textLayer, textContains;
            handles (hex) cho entity_geometry, blockName cho attributes_of; imageWidth/source cho snapshot.
    document_id: lấy từ document_context, giữ nguyên giữa các trang để chặn đổi DWG.
    as_json: trả đủ dữ liệu và metadata phân trang, kể cả layers/stats/drawing_info.
    """
    valid = panel_api.ALLOWED_QUERIES
    if not isinstance(query_type, str) or query_type not in valid:
        return f"❌ query_type không hợp lệ. Chọn một trong: {', '.join(sorted(valid))}"

    if params is not None and not isinstance(params, dict):
        return "❌ params phải là JSON object"
    config = dict(params or {})
    if query_type in {"entities", "inserts", "text", "blocks", "layers"}:
        config.setdefault("limit", limit)
        config.setdefault("offset", offset)
    body: dict[str, Any] = {"query": query_type}
    if config:
        body["config"] = config
    if document_id:
        body["documentId"] = document_id
    try:
        body = panel_api.prepare_bridge_payload("/query", body)
    except ValueError as exc:
        return f"❌ {exc}"

    result = _fetch("/query", body)
    if as_json:
        return json.dumps(result, ensure_ascii=False, indent=2)
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
            lines.append(f"  {l.get('name', ''):<35} color={l.get('colorIndex'):>3}  {status}")
        if count > 5:
            lines.append(f"  ... và {count-5} layer khác")
        if result.get("hasMore"):
            lines.append(f"  Trang tiếp theo: offset={result.get('nextOffset')}")
        return "\n".join(lines)

    if query_type == "layouts":
        layouts = result.get("layouts", [])
        count = result.get("count", len(layouts))
        names = [l.get("name", "") for l in layouts]
        return f"📋 {count} layouts: {', '.join(names)}"

    # Generic fallback — trả JSON
    return json.dumps(result, ensure_ascii=False, indent=2)


# ── Tool 4: Execute ───────────────────────────────────────────────────────────

def build_execute_payload(
    command: str,
    *,
    block_name: str = "",
    attribute_tag: str = "",
    prefix: str = "",
    start_number: int = 1,
    step: int = 1,
    pad_width: int = 0,
    purge_unused: bool = True,
    audit_errors: bool = True,
    output_path: str = "",
    input_path: str = "",
    create_missing: bool = False,
    dry_run: bool = True,
    confirm: str = "",
    document_id: str = "",
    preview_token: str = "",
    background: bool = False,
) -> dict[str, Any]:
    """Dựng payload /execute rồi đưa qua ĐÚNG bộ validate của gateway (panel_api).

    dry_run=False không có `confirm` khớp chuỗi trong panel_api.CONFIRMATIONS → ValueError.
    """
    if command not in panel_api.ALLOWED_COMMANDS:
        raise ValueError(f"command không hợp lệ. Chọn: {', '.join(sorted(panel_api.ALLOWED_COMMANDS))}")

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
        config = {"outputPath": output_path or DEFAULT_LAYER_CSV}
    else:  # LayerImport
        if not input_path:
            raise ValueError("LayerImport cần input_path (file CSV do LayerExport sinh ra, nằm trong thư mục tạm)")
        config = {"inputPath": input_path, "createMissing": create_missing, "dryRun": dry_run}

    payload: dict[str, Any] = {"command": command, "config": config}
    if type(background) is not bool:
        raise ValueError("background phải là boolean")
    if background:
        payload["async"] = True
    if document_id:
        payload["documentId"] = document_id
    if preview_token:
        payload["previewToken"] = preview_token
    if confirm:
        payload["confirmation"] = confirm
    return panel_api.prepare_bridge_payload("/execute", payload)


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
    input_path: str = "",
    create_missing: bool = False,
    dry_run: bool = True,
    confirm: str = "",
    document_id: str = "",
    preview_token: str = "",
    background: bool = False,
) -> str:
    """
    Thực thi lệnh vào AutoCAD.

    command: AutoNumbering | DrawingCleanup | LayerExport | LayerImport
    dry_run: True = xem trước không ghi thật (mặc định True để an toàn)
    document_id, preview_token: lấy từ kết quả preview đã duyệt; giữ nguyên khi gửi lại lần ghi.
    background: trả job_id ngay cho lệnh dài; dùng autocad_progress/autocad_cancel theo dõi và yêu cầu hủy.
    confirm: BẮT BUỘC khi dry_run=False — chuỗi xác nhận theo lệnh:
             DrawingCleanup → "DELETE_UNUSED", AutoNumbering → "WRITE_AUTONUMBER",
             LayerImport → "IMPORT_LAYERS". Chỉ truyền sau khi kỹ sư đã xem trước và đồng ý.

    AutoNumbering params: block_name, attribute_tag, prefix, start_number, step, pad_width
    DrawingCleanup params: purge_unused, audit_errors
    LayerExport params: output_path (CSV; mặc định <Temp>/dhcb_layers_export.csv)
    LayerImport params: input_path (CSV, bắt buộc), create_missing, dry_run
    """
    try:
        payload = build_execute_payload(
            command,
            block_name=block_name, attribute_tag=attribute_tag, prefix=prefix,
            start_number=start_number, step=step, pad_width=pad_width,
            purge_unused=purge_unused, audit_errors=audit_errors,
            output_path=output_path, input_path=input_path, create_missing=create_missing,
            dry_run=dry_run, confirm=confirm, document_id=document_id, preview_token=preview_token,
            background=background,
        )
    except ValueError as exc:
        return f"❌ {exc}"

    result = _fetch("/execute", payload)
    if background and result.get("id"):
        return json.dumps(result, ensure_ascii=False, indent=2)

    success = result.get("success", False)
    summary = result.get("summary", "")
    count = result.get("affectedCount", 0)
    messages = result.get("messages", [])
    errors = result.get("errors", [])

    icon = "❌" if not success else "⚠️" if result.get("partialSuccess") or errors else "✅"
    dry_note = " [DRY RUN — chưa ghi thật]" if dry_run and command != "LayerExport" else ""

    lines = [f"{icon} {summary}{dry_note}", f"   Affected: {count}"]
    if result.get("id"):
        lines.append(f"   job_id: {result['id']} — dùng autocad_progress để đọc kết quả; giữ nguyên preview_token.")
    if result.get("changedIds"):
        lines.append("   Changed: " + ", ".join(str(value) for value in result["changedIds"][:20]))
    if result.get("previewToken"):
        lines.append(f"   document_id: {result['documentId']} · preview_token: {result['previewToken']}")
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
    output_path: đường dẫn file CSV (mặc định: <Temp>/dhcb_layers_export.csv; phải nằm trong thư mục tạm)
    """
    try:
        payload = build_execute_payload("LayerExport", output_path=output_path)
    except ValueError as exc:
        return f"❌ {exc}"
    output_path = payload["config"]["outputPath"]

    result = _fetch("/execute", payload)
    if result.get("success") and not result.get("partialSuccess") and not result.get("errors"):
        count = result.get("affectedCount", 0)
        return f"✅ Xuất {count} layers → {output_path}"
    return f"❌ {result.get('summary', str(result))}"


if __name__ == "__main__":
    mcp.run()
