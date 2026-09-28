"""Sinh src/DhcbTools.Shared.Logic/Ids/ifc-schemas.txt — bảng lược đồ IFC mà bộ kiểm IDS đường IFC cần.

Mỗi lớp một dòng: TÊN_LỚP  LỚP_CHA  thuộc_tính_riêng… (thuộc tính forward, đúng thứ tự STEP; thuộc tính kế
thừa lấy theo chuỗi lớp cha). Ba lược đồ IDS 1.0 nhắc tới: IFC2X3, IFC4, IFC4X3_ADD2.

Vì sao cần: facet attribute của IDS hỏi thuộc tính theo TÊN (RefractionIndex, NumberOfRisers, EditionDate…),
còn file STEP chỉ có VỊ TRÍ. Bảng tay cũ chỉ có vài lớp nên 20 ca attribute của buildingSMART trượt.

Chỉ chạy khi đổi phiên bản lược đồ — cần ifcopenshell (không phải phụ thuộc lúc chạy của DHCB):
    python -m pip install ifcopenshell
    python tools/ifc-schema/sinh-luoc-do.py
"""
import pathlib
import sys

import ifcopenshell

SCHEMAS = ("IFC2X3", "IFC4", "IFC4X3_ADD2")
OUT = pathlib.Path(__file__).resolve().parents[2] / "src/DhcbTools.Shared.Logic/Ids/ifc-schemas.txt"


def lines(schema_name):
    schema = ifcopenshell.ifcopenshell_wrapper.schema_by_name(schema_name)
    for entity in sorted(schema.entities(), key=lambda e: e.name().upper()):
        parent = entity.supertype()
        own = [a.name() for a in entity.attributes()]
        yield " ".join([entity.name().upper(), parent.name().upper() if parent else "-"] + own)


def main():
    out = [
        "# Sinh bởi tools/ifc-schema/sinh-luoc-do.py (ifcopenshell " + ifcopenshell.version + ") — đừng sửa tay.",
        "# Mỗi dòng: LỚP LỚP_CHA thuộc_tính_forward_riêng… ; '@' mở một lược đồ.",
    ]
    for name in SCHEMAS:
        out.append("@" + name)
        out.extend(lines(name))
    OUT.write_text("\n".join(out) + "\n", encoding="utf-8")
    print(OUT, sum(1 for _ in out), "dòng")
    return 0


if __name__ == "__main__":
    sys.exit(main())
