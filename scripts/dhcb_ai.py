#!/usr/bin/env python3
"""
dhcb_ai.py — lớp AI OFFLINE từ terminal (mục 5). Mọi thứ chạy trên máy, không gửi dữ liệu ra ngoài.

  spec   --pdf thuyet-minh.pdf --out spec.txt      đổi PDF → text (pdftotext nếu có, không thì pypdf nếu cài),
                                                   rồi gửi cho Revit lệnh SpecToConfig (nếu Revit đang mở)
  warnings --log logs/2026-09-01/run.jsonl         tóm tắt cảnh báo chạy đêm (gọi BatchRunner --report-only --analyze
                                                   nếu có; không thì gom theo mẫu đơn giản bằng Python)
  ollama-check                                     kiểm tra Ollama local (http://127.0.0.1:11434) và model trong ai.json

Cấu hình model local (tuỳ chọn) — %APPDATA%\\DHCB\\ai.json:
  { "enabled": true, "endpoint": "http://127.0.0.1:11434", "model": "qwen3:8b", "timeoutSeconds": 120 }
  (model mặc định qwen3:8b — cùng giá trị với OllamaClient trong add-in và configs/ai.sample.json)
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request

DEFAULT_MODEL = "qwen3:8b"
LOOPBACK_HOSTS = {"127.0.0.1", "localhost", "::1"}


def appdata_dhcb() -> str:
    base = os.environ.get("APPDATA") or os.path.join(os.path.expanduser("~"), ".config")
    return os.path.join(base, "DHCB")


def pdf_to_text(pdf: str, out: str) -> bool:
    if shutil.which("pdftotext"):
        try:
            subprocess.run(["pdftotext", "-layout", pdf, out], check=True)
        except (subprocess.CalledProcessError, OSError) as ex:
            print(f"pdftotext lỗi ({ex}). Kiểm tra file PDF có mở được không, hoặc `pip install pypdf` để dùng đường dự phòng.",
                  file=sys.stderr)
            return False
        return True
    try:
        from pypdf import PdfReader  # type: ignore
    except ImportError:
        print("Cần `pdftotext` (poppler) hoặc `pip install pypdf` để đọc PDF.", file=sys.stderr)
        return False
    reader = PdfReader(pdf)
    with open(out, "w", encoding="utf-8") as f:
        for page in reader.pages:
            f.write((page.extract_text() or "") + "\n")
    return True


def cmd_spec(args):
    if args.pdf:
        if not pdf_to_text(args.pdf, args.out):
            sys.exit(2)
        print(f"Đã đổi PDF → {args.out}")
    text_path = args.out if args.pdf else args.text
    if not text_path or not os.path.exists(text_path):
        print("Cần --pdf hoặc --text.", file=sys.stderr)
        sys.exit(2)

    cfg_out = args.config_out or os.path.join(appdata_dhcb(), "configs", "revit", "project-init-from-spec.json")
    try:
        import dhcb_agent
    except ImportError as ex:
        print(f"Thiếu dhcb_agent.py cạnh script này ({ex}).", file=sys.stderr)
        sys.exit(1)
    # dhcb_agent.send tự đổi lỗi HTTP/kết nối thành dict {success: False}; chỉ lỗi I/O hoặc JSON hỏng mới ném ra.
    try:
        result = dhcb_agent.send("revit", "SpecToConfig", {"inputPath": os.path.abspath(text_path), "outputPath": cfg_out})
    except (OSError, ValueError) as ex:
        print(f"Không gửi được cho Revit ({ex}). Mở Revit và bấm nút 'Thuyết minh → config', hoặc chạy lại.", file=sys.stderr)
        sys.exit(1)
    dhcb_agent.print_result(result)
    if not result.get("success"):
        sys.exit(1)


def cmd_warnings(args):
    runner = shutil.which("DhcbTools.BatchRunner") or shutil.which("DhcbTools.BatchRunner.exe")
    if runner and args.job:
        sys.exit(subprocess.call([runner, "--job", args.job, "--report-only", "--analyze", "--log-dir", os.path.dirname(os.path.dirname(args.log))]))

    # Fallback thuần Python: gom theo từ khoá.
    patterns = [
        ("Connector MEP hở", ["connector hở", "open connector", "not connected"]),
        ("Va chạm", ["va chạm", "clash"]),
        ("Tham số trống", ["thiếu giá trị", "missing", "required"]),
        ("Đặt tên sai quy tắc", ["không khớp mẫu", "pattern"]),
        ("View thừa", ["view thừa", "unplaced"]),
        ("Không mở được file", ["không mở được", "cannot open"]),
    ]
    groups = {}
    with open(args.log, "r", encoding="utf-8") as f:
        for line in f:
            try:
                e = json.loads(line)
            except json.JSONDecodeError:
                continue
            for msg in e.get("messages", []) + e.get("errors", []) + ([] if e.get("success") else [e.get("summary", "")]):
                low = msg.lower()
                cause = next((c for c, kws in patterns if any(k in low for k in kws)), "Khác")
                groups.setdefault(cause, []).append((e.get("file"), msg))
    print(f"# Tóm tắt cảnh báo — {args.log}\n")
    for cause, items in sorted(groups.items(), key=lambda kv: -len(kv[1])):
        print(f"- **{cause}**: {len(items)} dòng, {len({f for f, _ in items})} file")
        for _, m in items[:3]:
            print(f"    - {m[:160]}")


def is_loopback(endpoint: str) -> bool:
    """Chỉ nhận http(s) tới 127.0.0.1 / localhost / ::1 — so theo hostname đã parse, không so tiền tố chuỗi
    (tiền tố "http://127.0.0.1" cũng khớp "http://127.0.0.1.evil.example")."""
    try:
        parsed = urllib.parse.urlsplit(endpoint)
        host = parsed.hostname
    except ValueError:
        return False
    return parsed.scheme in ("http", "https") and host is not None and host.lower() in LOOPBACK_HOSTS


def cmd_ollama_check(_args):
    path = os.path.join(appdata_dhcb(), "ai.json")
    settings = {"enabled": False, "endpoint": "http://127.0.0.1:11434", "model": DEFAULT_MODEL}
    if os.path.exists(path):
        with open(path, "r", encoding="utf-8") as f:
            settings.update(json.load(f))
    print(f"ai.json: {path} → {json.dumps(settings, ensure_ascii=False)}")
    if not is_loopback(settings["endpoint"]):
        print("✗ endpoint không phải loopback — add-in sẽ từ chối (offline bắt buộc).")
        sys.exit(1)
    try:
        with urllib.request.urlopen(settings["endpoint"].rstrip("/") + "/api/tags", timeout=5) as resp:
            tags = json.loads(resp.read().decode("utf-8"))
        names = [m.get("name") for m in tags.get("models", [])]
        print(f"✓ Ollama đang chạy, model có sẵn: {', '.join(names) or '(chưa pull model nào)'}")
        if settings["model"] not in names and not any(n.startswith(settings["model"].split(":")[0]) for n in names):
            print(f"! Model '{settings['model']}' chưa có — chạy: ollama pull {settings['model']}")
    except (urllib.error.URLError, OSError, ValueError) as ex:
        print(f"✗ Không kết nối được Ollama ({ex}). Không bắt buộc: mọi tính năng AI đều có đường heuristic offline.")
        sys.exit(1)


def main():
    parser = argparse.ArgumentParser(description="DHCB AI offline")
    sub = parser.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("spec")
    p.add_argument("--pdf")
    p.add_argument("--text")
    p.add_argument("--out", default="spec.txt")
    p.add_argument("--config-out")
    p.set_defaults(func=cmd_spec)
    p = sub.add_parser("warnings")
    p.add_argument("--log", required=True)
    p.add_argument("--job")
    p.set_defaults(func=cmd_warnings)
    p = sub.add_parser("ollama-check")
    p.set_defaults(func=cmd_ollama_check)
    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    main()
