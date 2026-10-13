# Gợi ý nâng cấp — rà soát 2026-10-13

> Lấy từ đợt rà soát chéo 19 repo của chủ sở hữu (ma trận vệ sinh ở [seeker19110/stored, mục 23 §8.1](https://github.com/seeker19110/stored/blob/main/23-own-repos-lessons/README.md); gợi ý tích hợp ở §7b của cùng file). Số liệu đo bằng lệnh trên HEAD (đếm `uses:` ghim SHA 40 ký tự, khối `permissions:`, `dependabot.yml`, `.claude/settings.json`), không suy ra từ văn xuôi. Đây là **đề xuất**, chưa sửa code. Mỗi mục kèm bằng chứng để kiểm lại trước khi làm; mục nào đã xử lý thì đánh dấu hoặc xoá.

## Hiện trạng đo được

| HEAD đọc | Action ghim SHA | Workflow thiếu `permissions:` cấp đầu | Dependabot | LICENSE · SECURITY | File test |
|---|---|---|---|---|---|
| `d6b590d` (2026-10-10) | 28/28 | – | github-actions, pip | có · có | 203 |

## Đề xuất (🔴 cao · 🟠 trung bình · 🟢 nhẹ)

| # | Mức | Đề xuất | Bằng chứng | Kiểm xong khi |
|---|---|---|---|---|
| 1 | 🟢 | Nâng `requires-python` từ `>=3.9` lên `>=3.11` (Python 3.9 hết hỗ trợ từ 10/2025) | `pyproject.toml` ghi `requires-python = ">=3.9"` | CI `tests.yml` xanh trên 3.11+ |

## Giữ nguyên (điểm mạnh)

- 28/28 action ghim SHA, Dependabot, secret-scan, dependency-audit; agent khép vòng dryRun → confirm → snapshot; bridge loopback có token; bằng chứng tách khỏi tuyên bố.

## Gợi ý tích hợp từ catalog `stored`

- Đối chiếu lớp loopback bridge với egress allowlist và chuỗi xác nhận theo lệnh của Decepticon (stored mục 12 §3b, §3g).

## Ghi chú chung

- Ghim action theo SHA bằng [pinact](https://github.com/suzuki-shunsuke/pinact) hoặc [ratchet](https://github.com/sethvargo/ratchet), giữ comment tag (`# v7.0.1`), rồi để Dependabot `github-actions` tự nâng SHA. Đây là cách projects-template, xboss và Sales-Hunter đang làm.
- Chuẩn tham chiếu là `seeker19110/projects-template`: 25/25 action ghim SHA, mọi workflow có `permissions:` cấp đầu, Dependabot cho npm, github-actions và pip.
