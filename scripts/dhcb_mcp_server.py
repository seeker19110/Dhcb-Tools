#!/usr/bin/env python3
"""
dhcb_mcp_server.py — MCP server (stdio, JSON-RPC 2.0) bọc HTTP Bridge của Revit/AutoCAD (mục 6.2).
Không dependency ngoài, chạy offline. Cho Claude Desktop / Claude Code / bất kỳ MCP client nào gọi lệnh DHCB.

  tools/list  — sinh từ GET /tools của Bridge (chính bảng CommandCatalog) + tool `query` và `chat`
  tools/call  — POST /execute (luôn ép dryRun:true trừ khi tham số confirm=true), /query, /chat
  AutoCAD: `query` kèm dwgPath đọc bản SAO của DWG bằng accoreconsole (DHCB_QUERY) — không cần mở AutoCAD
  Tốc độ: `queries` gộp nhiều truy vấn vào MỘT lần mở DWG, `dwgPaths` đọc nhiều file song song, kết quả đọc ngầm
  được nhớ theo (file, mtime, cỡ), tools/call đọc ngầm chạy song song; lệnh qua Bridge vẫn tuần tự đúng thứ tự gửi.

Cấu hình Claude Desktop: xem docs/ai-offline.md, mục "MCP với Claude Desktop / Claude Code"
(hoặc dùng gói .mcpb dựng bằng scripts/pack-mcpb.ps1 — không phải sửa file cấu hình nào).
"""

import collections
import json
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import time
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
try:
    import dhcb_agent  # cùng thư mục
except ImportError:  # pragma: no cover
    from scripts import dhcb_agent  # type: ignore

APP = (sys.argv[1] if len(sys.argv) > 1 else "revit").lower()
APP_NAMES = {"revit": "Revit", "autocad": "AutoCAD"}
if APP not in APP_NAMES:
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
    "sheets": ("SheetRename", "RevisionOnSheets", "SheetBatchCreate", "BatchExport", "SheetIndex"),
    "cleanup": ("RemoveUnusedViews", "StylePurge", "DrawingCleanup", "FamilyAudit", "LayerTranslate", "EntityLayerAssign", "PolylineImport", "EntityEdit", "BlockInsert", "ElementRename"),
    "check": ("HealthReport", "ParameterRuleCheck", "ClashDetection", "ConnectorChecker", "LayerStandardCheck", "XrefAudit", "DrawingCompare"),
    "mep": ("SleeveAuto", "ElevationTag", "HangerAuto", "PipeSplitter", "RouteFromLines", "DevicePlacement", "SizingProposal", "ApplySizing", "SystemColor", "SystemName", "FlowNumbering"),
    "project": ("ProjectInfo", "LevelSetup", "GridSetup", "FamilyLoader", "ProjectFromTemplate", "TransferStandards", "GridFromCsv", "GridExtract", "DrawingCombine", "XrefAttach"),
    "ai": ("CadLayerMap", "SpecToConfig", "ColorByParameter", "AutoNumbering", "AttributeIncrement", "TextReplace"),
}

# Giữ 2024-11-05: server chỉ có capability `tools` (không resources/prompts/logging, không
# tools/list_changed), là tập con hợp lệ của phiên bản này; phiên bản 2025-xx đưa thêm yêu cầu
# (ví dụ header MCP-Protocol-Version với transport HTTP, elicitation) mà mã ở đây chưa hiện thực.
# Client mới vẫn thương lượng được vì server trả phiên bản nó hỗ trợ trong `initialize`.
PROTOCOL_VERSION = "2024-11-05"

# Claude Desktop gọi tools/list ngay khi khởi động, thường là lúc Revit chưa mở — trước đây lúc đó
# server trả về DUY NHẤT tool query/chat và người dùng tưởng gói cài hỏng. Nay danh mục lệnh được
# nhớ lại sau mỗi lần lấy được, nên vẫn liệt kê đủ lệnh kèm ghi chú "chưa kết nối".
CATALOG_CACHE = os.path.join(
    os.environ["APPDATA"], "DHCB", f"tools-cache-{APP}.json")


def _load_catalog() -> tuple:
    """Trả (catalog, còn sống). Bridge chạy thì lấy mới và ghi cache; không thì đọc cache."""
    catalog = dhcb_agent.request(APP, "GET", "/tools")
    if _valid_catalog(catalog) and catalog.get("tools"):
        try:
            os.makedirs(os.path.dirname(CATALOG_CACHE), exist_ok=True)
            with open(CATALOG_CACHE, "w", encoding="utf-8") as f:
                json.dump(catalog, f, ensure_ascii=False)
        except OSError:
            pass  # Không ghi được cache thì thôi, lần sau lấy lại từ Bridge.
        return catalog, True

    try:
        with open(CATALOG_CACHE, encoding="utf-8") as f:
            cached = json.load(f)
        if _valid_catalog(cached):
            return cached, False
    except (OSError, ValueError):
        pass
    return ({**catalog, "tools": []} if "tools" in catalog else catalog) if isinstance(catalog, dict) else {"tools": []}, False


CATALOG_TTL_SECONDS = 5.0
_catalog_memo = None  # (hạn monotonic, catalog, còn sống)
_catalog_lock = threading.Lock()


def _catalog() -> tuple:
    """`_load_catalog` có nhớ ngắn hạn.

    Bridge chưa mở thì mỗi lần nối 127.0.0.1 bị từ chối mất ~2 s trên Windows; không nhớ thì tools/list và MỖI lệnh
    đều trả thêm 2 s đó chỉ để biết lại điều vừa biết. Hạn ngắn nên mở AutoCAD xong vài giây là thấy danh mục mới.
    """
    global _catalog_memo
    with _catalog_lock:
        memo = _catalog_memo
        if memo is not None and time.monotonic() < memo[0]:
            return memo[1], memo[2]
        catalog, live = _load_catalog()
        _catalog_memo = (time.monotonic() + CATALOG_TTL_SECONDS, catalog, live)
        return catalog, live


def _valid_catalog(catalog) -> bool:
    """Cache lỗi/cũ không được làm mất tools/list hoặc đoán lệnh ghi là lệnh đọc."""
    if not isinstance(catalog, dict) or not isinstance(catalog.get("tools"), list):
        return False
    for tool in catalog["tools"]:
        if not isinstance(tool, dict) or not isinstance(tool.get("name"), str) \
                or not isinstance(tool.get("description", ""), str) \
                or type(tool.get("writesModel", False)) is not bool:
            return False
        schema = tool.get("inputSchema", {})
        if not isinstance(schema, dict) or not isinstance(schema.get("properties", {}), dict) \
                or not all(isinstance(field, dict) for field in schema.get("properties", {}).values()):
            return False
    return True


QUERY_HELP = {
    "revit": (
        "Đọc ngữ cảnh Revit (không ghi). document_context trả định danh phiên model; document_info, levels, views, "
        "sheets, rooms, elements, families, warnings, links, stats; element_geometry (hộp bao/đường tâm/connector, "
        "params: elementIds hoặc categories), parameters_of (tham số của category — dùng trước khi dựng config), "
        "schedule_rows, snapshot (ảnh PNG của view), selection (truyền elementIds để ĐẶT lựa chọn), "
        "show_elements (zoom tới phần tử), active_view."
    ),
    "autocad": (
        "Đọc bản vẽ AutoCAD (không ghi): document_context, drawing_info, layers, blocks, inserts, entities, text, "
        "xrefs, layouts, stats, entity_geometry (params.handles), attributes_of, block_contents (chữ/line trong "
        "block lồng), selection, show_entities, active_layout, snapshot (ảnh PNG trả về dạng ảnh xem được; params.window "
        "[x1,y1,x2,y2] hoặc params.handles để khoanh vùng, imageWidth, source live|plot|thumbnail — chạy ngầm thì plot "
        "đúng trạng thái hiện tại). Lọc bằng params: layerContains, "
        "entityType, blockName, blockNameContains, textContains, limit, offset. Kèm dwgPath thì đọc ngầm bản sao "
        "của file DWG bằng accoreconsole — không cần mở AutoCAD (bỏ qua documentId; selection/show_entities/"
        "active_layout chỉ có khi AutoCAD mở). NHANH: cần nhiều truy vấn thì gửi một lần bằng queries "
        "[{query, params}] (đọc ngầm chỉ mở DWG một lần); nhiều file thì dwgPaths [..] chạy song song; kết quả đọc "
        "ngầm được nhớ tới khi file đổi."
    ),
}


def tool_list() -> list:
    tools = []
    catalog, live = _catalog()
    offline_note = "" if live else f" [{APP_NAMES[APP]} chưa mở — mở phần mềm rồi thử lại]"
    allowed = set(n.lower() for n in GROUPS.get(GROUP, ())) if GROUP else None
    for t in catalog.get("tools", []):
        if READ_ONLY and t.get("writesModel"):
            continue
        if allowed is not None and t["name"].lower() not in allowed:
            continue
        props = dict(t.get("inputSchema", {}).get("properties", {}))
        if t.get("writesModel"):
            props["confirm"] = {"type": "boolean", "description": "true = chạy THẬT (mặc định chỉ xem trước dryRun)"}
            props["documentId"] = {"type": "string", "description": "ID model từ kết quả preview đã duyệt"}
            props["previewToken"] = {"type": "string", "description": "Token preview đã duyệt; giữ nguyên khi gửi lại, không tự preview lại để vượt lỗi"}
        tools.append({
            "name": t["name"],
            "description": t.get("description", "")
                           + (" (sửa mô hình — mặc định xem trước)" if t.get("writesModel") else "")
                           + offline_note,
            "inputSchema": {"type": "object", "properties": props},
        })
    query_props = {
        "query": {"type": "string"}, "params": {"type": "object"},
        "queries": {"type": "array", "description": "nhiều truy vấn trong một lần gọi: tên truy vấn hoặc {query, params}",
                    "items": {"type": ["string", "object"]}},
        "documentId": {"type": "string", "description": "ID từ document_context; giữ nguyên giữa các trang"},
    }
    if APP == "autocad":
        query_props["dwgPath"] = {"type": "string",
                                  "description": "file .dwg để đọc ngầm bằng accoreconsole (không cần mở AutoCAD)"}
        query_props["dwgPaths"] = {"type": "array", "items": {"type": "string"},
                                   "description": "nhiều file .dwg, đọc ngầm song song"}
    tools.append({
        "name": "query",
        "description": QUERY_HELP[APP],
        "inputSchema": {"type": "object", "properties": query_props},
    })
    if allowed is not None and "chat" not in allowed:
        return tools
    tools.append({
        "name": "chat",
        "description": "Dịch câu tiếng Việt sang lệnh + config đề xuất (không chạy).",
        "inputSchema": {"type": "object", "properties": {"text": {"type": "string"}}, "required": ["text"]},
    })
    return tools


def _json_field_names(catalog: dict, name: str) -> set:
    """Tên field JSON thô của một lệnh, lấy từ schema chuẩn do Bridge công bố."""
    for tool in catalog.get("tools", []):
        if tool.get("name", "").lower() == name.lower():
            properties = tool.get("inputSchema", {}).get("properties", {})
            return {key for key, schema in properties.items() if schema.get("jsonEncoded") is True}
    return set()


# ---- Đọc DWG ngầm bằng accoreconsole (AutoCAD) --------------------------------------------------
ACAD_YEARS = (2027, 2026, 2025, 2024, 2023, 2022)
OFFLINE_TIMEOUT_SECONDS = 300


def _offline_host():
    """(accoreconsole.exe, DhcbTools.AutoCAD.Core.dll) của bản AutoCAD mới nhất có đủ cặp, hoặc (None, None).

    DHCB_ACCORECONSOLE / DHCB_ACAD_CORE_DLL ghi đè (ví dụ trỏ vào bản build trong repo)."""
    console_override = os.environ.get("DHCB_ACCORECONSOLE")
    plugin_override = os.environ.get("DHCB_ACAD_CORE_DLL")
    program_files = os.environ.get("ProgramFiles", r"C:\Program Files")
    for year in ACAD_YEARS:
        console = console_override or os.path.join(program_files, "Autodesk", f"AutoCAD {year}", "accoreconsole.exe")
        plugin = plugin_override or os.path.join(
            os.environ["APPDATA"], "Autodesk", "ApplicationPlugins", "DhcbTools.bundle", "Contents", str(year),
            "DhcbTools.AutoCAD.Core.dll")
        if os.path.isfile(console) and os.path.isfile(plugin):
            return console, plugin
    return None, None


def _console_text(raw: bytes) -> str:
    """accoreconsole ghi stdout UTF-16LE (có khi lẫn ANSI); bỏ NUL là đủ để dò thông báo lỗi."""
    return raw.decode("utf-8", errors="replace").replace("\x00", "")


MAX_BATCH_QUERIES = 32
MAX_BATCH_FILES = 64


def _parallel_default() -> int:
    """Số accoreconsole chạy cùng lúc: nửa số nhân, tối đa 10 (file lớn ăn ~1 GB RAM mỗi tiến trình)."""
    try:
        wanted = int(os.environ.get("DHCB_OFFLINE_PARALLEL", "0"))
    except ValueError:
        wanted = 0
    return wanted if wanted > 0 else max(2, min((os.cpu_count() or 4) // 2, 10))


OFFLINE_PARALLEL = _parallel_default()
_console_slots = threading.BoundedSemaphore(OFFLINE_PARALLEL)

# Kết quả đọc ngầm nhớ theo (file, mtime, cỡ, DLL, truy vấn): hỏi lại cùng câu trên file chưa đổi thì trả ngay thay vì
# mở lại DWG (2–10 s). Giữ chuỗi JSON nên mỗi lần trả là một bản riêng; trần theo byte, bỏ mục lâu chưa dùng nhất.
CACHE_MAX_BYTES = 64 * 1024 * 1024
_cache = collections.OrderedDict()
_cache_bytes = 0
_cache_lock = threading.Lock()


def _cache_key(dwg_path: str, plugin: str, query: str, params: dict):
    try:
        stat = os.stat(dwg_path)
        return (os.path.normcase(os.path.abspath(dwg_path)), stat.st_mtime_ns, stat.st_size,
                plugin, os.stat(plugin).st_mtime_ns, query, json.dumps(params, sort_keys=True, ensure_ascii=False))
    except (OSError, TypeError, ValueError):
        return None


def _cache_get(key):
    if key is None:
        return None
    with _cache_lock:
        text = _cache.get(key)
        if text is None:
            return None
        _cache.move_to_end(key)
    result = json.loads(text)
    if isinstance(result, dict):
        result["cached"] = True
    return result


def _cache_put(key, result) -> None:
    global _cache_bytes
    if key is None or _failed(result):
        return
    text = json.dumps(result, ensure_ascii=False)
    if len(text) > CACHE_MAX_BYTES // 4:
        return
    with _cache_lock:
        old = _cache.pop(key, None)
        if old is not None:
            _cache_bytes -= len(old)
        _cache[key] = text
        _cache_bytes += len(text)
        while _cache_bytes > CACHE_MAX_BYTES:
            _key, dropped = _cache.popitem(last=False)
            _cache_bytes -= len(dropped)


def _failed(result) -> bool:
    return isinstance(result, dict) and (result.get("success") is False or "error" in result)


def _run_console(dwg_path: str, console: str, plugin: str, requests: list) -> list:
    """Một phiên accoreconsole trên BẢN SAO của DWG, chạy lần lượt mọi truy vấn; trả kết quả theo đúng thứ tự."""
    with tempfile.TemporaryDirectory(prefix="dhcb-query-") as work:
        copy = os.path.join(work, "ban-sao.dwg")
        shutil.copyfile(dwg_path, copy)
        script_path = os.path.join(work, "query.scr")
        # Mỗi câu trả lời prompt một dòng, không bọc nháy (DHCB_QUERY đọc GetString AllowSpaces) — như AcadScriptGen.
        script = ["FILEDIA 0", "SECURELOAD 0", f'NETLOAD "{plugin}"']
        out_paths = []
        for index, (query, params) in enumerate(requests):
            suffix = f"-{index}" if index else ""
            query_path = os.path.join(work, f"query{suffix}.json")
            out_path = os.path.join(work, f"result{suffix}.json")
            with open(query_path, "w", encoding="utf-8") as f:
                json.dump({"query": query, "params": params}, f, ensure_ascii=False)
            script += ["DHCB_QUERY", query_path, out_path]
            out_paths.append(out_path)
        with open(script_path, "w", encoding="utf-8") as f:
            f.write("\n".join(script + ["QUIT Y"]) + "\n")
        try:
            with _console_slots:
                proc = subprocess.run(
                    # cwd = thư mục tạm: snapshot plot ghi plot.log vào thư mục hiện hành — không rải ra chỗ người dùng.
                    [console, "/i", copy, "/s", script_path, "/l", "en-US"], capture_output=True, cwd=work,
                    timeout=OFFLINE_TIMEOUT_SECONDS, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        except subprocess.TimeoutExpired:
            failure = {"success": False, "summary": f"accoreconsole quá {OFFLINE_TIMEOUT_SECONDS} s khi đọc {dwg_path}."}
            return [dict(failure) for _ in requests]
        results = []
        failure = None
        for out_path in out_paths:
            if os.path.isfile(out_path):
                with open(out_path, encoding="utf-8-sig") as f:
                    result = json.load(f)
                if isinstance(result, dict):
                    result.setdefault("source", "accoreconsole")
                results.append(result)
                continue
            if failure is None:
                output = _console_text(proc.stdout + proc.stderr)
                if "Unknown command" in output and "DHCB_QUERY" in output:
                    reason = f"{plugin} là bản cũ, chưa có lệnh DHCB_QUERY — cập nhật DHCB Tools."
                elif "Unable to load" in output:
                    reason = f"accoreconsole không NETLOAD được {plugin}."
                else:
                    reason = "accoreconsole không tạo file kết quả."
                failure = {"success": False, "summary": reason, "consoleTail": " ".join(output.split())[-400:]}
            results.append(dict(failure))
    return results


def _offline_batch(dwg_path: str, requests: list) -> list:
    """Kết quả của từng (query, params) trên một DWG: lấy từ bộ nhớ nếu file chưa đổi, phần còn lại chạy MỘT phiên."""
    if not os.path.isfile(dwg_path):
        return [{"success": False, "summary": f"Không thấy file DWG: {dwg_path}"} for _ in requests]
    console, plugin = _offline_host()
    if console is None:
        return [{"success": False, "summary": "Không tìm thấy accoreconsole.exe (AutoCAD 2022–2027) kèm "
                 "DhcbTools.AutoCAD.Core.dll trong bundle DHCB; cài DHCB Tools hoặc đặt DHCB_ACCORECONSOLE/"
                 "DHCB_ACAD_CORE_DLL."} for _ in requests]
    keys = [_cache_key(dwg_path, plugin, query, params) for query, params in requests]
    results = [_cache_get(key) for key in keys]
    missing = [i for i, result in enumerate(results) if result is None]
    if missing:
        fresh = _run_console(dwg_path, console, plugin, [requests[i] for i in missing])
        for i, result in zip(missing, fresh):
            results[i] = result
            _cache_put(keys[i], result)
    return results


def _offline_query(dwg_path: str, query: str, params: dict) -> dict:
    """Chạy truy vấn chỉ đọc trên BẢN SAO của DWG (file gốc không bị khoá, không bị sửa)."""
    return _offline_batch(dwg_path, [(query, params)])[0]


def _query_requests(arguments: dict):
    """Danh sách (query, params) từ `queries` (gộp) hoặc `query`/`params` (đơn); chuỗi = thông báo lỗi."""
    queries = arguments.get("queries")
    if queries is None:
        params = arguments.get("params") or {}
        if not isinstance(params, dict):
            return "params phải là object."
        return [(arguments.get("query", ""), params)]
    if not isinstance(queries, list) or not 0 < len(queries) <= MAX_BATCH_QUERIES:
        return f"queries phải là mảng 1–{MAX_BATCH_QUERIES} phần tử."
    requests = []
    for item in queries:
        if isinstance(item, str):
            item = {"query": item}
        if not isinstance(item, dict) or not isinstance(item.get("query"), str) \
                or not isinstance(item.get("params") or {}, dict):
            return "Mỗi phần tử của queries là tên truy vấn hoặc {query, params}."
        requests.append((item["query"], item.get("params") or {}))
    return requests


def _pack(requests: list, results: list) -> dict:
    return {"results": [{"query": query, "result": result} for (query, _params), result in zip(requests, results)]}


def _summarize(payload: dict, results: list) -> dict:
    """Ghi số truy vấn hỏng; hỏng TẤT CẢ thì cả lần gọi là lỗi (client đánh dấu isError)."""
    failed = sum(1 for result in results if _failed(result))
    if failed:
        payload["failed"] = failed
    if results and failed == len(results):
        payload["success"] = False
        payload["summary"] = "Mọi truy vấn đều lỗi — xem từng mục."
    return payload


def _query(arguments: dict) -> dict:
    requests = _query_requests(arguments)
    if isinstance(requests, str):
        return {"success": False, "summary": requests}
    batch = arguments.get("queries") is not None
    paths = arguments.get("dwgPaths") if APP == "autocad" else None
    single = arguments.get("dwgPath") if APP == "autocad" else None
    if paths or single:
        if paths is None:
            paths = [single]
        if not isinstance(paths, list) or len(paths) > MAX_BATCH_FILES \
                or not all(isinstance(path, str) and path for path in paths):
            return {"success": False, "summary": f"dwgPath phải là chuỗi; dwgPaths là mảng tối đa {MAX_BATCH_FILES} đường dẫn."}
        if arguments.get("dwgPaths") is None:
            results = _offline_batch(single, requests)
            return _summarize({"source": "accoreconsole", **_pack(requests, results)}, results) if batch else results[0]
        with ThreadPoolExecutor(max_workers=min(len(paths), OFFLINE_PARALLEL)) as pool:
            per_file = list(pool.map(lambda path: _offline_batch(path, requests), paths))
        files = [{"dwgPath": path, **(_pack(requests, results) if batch else {"result": results[0]})}
                 for path, results in zip(paths, per_file)]
        return _summarize({"source": "accoreconsole", "files": files}, [r for results in per_file for r in results])

    extra = {}
    if "documentId" in arguments:
        document_id = arguments["documentId"]
        if not isinstance(document_id, str) or not document_id.strip() or len(document_id) > 4096:
            return {"success": False, "summary": "documentId phải là chuỗi không rỗng, tối đa 4096 ký tự."}
        extra["documentId"] = document_id
    results = [dhcb_agent.request(APP, "POST", "/query", {"query": query, "params": params, **extra})
               for query, params in requests]
    return _summarize(_pack(requests, results), results) if batch else results[0]


def call_tool(name: str, arguments: dict) -> dict:
    if name == "query":
        return _query(arguments)
    if name == "chat":
        return dhcb_agent.request(APP, "POST", "/chat", {"text": arguments.get("text", "")})
    # Một lần lấy danh mục cho cả kiểm --read-only lẫn tên field JSON. Dùng _load_catalog (có cache) chứ
    # không gọi thẳng /tools: Bridge trục trặc nhất thời làm catalog rỗng thì lệnh ghi lọt qua --read-only.
    catalog, _live = _catalog()
    if READ_ONLY:
        # Chỉ chấp nhận lệnh đọc; lệnh ghi bị từ chối ngay tại server (không phụ thuộc client).
        tools = catalog.get("tools") or []
        if not tools:
            return {"success": False,
                    "summary": f"Server đang chạy --read-only nhưng chưa có danh mục lệnh để xác minh '{name}' "
                               "là lệnh đọc (Bridge chưa mở và chưa có cache) — từ chối cho an toàn."}
        readable = {t["name"].lower() for t in tools if not t.get("writesModel")}
        if name.lower() not in readable:
            return {"success": False, "summary": f"Server đang chạy --read-only; lệnh ghi hoặc chưa xác minh '{name}' bị chặn."}
    config = dict(arguments)
    confirm = config.pop("confirm", False)
    if type(confirm) is not bool:
        return {"success": False, "summary": "confirm phải là boolean true/false, không nhận chuỗi hoặc số."}
    # FieldKind.Json được Bridge công bố bằng jsonEncoded: true. Chỉ những field đó mới được
    # giải mã từ chuỗi JSON; text tự do như "{Discipline}-{Number}" phải đi nguyên vẹn.
    json_fields = _json_field_names(catalog, name)
    for key, value in list(config.items()):
        if key in json_fields and isinstance(value, str):
            try:
                config[key] = json.loads(value)
            except ValueError:
                pass
    # Lệnh nặng (SleeveAuto, AutoRoute, ClashDetection) vượt 30 s mặc định trên model thật;
    # server chặn trên ở 10 phút nên không sợ giữ hàng đợi mãi (giai đoạn 10.5).
    timeout_seconds = int(config.pop("timeoutSeconds", 0) or 0)
    # Nguyên tắc: AI chỉ đề xuất, kỹ sư xác nhận — không confirm thì luôn xem trước.
    config["dryRun"] = not confirm
    context = {}
    if "documentId" in config:
        context["document_id"] = config.pop("documentId")
    if "previewToken" in config:
        context["preview_token"] = config.pop("previewToken")
    return dhcb_agent.send(APP, name, config, timeout_seconds=timeout_seconds, **context)


MAX_IMAGE_BLOCKS = 4


def _split_images(result):
    """Tách ảnh (dict có ``base64`` + ``mimeType`` image/*) ra khối MCP ``image`` để client NHÌN được ảnh.

    Trước đây snapshot trả base64 nằm trong chuỗi JSON của khối ``text``: mô hình chỉ thấy một chuỗi ký tự dài
    vài trăm KB (tốn ngữ cảnh mà không "nhìn" được gì). Khối ``image`` theo đặc tả MCP được client đưa vào mô hình
    dưới dạng ảnh thật. Bản JSON giữ lại ``path``/``width``/``height`` và ghi chú thay cho base64 đã tách.
    """
    images = []

    def walk(node):
        if isinstance(node, dict):
            mime = node.get("mimeType")
            data = node.get("base64")
            if isinstance(mime, str) and mime.startswith("image/") and isinstance(data, str) and data:
                if len(images) < MAX_IMAGE_BLOCKS:
                    images.append({"type": "image", "data": data, "mimeType": mime})
                    node = dict(node, base64=None, imageBlock=len(images))
                return node
            return {k: walk(v) for k, v in node.items()}
        if isinstance(node, list):
            return [walk(v) for v in node]
        return node

    stripped = walk(result)
    return stripped, images


def tool_content(result):
    """Nội dung ``tools/call``: khối text JSON (đã bỏ base64) rồi tới các khối ảnh."""
    stripped, images = _split_images(result)
    # JSON gọn: bản thụt lề dài hơn ~50% (2.000 entity: 644 KB so với 430 KB) — mô hình trả tiền bằng ngữ cảnh và thời gian.
    return [{"type": "text", "text": json.dumps(stripped, ensure_ascii=False, separators=(",", ":"))}] + images


def respond(msg_id, result=None, error=None):
    payload = {"jsonrpc": "2.0", "id": msg_id}
    if error is not None:
        payload["error"] = error
    else:
        payload["result"] = result
    line = json.dumps(payload, ensure_ascii=False) + "\n"
    with _write_lock:
        sys.stdout.write(line)
        sys.stdout.flush()


_write_lock = threading.Lock()


def _is_offline_call(name, arguments: dict) -> bool:
    return name == "query" and APP == "autocad" and bool(arguments.get("dwgPath") or arguments.get("dwgPaths"))


def _answer_call(msg_id, name, arguments: dict) -> None:
    try:
        result = call_tool(name, arguments)
        is_error = isinstance(result, dict) and (("success" in result and (
            not result["success"] or bool(result.get("partialSuccess")) or bool(result.get("errors"))
        )) or "error" in result)
        respond(msg_id, {"content": tool_content(result), "isError": is_error})
    except Exception as ex:  # noqa: BLE001
        respond(msg_id, error={"code": -32000, "message": str(ex)})


def _stdin_lines():
    """Từng dòng JSON-RPC từ stdin, giải mã UTF-8 theo ĐÚNG đặc tả MCP.

    Trên Windows (Python < 3.15, không bật UTF-8 mode) stdin nối ống mang code page ANSI (cp1252/cp1258),
    trong khi Claude Desktop gửi UTF-8 thô. "Đ" (0x90) và "ờ" (0x9D) không có trong hai code page đó →
    UnicodeDecodeError làm sập server ngay câu tiếng Việt đầu tiên; các dấu khác thành mojibake và đi
    thẳng vào config lệnh (tiền tố "Tầng" ghi vào mô hình thành "Táº§ng"). Đọc byte rồi tự giải mã;
    dòng không phải UTF-8 thì bỏ như dòng JSON hỏng.
    """
    buffer = getattr(sys.stdin, "buffer", None)
    if buffer is None:  # stdin đã là luồng văn bản thuần (test thay bằng StringIO)
        yield from sys.stdin
        return
    for raw in buffer:
        try:
            yield raw.decode("utf-8")
        except UnicodeDecodeError:
            continue


def main():
    # Đọc ngầm (accoreconsole) chạy song song: 5 câu hỏi trên 5 DWG xong trong thời gian của câu chậm nhất.
    # Lệnh qua Bridge đi MỘT luồng, đúng thứ tự gửi — AutoCAD đằng nào cũng chạy tuần tự trên luồng UI, và
    # hai lệnh ghi không được đổi chỗ cho nhau. Vòng đọc stdin không bị chặn nên ping/tools/list vẫn trả ngay.
    offline_pool = ThreadPoolExecutor(max_workers=OFFLINE_PARALLEL, thread_name_prefix="dhcb-offline")
    bridge_pool = ThreadPoolExecutor(max_workers=1, thread_name_prefix="dhcb-bridge")
    try:
        _serve(offline_pool, bridge_pool)
    finally:
        # stdin đóng: chờ việc đã nhận trả lời xong rồi mới thoát, không bỏ rơi kết quả lệnh ghi.
        bridge_pool.shutdown(wait=True)
        offline_pool.shutdown(wait=True)


def _serve(offline_pool, bridge_pool):
    for line in _stdin_lines():
        line = line.strip()
        if not line:
            continue
        try:
            msg = json.loads(line)
        except json.JSONDecodeError:
            continue
        if not isinstance(msg, dict) or not isinstance(msg.get("method"), str):
            respond(None, error={"code": -32600, "message": "JSON-RPC request phải là object có method dạng chuỗi."})
            continue
        method = msg.get("method")
        msg_id = msg.get("id")
        params = msg.get("params", {})
        if not isinstance(params, dict):
            respond(msg_id, error={"code": -32602, "message": "params phải là JSON object."})
            continue

        if method == "initialize":
            respond(msg_id, {
                "protocolVersion": PROTOCOL_VERSION,
                "capabilities": {"tools": {}},
                "serverInfo": {"name": f"dhcb-{APP}" + ("-readonly" if READ_ONLY else "") + (f"-{GROUP}" if GROUP else ""), "version": "1.2.0"},
            })
        elif method == "notifications/initialized":
            continue
        elif method == "tools/list":
            try:
                respond(msg_id, {"tools": tool_list()})
            except Exception as ex:  # noqa: BLE001
                respond(msg_id, error={"code": -32000, "message": str(ex)})
        elif method == "tools/call":
            # tools/call là request cần kết quả. Notification không có id không được khởi chạy lệnh ghi.
            if "id" not in msg:
                continue
            if not isinstance(params.get("arguments", {}), dict):
                respond(msg_id, error={"code": -32602, "message": "arguments phải là JSON object."})
                continue
            name = params.get("name", "")
            arguments = params.get("arguments") or {}
            pool = offline_pool if _is_offline_call(name, arguments) else bridge_pool
            pool.submit(_answer_call, msg_id, name, arguments)
        elif method == "ping":
            respond(msg_id, {})
        elif msg_id is not None:
            respond(msg_id, error={"code": -32601, "message": f"Method không hỗ trợ: {method}"})


if __name__ == "__main__":
    main()
