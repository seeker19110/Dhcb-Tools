#!/usr/bin/env python3
"""Đối chiếu file toạ độ định vị của `SetoutExport` với file IFC do **bộ xuất IFC của Autodesk** ghi ra.

Vì sao cần: `SetoutExport` và bộ ca kiểm trong Revit đều là mã DHCB — chúng chỉ chứng minh lệnh chạy được,
không chứng minh toạ độ đúng. IFC xuất bằng Revit → IFC (Autodesk) là một đường **độc lập**: IFCSITE mang gốc
Survey và góc True North, IFCGRID mang từng trục, IFCCOLUMN mang hình học cột. Khớp nhau dưới vài mm thì tổ
trắc đạc tin được mà không cần ra công trường; lệch thì lộ đúng chỗ (§52: cột "Off Center" lệch tim 305 mm).

Cách dùng:
    python scripts/doi-chieu-setout-ifc.py setout.csv "Model.ifc" [--tol-mm 5]

IFC phải xuất ở toạ độ chung (mặc định của Revit 2024) và CSV ở hệ Survey, đơn vị m (mặc định của lệnh).
Mã thoát 0 = mọi điểm lệch ≤ ngưỡng · 1 = có điểm lệch quá · 2 = thiếu file/đầu vào hỏng.
"""

from __future__ import annotations

import csv
import math
import re
import sys
from pathlib import Path

FT = 0.3048
KEEP = (
    "IFCSITE(", "IFCGRID(", "IFCGRIDAXIS(", "IFCCOLUMN(", "IFCLOCALPLACEMENT(", "IFCAXIS2PLACEMENT3D(",
    "IFCAXIS2PLACEMENT2D(", "IFCCARTESIANPOINT(", "IFCDIRECTION(", "IFCPOLYLINE(", "IFCPRODUCTDEFINITIONSHAPE(",
    "IFCSHAPEREPRESENTATION(", "IFCEXTRUDEDAREASOLID(", "IFCMAPPEDITEM(", "IFCREPRESENTATIONMAP(",
    "IFCCARTESIANTRANSFORMATIONOPERATOR3D(", "IFCRECTANGLEPROFILEDEF(", "IFCCIRCLEPROFILEDEF(",
    "IFCARBITRARYCLOSEDPROFILEDEF(", "IFCARBITRARYPROFILEDEFWITHVOIDS(", "IFCBOOLEANCLIPPINGRESULT(", "IFCBOOLEANRESULT(", "IFCFACETEDBREP(",
    "IFCCLOSEDSHELL(", "IFCFACE(", "IFCFACEOUTERBOUND(", "IFCPOLYLOOP(",
)


def parse(text: str) -> tuple[str, list]:
    """`IFCX(a,'b',(#1,#2))` → ('IFCX', ['a', "'b'", ['#1', '#2']]). Chuỗi trong nháy đơn giữ nguyên."""
    kind = text[: text.index("(")]
    body = text[text.index("(") + 1 : -1]
    out: list = []
    stack = [out]
    tok = ""
    in_str = False
    for ch in body:
        if in_str:
            tok += ch
            in_str = ch != "'"
        elif ch == "'":
            in_str = True
            tok += ch
        elif ch == "(":
            new: list = []
            stack[-1].append(new)
            stack.append(new)
        elif ch in ",)":
            if tok.strip():
                stack[-1].append(tok.strip())
            tok = ""
            if ch == ")":
                stack.pop()
        else:
            tok += ch
    if tok.strip():
        stack[-1].append(tok.strip())
    return kind, out


def ref(token: str) -> int:
    return int(token[1:])


class Ifc:
    """Đọc lười: chỉ giữ dòng của các loại thực thể cần, phân tích khi chạm tới."""

    def __init__(self, lines) -> None:
        self.raw: dict[int, str] = {}
        for line in lines:
            if not line.startswith("#"):
                continue
            i = line.index("=")
            body = line[i + 1 :].strip().rstrip(";")
            # startswith với tuple là một lần gọi C — file 180 MB có 3 triệu dòng, `any(k in line)` mất vài phút.
            if body.startswith(KEEP):
                self.raw[int(line[1:i])] = body
        self._cache: dict[int, tuple[str, list]] = {}

    @classmethod
    def load(cls, path: Path) -> "Ifc":
        with open(path, encoding="utf-8", errors="ignore") as fh:
            return cls(fh)

    def get(self, i: int) -> tuple[str, list]:
        if i not in self._cache:
            self._cache[i] = parse(self.raw[i])
        return self._cache[i]

    def of_type(self, kind: str):
        prefix = kind + "("
        return [i for i, s in self.raw.items() if s.startswith(prefix)]

    def point(self, i: int) -> list[float]:
        p = [float(x) for x in self.get(i)[1][0]]
        return p + [0.0] * (3 - len(p))

    def direction(self, token: str, default: list[float]) -> list[float]:
        return self.point(ref(token)) if token != "$" else default

    def axis3(self, i: int):
        """IFCAXIS2PLACEMENT3D → (gốc, x, y, z)."""
        a = self.get(i)[1]
        loc = self.point(ref(a[0]))
        z = self.direction(a[1], [0.0, 0.0, 1.0])
        x = self.direction(a[2], [1.0, 0.0, 0.0])
        y = [z[1] * x[2] - z[2] * x[1], z[2] * x[0] - z[0] * x[2], z[0] * x[1] - z[1] * x[0]]
        return loc, x, y, z

    @staticmethod
    def apply(frame, p):
        loc, x, y, z = frame
        return [loc[k] + p[0] * x[k] + p[1] * y[k] + p[2] * z[k] for k in range(3)]

    def world(self, placement: int, p: list[float]) -> list[float]:
        """Đưa điểm cục bộ lên toạ độ chung qua chuỗi IFCLOCALPLACEMENT."""
        pid: int | None = placement
        while pid is not None:
            a = self.get(pid)[1]
            p = self.apply(self.axis3(ref(a[1])), p)
            pid = ref(a[0]) if a[0] != "$" else None
        return p

    # ── IFCSITE ──
    def site_origin(self):
        """(E m, N m, Z m, góc độ) của gốc nội bộ Revit trong hệ Survey — đúng dòng "Site … gốc nội bộ" của lệnh."""
        site = self.of_type("IFCSITE")[0]
        placement = ref(self.get(site)[1][5])
        loc, x, _, _ = self.axis3(ref(self.get(placement)[1][1]))
        return loc[0] * FT, loc[1] * FT, loc[2] * FT, math.degrees(math.atan2(x[1], x[0]))

    # ── Giao trục ──
    def grid_intersections(self) -> list[tuple[float, float]]:
        axes = []
        for grid in self.of_type("IFCGRID"):
            g = self.get(grid)[1]
            placement = ref(g[5])
            # Chỉ lấy trục mà CHÍNH grid này tham chiếu (UAxes/VAxes/WAxes). Lấy mọi IFCGRIDAXIS cho mỗi grid
            # thì file nhiều grid nhân số cặp lên hàng chục triệu — MemoryError trên Snowdon.
            for axis in [ref(t) for group in g[7:10] if isinstance(group, list) for t in group]:
                poly = ref(self.get(axis)[1][1])
                pts = [self.world(placement, self.point(ref(r))) for r in self.get(poly)[1][0]]
                if len(pts) == 2:
                    axes.append(pts)
        out = []
        for i in range(len(axes)):
            for j in range(i + 1, len(axes)):
                q = segment_intersection(axes[i], axes[j])
                if q is not None:
                    out.append(q)
        return out

    # ── Cột ──
    def item_points(self, item: int) -> list[list[float]]:
        """Đỉnh hình học của một representation item, trong hệ cục bộ của phần tử; loại không đọc → rỗng."""
        if item not in self.raw:
            return []
        kind, a = self.get(item)
        if kind == "IFCMAPPEDITEM":
            rmap = self.get(ref(a[0]))[1]
            frame = self.axis3(ref(rmap[0]))
            op = self.get(ref(a[1]))[1]
            ox = self.direction(op[0], [1.0, 0.0, 0.0])
            oy = self.direction(op[1], [0.0, 1.0, 0.0])
            oloc = self.point(ref(op[2]))
            scale = float(op[3]) if op[3] != "$" else 1.0
            oz = self.direction(op[4], [0.0, 0.0, 1.0]) if len(op) > 4 else [0.0, 0.0, 1.0]
            pts = []
            for it in self.get(ref(rmap[1]))[1][3]:
                pts += [self.apply(frame, q) for q in self.item_points(ref(it))]
            return [[oloc[k] + scale * (q[0] * ox[k] + q[1] * oy[k] + q[2] * oz[k]) for k in range(3)] for q in pts]
        if kind == "IFCEXTRUDEDAREASOLID":
            frame = self.axis3(ref(a[1]))
            return [self.apply(frame, q) for q in self.profile_points(ref(a[0]))]
        if kind in ("IFCBOOLEANCLIPPINGRESULT", "IFCBOOLEANRESULT"):
            return self.item_points(ref(a[1]))
        return self.reachable_points(item)

    def profile_points(self, profile: int) -> list[list[float]]:
        if profile not in self.raw:
            return []
        kind, a = self.get(profile)
        if kind in ("IFCRECTANGLEPROFILEDEF", "IFCCIRCLEPROFILEDEF"):
            pos = self.get(ref(a[2]))[1]
            c = self.point(ref(pos[0]))
            xd = self.direction(pos[1], [1.0, 0.0]) if len(pos) > 1 else [1.0, 0.0]
            yd = [-xd[1], xd[0]]
            if kind == "IFCRECTANGLEPROFILEDEF":
                w, h = float(a[3]) / 2, float(a[4]) / 2
                return [[c[0] + sx * w * xd[0] + sy * h * yd[0], c[1] + sx * w * xd[1] + sy * h * yd[1], 0.0]
                        for sx in (-1, 1) for sy in (-1, 1)]
            r = float(a[3])
            return [[c[0] + r * dx, c[1] + r * dy, 0.0] for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))]
        if kind in ("IFCARBITRARYCLOSEDPROFILEDEF", "IFCARBITRARYPROFILEDEFWITHVOIDS") and ref(a[2]) in self.raw:
            return [self.point(ref(r)) for r in self.get(ref(a[2]))[1][0]]
        return []

    def reachable_points(self, item: int) -> list[list[float]]:
        """B-rep và loại chưa xử lý: gom mọi IFCCARTESIANPOINT chạm tới được (không có transform lồng)."""
        seen: set[int] = set()
        out = []
        stack = [item]
        while stack:
            i = stack.pop()
            if i in seen or i not in self.raw:
                continue
            seen.add(i)
            kind, a = self.get(i)
            if kind == "IFCCARTESIANPOINT":
                out.append(self.point(i))
                continue
            todo = list(a)
            while todo:
                v = todo.pop()
                if isinstance(v, list):
                    todo += v
                elif v.startswith("#"):
                    stack.append(ref(v))
        return out

    def column_centres(self):
        """[(tên, E m, N m)] — tâm hộp bao trên mặt bằng của mỗi IFCCOLUMN; bỏ cột không có hình học."""
        out = []
        for col in self.of_type("IFCCOLUMN"):
            a = self.get(col)[1]
            placement = ref(a[5])
            pts = []
            for rep in self.get(ref(a[6]))[1][2]:
                r = self.get(ref(rep))[1]
                if r[1].strip("'") != "Body":
                    continue
                for it in r[3]:
                    pts += self.item_points(ref(it))
            if not pts:
                continue
            w = [self.world(placement, q) for q in pts]
            xs = [q[0] for q in w]
            ys = [q[1] for q in w]
            name = a[2].strip("'")
            if clipped(name, (max(xs) - min(xs)) * FT * 1000, (max(ys) - min(ys)) * FT * 1000):
                name += " [thân IFC bị cắt bởi join]"
            out.append((name, (min(xs) + max(xs)) / 2 * FT, (min(ys) + max(ys)) / 2 * FT))
        return out


NOMINAL = re.compile(r'(\d+(?:\.\d+)?)"\s*[DW]\s*x\s*(\d+(?:\.\d+)?)"\s*[DW]', re.IGNORECASE)


def clipped(type_name: str, width_mm: float, depth_mm: float, tol_mm: float = 10.0) -> bool:
    """Thân cột trong IFC nhỏ hơn kích thước danh nghĩa trong tên type (24"D x 24"W) → Revit đã cắt phần
    nối vào tường/sàn khi xuất; tâm thân đó KHÔNG phải tim cột, phải để riêng khi so."""
    m = NOMINAL.search(type_name)
    if not m:
        return False
    a, b = sorted(float(v) * 25.4 for v in m.groups())
    got = sorted((width_mm, depth_mm))
    return got[0] < a - tol_mm or got[1] < b - tol_mm


def segment_intersection(a, b):
    (x1, y1, _), (x2, y2, _) = a
    (x3, y3, _), (x4, y4, _) = b
    den = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4)
    if abs(den) < 1e-9:
        return None
    t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / den
    u = -((x1 - x2) * (y1 - y3) - (y1 - y2) * (x1 - x3)) / den
    if -1e-6 <= t <= 1 + 1e-6 and -1e-6 <= u <= 1 + 1e-6:
        return ((x1 + t * (x2 - x1)) * FT, (y1 + t * (y2 - y1)) * FT)
    return None


def read_setout(path: Path):
    """[(tên, E m, N m, mô tả)] từ CSV có tiêu đề N,E (thứ tự cột bất kỳ, đơn vị m)."""
    with open(path, encoding="utf-8-sig", newline="") as fh:
        rows = list(csv.DictReader(fh))
    return [(r["Name"], float(r["E"]), float(r["N"]), r.get("Desc", "")) for r in rows]


def nearest_mm(points, targets):
    """Với mỗi điểm: khoảng cách (mm) tới điểm IFC gần nhất, kèm tên."""
    out = []
    for name, e, n, _ in points:
        best = min(targets, key=lambda t: math.hypot(e - t[-2], n - t[-1]))
        out.append((math.hypot(e - best[-2], n - best[-1]) * 1000, name, best[0] if len(best) == 3 else "giao trục"))
    return out


def summarise(label: str, ds, tol_mm: float) -> str:
    if not ds:
        return f"{label}: không có điểm nào để so."
    v = sorted(d for d, _, _ in ds)
    over = [x for x in ds if x[0] > tol_mm]
    line = (f"{label}: {len(ds)} điểm — trung vị {v[len(v) // 2]:.1f} mm, lớn nhất {v[-1]:.1f} mm, "
            f"lệch quá {tol_mm:g} mm: {len(over)}")
    for d, name, target in sorted(over, key=lambda x: -x[0])[:10]:
        line += f"\n  {name}: lệch {d:.0f} mm (IFC gần nhất: {target})"
    return line


def main(argv=None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    tol = 5.0
    if "--tol-mm" in argv:
        i = argv.index("--tol-mm")
        tol = float(argv[i + 1])
        del argv[i : i + 2]
    if len(argv) != 2:
        print(__doc__)
        return 2
    csv_path, ifc_path = Path(argv[0]), Path(argv[1])
    if not csv_path.is_file() or not ifc_path.is_file():
        print(f"Không thấy file: {csv_path if not csv_path.is_file() else ifc_path}")
        return 2

    ifc = Ifc.load(ifc_path)
    e0, n0, z0, angle = ifc.site_origin()
    print(f"IFCSITE (Autodesk): gốc nội bộ ở E={e0:.3f} N={n0:.3f} Z={z0:.3f} m, True North xoay {angle:.4f}° "
          "— so với dòng \"Site … gốc nội bộ\" trong Messages của SetoutExport.")

    points = read_setout(csv_path)
    grids = [p for p in points if p[3].startswith("Grids")]
    columns = [p for p in points if p[3].startswith("Columns")]
    worst = 0.0
    if grids:
        ds = nearest_mm(grids, ifc.grid_intersections())
        print(summarise("Giao trục", ds, tol))
        worst = max(worst, max(d for d, _, _ in ds))
    if columns:
        centres = ifc.column_centres()
        ds = nearest_mm(columns, centres) if centres else []
        whole = [x for x in ds if "bị cắt" not in x[2]]
        cut = [x for x in ds if "bị cắt" in x[2]]
        print(summarise("Tim cột — cột IFC còn nguyên thân", whole, tol))
        if cut:
            print(summarise("Tim cột — cột IFC bị cắt bởi join (chỉ tham khảo, không tính vào mã thoát)", cut, tol))
        if whole:
            worst = max(worst, max(d for d, _, _ in whole))
    return 1 if worst > tol else 0


if __name__ == "__main__":
    sys.exit(main())
