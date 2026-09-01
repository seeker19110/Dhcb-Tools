# DHCB Revit Tools — Tài liệu nghiên cứu tổng hợp

Phạm vi đã chốt: add-in chạy trên **Revit desktop** (C#, Revit API), tự động hoá tối đa đến mức batch máy trạm, có tích hợp AI API. Không triển khai cloud (Design Automation).

---

## 1. Nền tảng kỹ thuật

- **Ngôn ngữ**: C# — .NET Framework 4.8 (Revit 2021–2024), .NET 8 (Revit 2025+); tham chiếu `RevitAPI.dll`, `RevitAPIUI.dll`.
- **Kiến trúc add-in**: `IExternalApplication` tạo Ribbon tab "DHCB Tools" + các `IExternalCommand`; UI dùng WPF; modeless qua `ExternalEvent`.
- **Multi-version**: một solution build nhiều phiên bản Revit (Directory.Build.props + conditional compilation).
- **Triển khai**: file `.addin` vào `%ProgramData%\Autodesk\Revit\Addins\<version>\` hoặc installer.

**Nguyên tắc vàng — tách logic khỏi UI:**

```
Dhcb-Tools/
├── src/
│   ├── DhcbTools.Core/    # logic thuần: Document + config JSON → xử lý → report
│   │                      # KHÔNG TaskDialog, KHÔNG Selection, KHÔNG WPF
│   ├── DhcbTools.Revit/   # vỏ desktop: Ribbon, WPF, IUpdater, events → gọi Core
│   │   ├── Commands/
│   │   ├── UI/
│   │   └── Resources/
│   └── DhcbTools.Batch/   # vỏ batch: hàng đợi file + Task Scheduler → gọi Core
└── docs/
```

Mọi lệnh nhận đầu vào là JSON config (không phụ thuộc hộp thoại), đầu ra là report (JSON/Excel) — điều kiện để chạy batch không người ngồi máy.

---

## 2. Các cấp độ tự động hoá (phạm vi desktop)

| Cấp | Mô tả | Cần người? | Công nghệ chính |
|---|---|---|---|
| 1 | Lệnh thủ công trên Ribbon | Có | `IExternalCommand` |
| 2 | Tự chạy theo sự kiện trong phiên | Có (đang làm việc) | `IUpdater`, `DocumentSynchronizedWithCentral`, `IFailuresPreprocessor` |
| 3 | **Batch nhiều file — đích của dự án** | Không (sau khi hẹn giờ) | Batch runner tự mở/xử lý/lưu từng file + Task Scheduler chạy đêm |
| 4 | Điều khiển từ xa / AI agent (tuỳ chọn về sau) | Tuỳ | HTTP/MCP bridge + `ExternalEvent` |

Yêu cầu bắt buộc cho cấp 2–3: `IFailuresPreprocessor` dùng chung để tự xử lý warning khi không có người; mỗi lệnh gói trong một transaction duy nhất (undo/preview được).

---

## 3. Quy trình dựng Revit A→Z và tự động hoá từng bước

Ký hiệu: 🟢 tự động toàn phần · 🟡 bán tự động (kỹ sư duyệt/chọn).

### Giai đoạn 0 — Khởi tạo dự án (~90% tự động)
| Bước | Mức | Cách làm |
|---|---|---|
| Tạo file từ template chuẩn công ty | 🟢 | Chọn loại dự án → copy template, đặt tên theo quy tắc, tạo workset chuẩn |
| Gán shared parameters, project info | 🟢 | Đọc config JSON/Excel của công ty |
| Load family theo bộ môn/loại dự án | 🟢 | Thư viện family có index, load theo danh mục |
| Browser organization, view template, filter | 🟢 | Transfer project standards từ file chuẩn |

### Giai đoạn 1 — Lưới trục, cao độ (~90% tự động)
| Bước | Mức | Cách làm |
|---|---|---|
| Link CAD/IFC/mô hình kiến trúc | 🟢 | Batch link + pin + shared coordinates theo config |
| Tạo Grid từ CAD | 🟢 | Đọc line + text layer trục trong DWG → `Grid` đúng tên |
| Tạo Level + view plan từ bảng Excel | 🟢 | Tên tầng, cao độ → `Level` + view hàng loạt |
| Copy/Monitor grid-level từ link | 🟡 | Chạy hàng loạt, báo cáo phần tử đã monitor |
| Scope box, view range theo tầng | 🟢 | Sinh từ phạm vi grid |

### Giai đoạn 2 — Dựng mô hình (~40–60% tự động, phần "người" nhất)

**Kiến trúc / Kết cấu:**
| Bước | Mức | Cách làm |
|---|---|---|
| Tường/cột/dầm/sàn từ CAD link | 🟡 | Nhận diện layer + polyline → dựng phần tử; map layer↔type lưu preset |
| Cửa/thiết bị từ block CAD | 🟡 | Map block name → family type, đặt theo insertion point |
| Room/Space + tên | 🟢 | Tạo room mọi vùng kín, lấy tên từ text CAD gần tâm phòng |
| Hoàn thiện theo phòng | 🟡 | Gán finish theo bảng phòng-vật liệu Excel |
| Mặt cắt shop drawing, dimension lưới trục | 🟡 | `ViewSection.CreateSection` + `NewDimension` |

**MEPF — trọng tâm, chi tiết ở mục 4.**

### Giai đoạn 3 — Kiểm tra & phối hợp (~80% tự động)
| Bước | Mức | Cách làm |
|---|---|---|
| Model checker theo quy tắc công ty | 🟢 | Chạy khi sync (event) + batch đêm; báo cáo Excel/HTML |
| Clash check nội bộ + với link | 🟢 | Solid intersection, nhóm theo cặp hệ, tạo 3D view khoanh vùng |
| Xử lý warnings | 🟡 | Phân loại tự động, tự sửa nhóm sửa được, còn lại kèm nút zoom |
| Đồng bộ Space↔Room, tham số liên bộ môn | 🟢 | IUpdater hoặc chạy định kỳ |
| Health report (warning, file size, view, family in-place…) | 🟢 | Xuất HTML/Excel |

### Giai đoạn 4 — Hồ sơ bản vẽ (~70% tự động)
| Bước | Mức | Cách làm |
|---|---|---|
| Tạo sheet hàng loạt từ Excel | 🟢 | Số hiệu, tên, title block, tham số khung tên |
| Nhân bản view + view template + đặt lên sheet | 🟢 | Theo quy tắc tầng/bộ môn, căn theo lưới sheet |
| Dimension + tag hàng loạt | 🟡 | Dim lưới trục/tường, tag cao độ/kích thước; kỹ sư dọn chỗ đè nhau |
| Schedule chuẩn, xuất Excel | 🟢 | Sinh từ định nghĩa lưu sẵn |
| Revision, cloud | 🟡 | Gán hàng loạt theo danh sách sheet |

### Giai đoạn 5 — Xuất bản & bàn giao (~95% tự động, chạy đêm được)
| Bước | Mức | Cách làm |
|---|---|---|
| In PDF/DWG hàng loạt, đặt tên đúng quy tắc | 🟢 | Batch export theo bộ chọn sheet (Revit 2022+ có PDF export API) |
| Xuất IFC/NWC theo mapping chuẩn | 🟢 | Config export lưu sẵn, batch nhiều file |
| Bóc khối lượng xuất Excel | 🟢 | Template bóc tách theo bộ môn |
| Purge + audit + compact trước bàn giao | 🟢 | Lệnh tổng, kèm health report |
| Thuyết minh bàn giao | 🟡 | AI soạn từ health report + danh mục bản vẽ, kỹ sư duyệt |

---

## 4. Tự động hoá MEPF (M–E–P–F)

### 4.1 Auto routing — 3 mức (làm từ dưới lên)
Revit có Generate Layout nhưng API tương ứng quá thô; thực tế tự viết bằng `Duct/Pipe/CableTray/Conduit.Create` + fitting API + `RoutingPreferenceManager`.

- **Mức A — bán tự động theo tuyến (làm trước, giá trị ngay)**: kỹ sư vẽ model line hoặc lấy tuyến từ CAD link → tool dựng duct/pipe/tray/conduit hoàn chỉnh, tự chèn elbow/tee đúng routing preference, đúng cao độ và độ dốc (slope cho thoát nước). Kèm "nối thông minh" 2 phần tử có sẵn (tự sinh đoạn trung gian + fitting, đổi cao độ bằng cặp elbow).
- **Mức B — tự động cục bộ theo quy tắc**: chọn nhóm thiết bị (miệng gió / sprinkler / thiết bị vệ sinh) + trục chính → tự sinh toàn bộ nhánh theo pattern chuẩn, tự chọn kích thước theo bảng. Bao gồm rải sprinkler theo tiêu chuẩn khoảng cách rồi nối về main.
- **Mức C — tự động toàn tuyến tránh va chạm (làm sau cùng)**: pathfinding 3D (A*) giữa 2 connector, né kết cấu và hệ khác, bám trần, số co tối thiểu. Giới hạn phạm vi (1 hệ, 1 tầng, hành lang) để khả thi.

### 4.2 Tính năng dùng chung mọi hệ (ưu tiên cao nhất)
| Tính năng | Mức | Ghi chú API |
|---|---|---|
| Sleeve/opening tại giao cắt tường-sàn-dầm | 🟢 | `ElementIntersectsSolidFilter` qua link, đặt family, ghi kích thước |
| Tag hàng loạt (size, cao độ, hệ) | 🟢 | `IndependentTag.Create`, tránh đè tag bằng bounding box |
| Điền cao độ đáy/đỉnh/tim vào tham số | 🟢 | Chạy real-time bằng IUpdater được |
| Hanger/support theo khoảng cách chuẩn | 🟢 | Rải theo `LocationCurve`, bắt vào kết cấu bằng `ReferenceIntersector` |
| Chia ống/máng theo cây 3m/6m | 🟡 | `BreakCurve` |
| Kiểm tra connector hở toàn mô hình | 🟢 | Duyệt `ConnectorManager` |
| Đánh số thiết bị/đoạn theo tuyến hoặc phòng | 🟢 | Sắp theo connector graph |
| Tô màu/filter theo hệ, cập nhật System Name | 🟢 | Chạy khi sync được |

### 4.3 Theo từng hệ
- **HVAC**: sizing duct theo lưu lượng (equal friction/velocity, đọc `Flow` từ connector graph — điền giá trị đề xuất, kỹ sư duyệt); rải miệng gió theo phòng; tự chèn fire damper khi xuyên tường chống cháy (nhận diện wall rating).
- **Nước**: ống thoát độ dốc tự động + cleanout theo khoảng cách; sizing theo fixture unit; tag invert level tại điểm đầu/cuối/đổi hướng.
- **Điện**: rải đèn theo lưới trần, ổ cắm theo chu vi; tạo circuit + gán panel hàng loạt (`ElectricalSystem.Create`), đánh số mạch; điền chiều dài dây để bóc khối lượng; panel schedule xuất Excel.
- **PCCC**: rải sprinkler theo khoảng cách tiêu chuẩn/loại nguy cơ; kiểm tra vùng phủ, khoảng cách vật cản; đánh số zone.

### 4.4 Lưu ý kỹ thuật MEPF
- Đọc `RoutingPreferenceManager` thay vì hard-code family fitting; báo lỗi rõ khi thiếu fitting.
- Auto-connect fail khi góc lệch nhỏ/đoạn ngắn hơn fitting — cần fallback (dời điểm, báo user) thay vì rollback cả transaction.
- Sizing chỉ ở mức "đề xuất" — kỹ sư duyệt, tránh nhận trách nhiệm thiết kế.

---

## 5. Tích hợp AI API

- **SDK**: SDK C# chính thức của Anthropic (cùng ngôn ngữ add-in), gọi từ `DhcbTools.Core`; model mặc định `claude-opus-5`.
- **4 điểm cắm theo ROI**:
  1. **Map layer/block CAD → Revit type**: gửi danh sách layer + danh mục type của template, AI trả bảng mapping JSON đúng schema (structured outputs) → kỹ sư duyệt bảng thay vì map tay hàng trăm dòng. Tương tự: đặt tên phòng từ text CAD lộn xộn.
  2. **PDF thuyết minh/spec → config khởi tạo dự án**: gửi PDF thẳng lên API (document input, không cần OCR), trích số tầng, cao độ, hệ thống, tiêu chuẩn → JSON cho lệnh khởi tạo.
  3. **Phân tích báo cáo clash/warning**: nhóm theo nguyên nhân gốc, xếp ưu tiên, viết tóm tắt họp phối hợp — chạy đêm bằng Batch API (rẻ hơn 50%).
  4. *(Về sau)* **Tool use với whitelist lệnh Core** → trợ lý ra lệnh bằng tiếng Việt trong Revit.
- **Nguyên tắc an toàn**: AI chỉ sinh đề xuất/cấu hình; mọi thay đổi mô hình qua transaction của tool + kỹ sư duyệt; API key lưu ngoài repo (biến môi trường/DPAPI); không gửi dữ liệu dự án nhạy cảm khi chưa được phép.

---

## 6. Lộ trình triển khai

1. **Nền tảng**: khung add-in (Ribbon, loader, logging, multi-version) theo kiến trúc Core/vỏ; mọi lệnh nhận config JSON. Kèm 3 lệnh đầu: xuất-nhập Excel tham số, purge/dọn view thừa, đánh số tự động.
2. **Giai đoạn 5 + 0** (dễ, hiệu quả thấy ngay): batch export PDF/DWG/IFC, health report + purge tổng, khởi tạo dự án từ template/config.
3. **Giai đoạn 1 + 4**: grid/level từ CAD-Excel, sheet hàng loạt, tag/dim hàng loạt.
4. **MEPF**: sleeve tự động → tag/cao độ → routing mức A → hanger + chia ống → routing mức B → sizing.
5. **Giai đoạn 3 + cấp 2–3 tự động**: checker + clash + IUpdater + batch runner chạy đêm (Task Scheduler).
6. **Lớp AI**: bắt đầu bằng map layer CAD→type và PDF→config.
7. **Sau cùng (tuỳ nhu cầu)**: routing mức C (pathfinding), HTTP/MCP bridge.

---

## 7. Giới hạn Revit API cần nhớ

- Chỉ chạy trong ngữ cảnh Revit; mọi thay đổi phải trong `Transaction`; UI modeless phải qua `ExternalEvent`.
- Batch desktop vẫn cần 1 máy có license Revit; không có headless mode chính thức.
- Hiệu năng: gom thao tác hàng loạt vào ít transaction, tránh `Regenerate` nhiều lần.
- Chất lượng kết quả MEPF phụ thuộc family fitting + routing preference của dự án.
