# Lớp AI — offline

Giai đoạn 5 của [`roadmap.md`](roadmap.md), làm theo nguyên tắc **AI chỉ sinh đề xuất; mọi thay đổi mô hình đi qua
transaction của tool và có kỹ sư duyệt**, cộng thêm ràng buộc **không dữ liệu nào rời máy**:

- Mọi tính năng có **đường heuristic thuần** (regex, từ điển đồng nghĩa Việt/Anh, điểm khớp) chạy không cần model, không
  cần internet — đây là đường mặc định.
- Tuỳ chọn tinh chỉnh bằng **model ngôn ngữ chạy local** qua Ollama (`%APPDATA%\DHCB\ai.json`, mẫu ở
  [`configs/ai.sample.json`](../configs/ai.sample.json)). Add-in **từ chối endpoint không phải loopback**. Kết quả của model
  luôn đi qua bộ lọc của tool (chỉ nhận type có thật, chỉ chọn lệnh trong whitelist).
- Không có API key nào trong repo hay trong cấu hình.

| Tính năng | Lệnh / điểm vào | Phần thuần (test) | Đầu ra |
|---|---|---|---|
| 5.1 Map layer CAD → Revit type | Revit `CadLayerMap` (nút *Map layer CAD→Type*), AutoCAD `DHCB_LAYERMAP` | `Ai/LayerMappingSuggester` | CSV `Layer,RevitType,Confidence,NeedsReview,Reason` để duyệt trong Excel |
| 5.2 Thuyết minh → config | Revit `SpecToConfig`, `scripts/dhcb_ai.py spec --pdf …` | `Ai/SpecTextExtractor` | JSON đúng schema `LevelSetup` + `ProjectInfo`, `dryRun:true`, kèm dòng gốc để đối chiếu |
| 5.3 Phân tích cảnh báo chạy đêm | `BatchRunner --analyze`, `dhcb_ai.py warnings` | `Ai/WarningAnalyzer` | `warnings-summary.md`: gom theo nguyên nhân, thứ tự xử lý |
| 5.4 Ra lệnh tiếng Việt | Revit nút *Ra lệnh tiếng Việt*, AutoCAD `DHCB_AI`, Bridge `POST /chat`, `dhcb_agent.py … chat` | `Ai/CommandIntentParser` + `Ai/CommandCatalog` | Lệnh + config đề xuất (`dryRun:true`), kỹ sư xác nhận mới chạy |
| 6.2 MCP server | `scripts/dhcb_mcp_server.py revit|autocad` | — | `tools/list` từ `GET /tools`, `tools/call` ép `dryRun` trừ khi `confirm:true` |

## Whitelist lệnh

`Shared.Logic/Ai/CommandCatalog.cs` là nguồn sự thật duy nhất: tên lệnh, bí danh, mô tả, trường config, từ khoá. Bridge
(`GET /tools`), MCP server, intent parser và bảng dispatch (`RevitCommandTable`, `AcadCommandTable`) đều đọc từ đây.
Test `CommandCatalogTests` đối chiếu catalog với mã nguồn Core và với bảng dispatch — thêm lệnh Core mà quên khai báo là
CI đỏ (đúng yêu cầu §2.6 của đặc tả kiểm thử).

## Giai đoạn 7 — theo giới hạn thực tế của model local

- **Structured outputs:** `OllamaClient` gửi `format` = JSON Schema (không phải `"json"`), ép cú pháp hợp lệ ở tầng token;
  schema phẳng, ít trường bắt buộc (`MappingSchema`, `ChoiceSchema`).
- **≤ 8 ứng viên một lượt:** `CommandIntentParser.Candidates()` lọc bằng heuristic rồi `OllamaClient.ChooseCommand()` chỉ
  cho model chọn trong danh sách đó; kết quả ngoài whitelist → null.
- **Model mặc định `qwen3:8b`** (ổn nhất về tool-calling/JSON trong benchmark 2026; gemma3 không hỗ trợ tool).
  Cùng một giá trị ở ba chỗ: `OllamaClient.Settings.Model`, [`configs/ai.sample.json`](../configs/ai.sample.json),
  và `scripts/dhcb_ai.py` (`DEFAULT_MODEL`).
- **MCP server:** `--read-only` chỉ lộ tool đọc (tương đồng Revit 2027 MCP Server tech preview), `--group <query|data|sheets|cleanup|check|mep|project|ai>`
  lộ một nhóm để agent local chọn đúng hơn.

## Model local (tuỳ chọn)

```powershell
ollama pull qwen3:8b
copy configs\ai.sample.json %APPDATA%\DHCB\ai.json   # đặt "enabled": true
python scripts\dhcb_ai.py ollama-check
```

Model chỉ được dùng ở hai chỗ: chọn type cho layer (5.1) và — khi muốn — viết lại bản tóm tắt cảnh báo. Không có chỗ nào
model được sinh code hay gọi API Revit/AutoCAD trực tiếp.

## MCP với Claude Desktop / Claude Code

**Đây là chỗ duy nhất trong repo giữ đoạn cấu hình này** — các trang khác chỉ liên kết về đây.

Cách gọn nhất là gói `.mcpb`: `.\scripts\pack-mcpb.ps1` rồi mở file `.mcpb` bằng Claude Desktop, không phải sửa
file cấu hình nào (xem [`../tools/mcpb/README.md`](../tools/mcpb/README.md)). Muốn khai tay thì thêm vào
`claude_desktop_config.json` (thay `<repo>` bằng đường dẫn repo trên máy bạn):

```json
"mcpServers": {
  "dhcb-revit":   { "command": "python", "args": ["<repo>/scripts/dhcb_mcp_server.py", "revit"] },
  "dhcb-autocad": { "command": "python", "args": ["<repo>/scripts/dhcb_mcp_server.py", "autocad"] }
}
```

Thêm `"--read-only"` hoặc `"--group", "<nhóm>"` vào `args` để thu hẹp bộ tool.

Token Bridge đọc từ `%APPDATA%\DHCB\bridge-token.txt` hoặc `DHCB_BRIDGE_TOKEN`. Mọi tool sửa mô hình chạy xem trước
trước; truyền `confirm: true` để chạy thật.
