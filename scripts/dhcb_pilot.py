#!/usr/bin/env python3
"""Tổng hợp CSV thí điểm cục bộ; không gửi dữ liệu và không suy ra hiệu quả khi chưa có mẫu."""
from __future__ import annotations

import argparse
import csv
from datetime import date
import html
import json
import math
from pathlib import Path
import sys

FIELDS = ("Ngay", "MaNguoi", "MaDuAn", "UngDung", "Lenh", "PhutLamTay",
          "PhutDungToolGomCauHinhVaSua", "HoanThanh", "SoLoi")


def analyze(path: Path) -> dict:
    commands: dict[str, dict] = {}
    errors = []
    placeholders = 0
    with path.open(encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        if not set(FIELDS).issubset(reader.fieldnames or ()):
            raise ValueError("CSV thiếu cột: " + ", ".join(sorted(set(FIELDS) - set(reader.fieldnames or ()))))
        for line, row in enumerate(reader, 2):
            if not any((row.get(k) or "").strip() for k in FIELDS if k != "Lenh"):
                placeholders += 1
                continue
            try:
                if None in row or any(not (row.get(k) or "").strip() for k in FIELDS):
                    raise ValueError("dòng thiếu trường hoặc thừa cột")
                date.fromisoformat(row["Ngay"].strip())
                manual = float(row["PhutLamTay"])
                tool = float(row["PhutDungToolGomCauHinhVaSua"])
                failures = int(row["SoLoi"])
                if not all(math.isfinite(n) and n >= 0 for n in (manual, tool, failures)):
                    raise ValueError("thời gian/số lỗi phải hữu hạn và không âm")
                completed = row["HoanThanh"].strip().lower()
                if completed not in ("true", "false", "1", "0", "có", "không"):
                    raise ValueError("HoanThanh cần true/false, 1/0 hoặc có/không")
            except ValueError as error:
                errors.append({"line": line, "error": str(error)})
                continue
            key = row["UngDung"].strip() + "/" + row["Lenh"].strip()
            item = commands.setdefault(key, {"samples": 0, "completed": 0, "errors": 0,
                                              "manualMinutes": 0.0, "toolMinutes": 0.0})
            item["samples"] += 1
            item["completed"] += completed in ("true", "1", "có")
            item["errors"] += failures
            item["manualMinutes"] += manual
            item["toolMinutes"] += tool
    for item in commands.values():
        item["savedMinutes"] = item["manualMinutes"] - item["toolMinutes"]
        item["savedPercent"] = 100 * item["savedMinutes"] / item["manualMinutes"] if item["manualMinutes"] else None
        item["completionRate"] = item["completed"] / item["samples"]
    return {"status": "invalid" if errors else "measured" if commands else "no-data",
            "samples": sum(item["samples"] for item in commands.values()),
            "placeholders": placeholders, "commands": commands, "errors": errors}


def render(report: dict) -> str:
    rows = []
    for command, item in sorted(report["commands"].items()):
        saved = "—" if item["savedPercent"] is None else f'{item["savedPercent"]:.1f}%'
        rows.append(f'<tr><td>{html.escape(command)}</td><td>{item["samples"]}</td>'
                    f'<td>{item["completed"]}</td><td>{item["errors"]}</td>'
                    f'<td>{item["manualMinutes"]:.1f}</td><td>{item["toolMinutes"]:.1f}</td><td>{saved}</td></tr>')
    issues = "".join(f'<li>Dòng {item["line"]}: {html.escape(item["error"])}</li>' for item in report["errors"])
    state = {"invalid": "Có dữ liệu lỗi — sửa CSV trước khi kết luận.",
             "measured": "Số liệu đã nhập; tác vụ bỏ cuộc vẫn tính toàn bộ thời gian dùng tool.",
             "no-data": "Chưa có dữ liệu sử dụng thật; chưa thể kết luận mức tiết kiệm."}[report["status"]]
    return ('<!doctype html><html lang="vi"><meta charset="utf-8"><title>Thí điểm DHCB</title>'
            '<style>body{font:16px system-ui;margin:32px}table{border-collapse:collapse}td,th{padding:10px;border:1px solid #ccc}</style>'
            f'<h1>Thí điểm DHCB Tools</h1><p>{state}</p><p>{report["samples"]} tác vụ; {report["placeholders"]} dòng mẫu chưa nhập.</p>'
            '<table><tr><th>Ứng dụng/lệnh</th><th>Tác vụ</th><th>Hoàn thành</th><th>Lỗi</th><th>Phút làm tay</th>'
            '<th>Phút tool gồm sửa</th><th>Tiết kiệm</th></tr>' + "".join(rows) + '</table><ul>' + issues + '</ul></html>')


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("csv", type=Path)
    parser.add_argument("--output", type=Path, help="Báo cáo .json hoặc .html")
    args = parser.parse_args(argv)
    try:
        if args.output and args.output.suffix.lower() not in (".json", ".html"):
            raise ValueError("output cần đuôi .json hoặc .html")
        if args.output and args.output.resolve() == args.csv.resolve():
            raise ValueError("output không được ghi đè CSV đầu vào")
        report = analyze(args.csv)
        serialized = json.dumps(report, ensure_ascii=False, indent=2)
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(render(report) if args.output.suffix.lower() == ".html" else serialized + "\n", encoding="utf-8")
        print(serialized)
        return 1 if report["errors"] else 0
    except (OSError, ValueError) as error:
        print("Không tổng hợp được thí điểm: " + str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
