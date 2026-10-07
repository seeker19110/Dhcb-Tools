"""Sinh bảng thuộc tính theo lược đồ IFC (IFC2X3, IFC4, IFC4X3_ADD2) cho bộ kiểm IDS trên đường file IFC.

Facet <attribute> của IDS hỏi thuộc tính bất kỳ của bất kỳ lớp nào (IfcTask.IsMilestone,
IfcSurfaceStyleRefraction.RefractionIndex…). Muốn trả lời đúng phải biết vị trí và kiểu của từng thuộc tính —
tức lược đồ. Script đọc lược đồ từ IfcOpenShell và ghi một bảng văn bản gọn, nhúng vào DhcbTools.Shared.Logic
(IfcSchemaTable đọc). Chỉ chạy lại khi đổi/thêm lược đồ; kết quả được commit, CI không cần IfcOpenShell.

Định dạng mỗi dòng (sau dòng "@SCHEMA"):  LỚP<CHA|Tên:K,Tên:K|~TênDẫnXuất,…
  K: S chuỗi, R số thực, I số nguyên, B boolean, L logical, E enum, N thực thể, X select, A tập hợp, Y nhị phân.
  Thuộc tính liệt kê là thuộc tính RIÊNG của lớp (cha nối vào trước); "~Tên" = thuộc tính của lớp cha mà lớp
  này khai lại thành dẫn xuất (ghi "*" trong STEP, IDS không kiểm được).

Dùng:  pip install ifcopenshell && python scripts/gen-ifc-schema.py
"""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src" / "DhcbTools.Shared.Logic" / "Ifc" / "ifc-schema.txt"
SCHEMAS = ["IFC2X3", "IFC4", "IFC4X3_ADD2"]
SIMPLE = {"string": "S", "real": "R", "number": "R", "integer": "I", "boolean": "B", "logical": "L", "binary": "Y"}


def kind(attribute_type) -> str:
    """Mã kiểu nền: đi qua chuỗi kiểu khai báo (IfcPositiveRatioMeasure → IfcRatioMeasure → real) tới kiểu cuối."""
    current = attribute_type
    for _ in range(32):
        text = str(current)
        for prefix, code in (("<entity", "N"), ("<select", "X"), ("<enumeration", "E")):
            if text.startswith(prefix):
                return code
        if hasattr(current, "type_of_element"):
            return "A"
        if text.strip("<>") in SIMPLE:
            return SIMPLE[text.strip("<>")]
        if not hasattr(current, "declared_type"):
            break
        current = current.declared_type()
    raise ValueError(f"không rõ kiểu nền của {attribute_type}")


def lines_for(schema_name: str) -> list[str]:
    import ifcopenshell.ifcopenshell_wrapper as wrapper

    schema = wrapper.schema_by_name(schema_name)
    out = ["@" + schema_name]
    for entity in sorted(schema.entities(), key=lambda e: e.name().upper()):
        parent = entity.supertype()
        own = ",".join(a.name() + ":" + kind(a.type_of_attribute()) for a in entity.attributes())
        all_attributes = entity.all_attributes()
        own_names = {a.name() for a in entity.attributes()}
        parent_derived = set()
        if parent is not None:
            parent_derived = {a.name() for a, d in zip(parent.all_attributes(), parent.derived()) if d}
        derived = [a.name() for a, d in zip(all_attributes, entity.derived())
                   if d and a.name() not in own_names and a.name() not in parent_derived]
        line = entity.name().upper() + ("<" + parent.name().upper() if parent is not None else "") + "|" + own
        if derived:
            line += "|" + ",".join("~" + name for name in derived)
        out.append(line)
    return out


def main() -> int:
    lines = ["# Sinh bởi scripts/gen-ifc-schema.py từ IfcOpenShell — không sửa tay."]
    for schema in SCHEMAS:
        lines.extend(lines_for(schema))
    OUTPUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"{OUTPUT.relative_to(ROOT)}: {len(lines)} dòng")
    return 0


if __name__ == "__main__":
    sys.exit(main())
