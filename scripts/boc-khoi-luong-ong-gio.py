#!/usr/bin/env python3
"""Bóc khối lượng ống gió chữ nhật từ bản vẽ AutoCAD 2D → Excel (mét dài + m² tôn), đánh số từng đoạn.

Bản vẽ ĐHKK xuất từ Revit sang DWG không còn đối tượng ống: mỗi đoạn ống chỉ là **hai đường thẳng song song**
trên layer `M-HVAC-DUCT*`, kích thước nằm ở tag chữ (`SEAD - 700x300`), phụ kiện là block mang tên family
Revit (`VHT_M_DF_Rec_C_Elbow_Square - …`). Script dựng lại ống từ ba thứ đó:

1. lấy Line trên layer ống trong khung mặt bằng (bỏ layer CNTR/ANNO/IDEN — tim ống, ghi chú, tag);
2. ghép cặp hai cạnh song song cách nhau 80–2000 mm, chồng nhau ≥ 60 % → một đoạn ống, dài = phần chồng;
3. gán R×C theo tag ống (hệ kết thúc bằng `AD`: SEAD/SAD/RAD/FAD/EAD/PAD) gần nhất có một cạnh khớp khoảng
   cách hai cạnh (±30 mm, trong 8 m) — miệng gió/van (OBD, PAG, SAG…) không phải ống nên không dùng làm tag;
4. đếm block phụ kiện theo family;
5. ghi .xlsx: Tổng hợp (theo hệ + kích thước, công thức `2×(R+C)×L`, ô hao hụt), Chi tiết (STT, mã đoạn
   `SEAD-001`…), Phụ kiện, Chưa xác định (cặp đường không gán được — kỹ sư kiểm tay), Ghi chú.

Chỉ ĐỌC bản vẽ. Kết quả là ước lượng có kiểm được: mỗi dòng chi tiết mang handle hai cạnh để tra lại.

Cách dùng (bản vẽ đang mở trong AutoCAD, Bridge 8766 đang bật):
    python scripts/boc-khoi-luong-ong-gio.py --bbox -170000,300000,-40000,340000 -o KL-ong-gio.xlsx

Hoặc từ JSON đã lưu (đầu ra `query entities` / `query text` / `entity_geometry`):
    python scripts/boc-khoi-luong-ong-gio.py --entities e.json --texts t.json [--geometry g.json] -o kl.xlsx

Mã thoát 0 = đã ghi file · 1 = không tìm thấy đoạn ống nào · 2 = đầu vào hỏng/không kết nối được Bridge.
"""

from __future__ import annotations

import argparse
import collections
import json
import math
import os
import re
import sys
import time
import zipfile
from pathlib import Path, PureWindowsPath
from xml.sax.saxutils import escape

SYSTEMS = {
    "SEAD": "Hút khói (Smoke Exhaust Air Duct)", "SAD": "Gió cấp (Supply Air Duct)",
    "RAD": "Gió hồi (Return Air Duct)", "FAD": "Gió tươi (Fresh Air Duct)",
    "EAD": "Gió thải (Exhaust Air Duct)", "PAD": "Tạo áp (Pressurization Air Duct)",
}
ORDER = list(SYSTEMS)
FITTINGS = {
    "Rec_TDC_Union": "Mối nối bích TDC", "Rec_C_Union": "Mối nối (C)", "Rec_C_Elbow_Square": "Cút vuông 90°",
    "Rec_C_Elbow_45": "Cút 45°", "Rec_Tap_Round": "Tê/chân rẽ tròn", "Rec_Cap_End": "Nắp bịt đầu ống",
    "Rec_C_Transition_Round": "Côn chữ nhật → tròn", "Rec_C_Transition": "Côn thu (C)",
    "Rec_TDC_Transition": "Côn thu bích TDC", "Rec_C_Tap": "Chân rẽ chữ nhật (C)",
    "Rec_TDC_Tap": "Chân rẽ chữ nhật bích TDC", "MotorizedFireDamper": "Van chặn lửa có motor",
    "REC_Flexible": "Ống mềm",
}
SKIP_LAYER = ("CNTR", "ANNO", "IDEN")
TAG = re.compile(r"\b([A-Z]{2,4})\s*-\s*(\d{2,4})\s*[xX×]\s*(\d{2,4})")
FAMILY = re.compile(r"(Rec_\w+|MotorizedFireDamper|REC_Flexible)")


# ── Đọc dữ liệu ──────────────────────────────────────────────────────────────

def clean_mtext(text: str) -> str:
    """Bỏ mã định dạng MText (`\\pxql;`, `\\fArial|b1;`, `{}`), `\\P` → ` | `."""
    text = re.sub(r"\\p[^;]*;", "", text)
    text = re.sub(r"\\[A-OQ-Za-z][^;\\{}]*;", "", text)
    text = text.replace("\\P", " | ")
    return re.sub(r"\s+", " ", re.sub(r"[{}]", "", text)).strip()


def in_box(x: float, y: float, bbox) -> bool:
    return bbox is None or (bbox[0] <= x <= bbox[2] and bbox[1] <= y <= bbox[3])


def parse_tags(texts: list, bbox=None) -> list:
    """Tag ống gió `HỆ - RxC`; chỉ hệ kết thúc bằng AD (ống), bỏ chi tiết xref ghép (`…$0$…`)."""
    tags = []
    for t in texts:
        if "$0$" in t.get("layer", ""):
            continue
        m = TAG.search(clean_mtext(t.get("text", "")))
        if not m or not m.group(1).endswith("AD") or not in_box(t["x"], t["y"], bbox):
            continue
        tags.append({"sys": m.group(1), "W": int(m.group(2)), "H": int(m.group(3)), "x": t["x"], "y": t["y"]})
    return tags


def duct_segments(entities: list, bbox=None, prefix: str = "M-HVAC-DUCT") -> list:
    segs = []
    for e in entities:
        layer = e.get("layer", "")
        if e.get("type") != "Line" or not layer.startswith(prefix) or any(k in layer for k in SKIP_LAYER):
            continue
        a, b = e["start"], e["end"]
        if not in_box(a["x"], a["y"], bbox):
            continue
        length = math.hypot(b["x"] - a["x"], b["y"] - a["y"])
        if length >= 50:
            segs.append({"handle": e["handle"], "layer": layer, "x1": a["x"], "y1": a["y"],
                         "x2": b["x"], "y2": b["y"], "len": length})
    return segs


# ── Hình học ─────────────────────────────────────────────────────────────────

def direction(s: dict) -> tuple:
    ux, uy = (s["x2"] - s["x1"]) / s["len"], (s["y2"] - s["y1"]) / s["len"]
    if ux < -1e-9 or (abs(ux) < 1e-9 and uy < 0):
        ux, uy = -ux, -uy
    return ux, uy


def pair_segments(segs: list, min_w: float = 80, max_w: float = 2000, min_overlap: float = 0.6) -> list:
    """Mỗi cạnh ghép với cạnh song song gần nhất phía sau nó trong danh sách → đoạn ống."""
    pairs = []
    dirs = [direction(s) for s in segs]
    for i, s in enumerate(segs):
        ux, uy = dirs[i]
        best = None
        for j in range(i + 1, len(segs)):
            t = segs[j]
            if abs(ux * dirs[j][1] - uy * dirs[j][0]) > 0.01:
                continue
            width = abs((t["x1"] - s["x1"]) * -uy + (t["y1"] - s["y1"]) * ux)
            if not min_w <= width <= max_w:
                continue
            proj = [(x - s["x1"]) * ux + (y - s["y1"]) * uy
                    for x, y in ((s["x1"], s["y1"]), (s["x2"], s["y2"]), (t["x1"], t["y1"]), (t["x2"], t["y2"]))]
            overlap = min(max(proj[:2]), max(proj[2:])) - max(min(proj[:2]), min(proj[2:]))
            if overlap < min_overlap * min(s["len"], t["len"]):
                continue
            if best is None or width < best[0]:
                best = (width, t, overlap)
        if best:
            width, t, overlap = best
            pairs.append({"w": round(width), "len": overlap, "a": s["handle"], "b": t["handle"], "layer": s["layer"],
                          "mid": [(s["x1"] + s["x2"] + t["x1"] + t["x2"]) / 4, (s["y1"] + s["y2"] + t["y1"] + t["y2"]) / 4]})
    return pairs


def assign_sizes(pairs: list, tags: list, tol: float = 30, radius: float = 8000) -> list:
    out = []
    for p in pairs:
        best = None
        for t in tags:
            if abs(t["W"] - p["w"]) <= tol or abs(t["H"] - p["w"]) <= tol:
                d = math.hypot(t["x"] - p["mid"][0], t["y"] - p["mid"][1])
                if best is None or d < best[0]:
                    best = (d, t)
        if best and best[0] <= radius:
            out.append({**p, "sys": best[1]["sys"], "W": best[1]["W"], "H": best[1]["H"], "tagDist": round(best[0])})
        else:
            out.append({**p, "sys": None})
    return out


def number_segments(assigned: list) -> list:
    """Sắp theo hệ → tiết diện lớn trước → vị trí (trên→dưới theo dải 2 m, trái→phải), gán STT và mã `HỆ-001`."""
    ok = [a for a in assigned if a["sys"]]
    ok.sort(key=lambda a: (ORDER.index(a["sys"]) if a["sys"] in ORDER else len(ORDER), -a["W"] * a["H"],
                           a["W"], a["H"], -round(a["mid"][1] / 2000), a["mid"][0]))
    counter = collections.Counter()
    for n, a in enumerate(ok, 1):
        counter[a["sys"]] += 1
        a["stt"], a["code"] = n, f"{a['sys']}-{counter[a['sys']]:03d}"
    return ok


def count_fittings(geometry: list, bbox=None, prefix: str = "M-HVAC-DUCT") -> collections.Counter:
    c = collections.Counter()
    for e in geometry:
        p = e.get("position")
        if e.get("type") != "BlockReference" or not e.get("layer", "").startswith(prefix) or not p:
            continue
        if not in_box(p["x"], p["y"], bbox):
            continue
        family = re.split(r"\s+-\s+", e.get("blockName") or "")[0]
        m = FAMILY.search(family)
        c[m.group(1) if m else family] += 1
    return c


# ── Ghi .xlsx (thư viện chuẩn, không cần openpyxl) ──────────────────────────

class F(str):
    """Ô công thức (không có dấu `=` đầu)."""


def col_name(n: int) -> str:
    s = ""
    while n:
        n, r = divmod(n - 1, 26)
        s = chr(65 + r) + s
    return s


def sheet_xml(rows: list, widths: list, styles: dict) -> str:
    """rows: list các list giá trị (str | int | float | F | None). styles: {số dòng (1-based): id style}."""
    cols = "".join(f'<col min="{i}" max="{i}" width="{w}" customWidth="1"/>' for i, w in enumerate(widths, 1))
    out = []
    for r, row in enumerate(rows, 1):
        st = styles.get(r, 0)
        cells = []
        for c, v in enumerate(row, 1):
            ref = f"{col_name(c)}{r}"
            s = f' s="{st}"' if st else ""
            if v is None:
                continue
            if isinstance(v, F):
                cells.append(f"<c r=\"{ref}\"{s}><f>{escape(v)}</f></c>")
            elif isinstance(v, (int, float)):
                cells.append(f"<c r=\"{ref}\"{s}><v>{v}</v></c>")
            else:
                cells.append(f"<c r=\"{ref}\"{s} t=\"inlineStr\"><is><t xml:space=\"preserve\">{escape(str(v))}</t></is></c>")
        out.append(f'<row r="{r}">{"".join(cells)}</row>')
    return ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
            f'<cols>{cols}</cols><sheetData>{"".join(out)}</sheetData></worksheet>')


STYLES = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
          '<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
          '<numFmts count="1"><numFmt numFmtId="164" formatCode="#,##0.00"/></numFmts>'
          '<fonts count="3"><font><sz val="11"/><name val="Calibri"/></font>'
          '<font><b/><sz val="11"/><name val="Calibri"/></font>'
          '<font><b/><sz val="11"/><color rgb="FFFFFFFF"/><name val="Calibri"/></font></fonts>'
          '<fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill>'
          '<fill><patternFill patternType="solid"><fgColor rgb="FF1F4E78"/></patternFill></fill></fills>'
          '<borders count="1"><border/></borders><cellStyleXfs count="1"><xf/></cellStyleXfs>'
          '<cellXfs count="4"><xf/><xf fontId="1" applyFont="1"/>'
          '<xf fontId="2" fillId="2" applyFont="1" applyFill="1"/>'
          '<xf numFmtId="164" fontId="1" applyNumberFormat="1" applyFont="1"/></cellXfs>'
          '<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>')
BOLD, HEAD, TOTAL = 1, 2, 3


def write_xlsx(path: Path, sheets: list) -> None:
    """sheets: list (tên, rows, widths, styles)."""
    n = len(sheets)
    ct = "".join(f'<Override PartName="/xl/worksheets/sheet{i}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
                 for i in range(1, n + 1))
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml",
                   '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
                   '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                   '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
                   '<Default Extension="xml" ContentType="application/xml"/>'
                   '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
                   '<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>'
                   f'{ct}</Types>')
        z.writestr("_rels/.rels",
                   '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
                   '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                   '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>'
                   '</Relationships>')
        z.writestr("xl/workbook.xml",
                   '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
                   '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
                   'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>'
                   + "".join(f'<sheet name="{escape(s[0])}" sheetId="{i}" r:id="rId{i}"/>' for i, s in enumerate(sheets, 1))
                   + '</sheets><calcPr fullCalcOnLoad="1"/></workbook>')
        z.writestr("xl/_rels/workbook.xml.rels",
                   '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
                   '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                   + "".join(f'<Relationship Id="rId{i}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i}.xml"/>'
                             for i in range(1, n + 1))
                   + f'<Relationship Id="rId{n + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>'
                   '</Relationships>')
        z.writestr("xl/styles.xml", STYLES)
        for i, (_, rows, widths, styles) in enumerate(sheets, 1):
            z.writestr(f"xl/worksheets/sheet{i}.xml", sheet_xml(rows, widths, styles))


def build_sheets(numbered: list, unassigned: list, fittings: collections.Counter, source: str, waste: float) -> list:
    # Chi tiết — dòng 1 tiêu đề, dòng 3 header, dữ liệu từ dòng 4.
    det = [["CHI TIẾT TỪNG ĐOẠN ỐNG GIÓ — đánh số theo hệ, tiết diện, vị trí"], [],
           ["STT", "Mã đoạn", "Hệ", "Kích thước", "R (mm)", "C (mm)", "Dài (m)", "Chu vi (m)", "Diện tích (m²)",
            "X tâm", "Y tâm", "Layer", "Handle cạnh 1", "Handle cạnh 2", "Cách tag (mm)"]]
    for a in numbered:
        r = len(det) + 1
        det.append([a["stt"], a["code"], a["sys"], f"{a['W']}x{a['H']}", a["W"], a["H"], round(a["len"] / 1000, 3),
                    F(f"2*(E{r}+F{r})/1000"), F(f"ROUND(G{r}*H{r},3)"), round(a["mid"][0]), round(a["mid"][1]),
                    a["layer"], a["a"], a["b"], a["tagDist"]])
    last = len(det)
    det.append([None] * 5 + ["Tổng", F(f"SUM(G4:G{last})"), None, F(f"SUM(I4:I{last})")])

    # Tổng hợp — header dòng 5, dữ liệu từ dòng 6.
    tot = [["BẢNG KHỐI LƯỢNG ỐNG GIÓ"], [f"Nguồn: {source}"],
           [None] * 7 + ["Hao hụt", waste], [],
           ["STT", "Hệ thống", "Mã", "Kích thước R×C (mm)", "R (mm)", "C (mm)", "Chiều dài (m)", "Chu vi (m)",
            "Diện tích tôn (m²)", "DT có hao hụt (m²)", "Số đoạn"]]
    styles_tot = {1: BOLD, 5: HEAD}
    groups = list(collections.OrderedDict.fromkeys((a["sys"], a["W"], a["H"]) for a in numbered))
    cur, n = None, 0
    for s, w, h in groups:
        if s != cur:
            cur = s
            tot.append([None, SYSTEMS.get(s, s)])
            styles_tot[len(tot)] = BOLD
        n += 1
        r = len(tot) + 1
        tot.append([n, SYSTEMS.get(s, s), s, f"{w}x{h}", w, h,
                    F(f"SUMIFS('Chi tiết'!G:G,'Chi tiết'!C:C,C{r},'Chi tiết'!D:D,D{r})"), F(f"2*(E{r}+F{r})/1000"),
                    F(f"ROUND(G{r}*H{r},2)"), F(f"ROUND(I{r}*(1+$I$3),2)"),
                    F(f"COUNTIFS('Chi tiết'!C:C,C{r},'Chi tiết'!D:D,D{r})")])
    end = len(tot)
    tot.append([None, "TỔNG CỘNG", None, None, None, None, F(f"SUM(G6:G{end})"), None,
                F(f"SUM(I6:I{end})"), F(f"SUM(J6:J{end})"), F(f"SUM(K6:K{end})")])
    styles_tot[len(tot)] = TOTAL
    tot += [[], [None, "Diện tích tôn = 2 × (R + C) × chiều dài. Đổi ô I3 để thay tỉ lệ hao hụt."]]

    fit = [["THỐNG KÊ PHỤ KIỆN ỐNG GIÓ (đếm block trên layer ống)"], [],
           ["STT", "Loại phụ kiện", "Family Revit", "Số lượng", "ĐVT"]]
    for i, (k, v) in enumerate(fittings.most_common(), 1):
        fit.append([i, FITTINGS.get(k, k), k, v, "cái"])
    fit.append([None, "Tổng", None, F(f"SUM(D4:D{len(fit)})")])

    un = [["CẶP ĐƯỜNG SONG SONG CHƯA GÁN ĐƯỢC KÍCH THƯỚC — kiểm tay (côn, cút, chạc, cách nhiệt…)"], [],
          ["STT", "Khoảng cách 2 cạnh (mm)", "Dài (m)", "X tâm", "Y tâm", "Layer", "Handle cạnh 1", "Handle cạnh 2"]]
    for i, a in enumerate(sorted(unassigned, key=lambda a: (a["w"], a["mid"][0])), 1):
        un.append([i, a["w"], round(a["len"] / 1000, 3), round(a["mid"][0]), round(a["mid"][1]), a["layer"], a["a"], a["b"]])
    un.append([None, "Tổng", F(f"SUM(C4:C{len(un)})")])

    notes = [["CÁCH TÍNH"],
             ["1. Hai cạnh Line song song trên layer ống (bỏ CNTR/ANNO/IDEN), cách 80–2000 mm, chồng ≥ 60 % → một đoạn; dài = phần chồng."],
             ["2. R×C lấy từ tag ống (SEAD/SAD/RAD/FAD/EAD/PAD - RxC) gần nhất có một cạnh khớp khoảng cách hai cạnh (±30 mm, ≤ 8 m)."],
             ["3. Diện tích tôn = 2 × (R + C) / 1000 × chiều dài (m²); hao hụt theo ô I3 trang Tổng hợp."],
             [], ["GIỚI HẠN — kỹ sư kiểm trước khi dùng"],
             ["• Không tính ống vẽ bằng polyline/spline/trong block, chi tiết xref ghép, khung ngoài vùng --bbox."],
             ["• Phụ kiện chỉ đếm số lượng; diện tích tôn phụ kiện chưa cộng. Cặp không gán được ở trang Chưa xác định."],
             ["• Hai tiết diện cùng bề rộng nằm sát nhau có thể gán nhầm — xem cột Cách tag; cặp ~100 mm có thể là viền/cách nhiệt."]]
    return [("Tổng hợp", tot, [6, 34, 8, 18, 9, 9, 13, 11, 16, 17, 9], styles_tot),
            ("Chi tiết", det, [6, 11, 8, 12, 8, 8, 9, 10, 12, 12, 12, 16, 13, 13, 12], {1: BOLD, 3: HEAD, len(det): TOTAL}),
            ("Phụ kiện", fit, [6, 28, 30, 10, 7], {1: BOLD, 3: HEAD, len(fit): TOTAL}),
            ("Chưa xác định", un, [6, 14, 10, 12, 12, 16, 13, 13], {1: BOLD, 3: HEAD, len(un): TOTAL}),
            ("Ghi chú", notes, [130], {1: BOLD, 6: BOLD})]


# ── Bridge ───────────────────────────────────────────────────────────────────

def fetch_live(agent, app: str = "autocad", chunk: int = 25, retries: int = 3, pause: float = 2.0) -> tuple:
    """Đọc bản vẽ đang mở: entities (phân trang), text, rồi entity_geometry cho block trên layer ống."""
    entities, offset = [], 0
    while offset is not None:
        r = agent.request(app, "POST", "/query", {"query": "entities", "params": {"limit": 2000, "offset": offset}}, timeout=120)
        if "entities" not in r:
            raise RuntimeError(r.get("error") or "query entities không trả dữ liệu")
        entities += r["entities"]
        offset = r.get("nextOffset") if r.get("hasMore") else None
    t = agent.request(app, "POST", "/query", {"query": "text", "params": {"limit": 5000}}, timeout=120)
    if "texts" not in t:
        raise RuntimeError(t.get("error") or "query text không trả dữ liệu")
    handles = [e["handle"] for e in entities if e.get("type") == "BlockReference" and "DUCT" in e.get("layer", "").upper()]
    geometry = []
    for i in range(0, len(handles), chunk):
        for _ in range(retries):
            g = agent.request(app, "POST", "/query", {"query": "entity_geometry", "params": {"handles": handles[i:i + chunk]}}, timeout=120)
            if "entities" in g:
                geometry += g["entities"]
                break
            time.sleep(pause)
    info = agent.request(app, "POST", "/query", {"query": "drawing_info"}, timeout=60)
    # Đường dẫn do AutoCAD (Windows) trả về; PureWindowsPath tách được cả khi script chạy trên Linux/WSL.
    return entities, t["texts"], geometry, PureWindowsPath(info.get("filename") or "").name


def load_json(path: str, key: str = "") -> list:
    data = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    return data[key] if key and isinstance(data, dict) else data


def parse_bbox(text: str):
    parts = [float(v) for v in text.split(",")]
    if len(parts) != 4:
        raise ValueError("--bbox cần 4 số: xmin,ymin,xmax,ymax")
    return (min(parts[0], parts[2]), min(parts[1], parts[3]), max(parts[0], parts[2]), max(parts[1], parts[3]))


def main(argv=None, agent=None) -> int:
    ap = argparse.ArgumentParser(description="Bóc khối lượng ống gió từ bản vẽ AutoCAD → Excel")
    ap.add_argument("-o", "--output", required=True, help="file .xlsx")
    ap.add_argument("--bbox", help="khung mặt bằng xmin,ymin,xmax,ymax (đơn vị bản vẽ) — nên luôn truyền")
    ap.add_argument("--layer-prefix", default="M-HVAC-DUCT")
    ap.add_argument("--waste", type=float, default=0.05, help="tỉ lệ hao hụt tôn (mặc định 0.05)")
    ap.add_argument("--entities", help="JSON đầu ra query entities (bỏ trống = đọc trực tiếp qua Bridge)")
    ap.add_argument("--texts", help="JSON đầu ra query text")
    ap.add_argument("--geometry", help="JSON đầu ra entity_geometry cho block phụ kiện")
    ap.add_argument("--source", default="", help="tên bản vẽ ghi vào bảng")
    args = ap.parse_args(argv)
    try:
        bbox = parse_bbox(args.bbox) if args.bbox else None
        if args.entities:
            if not args.texts:
                raise ValueError("--entities cần đi kèm --texts")
            entities, texts = load_json(args.entities, "entities"), load_json(args.texts, "texts")
            geometry = load_json(args.geometry, "entities") if args.geometry else []
            source = args.source or Path(args.entities).name
        else:
            if agent is None:  # pragma: no cover — nhánh chạy thật, test truyền agent giả
                sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
                import dhcb_agent as agent
            entities, texts, geometry, name = fetch_live(agent)
            source = args.source or name
    except (OSError, ValueError, RuntimeError, KeyError) as ex:
        print(f"Lỗi đầu vào: {ex}", file=sys.stderr)
        return 2

    if bbox is None:
        print("Cảnh báo: không có --bbox — tính cả khung mặt cắt/chi tiết nếu chúng dùng layer ống.", file=sys.stderr)
    pairs = pair_segments(duct_segments(entities, bbox, args.layer_prefix))
    assigned = assign_sizes(pairs, parse_tags(texts, bbox))
    numbered = number_segments(assigned)
    unassigned = [a for a in assigned if not a["sys"]]
    fittings = count_fittings(geometry, bbox, args.layer_prefix)
    if not numbered:
        print("Không tìm thấy đoạn ống nào có kích thước — kiểm --bbox và --layer-prefix.", file=sys.stderr)
        return 1
    write_xlsx(Path(args.output), build_sheets(numbered, unassigned, fittings, source, args.waste))

    totals = collections.defaultdict(float)
    for a in numbered:
        totals[(a["sys"], a["W"], a["H"])] += a["len"] / 1000
    length = sum(totals.values())
    area = sum(2 * (w + h) / 1000 * L for (_, w, h), L in totals.items())
    print(f"Đã ghi {args.output}: {len(numbered)} đoạn, {length:.2f} m, {area:.2f} m² tôn "
          f"(+{args.waste:.0%}: {area * (1 + args.waste):.2f} m²), {sum(fittings.values())} phụ kiện, "
          f"{len(unassigned)} cặp chưa xác định ({sum(a['len'] for a in unassigned) / 1000:.2f} m).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
