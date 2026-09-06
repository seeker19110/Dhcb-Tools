"""Sinh mục lục cho docs/bang-chung-test.md (§61).

File bằng chứng đã hơn 3.100 dòng / 60 mục; người mới không tìm được kết luận nếu không đọc tuần tự. Script này
đặt một khối mục lục giữa hai dấu `<!-- muc-luc:bat-dau -->` … `<!-- muc-luc:ket-thuc -->` ngay dưới tiêu đề,
mỗi mục một dòng: số §, tiêu đề, và link neo. Chạy lại là cập nhật; `tests/python/test_muc_luc_bang_chung.py`
đỏ khi mục lục lệch tiêu đề thật (cùng cách DocCommandTableTests giữ bảng lệnh).

Dùng:  python scripts/muc-luc-bang-chung.py            # cập nhật tại chỗ
       python scripts/muc-luc-bang-chung.py --check    # chỉ kiểm, mã thoát 1 nếu lệch
"""
import re
import sys
import unicodedata
from pathlib import Path

START = "<!-- muc-luc:bat-dau -->"
END = "<!-- muc-luc:ket-thuc -->"
HEADING = re.compile(r"^## (\d+)\. (.+?)\s*$", re.MULTILINE)


def slug(text: str) -> str:
    """Neo kiểu GitHub: bỏ dấu câu, giữ chữ (kể cả có dấu), khoảng trắng → '-'."""
    t = unicodedata.normalize("NFC", text).lower()
    t = re.sub(r"[^\w\s-]", "", t)
    return re.sub(r"\s+", "-", t.strip())


def build_toc(text: str) -> str:
    lines = ["**Mục lục** (sinh bằng `scripts/muc-luc-bang-chung.py`, đừng sửa tay):", ""]
    for m in HEADING.finditer(text):
        n, title = m.group(1), m.group(2)
        lines.append(f"- §{n} — [{title}](#{slug(n + '-' + title)})")
    return "\n".join(lines)


def render(text: str) -> str:
    toc = f"{START}\n{build_toc(strip_toc(text))}\n{END}"
    if START in text and END in text:
        pre, rest = text.split(START, 1)
        _, post = rest.split(END, 1)
        return pre + toc + post
    # chưa có: chèn sau dòng tiêu đề đầu tiên và dòng trống kế tiếp
    first_nl = text.index("\n")
    return text[: first_nl + 1] + "\n" + toc + "\n" + text[first_nl + 1 :]


def strip_toc(text: str) -> str:
    if START in text and END in text:
        pre, rest = text.split(START, 1)
        _, post = rest.split(END, 1)
        return pre + post
    return text


def main(argv, path=None):
    # Console cp1252 trên Windows ném UnicodeEncodeError khi in tiếng Việt (bẫy §48 của check-coverage.py).
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    path = Path(path) if path else Path(__file__).resolve().parents[1] / "docs" / "bang-chung-test.md"
    text = path.read_text(encoding="utf-8")
    new = render(text)
    if "--check" in argv:
        if new != text:
            print("Mục lục bang-chung-test.md lệch tiêu đề thật — chạy: python scripts/muc-luc-bang-chung.py")
            return 1
        print("Mục lục khớp.")
        return 0
    path.write_text(new, encoding="utf-8")
    print(f"Đã cập nhật mục lục: {len(HEADING.findall(strip_toc(new)))} mục.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
