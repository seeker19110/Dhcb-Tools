"""scripts/gen-ifc-schema.py — sinh bảng lược đồ IFC; test không cần IfcOpenShell (giả module lược đồ)."""
import importlib.util
import io
import sys
import tempfile
import types
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("gen_ifc_schema", ROOT / "scripts" / "gen-ifc-schema.py")
gen = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gen)


class Simple:
    def __init__(self, name):
        self.name = name

    def __str__(self):
        return "<" + self.name + ">"


class Named:
    """Kiểu khai báo (IfcLabel → string): đi tiếp qua declared_type()."""

    def __init__(self, name, inner):
        self.label, self.inner = name, inner

    def __str__(self):
        return "<type " + self.label + ": " + str(self.inner) + ">"

    def declared_type(self):
        return self.inner


class Tagged:
    def __init__(self, text):
        self.text = text

    def __str__(self):
        return self.text


class Aggregate:
    def __str__(self):
        return "<list>"

    def type_of_element(self):
        return None


class Attr:
    def __init__(self, name, kind):
        self._name, self.kind = name, kind

    def name(self):
        return self._name

    def type_of_attribute(self):
        return self.kind


class Entity:
    def __init__(self, name, parent, own, derived=()):
        self._name, self.parent, self.own, self.derived_names = name, parent, own, set(derived)

    def name(self):
        return self._name

    def supertype(self):
        return self.parent

    def attributes(self):
        return self.own

    def all_attributes(self):
        return (self.parent.all_attributes() if self.parent else []) + self.own

    def derived(self):
        return tuple(a.name() in self.derived_names for a in self.all_attributes())


def fake_wrapper():
    label = Named("IfcLabel", Simple("string"))
    root = Entity("IfcRoot", None, [Attr("GlobalId", label), Attr("OwnerHistory", Tagged("<entity IfcOwnerHistory>"))])
    item = Entity("IfcItem", root, [Attr("Ratio", Named("IfcPositiveRatioMeasure", Named("IfcRatioMeasure", Simple("real")))),
                                    Attr("Kind", Tagged("<enumeration IfcKindEnum: (A, B)>")),
                                    Attr("Value", Tagged("<select IfcValue>")),
                                    Attr("Items", Aggregate())], derived=["GlobalId"])
    child = Entity("IfcChild", item, [], derived=["GlobalId", "Ratio"])
    schema = types.SimpleNamespace(entities=lambda: [child, root, item])
    return types.SimpleNamespace(schema_by_name=lambda name: schema)


class GenIfcSchemaTests(unittest.TestCase):
    def test_kind_di_qua_chuoi_kieu_khai_bao(self):
        self.assertEqual("R", gen.kind(Named("IfcPositiveRatioMeasure", Named("IfcRatioMeasure", Simple("real")))))
        self.assertEqual("S", gen.kind(Named("IfcLabel", Simple("string"))))
        self.assertEqual("N", gen.kind(Tagged("<entity IfcWall>")))
        self.assertEqual("A", gen.kind(Aggregate()))
        with self.assertRaises(ValueError):
            gen.kind(Tagged("<lạ>"))

    def test_main_ghi_bang_thuoc_tinh_rieng_va_dan_xuat_moi(self):
        wrapper = fake_wrapper()
        package = types.ModuleType("ifcopenshell")
        package.ifcopenshell_wrapper = wrapper
        with tempfile.TemporaryDirectory() as tmp, \
                mock.patch.dict(sys.modules, {"ifcopenshell": package, "ifcopenshell.ifcopenshell_wrapper": wrapper}), \
                mock.patch.object(gen, "ROOT", Path(tmp)), \
                mock.patch.object(gen, "OUTPUT", Path(tmp) / "out.txt"), \
                mock.patch.object(gen, "SCHEMAS", ["IFC4"]):
            out = io.StringIO()
            with redirect_stdout(out):
                self.assertEqual(0, gen.main())
            lines = (Path(tmp) / "out.txt").read_text(encoding="utf-8").splitlines()
        self.assertIn("dòng", out.getvalue())
        self.assertEqual("@IFC4", lines[1])
        self.assertEqual("IFCCHILD<IFCITEM||~Ratio", lines[2])
        self.assertEqual("IFCITEM<IFCROOT|Ratio:R,Kind:E,Value:X,Items:A|~GlobalId", lines[3])
        self.assertEqual("IFCROOT|GlobalId:S,OwnerHistory:N", lines[4])

    def test_bang_da_commit_co_du_ba_luoc_do(self):
        text = gen.OUTPUT.read_text(encoding="utf-8")
        for schema in gen.SCHEMAS:
            self.assertIn("\n@" + schema + "\n", text)
        self.assertIn("\nIFCROOT|GlobalId:S,OwnerHistory:N,Name:S,Description:S\n", text)


if __name__ == "__main__":
    unittest.main()
