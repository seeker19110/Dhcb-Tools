#!/usr/bin/env python3
"""Cổng phủ dòng cho tầng C#: đọc cobertura của coverlet và liệt kê ĐÚNG dòng nào chưa chạy.

    python3 scripts/check-coverage.py <thư mục kết quả> [ngưỡng phần trăm, mặc định 100]

Vì sao có file này thay vì /p:Threshold của coverlet.msbuild: repo dùng coverlet.collector (chạy
qua `dotnet test --collect`), bản msbuild mới có cờ ngưỡng và nó lại không đi cùng `--no-build`.
Đổi lại, khi đỏ thì thông báo chỉ thẳng file:dòng chưa phủ chứ không chỉ nói một con số.
"""

from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
from pathlib import Path

MAX_LISTED = 40


def main(argv: list[str]) -> int:
    results = Path(argv[1] if len(argv) > 1 else "./coverage")
    threshold = float(argv[2]) if len(argv) > 2 else 100.0

    reports = sorted(results.rglob("coverage.cobertura.xml"), key=lambda p: p.stat().st_mtime)
    if not reports:
        print(f"Không tìm thấy coverage.cobertura.xml trong {results} — bước test có chạy --collect không?")
        return 2

    root = ET.parse(reports[-1]).getroot()
    percent = float(root.get("line-rate", "0")) * 100

    missed: list[tuple[str, int]] = []
    for klass in root.iter("class"):
        lines = klass.find("lines")
        for line in lines if lines is not None else []:
            if line.get("hits") == "0":
                missed.append((klass.get("filename", "?"), int(line.get("number", "0"))))

    print(f"Phủ dòng: {percent:.2f}% (ngưỡng {threshold:g}%) — {len(missed)} dòng chưa chạy")
    if percent + 1e-9 >= threshold:
        return 0

    for filename, number in sorted(set(missed))[:MAX_LISTED]:
        print(f"  chưa phủ: {filename}:{number}")
    if len(set(missed)) > MAX_LISTED:
        print(f"  … và {len(set(missed)) - MAX_LISTED} dòng nữa")
    print("Thêm test cho những dòng trên, hoặc đánh dấu [ExcludeFromCodeCoverage] kèm lý do nếu nhánh đó "
          "thật sự không chạy được trên CI (ví dụ mã chỉ chạy trên Windows).")
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
