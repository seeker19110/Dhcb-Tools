#!/usr/bin/env python3
"""Bóc khối lượng ống gió chữ nhật từ bản vẽ AutoCAD 2D → Excel theo **đốt**, box bít đầu và co, m² tôn.

Bản vẽ ĐHKK xuất từ Revit sang DWG không còn đối tượng ống: mỗi đoạn ống chỉ là **hai đường thẳng song song**
trên layer `M-HVAC-DUCT*`, kích thước nằm ở tag chữ (`SEAD - 700x300`), mối nối là block `…Rec_TDC_Union…` /
`…Rec_C_Union…` vắt ngang ống, co/côn/tê là block mang tên family Revit. Ống thi công chia thành đốt có chiều
dài tối đa theo loại nẹp — **TDC 1110 mm, nẹp C và bích V 1180 mm** — cộng các đoạn bù; đoạn cuối ở đầu kín dài
hơn đốt tối đa là **box gió bít đầu** (sản xuất riêng). Script dựng lại đúng cách chia đó:

1. lấy Line trên layer ống trong khung mặt bằng (bỏ layer CNTR/ANNO/IDEN — tim ống, ghi chú, tag);
2. ghép cặp hai cạnh song song cách nhau 80–2000 mm, chồng nhau ≥ 60 % → một mảnh ống; bỏ mảnh nằm trọn trong
   hộp bao phụ kiện (nét vẽ bên trong co, ống mềm, cổ tê);
3. nối các mảnh cùng trục, cùng bề rộng thành **tuyến** (khe ≤ 80 mm, hoặc ≤ 300 mm mà có block mối nối trong khe);
4. gán R×C cho mảnh theo tag ống (hệ kết thúc bằng `AD`: SEAD/SAD/RAD/FAD/EAD/PAD) gần nhất có một cạnh khớp
   bề rộng (±30 mm, trong 8 m) — miệng gió/van (OBD, PAG, SAG…) không phải ống; tuyến lấy kích thước chiếm
   nhiều chiều dài nhất; loại nẹp lấy từ block mối nối trên tuyến, tuyến không có mối nối thì theo tuyến cùng
   kích thước, rồi cùng hệ;
5. **cắt tuyến tại tâm block mối nối** (không có hình học block thì tại khe giữa các mảnh), rồi chia tiếp theo
   quy tắc của công cụ BKL.lsp: khoảng dài hơn L max quá 80 mm chia L max từ đầu khoảng, đoạn cuối < 300 mm cộng
   đốt trước rồi chia đôi, khoảng có chân rẽ/miệng gió bên hông được tới 1400 mm; làm tròn 10 mm như dim, từ
   L max − 10 trở lên tính bằng L max (*đốt chuẩn*). Khoảng cuối ở đầu kín (nắp bịt hoặc không nối gì) dài hơn
   L max, tới 1400 mm, là *box bít đầu*; còn lại là *đoạn bù*;
6. nhận diện **co** (block `…Elbow…`): hai nửa co 45° nối bằng đoạn chéo ngắn gộp thành một co vuông (đoạn chéo
   là cổ co, không tính ống), góc theo hướng tuyến nối vào, kích thước và loại nẹp theo tuyến nối vào;
7. ghi .xlsx: Tổng hợp (theo hệ + kích thước: đốt chuẩn, đoạn bù, box, co, m² có công thức, ô hao hụt),
   Chi tiết (STT, mã đốt `SEAD-001`, tuyến, loại), Đoạn bù & box, Co, Phụ kiện, Chưa xác định, Ghi chú.

8. trang **Theo mẫu** (đúng cột Sheet1 bản bóc tay): chia trục/cụm, số TT chạy liên tục từ đầu gần van chặn lửa
   (cửa trục) ra ngoài, xuyên qua co, giảm (block côn) và gót giày (block chân rẽ); `--lsp` ghi thêm file AutoLISP
   vẽ đúng các số TT đó + DIM chiều dài đốt lên hai layer riêng `DHCB-KL-STT` / `DHCB-KL-DIM`.

Chỉ ĐỌC bản vẽ; file LISP do kỹ sư tự chạy (lệnh DHCB_KL_GHI hỏi xác nhận, một lần UNDO). Kết quả là ước lượng có
kiểm được: mỗi dòng chi tiết mang toạ độ tâm và handle để tra lại.

Cách dùng (bản vẽ đang mở trong AutoCAD, Bridge 8766 đang bật):
    python scripts/boc-khoi-luong-ong-gio.py --bbox=-170000,300000,-40000,340000 -o KL-ong-gio.xlsx

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
    "Rec_TDC_Union": "Mối nối bích TDC", "Rec_C_Union": "Mối nối (C)", "Rec_C_Elbow_Square": "Co vuông 90°",
    "Rec_C_Elbow_45": "Co 45°", "Rec_Tap_Round": "Tê/chân rẽ tròn", "Rec_Cap_End": "Nắp bịt đầu ống",
    "Rec_C_Transition_Round": "Côn chữ nhật → tròn", "Rec_C_Transition": "Côn thu (C)",
    "Rec_TDC_Transition": "Côn thu bích TDC", "Rec_C_Tap": "Chân rẽ chữ nhật (C)",
    "Rec_TDC_Tap": "Chân rẽ chữ nhật bích TDC", "MotorizedFireDamper": "Van chặn lửa có motor",
    "REC_Flexible": "Ống mềm",
}
# Quy tắc chia ống — cùng giá trị mặc định với công cụ BKL.lsp (BKL_QUYTAC) mà tổ bóc khối lượng đang dùng.
MAX_DOT = {"TDC": 1110, "C": 1180, "V": 1180}  # đốt dài nhất theo kiểu mối ghép (mm)
JOINT_TABLE = ((0, "TDC"), (2000, "V"))        # chưa rõ mối ghép: theo cạnh lớn
TAP_MAX = 1400    # đoạn thẳng có chân rẽ / miệng gió bên hông được dài tới
LEN_TOL = 80      # chỉ chia khi dài hơn L max quá chừng này
SCRAP_LEN = 300   # đoạn cuối ngắn hơn → cộng đốt nguyên trước rồi chia đôi
LEN_STEP = 10     # bước làm tròn
# Loại phụ kiện theo từ khoá trong tên family (bỏ mã phần tử Revit), thứ tự ưu tiên như BKL.
FIT_KINDS = (
    (None, ("UNION", "HANGER", "CANVAS", "SUPPORT")),
    ("ENDCAP", ("ENDCAP", "CAP_END", "BIT ONG")),
    ("DAMPER", ("DAMPER", "VCD", "VAN GIO")),
    ("TAP", ("TAP", "TAKEOFF", "SHOE", "CHAN RE")),
    ("TRANS", ("TRANSITION", "REDUCER", "CON THU", "CON GIAM")),
    ("TERM", ("MIENG GIO", "GRILLE", "DIFFUSER", "LOUVER", "REGISTER")),
    ("ELBOW", ("ELBOW", "BEND")),
    ("TEE", ("TEE", "CHAC BA")),
    ("BOX", ("AIR BOX", "PLENUM", "HOP GIO")),
    ("FLEX", ("FLEXIBLE", "ONG MEM")),
)
TERM_NAME = re.compile(r"^\$?GV\d", re.I)  # block miệng gió vẽ tay kiểu "$GV1200x400"
SKIP_LAYER = ("CNTR", "ANNO", "IDEN")
TAG = re.compile(r"\b([A-Z]{2,4})\s*-\s*(\d{2,4})\s*[xX×]\s*(\d{2,4})")
TAG_END = re.compile(r"\bBO[DGT]\s*=|\b[A-Z]{2,4}\s*-\s*\d{2,4}\s*[xX×]")  # đuôi tag dừng ở cao độ / tag kế
FLANGE_GAP = (15, 25)   # nét bích TDC vẽ 2–3 nét song song cách nhau ~20 mm, cùng chiều dài
FLANGE_PAD = 60         # cạnh ống vẽ theo mép bích: rộng hơn tag tới chừng này
DEFAULT_EI = {"SEAD": "EI60"}  # ống hút khói không ghi gì: bọc EI60; ghi "+ EI120" trên tag thì theo tag
TOLE_TABLE = ((1250, 0.95), (500, 0.75))  # cạnh lớn nhất > 1250 → 0.95; > 500 → 0.75; còn lại 0.58
TOLE_MIN, TOLE_EI = 0.58, 0.75
FAMILY = re.compile(r"(Rec_\w+|MotorizedFireDamper|REC_Flexible)")
JOINT = re.compile(r"Rec_([A-Za-z]+)_Union\b")
STD_LABEL, FILL_LABEL, BOX_LABEL = "Đốt chuẩn", "Đoạn bù", "Box bít đầu"


# ── Đọc dữ liệu ──────────────────────────────────────────────────────────────

def clean_mtext(text: str) -> str:
    """Bỏ mã định dạng MText (`\\pxql;`, `\\fArial|b1;`, `{}`), `\\P` → ` | `."""
    text = re.sub(r"\\p[^;]*;", "", text)
    text = re.sub(r"\\[A-OQ-Za-z][^;\\{}]*;", "", text)
    text = text.replace("\\P", " | ")
    return re.sub(r"\s+", " ", re.sub(r"[{}]", "", text)).strip()


def in_box(x: float, y: float, bbox) -> bool:
    return bbox is None or (bbox[0] <= x <= bbox[2] and bbox[1] <= y <= bbox[3])


def tag_spec(rest: str) -> str:
    """Phần ghi thêm sau kích thước trên tag (`SEAD- 700x300+ EI120`, `FAD- 450x250+ | TIÊU ÂM TRONG 50mm`) — tới
    cao độ BOD/BOG hoặc tag kế; bỏ dấu + và | ."""
    m = TAG_END.search(rest)
    rest = rest[:m.start()] if m else rest
    return re.sub(r"\s+", " ", re.sub(r"[|+]", " ", rest)).strip().upper()


def ei_class(sys_: str, spec: str = "") -> str:
    """Cấp chống cháy của ống: theo tag (`EI120`, `EI 60`…), không ghi thì mặc định theo hệ (SEAD → EI60)."""
    m = re.search(r"\bEI\s*(\d{2,3})\b", spec or "")
    return f"EI{m.group(1)}" if m else DEFAULT_EI.get(sys_, "")


def tole_thickness(dims, ei: str = "") -> float:
    """Độ dày tole (mm): ống bọc EI luôn 0.75; còn lại theo cạnh lớn nhất (> 1250 → 0.95, > 500 → 0.75, ≤ 500 → 0.58)."""
    if ei:
        return TOLE_EI
    edge = max((d for d in dims if isinstance(d, (int, float))), default=0)
    for limit, thick in TOLE_TABLE:
        if edge > limit:
            return thick
    return TOLE_MIN


def parse_tags(texts: list, bbox=None) -> list:
    """Tag ống gió `HỆ - RxC [+ ghi thêm]`; chỉ hệ kết thúc bằng AD (ống), bỏ chi tiết xref ghép (`…$0$…`).
    Một chữ có thể chứa nhiều tag (MLeader/MText nhiều dòng)."""
    tags = []
    for t in texts:
        if "$0$" in t.get("layer", "") or not in_box(t["x"], t["y"], bbox):
            continue
        s = clean_mtext(t.get("text", ""))
        for m in TAG.finditer(s):
            if m.group(1).endswith("AD"):
                tags.append({"sys": m.group(1), "W": int(m.group(2)), "H": int(m.group(3)), "x": t["x"], "y": t["y"],
                             "spec": tag_spec(s[m.end():])})
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


def flange_joints(segs: list) -> tuple:
    """Bản vẽ không có block mối nối (ống vẽ tay rồi phá block): bích TDC là 2–3 nét ngang ống song song, cùng
    chiều dài, cách nhau ~20 mm. Trả ([(x, y, "TDC")] tâm từng bích, các nét còn lại) — nét bích không được
    ghép cặp thành ống (hai bích cạnh nhau sẽ thành một 'ống' dài 20 mm)."""
    def twin(a, b):
        if abs(a["len"] - b["len"]) > 6:
            return None
        ua, ub = direction(a), direction(b)
        if abs(ua[0] * ub[0] + ua[1] * ub[1]) < 0.995:
            return None
        (ta, oa), (tb, ob) = axis(ua, a["x1"], a["y1"]), axis(ua, b["x1"], b["y1"])
        tb2 = axis(ua, b["x2"], b["y2"])[0]
        ta2 = axis(ua, a["x2"], a["y2"])[0]
        same_span = abs(min(ta, ta2) - min(tb, tb2)) <= 6
        return FLANGE_GAP[0] <= abs(ob - oa) <= FLANGE_GAP[1] and same_span
    grid = collections.defaultdict(list)
    for i, s in enumerate(segs):
        grid[(round((s["x1"] + s["x2"]) / 1000), round((s["y1"] + s["y2"]) / 1000))].append(i)
    flange = set()
    for i, s in enumerate(segs):
        if not 80 <= s["len"] <= 2100:
            continue
        gx, gy = round((s["x1"] + s["x2"]) / 1000), round((s["y1"] + s["y2"]) / 1000)
        for j in (j for dx in (-1, 0, 1) for dy in (-1, 0, 1) for j in grid[(gx + dx, gy + dy)]):
            if j != i and twin(s, segs[j]):
                flange.update((i, j))
    centers = []
    for i in sorted(flange):
        s = segs[i]
        x, y = (s["x1"] + s["x2"]) / 2, (s["y1"] + s["y2"]) / 2
        if not any(math.hypot(x - cx, y - cy) <= FLANGE_GAP[1] * 3 for cx, cy, _ in centers):
            centers.append((x, y, "TDC"))
    # gộp 2–3 nét của một bích về tâm giữa
    merged = []
    for x, y, k in centers:
        group = [segs[i] for i in flange if math.hypot((segs[i]["x1"] + segs[i]["x2"]) / 2 - x,
                                                        (segs[i]["y1"] + segs[i]["y2"]) / 2 - y) <= FLANGE_GAP[1] * 3]
        merged.append((sum((g["x1"] + g["x2"]) / 2 for g in group) / len(group),
                       sum((g["y1"] + g["y2"]) / 2 for g in group) / len(group), k))
    return merged, [s for i, s in enumerate(segs) if i not in flange]


def _center(e: dict):
    mn, mx = e.get("extentsMin"), e.get("extentsMax")
    if mn and mx:
        return (mn["x"] + mx["x"]) / 2, (mn["y"] + mx["y"]) / 2
    p = e.get("position")
    return (p["x"], p["y"]) if p else None


def joint_points(geometry: list, bbox=None) -> list:
    """(x, y, loại nẹp) tại tâm hộp bao block mối nối `…Rec_<LOẠI>_Union` — chỗ cắt đốt."""
    pts = []
    for e in geometry:
        m = JOINT.search(e.get("blockName") or "") if e.get("type") == "BlockReference" else None
        c = _center(e) if m else None
        if c and in_box(c[0], c[1], bbox):
            pts.append((c[0], c[1], m.group(1).upper()))
    return pts


def family_part(name: str) -> str:
    """'Họ - Kiểu-1234567-TÊN VIEW' → 'Họ - Kiểu' (bỏ mã phần tử Revit `-1234567-` / `-V377-` và tên view)."""
    return re.split(r"-(?:\d{6,}|V\d{1,5})-", name, maxsplit=1)[0]


def fit_kind(name: str):
    """Loại phụ kiện theo từ khoá trong tên family; None = không phải phụ kiện (mối nối, giá treo…) hoặc không rõ."""
    if TERM_NAME.match(name):
        return "TERM"
    u = family_part(name).upper()
    for kind, keys in FIT_KINDS:
        if any(k in u for k in keys):
            return kind
    return None


def fitting_boxes(geometry: list, bbox=None, prefix: str = "M-HVAC-DUCT") -> list:
    """Hộp bao các phụ kiện nhận diện được (trừ mối nối): co, côn, tê, chân rẽ, nắp bịt, van, ống mềm, miệng gió.
    `duct` = nằm trên layer ống (miệng gió thường ở layer riêng)."""
    out = []
    for e in geometry:
        name = e.get("blockName") or ""
        mn, mx = e.get("extentsMin"), e.get("extentsMax")
        kind = fit_kind(name) if e.get("type") == "BlockReference" and mn and mx else None
        if kind and in_box((mn["x"] + mx["x"]) / 2, (mn["y"] + mx["y"]) / 2, bbox):
            out.append({"name": name, "family": re.split(r"\s+-\s+", name)[0], "kind": kind,
                        "duct": e.get("layer", "").startswith(prefix), "box": (mn["x"], mn["y"], mx["x"], mx["y"])})
    return out


def _near(box: tuple, x: float, y: float, pad: float) -> bool:
    return box[0] - pad <= x <= box[2] + pad and box[1] - pad <= y <= box[3] + pad


# ── Hình học ─────────────────────────────────────────────────────────────────

def direction(s: dict) -> tuple:
    """Vectơ đơn vị đã chuẩn hoá chiều (x dương, hoặc y dương khi đứng), làm tròn để so khớp được."""
    ux, uy = (s["x2"] - s["x1"]) / s["len"], (s["y2"] - s["y1"]) / s["len"]
    if ux < -1e-9 or (abs(ux) < 1e-9 and uy < 0):
        ux, uy = -ux, -uy
    return round(ux, 3) + 0.0, round(uy, 3) + 0.0


def axis(u: tuple, x: float, y: float) -> tuple:
    """(toạ độ dọc trục t, toạ độ vuông góc off) của điểm theo phương u."""
    return x * u[0] + y * u[1], -x * u[1] + y * u[0]


def point(u: tuple, t: float, off: float) -> tuple:
    """Ngược của `axis`."""
    return t * u[0] - off * u[1], t * u[1] + off * u[0]


def pair_segments(segs: list, min_w: float = 80, max_w: float = 2000, min_overlap: float = 0.6) -> list:
    """Mỗi cạnh ghép với cạnh song song gần nhất phía sau nó trong danh sách → mảnh ống [t0, t1] trên trục."""
    pairs = []
    dirs = [direction(s) for s in segs]
    for i, s in enumerate(segs):
        u = dirs[i]
        best = None
        for j in range(i + 1, len(segs)):
            t = segs[j]
            if abs(u[0] * dirs[j][1] - u[1] * dirs[j][0]) > 0.01:
                continue
            width = abs(axis(u, t["x1"], t["y1"])[1] - axis(u, s["x1"], s["y1"])[1])
            if not min_w <= width <= max_w:
                continue
            ps = sorted(axis(u, x, y)[0] for x, y in ((s["x1"], s["y1"]), (s["x2"], s["y2"])))
            pt = sorted(axis(u, x, y)[0] for x, y in ((t["x1"], t["y1"]), (t["x2"], t["y2"])))
            t0, t1 = max(ps[0], pt[0]), min(ps[1], pt[1])
            if t1 - t0 < min_overlap * min(s["len"], t["len"]):
                continue
            if best is None or width < best[0]:
                best = (width, t, t0, t1)
        if best:
            width, t, t0, t1 = best
            off = (axis(u, s["x1"], s["y1"])[1] + axis(u, t["x1"], t["y1"])[1]) / 2
            xs, ys = (s["x1"], s["x2"], t["x1"], t["x2"]), (s["y1"], s["y2"], t["y1"], t["y2"])
            pairs.append({"w": round(width), "len": t1 - t0, "a": s["handle"], "b": t["handle"], "layer": s["layer"],
                          "u": u, "off": off, "t0": t0, "t1": t1, "mid": list(point(u, (t0 + t1) / 2, off)),
                          "extent": (min(xs), min(ys), max(xs), max(ys))})
    return pairs


def drop_inside_fittings(pairs: list, boxes: list, pad: float = 10) -> list:
    """Bỏ mảnh có cả hai cạnh nằm trọn trong hộp bao một phụ kiện — đó là nét vẽ của phụ kiện, không phải ống."""
    keep = []
    for p in pairs:
        e = p["extent"]
        if not any(b["duct"] and _near(b["box"], e[0], e[1], pad) and _near(b["box"], e[2], e[3], pad) for b in boxes):
            keep.append(p)
    return keep


def assign_sizes(pairs: list, tags: list, tol: float = 30, radius: float = 8000, pad: float = FLANGE_PAD) -> list:
    """Tag gần nhất có một cạnh khớp bề rộng mảnh (±tol). Cạnh ống vẽ theo mép bích thì rộng hơn tag tới `pad` mm:
    vẫn nhận, nhưng tag khớp đúng (±tol) luôn được ưu tiên hơn tag chỉ khớp nhờ bích."""
    out = []
    for p in pairs:
        best = None
        for t in tags:
            diffs = [p["w"] - t["W"], p["w"] - t["H"]]
            if any(abs(d) <= tol for d in diffs):
                rank = 0
            elif any(tol < d <= pad for d in diffs):
                rank = 1
            else:
                continue
            d = math.hypot(t["x"] - p["mid"][0], t["y"] - p["mid"][1])
            if d <= radius and (best is None or (rank, d) < best[:2]):
                best = (rank, d, t)
        if best:
            t = best[2]
            out.append({**p, "sys": t["sys"], "W": t["W"], "H": t["H"], "tagDist": round(best[1]),
                        "spec": t.get("spec", "")})
        else:
            out.append({**p, "sys": None})
    return out


def _joints_between(joints: list, u: tuple, off: float, w: float, lo: float, hi: float) -> list:
    """[(t, loại nẹp)] của mối nối nằm trên tim tuyến trong khoảng [lo, hi]."""
    out = []
    for x, y, kind in joints:
        t, o = axis(u, x, y)
        if abs(o - off) <= w / 2 + 60 and lo - 1 <= t <= hi + 1:
            out.append((t, kind))
    return sorted(out)


def build_runs(pieces: list, joints: list, gap: float = 80, joint_gap: float = 300) -> list:
    """Nối mảnh cùng trục (|Δoff| ≤ 30, |Δw| ≤ 20) thành tuyến; gán kích thước và loại nẹp cho tuyến."""
    runs = []
    for p in sorted(pieces, key=lambda p: (p["u"], p["t0"])):
        for r in runs:
            if r["u"] != p["u"] or abs(r["off"] - p["off"]) > 30 or abs(r["w"] - p["w"]) > 20:
                continue
            g = p["t0"] - r["t1"]
            if -5 <= g <= gap or (0 < g <= joint_gap and _joints_between(joints, r["u"], r["off"], r["w"], r["t1"], p["t0"])):
                if g > 0:
                    r["gaps"].append(r["t1"] + g / 2)
                r["t1"] = max(r["t1"], p["t1"])
                r["items"].append(p)
                break
        else:
            runs.append({"u": p["u"], "off": p["off"], "w": p["w"], "t0": p["t0"], "t1": p["t1"], "items": [p], "gaps": []})
    by_size, by_sys = collections.defaultdict(collections.Counter), collections.defaultdict(collections.Counter)
    for r in runs:
        size = collections.defaultdict(float)
        for p in r["items"]:
            if p["sys"]:
                size[(p["sys"], p["W"], p["H"])] += p["len"]
        r["size"] = max(size, key=size.get) if size else None
        specs = collections.Counter(p.get("spec", "") for p in r["items"] if p["sys"] and (p["sys"], p["W"], p["H"]) == r["size"])
        r["spec"] = specs.most_common(1)[0][0] if specs else ""
        kinds = collections.Counter(k for _, k in _joints_between(joints, r["u"], r["off"], r["w"], r["t0"], r["t1"]))
        r["joint"] = kinds.most_common(1)[0][0] if kinds else None
        if r["size"] and r["joint"]:
            by_size[r["size"]][r["joint"]] += 1
            by_sys[r["size"][0]][r["joint"]] += 1
    for r in runs:  # tuyến không có mối nối: theo tuyến cùng kích thước, rồi cùng hệ, rồi bảng theo cạnh lớn
        if r["size"] and not r["joint"]:
            src = by_size.get(r["size"]) or by_sys.get(r["size"][0])
            r["joint"] = src.most_common(1)[0][0] if src else joint_by_size(r["size"][1], r["size"][2])
    return runs


def joint_by_size(w: int, h: int) -> str:
    kind = JOINT_TABLE[0][1]
    for edge, k in JOINT_TABLE:
        if max(w, h) >= edge:
            kind = k
    return kind


def round_len(v: float, step: int = LEN_STEP) -> int:
    """Làm tròn tới bội `step` gần nhất — bằng số dim hiển thị trên bản vẽ (dimstyle làm tròn 10 mm). BKL áp
    kiểu 'le7' lên chính số dim đã làm tròn đó, nên với số đo hình học thì làm tròn gần nhất mới khớp bản vẽ
    (767 → 770 như dim, le7 trên 767 sẽ ra 760)."""
    return int(v / step + 0.5) * step


def split_interval(s0: float, e: float, lmax: float, tol: float = LEN_TOL, scrap: float = SCRAP_LEN) -> list:
    """Vị trí cắt thêm trong khoảng [s0, e] giữa hai mối nối: dài hơn lmax + tol → chia lmax từ đầu khoảng;
    đoạn cuối < scrap → cộng với đốt nguyên trước rồi chia đôi."""
    if e - s0 <= lmax + tol:
        return []
    cuts, c = [], s0 + lmax
    while c < e - 5:
        cuts.append(c)
        c += lmax
    if e - cuts[-1] < scrap:
        cuts[-1] = (e + (cuts[-2] if len(cuts) > 1 else s0)) / 2
    return cuts


def _end_state(run: dict, t: float, runs: list, boxes: list) -> str:
    """'cap' (có nắp bịt) · 'open' (không nối gì — đầu kín) · 'fitting' (nối phụ kiện/tuyến khác)."""
    x, y = point(run["u"], t, run["off"])
    hits = [b for b in boxes if b["duct"] and _near(b["box"], x, y, 60)]
    if any(b["kind"] == "ENDCAP" for b in hits):
        return "cap"
    if hits:
        return "fitting"
    for r in runs:
        if r is not run and any(math.hypot(x - ex, y - ey) <= r["w"] for ex, ey in
                                (point(r["u"], r["t0"], r["off"]), point(r["u"], r["t1"], r["off"]))):
            return "fitting"
    return "open"


def _side_branch(run: dict, s0: float, e: float, boxes: list) -> bool:
    """Có chân rẽ / miệng gió (TAP, TERM) bám bên hông khoảng [s0, e] của tuyến không."""
    for b in boxes:
        if b["kind"] not in ("TAP", "TERM"):
            continue
        corners = [axis(run["u"], x, y) for x in (b["box"][0], b["box"][2]) for y in (b["box"][1], b["box"][3])]
        ts, os_ = [c[0] for c in corners], [c[1] for c in corners]
        if min(ts) < e and max(ts) > s0 and min(os_) <= run["off"] + run["w"] / 2 + 150 and max(os_) >= run["off"] - run["w"] / 2 - 150:
            return True
    return False


def split_runs(runs: list, joints: list, boxes: list = ()) -> list:
    """Cắt tuyến có kích thước thành đốt theo quy tắc BKL:
    - cắt tại mối nối (không có hình học block thì tại khe giữa các mảnh cạnh);
    - khoảng dài hơn L max (+ LEN_TOL) thì chia L max từ đầu khoảng, đoạn cuối < SCRAP_LEN cộng đốt trước rồi chia đôi;
      khoảng có chân rẽ / miệng gió bên hông được dài tới TAP_MAX;
    - khoảng cuối ở đầu kín (nắp bịt hoặc không nối gì) dài hơn L max, tới TAP_MAX (+ LEN_TOL) = box bít đầu, không chia;
    - chiều dài làm tròn 'le7'; từ L max − 10 tới L max + LEN_TOL tính bằng L max (đốt chuẩn)."""
    out = []
    for r in runs:
        if not r["size"] or r.get("elbow_part"):  # đoạn chéo giữa hai nửa co là cổ co, không phải ống
            continue
        if joints:
            cuts = [t for t, _ in _joints_between(joints, r["u"], r["off"], r["w"], r["t0"], r["t1"])
                    if r["t0"] + 20 < t < r["t1"] - 20]
        else:
            cuts = r["gaps"]
        b = [r["t0"]] + cuts + [r["t1"]]
        sys_, w, h = r["size"]
        dot = MAX_DOT[r["joint"]]
        closed = {0: _end_state(r, r["t0"], runs, boxes) != "fitting",
                  len(b) - 2: _end_state(r, r["t1"], runs, boxes) != "fitting"}
        tag = min((p["tagDist"] for p in r["items"] if p["sys"]), default=None)
        pieces = []  # (s, e, loại, ghi chú)
        for k in range(len(b) - 1):
            s0, e = b[k], b[k + 1]
            side = dot <= 2000 and _side_branch(r, s0, e, boxes)
            if closed.get(k) and dot + LEN_TOL < e - s0 <= TAP_MAX + LEN_TOL:
                pieces.append((s0, e, BOX_LABEL, ""))
                continue
            limit = max(dot, TAP_MAX) if side else dot
            edges = [s0] + split_interval(s0, e, limit) + [e]
            for a, z in zip(edges, edges[1:]):
                pieces.append((a, z, None, "có chân rẽ/miệng gió bên hông" if side and z - a > dot + LEN_TOL else ""))
        for s0, e, kind, note in pieces:
            raw = round_len(e - s0)  # cắt cách đầu tuyến > 20 mm nên luôn > 0
            length = dot if kind is None and dot - 10 <= raw <= dot + LEN_TOL and not note else raw
            kind = kind or (STD_LABEL if length == dot else FILL_LABEL)
            x, y = point(r["u"], (s0 + e) / 2, r["off"])
            ei = ei_class(sys_, r.get("spec", ""))
            out.append({"run": r, "sys": sys_, "W": w, "H": h, "joint": r["joint"], "dot": dot, "len": length,
                        "kind": kind, "note": note, "x": x, "y": y, "handle": r["items"][0]["a"], "tagDist": tag,
                        "spec": r.get("spec", ""), "ei": ei, "tole": tole_thickness((w, h), ei)})
    return out


def number_pieces(pieces: list) -> list:
    """Sắp theo hệ → tiết diện lớn trước → tuyến (trên→dưới theo dải 2 m, trái→phải) → dọc tuyến; gán STT, mã đốt
    `HỆ-001` và mã tuyến `T01`."""
    order = {}
    for p in pieces:
        r = p["run"]
        x0, y0 = point(r["u"], r["t0"], r["off"])
        order.setdefault(id(r), (ORDER.index(p["sys"]) if p["sys"] in ORDER else len(ORDER), -p["W"] * p["H"],
                                 p["W"], p["H"], -round(y0 / 2000), x0))
    pieces = sorted(pieces, key=lambda p: (order[id(p["run"])], axis(p["run"]["u"], p["x"], p["y"])[0]))
    counter, runs = collections.Counter(), {}
    for n, p in enumerate(pieces, 1):
        counter[p["sys"]] += 1
        runs.setdefault(id(p["run"]), len(runs) + 1)
        p["stt"], p["code"], p["runCode"] = n, f"{p['sys']}-{counter[p['sys']]:03d}", f"T{runs[id(p['run'])]:02d}"
    return pieces


def find_elbows(boxes: list, runs: list, diag_max: float = 400) -> list:
    """Co (phụ kiện loại ELBOW trên layer ống), theo cách bóc tay:
    - Revit xuất một co vuông 90° thành **hai block nửa co 45°** nối nhau bằng một đoạn chéo ngắn → gộp thành một
      co; đoạn chéo (< `diag_max`, chạm ≥ 2 block co) là cổ co, đánh dấu `elbow_part` để không tính thành ống, và
      chiều dài của nó ghi làm R;
    - góc lấy theo hướng các tuyến chính nối vào (≥ 2 hướng); chỉ một hướng thì theo tên family (`_45` → 45°,
      còn lại 90°) — tên family không đáng tin khi đủ tuyến (block `Elbow_45` nối hai tuyến vuông góc là co 90°);
    - kích thước/nẹp theo tuyến chính nối vào."""
    eb = [b for b in boxes if b["kind"] == "ELBOW" and b["duct"]]
    sized = [r for r in runs if r["size"]]
    touch = {id(r): {i for i, b in enumerate(eb) for t in (r["t0"], r["t1"])
                     if _near(b["box"], *point(r["u"], t, r["off"]), 60)} for r in sized}
    parent = list(range(len(eb)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for r in sized:
        if abs(r["u"][0]) > 0.01 and abs(r["u"][1]) > 0.01 and r["t1"] - r["t0"] < diag_max and len(touch[id(r)]) >= 2:
            r["elbow_part"] = True
            first, *others = sorted(touch[id(r)])
            for i in others:
                parent[find(i)] = find(first)
    groups = collections.defaultdict(set)
    for i in range(len(eb)):
        groups[find(i)].add(i)
    out = []
    for members in groups.values():
        linked = [r for r in sized if touch[id(r)] & members]
        mains = [r for r in linked if not r.get("elbow_part")]
        necks = [r for r in linked if r.get("elbow_part")]
        weight = collections.Counter()
        for r in mains or necks:
            weight[r["size"]] += r["t1"] - r["t0"]
        size = weight.most_common(1)[0][0] if weight else (None, None, None)
        # chỉ tuyến cùng R×C với co (khác hệ vẫn tính — co nối RAD với SAD cùng 400x250): nét trong block co hay
        # bị ghép nhầm thành "tuyến" nhỏ chéo
        src = [r for r in mains or necks if r["size"][1:] == size[1:]]
        dirs = sorted({r["u"] for r in src if not r.get("elbow_part")})
        family = eb[min(members)]["family"]
        if len(dirs) >= 2:
            dot = abs(dirs[0][0] * dirs[1][0] + dirs[0][1] * dirs[1][1])
            angle = round(math.degrees(math.acos(min(1.0, dot))) / 45) * 45 or 90
        else:
            m = re.search(r"Elbow_(\d+)", family)
            angle = int(m.group(1)) if m else 90
        cx = sum((eb[i]["box"][0] + eb[i]["box"][2]) / 2 for i in members) / len(members)
        cy = sum((eb[i]["box"][1] + eb[i]["box"][3]) / 2 for i in members) / len(members)
        out.append({"sys": size[0], "W": size[1], "H": size[2], "angle": angle,
                    "R": round_len(max(r["t1"] - r["t0"] for r in necks)) if necks else None, "halves": len(members),
                    "joint": collections.Counter(r["joint"] for r in src).most_common(1)[0][0] if src else "?",
                    "x": cx, "y": cy, "family": family, "links": [r for r in src if not r.get("elbow_part")],
                    "boxes": [eb[i]["box"] for i in sorted(members)]})
    out.sort(key=lambda e: (ORDER.index(e["sys"]) if e["sys"] in ORDER else len(ORDER), -(e["W"] or 0) * (e["H"] or 0),
                            e["angle"], -round(e["y"] / 2000), e["x"]))
    for n, e in enumerate(out, 1):
        e["stt"], e["code"] = n, f"CO-{n:03d}"
    return out


# ── Chia trục / cụm và đánh số TT như bản bóc tay ───────────────────────────

SHAFT_LABEL = re.compile(r"HÚT KHÓI|ĐIỀU ÁP|BÙ GIÓ|HÚT RÁC|FAF|FCU|AHU|PAU", re.I)
SYS_LABEL = {"SEAD": "HÚT KHÓI", "FAD": "FAF", "RAD": "FCU", "SAD": "FCU", "PAD": "ĐIỀU ÁP", "EAD": "HÚT"}
SYS_TITLE = {"RAD": "GIÓ HỒI (RAD)", "SAD": "GIÓ CẤP (SAD)", "FAD": "GIÓ TƯƠI (FAD)", "EAD": "GIÓ THẢI (EAD)",
             "PAD": "TẠO ÁP (PAD)", "SEAD": "HÚT KHÓI (SEAD)"}


def _run_ends(r: dict) -> list:
    return [point(r["u"], t, r["off"]) for t in (r["t0"], r["t1"])]


def shaft_labels(texts: list, bbox=None) -> list:
    """(x, y, chữ) của nhãn trục/thiết bị (HÚT KHÓI 700x1300 T/A, FAF-2F-01…) trong khung."""
    out = []
    for t in texts:
        s = clean_mtext(t.get("text", ""))
        if in_box(t["x"], t["y"], bbox) and SHAFT_LABEL.search(s):
            out.append((t["x"], t["y"], s))
    return out


def _dist(p: tuple, q: tuple) -> float:
    return math.hypot(p[0] - q[0], p[1] - q[1])


def _elbow_item(e: dict) -> dict:
    return {"ma": "C", "elbow": e, "sys": e["sys"], "W": e["W"], "H": e["H"], "R": e["R"], "angle": e["angle"],
            "joint": e["joint"], "x": e["x"], "y": e["y"]}


def _fitting_item(c: dict, enter: dict) -> dict:
    """Giảm (côn) / gót giày (chân rẽ) như bản bóc tay: W×H theo tuyến đi vào, W'×H' theo tuyến còn lại (nếu khác),
    L = bề dài hộp bao block dọc tuyến đi vào."""
    b, u = c["box"], enter["u"]
    ts = [axis(u, x, y)[0] for x in (b["box"][0], b["box"][2]) for y in (b["box"][1], b["box"][3])]
    other = next((o for o in c["runs"] if o is not enter and o["size"][1:] != enter["size"][1:]), None)
    return {"ma": "G", "kind": b["kind"], "sys": enter["size"][0], "W": enter["size"][1], "H": enter["size"][2],
            "W2": other["size"][1] if other else None, "H2": other["size"][2] if other else None,
            "len": round_len(max(ts) - min(ts)), "joint": enter["joint"], "x": c["x"], "y": c["y"]}


def build_sections(runs: list, pieces: list, elbows: list, boxes: list, labels: list, order: str = "truc") -> list:
    """Chia trục/cụm và đánh số TT **như bản bóc tay**: số chạy liên tục dọc chuỗi ống từ đầu gần nhãn trục ra
    ngoài, xuyên qua co, giảm (block côn) và gót giày (block chân rẽ) — mỗi thứ một số. Cụm = các tuyến nối nhau
    (đầu chạm đầu, cùng chạm một co/côn/chân rẽ); đặt tên theo nhãn trục gần nhất cùng loại hệ; các cụm cùng tên
    gộp một mục, mỗi cụm đánh lại từ 1. Trả [(tên, [cụm, …])], cụm = list mục {"ma": T/B/C/G, …, "tt": số}."""
    sized = [r for r in runs if r["size"] and not r.get("elbow_part")]
    ids = {id(r) for r in sized}
    conns = [{"ma": "C", "runs": [r for r in e["links"] if id(r) in ids], "x": e["x"], "y": e["y"], "elbow": e,
              "boxes": e["boxes"]} for e in elbows if e["W"]]
    for b in boxes:
        if b["duct"] and b["kind"] in ("TRANS", "TAP"):
            rs = [r for r in sized if any(_near(b["box"], *p, 60) for p in _run_ends(r))]
            conns.append({"ma": "G", "runs": rs, "x": (b["box"][0] + b["box"][2]) / 2,
                          "y": (b["box"][1] + b["box"][3]) / 2, "box": b, "boxes": [b["box"]]})

    def touching(p, q, pad=60):
        return p[0] - pad <= q[2] and q[0] - pad <= p[2] and p[1] - pad <= q[3] and q[1] - pad <= p[3]

    # phụ kiện đặt sát nhau (giảm ngay sau co…) là một chuỗi: nối chúng và dồn tuyến của nhau
    adj = collections.defaultdict(list)
    for i, a in enumerate(conns):
        for b in conns[i + 1:]:
            if any(touching(p, q) for p in a["boxes"] for q in b["boxes"]):
                adj[id(a)].append(b)
                adj[id(b)].append(a)
    loose = [c for c in conns if c["ma"] == "C" and not c["runs"] and not adj[id(c)]]  # co không nối gì
    conns = [c for c in conns if c["runs"] or adj[id(c)]]
    run_conns, direct = collections.defaultdict(list), collections.defaultdict(list)
    for c in conns:
        for r in c["runs"]:
            run_conns[id(r)].append(c)

    def chain(c, seen=None):
        seen = seen if seen is not None else []
        seen.append(c)
        for o in adj[id(c)]:
            if all(o is not s for s in seen):
                chain(o, seen)
        return seen

    dampers = [b for b in boxes if b["kind"] == "DAMPER"]
    for i, a in enumerate(sized):
        for b in sized[i + 1:]:
            if any(_dist(p, q) <= max(a["w"], b["w"]) for p in _run_ends(a) for q in _run_ends(b)):
                direct[id(a)].append(b)
                direct[id(b)].append(a)

    parent = {id(r): id(r) for r in sized}

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for r in sized:
        for o in direct[id(r)] + [o for c in run_conns[id(r)] for x in chain(c) for o in x["runs"]]:
            parent[find(id(o))] = find(id(r))
    groups = collections.defaultdict(list)
    for r in sized:
        groups[find(id(r))].append(r)
    by_run = collections.defaultdict(list)
    for p in pieces:
        by_run[id(p["run"])].append(p)

    emitted, clusters = set(), []
    for rs in groups.values():
        sys_ = collections.Counter(r["size"][0] for r in rs).most_common(1)[0][0]
        pts = [p for r in rs for p in _run_ends(r)]
        cand = [lb for lb in labels if SYS_LABEL.get(sys_, "\0") in lb[2].upper()]
        best = min(cand, key=lambda lb: min(_dist(lb, p) for p in pts)) if cand else None
        title = ("TRỤC " + best[2]) if best and sys_ == "SEAD" else \
            SYS_TITLE.get(sys_, sys_) + (f" — gần {best[2]}" if best else "")
        anchor = (best[0], best[1]) if best else pts[0]
        # bản tay đánh số từ trục: đầu tuyến gần van chặn lửa nhất (cửa trục, ≤ 2 m — đoạn nối vào trục hay vẽ
        # bằng nét thường nên không chạm hẳn); không có van thì theo nhãn
        def to_damper(p):
            return min((math.hypot(max(dm["box"][0] - p[0], 0, p[0] - dm["box"][2]),
                                   max(dm["box"][1] - p[1], 0, p[1] - dm["box"][3])) for dm in dampers), default=math.inf)

        near_damper = min(pts, key=to_damper)
        start_pt = near_damper if to_damper(near_damper) <= 2000 else anchor
        items, visited = [], set()

        def emit(c, enter):
            """Phát co/giảm/gót và cả chuỗi phụ kiện sát nó; trả các (tuyến, điểm vào) đi tiếp qua chuỗi."""
            out = []
            for x in chain(c):
                if id(x) not in emitted:
                    emitted.add(id(x))
                    items.append(_elbow_item(x["elbow"]) if x["ma"] == "C" else _fitting_item(x, enter))
                out += [(o, (x["x"], x["y"])) for o in x["runs"]]
            return out

        def walk(r, entry):
            visited.add(id(r))
            e0, e1 = _run_ends(r)
            near, far = (e0, e1) if _dist(entry, e0) <= _dist(entry, e1) else (e1, e0)
            cs = sorted((c for c in run_conns[id(r)] if id(c) not in emitted), key=lambda c: _dist((c["x"], c["y"]), near))
            before = [c for c in cs if _dist((c["x"], c["y"]), near) < _dist((c["x"], c["y"]), far)]
            after = [c for c in cs if c not in before]
            nexts = []
            for c in before:
                nexts += emit(c, r)
            forward = near is e0
            for p in sorted(by_run[id(r)], key=lambda p: axis(r["u"], p["x"], p["y"])[0], reverse=not forward):
                items.append({"ma": "B" if p["kind"] == BOX_LABEL else "T", "piece": p, "sys": p["sys"], "W": p["W"],
                              "H": p["H"], "len": p["len"], "joint": p["joint"], "x": p["x"], "y": p["y"],
                              "ei": p.get("ei", ""), "spec": p.get("spec", "")})
            tail = []
            for c in after:
                tail += emit(c, r)
            for o, p in tail + [(o, far) for o in direct[id(r)]] + nexts:
                if id(o) not in visited:
                    walk(o, p)
            return far

        pos = start_pt
        while len(visited) < len(rs):
            start = min((r for r in rs if id(r) not in visited), key=lambda r: min(_dist(pos, p) for p in _run_ends(r)))
            pos = walk(start, pos)
        key = (min(p[0] for p in pts),) if order == "trai-phai" else (_sys_key(sys_), title, min(_dist(anchor, p) for p in pts))
        clusters.append((key, title, items))
    rest = [_elbow_item(c["elbow"]) for c in conns + loose if id(c) not in emitted and c["ma"] == "C"]
    if rest:
        clusters.append(((math.inf,), "CO CHƯA GẮN ĐƯỢC VÀO TUYẾN", rest))
    if order == "trai-phai":  # cụm theo vị trí trên mặt bằng; tiêu đề lặp lại khi hệ đổi
        out = []
        for _, title, items in sorted(clusters, key=lambda c: c[0]):
            for n, it in enumerate(items, 1):
                it["tt"] = n
            if out and out[-1][0] == title:
                out[-1][1].append(items)
            else:
                out.append((title, [items]))
        return out
    sections = collections.OrderedDict()
    for _, title, items in sorted(clusters, key=lambda c: c[0]):
        for n, it in enumerate(items, 1):
            it["tt"] = n
        sections.setdefault(title, []).append(items)
    return list(sections.items())


def _item_key(it: dict) -> tuple:
    return (it["ma"], it["W"], it["H"], it.get("W2"), it.get("H2"), it.get("kind"), it.get("len"), it.get("R"),
            it.get("angle"), it["joint"], it["sys"], it.get("ei"), it.get("spec"))


def sample_rows(sections: list) -> tuple:
    """Sheet theo mẫu bóc tay (Sheet1): TT · MÃ · QUY CÁCH · W · H · W' · H' · W'' · H'' · L · R · Z · Đơn vị · SL · DT
    · Độ dày · Kiểu mối ghép · Ghi chú. Mục liền nhau giống hệt gộp một dòng, TT dạng 'a~b' — cùng số ghi lên bản vẽ."""
    rows = [["TT", "MÃ", "QUY CÁCH ỐNG", "W", "H", "W'", "H'", "W''", "H''", "L", "R", "Z", "Đơn vị", "SL", "DT (m2)",
             "Độ dày tole", "Kiểu mối ghép", "Ghi chú"]]
    styles = {1: HEAD}
    for title, clusters in sections:
        rows.append([None, None, title])
        styles[len(rows)] = BOLD
        for ci, items in enumerate(clusters):
            if ci:
                rows.append([])
            k = 0
            while k < len(items):
                j = k
                while j + 1 < len(items) and _item_key(items[j + 1]) == _item_key(items[k]):
                    j += 1
                it, cnt, r = items[k], j - k + 1, len(rows) + 1
                tt = str(it["tt"]) if cnt == 1 else f"{it['tt']}~{items[j]['tt']}"
                w, h = it["W"], it["H"]
                if it["ma"] == "C":
                    qc = (f"Co vuông 90 độ {w}x{h}" if it["angle"] == 90 else f"Co {it['angle']} độ {w}x{h}") + \
                        (f"xR{it['R']}" if it["R"] else "")
                    row = [tt, "C", qc, w, h, None, None, None, None, None, it["R"], it["angle"], "cái", cnt, None]
                    note = "DT co theo công thức của đơn vị bóc"
                elif it["ma"] == "G":
                    to = f"/{it['W2']}x{it['H2']}" if it["W2"] else ""
                    qc = (f"Giảm {w}x{h}{to} L{it['len']}" if it["kind"] == "TRANS" else
                          f"Gót giày {w}x{h}{to} L{it['len']}")
                    row = [tt, "G", qc, w, h, it["W2"], it["H2"], None, None, it["len"], None, None, "cái", cnt, None]
                    note = "DT giảm/gót theo công thức của đơn vị bóc"
                else:
                    box = it["ma"] == "B"
                    qc = f"Ống bịt đầu {w}x{h}xL{it['len']}" if box else f"Ống {w}x{h}xL{it['len']}"
                    cap = f"+D{r}*E{r}" if box else ""
                    row = [tt, it["ma"], qc, w, h, None, None, None, None, it["len"], None, None, "ống", cnt,
                           F(f"ROUND((2*(D{r}+E{r})*J{r}{cap})*N{r}/10^6,3)")]
                    note = it["piece"]["note"]
                ei = it.get("ei") or ei_class(it["sys"] or "", it.get("spec", ""))
                extra = it.get("spec", "") if it.get("spec") and not re.fullmatch(r"EI\s*\d+", it.get("spec", "")) else ""
                rows.append(row + [tole_thickness((w, h, it.get("W2"), it.get("H2")), ei),
                                   it["joint"] + ("/ bít đầu" if it["ma"] == "B" else ""),
                                   "; ".join(x for x in (f"ỐNG {ei}" if ei else "", extra, note) if x) or None])
                k = j + 1
        rows.append([])
    last = len(rows)
    rows.append([None, None, "TỔNG DT ống thẳng + bịt đầu (m2)", None, None, None, None, None, None, None, None,
                 None, None, None, F(f"SUM(O2:O{last})")])
    styles[len(rows)] = TOTAL
    return rows, [9, 5, 40, 7, 7, 6, 6, 6, 6, 7, 6, 6, 8, 6, 10, 9, 15, 28], styles


# ── File LISP ghi số TT + DIM lên bản vẽ ─────────────────────────────────────

LSP_STT, LSP_DIM = "DHCB-KL-STT", "DHCB-KL-DIM"
LSP_HEAD = r'''; DHCB Tools - ghi so TT va DIM chieu dai dot ong gio len ban ve (sinh boi boc-khoi-luong-ong-gio.py).
; APPLOAD file nay roi go DHCB_KL_GHI. Xoa de chay lai: DHCB_KL_XOA. Tat ca nam tren 2 layer rieng:
;   {stt} (so TT, cao {h}) va {dim} (DIM, dimstyle dung nhieu nhat trong ban ve).
; So TT va chu tren DIM trung voi sheet "Theo mau" cua file Excel cung lan chay.
; Khong dung ActiveX (vla-*) de chay duoc ca trong AutoCAD Core Console (accoreconsole).
(defun dhcb:layer (name col)
  (if (not (tblsearch "LAYER" name))
    (entmake (list '(0 . "LAYER") '(100 . "AcDbSymbolTableRecord") '(100 . "AcDbLayerTableRecord")
                   (cons 2 name) '(70 . 0) (cons 62 col)))))
(defun dhcb:top-dimstyle (/ ss i s a al)
  (if (setq ss (ssget "_X" '((0 . "DIMENSION"))))
    (progn
      (setq i 0)
      (repeat (sslength ss)
        (setq s (cdr (assoc 3 (entget (ssname ss i)))))
        (if (setq a (assoc s al)) (setq al (subst (cons s (1+ (cdr a))) a al)) (setq al (cons (cons s 1) al)))
        (setq i (1+ i)))
      (car (car (vl-sort al '(lambda (x y) (> (cdr x) (cdr y)))))))))
(defun dhcb:t (x y a s)
  (entmake (list '(0 . "TEXT") (cons 8 "{stt}") (list 10 x y 0.0) (list 11 x y 0.0) (cons 40 {h})
                 (cons 1 s) (cons 50 a) '(72 . 1) '(73 . 2))))
(defun dhcb:d (x1 y1 x2 y2 x3 y3 s / e)
  (command "_.DIMALIGNED" (list x1 y1) (list x2 y2) (list x3 y3))
  (if (setq e (entlast)) (entmod (subst (cons 1 s) (assoc 1 (entget e)) (entget e)))))
(defun dhcb:run (/ old ds)
  (setq old (list (getvar "CMDECHO") (getvar "OSMODE") (getvar "CLAYER")))
  (setvar "CMDECHO" 0)
  (command "_.UNDO" "_BE")
  (setvar "OSMODE" 0)
  (dhcb:layer "{stt}" 1) (dhcb:layer "{dim}" 3)
  (if (setq ds (dhcb:top-dimstyle)) (command "_.-DIMSTYLE" "_R" ds))
  (setvar "CLAYER" "{dim}")
'''
LSP_TAIL = r'''  (setvar "OSMODE" (nth 1 old)) (setvar "CLAYER" (nth 2 old))
  (command "_.UNDO" "_E")
  (setvar "CMDECHO" (nth 0 old))
  (princ "\nDa ghi {n} so TT va {m} DIM. Undo mot lan de bo."))
(defun c:DHCB_KL_GHI ()
  (initget "Yes No")
  (if (= (getkword "\nGhi {n} so TT va {m} DIM len layer {stt} / {dim}? [Yes/No] <No>: ") "Yes")
    (dhcb:run)
    (princ "\nDa huy, khong ghi gi."))
  (princ))
(defun c:DHCB_KL_XOA (/ ss)
  (initget "Yes No")
  (if (and (= (getkword "\nXoa toan bo doi tuong tren {stt} va {dim}? [Yes/No] <No>: ") "Yes")
           (setq ss (ssget "_X" '((8 . "{stt},{dim}")))))
    (command "_.ERASE" ss ""))
  (princ))
(princ "\nDHCB: go DHCB_KL_GHI de ghi so TT + DIM, DHCB_KL_XOA de xoa.")
(princ)
'''


def write_lsp(path: Path, sections: list, text_height: float = 150, text_gap: float = 250, dim_gap: float = 450) -> tuple:
    """File AutoLISP ghi số TT (mọi mục: ống, box, co, giảm, gót) và DIM chiều dài (ống thẳng, box) lên bản vẽ.
    Trả (số TT, số DIM).
    Số TT đặt bên trái tuyến (theo chiều u), DIM bên phải, chữ DIM ghi đè đúng chiều dài trong Excel."""
    def f(v):
        return f"{v:.3f}"

    body, n_txt, n_dim = [], 0, 0
    for _, clusters in sections:
        for items in clusters:
            for it in items:
                if it["ma"] in ("C", "G"):  # co, giảm, gót: số đặt giữa block phụ kiện
                    body.append(f'  (dhcb:t {f(it["x"])} {f(it["y"])} 0.0 "{it["tt"]}")')
                    n_txt += 1
                    continue
                r = it["piece"]["run"]
                (ux, uy), half = r["u"], r["w"] / 2
                nx, ny = -uy, ux
                cx, cy, L = it["x"], it["y"], it["len"]
                ang = math.atan2(uy, ux)
                body.append(f'  (dhcb:t {f(cx + nx * (half + text_gap))} {f(cy + ny * (half + text_gap))} {ang:.6f} "{it["tt"]}")')
                x1, y1 = cx - ux * L / 2 - nx * half, cy - uy * L / 2 - ny * half
                x2, y2 = cx + ux * L / 2 - nx * half, cy + uy * L / 2 - ny * half
                body.append(f'  (dhcb:d {f(x1)} {f(y1)} {f(x2)} {f(y2)} {f(cx - nx * (half + dim_gap))} '
                            f'{f(cy - ny * (half + dim_gap))} "{L}")')
                n_txt += 1
                n_dim += 1
    text = LSP_HEAD + "\n".join(body) + "\n" + LSP_TAIL
    for k, v in {"stt": LSP_STT, "dim": LSP_DIM, "h": f"{text_height:.1f}", "n": n_txt, "m": n_dim}.items():
        text = text.replace("{" + k + "}", str(v))
    path.write_bytes(text.replace("\n", "\r\n").encode("ascii"))  # AutoCAD đọc .lsp ANSI: giữ ASCII thuần
    return n_txt, n_dim


def count_fittings(geometry: list, bbox=None, prefix: str = "M-HVAC-DUCT") -> collections.Counter:
    c = collections.Counter()
    for e in geometry:
        if e.get("type") != "BlockReference" or not e.get("layer", "").startswith(prefix):
            continue
        p = _center(e)  # cùng tiêu chí với fitting_boxes để số co ở trang Phụ kiện khớp trang Co
        if not p or not in_box(p[0], p[1], bbox):
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


def _sys_key(s):
    return ORDER.index(s) if s in ORDER else len(ORDER)


def build_sheets(pieces: list, elbows: list, unsized: list, fittings: collections.Counter, source: str,
                 waste: float, sections: list = ()) -> list:
    D, C = "'Chi tiết'!", "Co!"
    # Chi tiết — header dòng 3, dữ liệu từ dòng 4. A STT · B Mã · C Tuyến · D Hệ · E KT · F R · G C · H Nẹp
    # · I Đốt tối đa · J Loại · K Dài mm · L Dài m · M Chu vi · N Diện tích · O X · P Y · Q Handle · R Cách tag · S Ghi chú
    det = [["CHI TIẾT TỪNG ĐỐT ỐNG GIÓ — đốt chuẩn theo nẹp (TDC 1110, C/V 1180), đoạn bù, box bít đầu"], [],
           ["STT", "Mã đốt", "Tuyến", "Hệ", "Kích thước", "R (mm)", "C (mm)", "Nẹp", "Đốt tối đa (mm)", "Loại",
            "Dài (mm)", "Dài (m)", "Chu vi (m)", "Diện tích (m²)", "X tâm", "Y tâm", "Handle cạnh tuyến",
            "Cách tag (mm)", "Ghi chú", "Bọc / ghi thêm (tag)", "Độ dày tole (mm)"]]
    for p in pieces:
        r = len(det) + 1
        cap = f"+F{r}*G{r}/1000000" if p["kind"] == BOX_LABEL else ""  # box bít một đầu: cộng tấm bít R×C
        det.append([p["stt"], p["code"], p["runCode"], p["sys"], f"{p['W']}x{p['H']}", p["W"], p["H"], p["joint"],
                    p["dot"], p["kind"], p["len"], F(f"K{r}/1000"), F(f"2*(F{r}+G{r})/1000"),
                    F(f"ROUND(L{r}*M{r}{cap},3)"), round(p["x"]), round(p["y"]), p["handle"], p["tagDist"], p["note"],
                    " ".join(x for x in (p.get("ei", ""), p.get("spec", "") if "EI" not in p.get("spec", "") else "") if x) or None,
                    p.get("tole", tole_thickness((p["W"], p["H"]), p.get("ei", "")))])
    last = len(det)
    det.append([None] * 10 + ["Tổng", F(f"SUM(L4:L{last})"), None, F(f"SUM(N4:N{last})")])

    # Co — header dòng 3. A STT · B Mã · C Loại · D Hệ · E KT · F R · G C · H Góc · I Nẹp · J Dài tim quy đổi
    # · K Chu vi · L Diện tích · M X · N Y · O Family · P Cổ co (đoạn chéo) · Q Số nửa block
    co = [["CO ỐNG GIÓ — diện tích = 2×(R+C) × chiều dài tim quy đổi; tim quy đổi = R × tan(góc/2) (co 90° = R)"], [],
          ["STT", "Mã co", "Loại", "Hệ", "Kích thước", "R (mm)", "C (mm)", "Góc (°)", "Nẹp", "Dài tim quy đổi (m)",
           "Chu vi (m)", "Diện tích (m²)", "X tâm", "Y tâm", "Family Revit", "Cổ co / đoạn chéo (mm)", "Số nửa block",
           "Độ dày tole (mm)"]]
    for e in elbows:
        r = len(co) + 1
        co.append([e["stt"], e["code"], f"Co {e['angle']}°", e["sys"] or "?",
                   f"{e['W']}x{e['H']}" if e["W"] else "?", e["W"], e["H"], e["angle"], e["joint"],
                   F(f"ROUND(F{r}*TAN(RADIANS(H{r}/2))/1000,3)"), F(f"2*(F{r}+G{r})/1000"), F(f"ROUND(J{r}*K{r},3)"),
                   round(e["x"]), round(e["y"]), e["family"], e["R"], e["halves"],
                   tole_thickness((e["W"], e["H"]), ei_class(e["sys"] or "")) if e["W"] else None])
    co.append([None, "Tổng", None, None, None, None, None, None, None, None, None, F(f"SUM(L4:L{len(co)})")])

    # Tổng hợp — header dòng 5, dữ liệu từ dòng 6. Cột A..S như header.
    tot = [["BẢNG KHỐI LƯỢNG ỐNG GIÓ — ĐỐT, BOX BÍT ĐẦU, CO"], [f"Nguồn: {source}"],
           [None] * 17 + ["Hao hụt", waste], [],
           ["STT", "Hệ thống", "Mã", "Kích thước R×C (mm)", "R (mm)", "C (mm)", "Nẹp", "Đốt tối đa (mm)",
            "Số đốt chuẩn", "Số đoạn bù", "Dài ống (m)", "Chu vi (m)", "DT ống (m²)", "Số box bít đầu", "DT box (m²)",
            "Số co", "DT co (m²)", "Tổng DT (m²)", "DT có hao hụt (m²)"]]
    styles_tot = {1: BOLD, 5: HEAD}
    info = collections.OrderedDict()
    for p in pieces:
        info.setdefault((p["sys"], p["W"], p["H"]), (p["joint"], p["dot"]))
    for e in elbows:
        if e["sys"]:
            info.setdefault((e["sys"], e["W"], e["H"]), (e["joint"], MAX_DOT.get(e["joint"], "")))
    groups = sorted(info, key=lambda k: (_sys_key(k[0]), -k[1] * k[2], k[1], k[2]))
    cur, n = None, 0
    for s, w, h in groups:
        if s != cur:
            cur = s
            tot.append([None, SYSTEMS.get(s, s)])
            styles_tot[len(tot)] = BOLD
        n += 1
        r = len(tot) + 1
        key = f"{D}D:D,C{r},{D}E:E,D{r}"
        joint, dot = info[(s, w, h)]
        tot.append([n, SYSTEMS.get(s, s), s, f"{w}x{h}", w, h, joint, dot,
                    F(f'COUNTIFS({key},{D}J:J,"{STD_LABEL}")'), F(f'COUNTIFS({key},{D}J:J,"{FILL_LABEL}")'),
                    F(f'SUMIFS({D}L:L,{key},{D}J:J,"<>{BOX_LABEL}")'), F(f"2*(E{r}+F{r})/1000"), F(f"ROUND(K{r}*L{r},2)"),
                    F(f'COUNTIFS({key},{D}J:J,"{BOX_LABEL}")'), F(f'SUMIFS({D}N:N,{key},{D}J:J,"{BOX_LABEL}")'),
                    F(f"COUNTIFS({C}D:D,C{r},{C}E:E,D{r})"), F(f"SUMIFS({C}L:L,{C}D:D,C{r},{C}E:E,D{r})"),
                    F(f"M{r}+O{r}+Q{r}"), F(f"ROUND(R{r}*(1+$S$3),2)")])
    end = len(tot)
    tot.append([None, "TỔNG CỘNG"] + [None] * 6 + [F(f"SUM({c}6:{c}{end})") for c in "IJK"] + [None]
               + [F(f"SUM({c}6:{c}{end})") for c in "MNOPQRS"])
    styles_tot[len(tot)] = TOTAL
    tot += [[], [None, "THEO ĐỘ DÀY TOLE", None, None, None, None, None, None, None, None, None, None, "DT ống + box (m²)",
                 None, None, None, "DT co (m²)", "Tổng DT (m²)", "DT có hao hụt (m²)"]]
    styles_tot[len(tot)] = BOLD
    for thick in (0.95, 0.75, TOLE_MIN):
        r = len(tot) + 1
        tot.append([None, f"Tole dày {thick} mm", None, None, None, None, None, None, None, None, None, None,
                    F(f"SUMIFS({D}N:N,{D}U:U,{thick})"), None, None, None, F(f"SUMIFS({C}L:L,{C}R:R,{thick})"),
                    F(f"M{r}+Q{r}"), F(f"ROUND(R{r}*(1+$S$3),2)")])
    tot += [[None, "Tole: cạnh lớn nhất > 1250 mm → 0.95; > 500 mm → 0.75; ≤ 500 mm → 0.58; ống bọc EI luôn 0.75 (SEAD không ghi → EI60)."]]
    tot += [[], [None, "Đốt tối đa theo nẹp: TDC 1110 mm, nẹp C / bích V 1180 mm. Box bít đầu sản xuất riêng, tính cả tấm bít R×C."],
            [None, "Diện tích ống = 2 × (R + C) × chiều dài; co theo trang Co. Đổi ô S3 để thay tỉ lệ hao hụt."]]

    # Đoạn bù & box — gom theo loại, hệ, kích thước, chiều dài.
    fill = [["ĐOẠN BÙ VÀ BOX BÍT ĐẦU — gom theo kích thước và chiều dài"], [],
            ["STT", "Loại", "Hệ", "Kích thước", "Dài (mm)", "Số lượng", "Tổng dài (m)", "Diện tích (m²)"]]
    keys = sorted({(p["kind"], p["sys"], p["W"], p["H"], p["len"]) for p in pieces if p["kind"] != STD_LABEL},
                  key=lambda k: (k[0] != BOX_LABEL, _sys_key(k[1]), -k[2] * k[3], k[2], k[3], k[4]))
    for i, (kind, s, w, h, length) in enumerate(keys, 1):
        r = len(fill) + 1
        crit = f'{D}J:J,B{r},{D}D:D,C{r},{D}E:E,D{r},{D}K:K,E{r}'
        fill.append([i, kind, s, f"{w}x{h}", length, F(f"COUNTIFS({crit})"), F(f"E{r}*F{r}/1000"),
                     F(f"SUMIFS({D}N:N,{crit})")])
    fill.append([None, "Tổng", None, None, None] + [F(f"SUM({c}4:{c}{len(fill)})") for c in "FGH"])

    fit = [["THỐNG KÊ PHỤ KIỆN ỐNG GIÓ (đếm block trên layer ống)"], [],
           ["STT", "Loại phụ kiện", "Family Revit", "Số lượng", "ĐVT"]]
    for i, (k, v) in enumerate(fittings.most_common(), 1):
        fit.append([i, FITTINGS.get(k, k), k, v, "cái"])
    fit.append([None, "Tổng", None, F(f"SUM(D4:D{len(fit)})")])

    un = [["TUYẾN CHƯA GÁN ĐƯỢC KÍCH THƯỚC — kiểm tay (côn, chạc, cách nhiệt…)"], [],
          ["STT", "Khoảng cách 2 cạnh (mm)", "Dài (m)", "X tâm", "Y tâm", "Layer", "Handle cạnh 1", "Handle cạnh 2"]]
    for i, r in enumerate(sorted(unsized, key=lambda r: (r["w"], r["items"][0]["mid"][0])), 1):
        p = r["items"][0]
        un.append([i, r["w"], round((r["t1"] - r["t0"]) / 1000, 3), round(p["mid"][0]), round(p["mid"][1]),
                   p["layer"], p["a"], p["b"]])
    un.append([None, "Tổng", F(f"SUM(C4:C{len(un)})")])

    notes = [["CÁCH TÍNH"],
             ["1. Hai cạnh Line song song trên layer ống (bỏ CNTR/ANNO/IDEN), cách 80–2000 mm, chồng ≥ 60 % → một mảnh; bỏ mảnh nằm trọn trong hộp bao phụ kiện."],
             ["2. Mảnh cùng trục, cùng bề rộng nối thành tuyến (khe ≤ 80 mm, hoặc ≤ 300 mm có block mối nối)."],
             ["3. R×C lấy từ tag ống (SEAD/SAD/RAD/FAD/EAD/PAD - RxC) gần nhất có một cạnh khớp bề rộng (±30 mm, ≤ 8 m); cạnh vẽ theo mép bích rộng hơn tag tới 60 mm vẫn nhận. Phần ghi sau dấu + (EI120, TIÊU ÂM…) ghi vào cột Bọc."],
             ["4. Nẹp theo block mối nối trên tuyến (Rec_TDC_Union / Rec_C_Union); không có block thì nhận bích TDC vẽ bằng 2–3 nét ngang cách nhau ~20 mm; tuyến không có mối nối lấy theo tuyến cùng kích thước, rồi cùng hệ, rồi theo cạnh lớn (< 2000 TDC, ≥ 2000 V)."],
             [f"5. Tuyến cắt tại tâm mối nối. Khoảng dài hơn L max quá {LEN_TOL} mm chia L max từ đầu khoảng; đoạn cuối < {SCRAP_LEN} mm cộng đốt trước rồi chia đôi; có chân rẽ/miệng gió bên hông được tới {TAP_MAX} mm (quy tắc BKL)."],
             [f"   L max: TDC 1110, C/V 1180. Làm tròn {LEN_STEP} mm gần nhất như số dim; từ L max − 10 tới L max + {LEN_TOL} tính bằng L max = đốt chuẩn."],
             [f"6. Khoảng cuối ở đầu kín (nắp bịt hoặc không nối gì) dài hơn L max, tới {TAP_MAX} (+{LEN_TOL}) mm = box bít đầu (sản xuất riêng, cộng tấm bít R×C), không chia."],
             ["7. Co: block Elbow; hai nửa co 45° nối nhau bằng đoạn chéo ngắn gộp thành một co (đoạn chéo là cổ co, không tính ống); góc theo hướng hai tuyến nối vào, chỉ một tuyến thì theo tên family; R×C và nẹp theo tuyến nối vào; DT = 2(R+C) × R·tan(góc/2)."],
             ["8. Diện tích tôn = 2 × (R + C) / 1000 × chiều dài (m²); hao hụt theo ô S3 trang Tổng hợp."],
             [], ["GIỚI HẠN — kỹ sư kiểm trước khi dùng"],
             ["• Không tính ống vẽ bằng polyline/spline/trong block, chi tiết xref ghép, khung ngoài vùng --bbox."],
             ["• Côn, tê, chân rẽ, van chỉ đếm số lượng (trang Phụ kiện); chưa tính m²."],
             ["• Đoạn có ghi chú 'có chân rẽ/miệng gió bên hông' dài hơn L max theo quy tắc chân rẽ — xem lại vị trí chân rẽ."],
             ["• Co không nối tuyến nào có kích thước để '?' — không vào trang Tổng hợp."]]
    sample = [("Theo mẫu", *sample_rows(sections))] if sections else []
    return [("Tổng hợp", tot, [6, 34, 8, 18, 8, 8, 7, 10, 10, 10, 11, 10, 12, 10, 11, 8, 11, 12, 14], styles_tot)] + sample + [
            ("Chi tiết", det, [6, 11, 7, 7, 12, 8, 8, 6, 10, 12, 9, 9, 10, 12, 12, 12, 16, 12, 24, 22, 10], {1: BOLD, 3: HEAD, len(det): TOTAL}),
            ("Đoạn bù & box", fill, [6, 13, 8, 12, 10, 10, 12, 14], {1: BOLD, 3: HEAD, len(fill): TOTAL}),
            ("Co", co, [6, 9, 9, 7, 12, 8, 8, 8, 6, 12, 10, 12, 12, 12, 30, 12, 9, 10], {1: BOLD, 3: HEAD, len(co): TOTAL}),
            ("Phụ kiện", fit, [6, 28, 30, 10, 7], {1: BOLD, 3: HEAD, len(fit): TOTAL}),
            ("Chưa xác định", un, [6, 14, 10, 12, 12, 16, 13, 13], {1: BOLD, 3: HEAD, len(un): TOTAL}),
            ("Ghi chú", notes, [140], {1: BOLD, 11: BOLD})]


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
    # Text cũng phân trang: bản vẽ gộp nhiều tầng dễ quá 5000 chữ, cắt mất tag của khung nằm sau.
    texts, offset = [], 0
    while offset is not None:
        t = agent.request(app, "POST", "/query", {"query": "text", "params": {"limit": 2000, "offset": offset}}, timeout=120)
        if "texts" not in t:
            raise RuntimeError(t.get("error") or "query text không trả dữ liệu")
        texts += t["texts"]
        offset = t.get("nextOffset") if t.get("hasMore") else None
    # Mọi block: mối nối/phụ kiện nằm trên layer ống, miệng gió thường ở layer riêng (cần để nhận chân rẽ bên hông).
    handles = [e["handle"] for e in entities if e.get("type") == "BlockReference"]
    geometry = []
    for i in range(0, len(handles), chunk):
        for _ in range(retries):
            g = agent.request(app, "POST", "/query", {"query": "entity_geometry", "params": {"handles": handles[i:i + chunk]}}, timeout=120)
            if "entities" in g:
                geometry += g["entities"]
                break
            time.sleep(pause)
    info = agent.request(app, "POST", "/query", {"query": "drawing_info"}, timeout=60)
    # Đường dẫn do AutoCAD (Windows) trả về; PureWindowsPath giữ đúng quy tắc ổ đĩa và dấu phân cách Windows.
    return entities, texts, geometry, PureWindowsPath(info.get("filename") or "").name


def load_json(path: str, key: str = "") -> list:
    data = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    return data[key] if key and isinstance(data, dict) else data


def parse_bbox(text: str):
    parts = [float(v) for v in text.split(",")]
    if len(parts) != 4:
        raise ValueError("--bbox cần 4 số: xmin,ymin,xmax,ymax")
    return (min(parts[0], parts[2]), min(parts[1], parts[3]), max(parts[0], parts[2]), max(parts[1], parts[3]))


def main(argv=None, agent=None) -> int:
    ap = argparse.ArgumentParser(description="Bóc khối lượng ống gió theo đốt từ bản vẽ AutoCAD → Excel")
    ap.add_argument("-o", "--output", required=True, help="file .xlsx")
    ap.add_argument("--bbox", help="khung mặt bằng xmin,ymin,xmax,ymax (đơn vị bản vẽ) — nên luôn truyền")
    ap.add_argument("--layer-prefix", default="M-HVAC-DUCT")
    ap.add_argument("--waste", type=float, default=0.05, help="tỉ lệ hao hụt tôn (mặc định 0.05)")
    ap.add_argument("--entities", help="JSON đầu ra query entities (bỏ trống = đọc trực tiếp qua Bridge)")
    ap.add_argument("--texts", help="JSON đầu ra query text")
    ap.add_argument("--geometry", help="JSON đầu ra entity_geometry cho block mối nối/phụ kiện")
    ap.add_argument("--source", default="", help="tên bản vẽ ghi vào bảng")
    ap.add_argument("--lsp", help="ghi thêm file AutoLISP vẽ số TT + DIM chiều dài đốt lên bản vẽ (lệnh DHCB_KL_GHI)")
    ap.add_argument("--text-height", type=float, default=150, help="cao chữ số TT trong file LISP (mặc định 150)")
    ap.add_argument("--order", choices=("trai-phai", "truc"), default="trai-phai",
                    help="thứ tự cụm trang Theo mẫu: trai-phai = theo vị trí trên mặt bằng (mặc định); truc = gom theo trục/hệ")
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
    joints = joint_points(geometry, bbox)
    boxes = fitting_boxes(geometry, bbox, args.layer_prefix)
    flanges, segs = flange_joints(duct_segments(entities, bbox, args.layer_prefix))
    joints = joints + [f for f in flanges if not any(math.hypot(f[0] - x, f[1] - y) <= 80 for x, y, _ in joints)]
    pairs = drop_inside_fittings(pair_segments(segs), boxes)
    if not joints:
        print("Cảnh báo: không có block mối nối hay nét bích (thiếu --geometry?) — cắt đốt theo khe giữa các mảnh cạnh.", file=sys.stderr)
    runs = build_runs(assign_sizes(pairs, parse_tags(texts, bbox)), joints)
    elbows = find_elbows(boxes, runs)  # trước split_runs: đánh dấu đoạn chéo cổ co để không tính thành ống
    pieces = number_pieces(split_runs(runs, joints, boxes))
    unsized = [r for r in runs if not r["size"]]
    fittings = count_fittings(geometry, bbox, args.layer_prefix)
    if not pieces:
        print("Không tìm thấy đoạn ống nào có kích thước — kiểm --bbox và --layer-prefix.", file=sys.stderr)
        return 1
    sections = build_sections(runs, pieces, elbows, boxes, shaft_labels(texts, bbox), args.order)
    write_xlsx(Path(args.output), build_sheets(pieces, elbows, unsized, fittings, source, args.waste, sections))

    kinds = collections.Counter(p["kind"] for p in pieces)
    length = sum(p["len"] for p in pieces if p["kind"] != BOX_LABEL) / 1000
    area = sum(2 * (p["W"] + p["H"]) / 1e6 * p["len"] + (p["W"] * p["H"] / 1e6 if p["kind"] == BOX_LABEL else 0)
               for p in pieces)
    area += sum(2 * (e["W"] + e["H"]) / 1e6 * e["W"] * math.tan(math.radians(e["angle"] / 2)) for e in elbows if e["W"])
    print(f"Đã ghi {args.output}: {kinds[STD_LABEL]} đốt chuẩn, {kinds[FILL_LABEL]} đoạn bù, {kinds[BOX_LABEL]} box bít đầu "
          f"trên {len({id(p['run']) for p in pieces})} tuyến ({length:.2f} m ống), {len(elbows)} co, {area:.2f} m² tôn "
          f"(+{args.waste:.0%}: {area * (1 + args.waste):.2f} m²), {sum(fittings.values())} phụ kiện, "
          f"{len(unsized)} tuyến chưa xác định ({sum(r['t1'] - r['t0'] for r in unsized) / 1000:.2f} m).")
    if args.lsp:
        n_txt, n_dim = write_lsp(Path(args.lsp), sections, args.text_height)
        print(f"Đã ghi {args.lsp}: {n_txt} số TT, {n_dim} DIM — APPLOAD rồi gõ DHCB_KL_GHI (xoá: DHCB_KL_XOA).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
