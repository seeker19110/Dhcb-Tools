# Đặc tả chi tiết — Nâng cấp chiều sâu 3 tính năng cốt lõi (MEP, Clash/BCF, IDS/IFC)

- **Trạng thái:** Đã phê duyệt, bắt đầu triển khai
- **Ngày:** 2026-09-13
- **Tác giả:** Hermes Agent & DHCB Engineering Team
- **Nhánh Git:** `feat/depth-upgrade-mep-clash-ids`

---

## 1. Tổng quan & Mục tiêu

Nhằm chuyển hướng chiến lược từ **mở rộng số lượng lệnh** sang **đào sâu chất lượng & độ tin cậy**, tài liệu này quy định chi tiết thiết kế kỹ thuật, hợp đồng dữ liệu, thuật toán và tiêu chí nghiệm thu cho 3 tính năng trọng điểm:

1. **MEP Routing & Né vật cản (`AutoRoute` / `PathFinder3D` / `SlopePlanner`):**
   - Đánh chỉ mục không gian 3D (Spatial Hash Grid) cho chướng ngại vật để tối ưu thời gian tìm đường.
   - Thêm bộ sinh đa phương án (`RouteOptionGenerator`) đánh giá ngắn nhất, ít rẽ nhất, thoáng nhất kèm bảng điểm breakdown.
   - Cấu hình lớp cách nhiệt & khoảng hở an toàn (Clearance) linh hoạt theo hệ thống.

2. **Clash Detection & BCF 2.1 Nâng cao (`ClashPlanner` / `ClashClassifier` / `BcfWriter`):**
   - Phân loại tự động va chạm Cứng (Hard Clash), va chạm Mềm (Soft/Clearance Clash) và dung sai thi công.
   - Sử dụng 3D Spatial Hash để tăng tốc độ phát hiện va chạm trên mô hình lớn.
   - Mở rộng BCF 2.1 xuất đầy đủ Viewpoint 3D Camera, Bounding Box và gợi ý tuyến né tự động.

3. **Toàn vẹn BuildingSMART IDS & Hồ sơ bàn giao NĐ 207 (`IdsEvaluator` / `IfcChecker` / `HandoverPackage`):**
   - Đạt 100% khả năng đánh giá mọi Facet chuẩn BuildingSMART IDS (`Entity`, `Attribute`, `Property`, `Classification`, `Material`, `PartOf`).
   - Tự động hóa kiểm tra hồ sơ bàn giao theo Nghị định 207/2026 (kiểm băm HashChain, chứng nhận IDS/IFC và bản vẽ).
   - Widget HTML xem trước và tương tác trực tiếp trong Hermes Chat.

---

## 2. Thiết kế chi tiết — Lát 1: MEP Routing & Né vật cản

### 2.1 Spatial Hash Grid cho PathFinder3D — **bỏ (audit 2026-09-23)**
- `PathFinder3D` đã raster hoá chướng ngại vào `OccupancyGrid` **một lần** khi khởi tạo (mỗi hộp chỉ tô các ô
  nó phủ), nên mỗi nút mở rộng của A* tra chướng ngại trong $O(1)$ sẵn rồi — không có vòng duyệt $O(N)$ nào để
  tối ưu. Lớp `ObstacleSpatialIndex3D` thêm ở PR #160 không được nơi nào gọi tới, đã xoá để không tạo ấn tượng
  sai về hiệu năng. Muốn nhanh hơn thật thì việc có ý nghĩa là tăng `StepMm` hoặc thu hẹp hộp tìm kiếm.

### 2.2 Đa phương án tuyền đường (`RouteOptionCandidate`)
Cung cấp 3 phương án tuyến đường chuẩn hóa:
1. **Option A (Shortest):** Ưu tiên chiều dài tuyến ngắn nhất.
2. **Option B (Least Turns):** Phạt rẽ cao (`TurnPenalty * 3` — hằng `RouteOptionGenerator.LeastTurnsPenaltyFactor`), giảm tối đa số phụ kiện fitting.
3. **Option C (Max Clearance):** Giữ khoảng cách xa nhất với các vật cản xung quanh.

Bảng điểm tổng hợp:
$$\text{Score} = w_1 \cdot \frac{L_{\min}}{L} + w_2 \cdot \frac{T_{\min}}{T} + w_3 \cdot \text{ClearanceScore}$$

với $L_{\min}$ = khoảng Manhattan đầu–cuối, $T_{\min}$ = số rẽ tối thiểu do `PathFinder3D` tính, ClearanceScore =
$\min(1, d_{\min}/\text{ClearanceMm})$, $w = (0{,}4;\ 0{,}4;\ 0{,}2)$. Thước đo **tuyệt đối**: một phương án duy nhất
không tự động được 1,00. Hai chiến lược cho cùng đường gấp khúc thì gộp làm một, tiêu đề ghi cả hai.

---

## 3. Thiết kế chi tiết — Lát 2: Clash Detection & BCF 2.1

### 3.1 Clash Classification Engine
- **Hard Clash:** có giao nhau và độ sâu xuyên $> \text{Tolerance}$ (mm). Độ sâu do bên gọi đo từ hình học;
  không có thì suy xấp xỉ bằng $\sqrt[3]{V_{\text{intersect}}}$ — so thể tích với $\text{Tolerance}^3$ như bản đầu
  là sai đơn vị (vết cà 5 mm trên mặt ống 200×200 = 200.000 mm³ ≫ 125 mm³).
- **Tolerance Flaw:** có giao nhau nhưng độ sâu $\le \text{Tolerance}$ — ghi nhận, không dời tuyến.
- **Soft Clash (Clearance Violation):** không giao nhau, khoảng cách $< \text{RequiredClearance}$.
- **None:** không giao nhau và khoảng cách đủ — **không** thành topic BCF (bản đầu xếp cặp này vào Tolerance Flaw
  nên mọi cặp ứng viên xa nhau đều thành topic rác).

### 3.2 BCF 2.1 Topic & Viewpoint Schema Expansion
- Tự động tính toán camera position & target vector dựa trên Bounding Box của điểm va chạm:
  - `CameraViewPoint`: Tọa độ tâm va chạm nới ra theo góc nhìn $45^\circ$.
  - `CameraDirection`: Vector nhìn về tâm va chạm.
  - `Component`: Đánh dấu ID của phần tử A (`Selection`) và phần tử B (`Highlight`).

---

## 4. Thiết kế chi tiết — Lát 3: IDS Full Spec & Handover NĐ 207

### 4.1 IDS Evaluator Enhancement
Bổ sung đầy đủ 6 Facets:
1. **Entity Facet:** Kiểm tra `IfcEntity` và `PredefinedType`.
2. **Attribute Facet:** Kiểm tra các thuộc tính gốc (`Name`, `Description`, `Tag`, `ObjectType`).
3. **Property Facet:** Kiểm tra Property Set, Property Name, Datatype và Value (Pattern, Range, Enum).
4. **Classification Facet:** Kiểm tra mã hệ phân loại (Uniclass, OmniClass, MasterFormat, NĐ 207).
5. **Material Facet:** Kiểm tra tên và vật liệu cấu thành.
6. **PartOf Facet:** Kiểm tra cấu trúc liên kết không gian / hệ thống (`IfcRelAggregates`, `IfcRelContainedInSpatialStructure`).

### 4.2 Handover Compliance Matrix (NĐ 207/2026)
- Xóa bỏ rủi ro hồ sơ bàn giao không đủ pháp lý:
  - `ValidateHashChain()`: Xác minh chuỗi băm các file trong gói bàn giao không bị can thiệp.
  - `ValidateIdsResult()`: Đảm bảo 100% tiêu chí IDS quy định đã đạt PASS.
  - `ValidateDrawingBindings()`: Đảm bảo bản vẽ PDF/DWG khớp mã hiệu với mô hình IFC/RVT.

---

## 5. Chiến lược kiểm thử & Tiêu chí nghiệm thu

1. **Unit Tests (100% Coverage trong Shared.Logic):**
   - Test benchmark thời gian chạy của Spatial Index vs Brute Force.
   - Test round-trip BCF 2.1 với Viewpoint XML validation.
   - Test bộ ca kiểm IDS mẫu (BuildingSMART official test suite).

2. **Integration Verification:**
   - Đảm bảo `dotnet test` và `pytest` xanh 100%.
   - Không gây hồi quy bất kỳ lệnh hiện có nào.

3. **Báo cáo & HTML Widget:**
   - Dựng Widget HTML tương tác minh họa kết quả routing đa phương án, danh mục Clash BCF và báo cáo IDS/Handover.
