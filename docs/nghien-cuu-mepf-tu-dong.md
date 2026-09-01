# Nghiên cứu tự động hoá MEPF (Mechanical – Electrical – Plumbing – Fire)

Danh mục đầy đủ các tính năng tự động / bán tự động cho kỹ sư MEPF, bổ sung cho mục 2.5 của tài liệu nghiên cứu tổng. Trọng tâm: **auto routing** và các tác vụ lặp lại chiếm nhiều giờ công nhất.

## 1. Auto routing — hiện trạng và khả năng triển khai

### 1.1 Revit hỗ trợ sẵn gì?
- Revit có **Generate Layout** (UI) cho duct/pipe từ hệ thống logic (system) — nhưng API tương ứng (`RouteSolution`) rất hạn chế, kết quả thô, gần như không sản phẩm thương mại nào dùng trực tiếp.
- API cung cấp đầy đủ "viên gạch" để tự viết routing: tạo `Duct/Pipe/CableTray/Conduit.Create`, nối bằng `NewElbowFitting/NewTeeFitting/NewTransitionFitting` (hoặc `Connector.ConnectTo`), và `RoutingPreferenceManager` để chọn fitting theo chuẩn dự án.

### 1.2 Ba mức triển khai auto routing (khuyến nghị làm từ dưới lên)

**Mức A — Bán tự động theo tuyến người vẽ (dễ, giá trị cao ngay):**
- Vẽ model line / polyline / chọn điểm → tool dựng duct/pipe/cable tray/conduit theo tuyến, tự chèn elbow/tee đúng routing preference, đúng cao độ và độ dốc (slope cho ống thoát nước).
- Chuyển tuyến từ CAD link (layer DWG) thành hệ ống/máng thật.
- "Nối thông minh" 2 phần tử đã có: tự sinh đoạn trung gian + fitting (giống lệnh Trim/Extend nhưng đa đoạn, đổi cao độ bằng cặp elbow tự động).

**Mức B — Tự động cục bộ theo quy tắc (trung bình):**
- Nối thiết bị đầu cuối vào trục chính gần nhất: chọn 1 nhóm miệng gió/đầu phun sprinkler/thiết bị vệ sinh + 1 trục chính → tool tự sinh nhánh (pattern chữ T/L, lên-ngang-xuống) cho từng thiết bị, tự chọn kích thước theo bảng.
- Sprinkler layout: rải đầu phun theo lưới/diện tích phòng (NFPA spacing), rồi tự nối nhánh về main — bài toán lặp lại nhiều nhất của hệ Fire.
- Đi conduit/cable tray điểm-tới-điểm theo hành lang cho trước.

**Mức C — Tự động toàn tuyến có tránh va chạm (khó, làm sau cùng):**
- Pathfinding 3D (A*/Dijkstra trên lưới không gian) giữa 2 connector, ràng buộc: tránh kết cấu và hệ khác (dựng BVH/solid từ `FilteredElementCollector`), bám trần, số co tối thiểu, khoảng cách bảo trì.
- Đây là bài toán các sản phẩm lớn cũng chỉ giải một phần; nên giới hạn phạm vi (1 hệ, 1 tầng, hành lang) để khả thi.

## 2. Danh mục tính năng tự động / bán tự động theo hệ

### 2.1 Dùng chung mọi hệ (ưu tiên cao nhất — giảm giờ công nhiều nhất)
| Tính năng | Tự động mức | Ghi chú API |
|---|---|---|
| Đặt sleeve/opening tại giao cắt với tường-sàn-dầm | Tự động toàn phần | `ElementIntersectsSolidFilter` qua link, đặt family instance, ghi tham số kích thước |
| Tag hàng loạt (kích thước, cao độ, hệ thống, BOD/TOP) | Tự động | `IndependentTag.Create`, tránh đè tag bằng kiểm tra bounding box |
| Điền cao độ đáy/đỉnh/tim vào tham số (theo tầng tham chiếu) | Tự động | đọc `Location` + `Level`, chạy được bằng IUpdater real-time |
| Hanger/support tự động theo khoảng cách tiêu chuẩn | Tự động | rải family theo `LocationCurve`, bắt vào kết cấu phía trên bằng `ReferenceIntersector` |
| Clash check nội bộ + báo cáo, đánh dấu, zoom tới | Tự động | `ElementIntersectsElementFilter`, xuất Excel/HTML |
| Đánh số thiết bị/đoạn ống theo tuyến hoặc theo phòng | Tự động | sắp theo thứ tự connector graph |
| Tự chia ống/máng theo chiều dài cây tiêu chuẩn (3m/6m) + union | Bán tự động | `BreakCurve` cho duct/pipe |
| Tạo shop drawing: mặt cắt qua tuyến + dimension + tag | Bán tự động | `ViewSection.CreateSection` + `NewDimension` |
| Cập nhật System Name/Abbreviation, tô màu theo hệ | Tự động | filter + override graphic, có thể chạy khi sync |
| Kiểm tra kết nối hở (open connector) toàn mô hình | Tự động | duyệt `ConnectorManager`, báo cáo phần tử chưa nối |

### 2.2 HVAC (M)
- Tính và gán kích thước duct theo lưu lượng (equal friction / velocity method) — đọc `Flow` từ connector graph, tra bảng, đổi size + transition tự động.
- Rải miệng gió theo phòng (theo diện tích/lưu lượng phòng từ Space), nối về nhánh (mức B).
- Chèn van/damper/phụ kiện hàng loạt tại vị trí quy tắc (qua tường chống cháy → fire damper, tự động nhờ giao cắt với tường có rating).
- Tính tổn thất áp sơ bộ trên tuyến dài nhất, xuất báo cáo.
- Đồng bộ Space ↔ Room từ link kiến trúc, gán airflow yêu cầu.

### 2.3 Ống nước (P)
- Đi ống thoát có độ dốc tự động theo tuyến (mức A + slope), tự đặt cleanout theo khoảng cách.
- Nối thiết bị vệ sinh vào trục đứng gần nhất (mức B), pattern cấp/thoát chuẩn.
- Tính kích thước ống cấp theo fixture unit (tra bảng), gán tự động.
- Đánh tag cao độ đáy ống (invert level) tại điểm đầu/cuối/đổi hướng — tác vụ tốn giờ nhất khi ra hồ sơ.
- Riser diagram tự động (schematic từ connector graph) — khó, để giai đoạn sau.

### 2.4 Điện (E)
- Đi cable tray/conduit theo tuyến (mức A), tự sinh fitting; chia đoạn theo cây tiêu chuẩn.
- Rải đèn theo lưới trần/phòng (theo lux yêu cầu đơn giản hoá), rải ổ cắm theo chu vi tường + khoảng cách.
- Tạo circuit + gán panel hàng loạt theo quy tắc (`ElectricalSystem.Create`), đánh số mạch, cân pha sơ bộ.
- Điền chiều dài dây (đo theo tuyến tray + rơi xuống thiết bị) vào tham số để bóc khối lượng.
- Panel schedule tự động cập nhật, xuất Excel.

### 2.5 Chữa cháy (F)
- Rải sprinkler theo tiêu chuẩn khoảng cách (ô lưới, khoảng cách tường), theo loại nguy cơ — tự động toàn phần theo phòng.
- Nối đầu phun về nhánh + main (mức B) với cao độ armover chuẩn.
- Kiểm tra khoảng cách phủ, khoảng cách tới vật cản, xuất báo cáo vi phạm.
- Đánh số zone, tag đầu phun hàng loạt.

## 3. Xếp hạng ưu tiên (giá trị ÷ công sức)

1. **Sleeve tự động** — mọi dự án đều cần, tiết kiệm nhiều ngày công, làm được ngay.
2. **Tag + điền cao độ hàng loạt** — ra hồ sơ nhanh gấp nhiều lần.
3. **Routing mức A (theo tuyến vẽ sẵn / từ CAD)** — nền tảng cho mọi thứ về sau.
4. **Hanger tự động, chia ống theo cây, open-connector check.**
5. **Routing mức B theo hệ (sprinkler + miệng gió + thiết bị vệ sinh).**
6. **Sizing tự động (duct/pipe/cable) + shop drawing tự động.**
7. **Routing mức C (pathfinding tránh va chạm)** — chỉ sau khi A/B đã ổn định.

## 4. Rủi ro & lưu ý kỹ thuật

- Chất lượng fitting phụ thuộc **routing preference + family fitting của dự án**; tool phải đọc `RoutingPreferenceManager` thay vì hard-code family, và báo lỗi rõ khi thiếu fitting phù hợp.
- Auto-connect hay thất bại khi góc lệch nhỏ/khoảng cách ngắn hơn kích thước fitting — cần fallback (dời điểm, báo người dùng) thay vì rollback cả transaction.
- Mọi lệnh chạy hàng loạt phải có `IFailuresPreprocessor` + preview/undo một transaction duy nhất.
- Sizing tính toán chỉ nên ở mức "điền giá trị đề xuất" — kỹ sư duyệt lại; tránh nhận trách nhiệm thiết kế.
