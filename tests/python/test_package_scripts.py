"""Script đi kèm gói phát hành (installer/batchrunner-scripts.txt) và cách release.yml dựng installer.

Trước đây release.yml chép mọi scripts/*.py, *.ps1 vào gói BatchRunner, nên máy kỹ sư nhận cả script ký số, CI và
sửa ruleset của repo. Giờ gói chỉ mang danh sách cho phép; bộ test này giữ danh sách đó đúng: mọi script tài liệu đi
kèm gói nhắc tới đều có, script phụ thuộc lẫn nhau đi cùng nhau, và mọi script trong scripts/ phải được xếp loại —
thêm script mới mà quên quyết định "đi kèm hay không" thì CI đỏ.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
MANIFEST = ROOT / "installer" / "batchrunner-scripts.txt"
RELEASE = ROOT / ".github" / "workflows" / "release.yml"
INSTALLER = ROOT / "installer" / "dhcb-tools.iss"

# Không đi kèm gói — mỗi tên kèm lý do. Script mới phải vào đây hoặc vào danh sách đi kèm.
NOT_SHIPPED = {
    "sign-addin.ps1": "ký số — cần chứng chỉ của tổ chức",
    "sign-release.ps1": "ký số trong release.yml",
    "check-coverage.py": "CI",
    "ids-conformance.py": "CI — bộ ca buildingSMART",
    "gen-ifc-schema.py": "sinh bảng lược đồ IFC từ IfcOpenShell — chỉ khi đổi lược đồ",
    "muc-luc-bang-chung.py": "CI — mục lục tài liệu",
    "apply-rulesets.py": "quản trị repo GitHub",
    "fix-ruleset.py": "quản trị repo GitHub",
    "pack-mcpb.ps1": "đóng gói .mcpb từ cây thư mục repo",
    "dung-family.ps1": "build add-in từ mã nguồn rồi mới dựng family",
    "run-in-revit-tests.ps1": "cần tests/suites của repo",
    "run-in-autocad-tests.ps1": "cần tests/suites của repo",
    "run-autocad-fixture-tests.ps1": "cần bộ fixture AutoLISP và tests/suites của repo",
}

SCRIPT_NAME = re.compile(r"[A-Za-z0-9_\-]+\.(?:py|ps1)\b")


def shipped() -> list[str]:
    lines = (line.strip() for line in MANIFEST.read_text(encoding="utf-8").splitlines())
    return [line for line in lines if line and not line.startswith("#")]


def mentioned_scripts(text: str) -> set[str]:
    """Tên script của scripts/ mà một văn bản nhắc tới (server.py, panel_api.py… ở thư mục khác thì bỏ)."""
    return {name for name in SCRIPT_NAME.findall(text) if (SCRIPTS / name).is_file()}


def docs_in_packages() -> list[Path]:
    """Tài liệu release.yml chép vào các gói (Revit, AutoCAD, BatchRunner) — đọc từ chính các dòng Copy-Item, nên thêm
    tài liệu vào gói là tự được xét."""
    lines = re.findall(r"Copy-Item\s+((?:docs/[\w\-.]+\.md\s*,?\s*)+)\s+\$pkg\s*$", RELEASE.read_text(encoding="utf-8"), re.M)
    docs = {doc.strip() for line in lines for doc in line.split(",")}
    return [ROOT / doc for doc in sorted(docs)]


def commands(text: str) -> list[str]:
    """Dòng lệnh của workflow — bỏ dòng chú thích (chú thích được phép nhắc tới cách làm cũ)."""
    return [line for line in text.splitlines() if not line.lstrip().startswith("#")]


class PackageScriptsTests(unittest.TestCase):
    def test_non_ascii_powershell_files_have_utf8_bom_for_windows_51(self) -> None:
        # 5.1 đọc file không BOM như ANSI; tiếng Việt có thể làm sai dấu nháy và hỏng cú pháp.
        for script in list(SCRIPTS.glob("*.ps1")) + list((ROOT / "tools" / "acceptance").glob("*.ps1")):
            with self.subTest(script=script.name):
                raw = script.read_bytes()
                if any(byte >= 128 for byte in raw):
                    self.assertTrue(raw.startswith(b"\xef\xbb\xbf"), f"{script.name}: cần BOM UTF-8 cho Windows PowerShell 5.1")

    def test_moi_dong_la_mot_script_co_that_khong_trung(self) -> None:
        names = shipped()
        self.assertTrue(names)
        self.assertEqual(len(names), len(set(names)))
        for name in names:
            with self.subTest(name=name):
                self.assertTrue((SCRIPTS / name).is_file(), f"{name} không có trong scripts/")

    def test_moi_script_deu_duoc_xep_loai(self) -> None:
        on_disk = {p.name for p in SCRIPTS.iterdir() if p.suffix in {".py", ".ps1"}}
        names = set(shipped())
        self.assertEqual(set(), names & set(NOT_SHIPPED), "script vừa đi kèm vừa bị loại")
        self.assertEqual(set(), on_disk - names - set(NOT_SHIPPED),
                         "script mới chưa xếp loại: thêm vào installer/batchrunner-scripts.txt hoặc NOT_SHIPPED")
        self.assertEqual(set(), set(NOT_SHIPPED) - on_disk, "NOT_SHIPPED còn tên script đã xoá")

    def test_tai_lieu_di_kem_goi_va_installer_nhac_toi_script_nao_thi_goi_co_script_do(self) -> None:
        names = set(shipped())
        sources = docs_in_packages() + [INSTALLER]
        self.assertGreaterEqual(len(sources), 3)
        for source in sources:
            with self.subTest(source=source.name):
                missing = mentioned_scripts(source.read_text(encoding="utf-8")) - names
                self.assertEqual(set(), missing, f"{source.name} hướng dẫn chạy script không có trong gói")

    def test_script_phu_thuoc_nhau_di_cung_nhau(self) -> None:
        """install-nightly-task.ps1 gọi don-ket-qua.ps1 cạnh nó; dhcb_mcp_server/dhcb_ai import dhcb_agent."""
        names = set(shipped())
        for name in names:
            text = (SCRIPTS / name).read_text(encoding="utf-8")
            if name.endswith(".ps1"):
                needed = set().union(*(mentioned_scripts(line) for line in text.splitlines() if "$PSScriptRoot" in line))
            else:
                needed = {f"{module}.py" for module in re.findall(r"^\s*import\s+(\w+)", text, re.M)
                          if (SCRIPTS / f"{module}.py").is_file()}
            with self.subTest(name=name):
                self.assertEqual(set(), needed - names, f"{name} cần script không đi kèm")

    def test_release_chep_theo_danh_sach_khong_chep_ca_thu_muc(self) -> None:
        release = "\n".join(commands(RELEASE.read_text(encoding="utf-8")))
        self.assertIn("installer/batchrunner-scripts.txt", release)
        self.assertNotRegex(release, r"Copy-Item\s+scripts/\*")

    def test_inno_setup_ghim_ban_6_khop_duong_dan_iscc(self) -> None:
        """Inno Setup 7 đã ra: cài không ghim là kéo 7.x, còn bước đóng gói gọi thư mục "Inno Setup 6"."""
        release = "\n".join(commands(RELEASE.read_text(encoding="utf-8")))
        installs = re.findall(r"choco install innosetup\b.*", release)
        self.assertTrue(installs, "release.yml không còn bước cài Inno Setup?")
        for install in installs:
            with self.subTest(install=install):
                self.assertRegex(install, r"--version=\d+\.\d+\.\d+", "release.yml phải ghim phiên bản Inno Setup")
        major = re.search(r"--version=(\d+)\.", installs[0]).group(1)
        self.assertIn(f"Inno Setup {major}\\ISCC.exe", release)

    def test_inno_setup_co_installdelete_va_update_packagecontents(self) -> None:
        """Kiểm tra installer gỡ DLL/bundle cũ khi bỏ chọn thành phần và lọc PackageContents.xml."""
        text = INSTALLER.read_text(encoding="utf-8")
        self.assertIn("[InstallDelete]", text)
        self.assertIn("not revit2024", text)
        self.assertIn("not acad2024", text)
        self.assertIn("procedure UpdatePackageContents", text)
        self.assertIn("procedure ReplaceXmlBlock", text)

        # Mô phỏng ReplaceXmlBlock với replacement rỗng trên PackageContents.xml thật
        xml_text = (ROOT / "installer" / "PackageContents.xml").read_text(encoding="utf-8")
        import xml.etree.ElementTree as ET

        # Parse XML gốc
        root = ET.fromstring(xml_text)
        components_before = root.findall("Components")
        self.assertEqual(len(components_before), 6)

        # Mô phỏng xoá block AutoCAD 2024
        marker = '<Components Description="AutoCAD 2024">'
        p1 = xml_text.find(marker)
        self.assertGreater(p1, -1)
        substr = xml_text[p1:]
        p2 = substr.find("</Components>")
        self.assertGreater(p2, -1)
        filtered_xml = xml_text[:p1] + xml_text[p1 + p2 + len("</Components>"):]

        root_after = ET.fromstring(filtered_xml)
        components_after = root_after.findall("Components")
        self.assertEqual(len(components_after), 5)
        descriptions = [c.attrib.get("Description") for c in components_after]
        self.assertNotIn("AutoCAD 2024", descriptions)
        self.assertIn("AutoCAD 2025", descriptions)


if __name__ == "__main__":
    unittest.main()
