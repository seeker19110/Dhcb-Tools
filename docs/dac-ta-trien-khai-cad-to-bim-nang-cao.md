# Đặc tả triển khai — CAD → BIM Revit nâng cao

- **Trạng thái:** Đề xuất, chưa triển khai
- **Ngày:** 2026-09-10
- **Đặc tả chức năng:** [`dac-ta-cad-to-bim-nang-cao.md`](dac-ta-cad-to-bim-nang-cao.md)

## 1. Quyết định kiến trúc

### 1.1 Chọn pipeline manifest, không gọi chéo AutoCAD ↔ Revit trực tiếp

```text
AutoCAD Adapter
    ↓ CadSemanticManifest v1
Shared.Logic: normalize → infer → validate → diff → score
    ↓ CadModelPlan v1
Revit Adapter: resolve types → preview → transactional apply
    ↓ bindings + ChangedIds
Bridge/MCP: inspect → verify → report
```

Lý do:

- AutoCAD giữ được semantic native của DIM, block, attribute và handle; Revit `ImportInstance.Geometry` không giữ đủ dữ liệu này.
- Manifest cho phép chạy batch, kiểm thử, lưu bằng chứng và tái hiện lỗi mà không cần đồng thời mở cả hai host.
- Logic suy luận nằm trong `Shared.Logic`, không bị khóa vào API Autodesk và test được trên CI.
- Adapter Autodesk chỉ đọc/ghi model và chuyển đổi DTO.

Không tạo service/microservice mới. Đây vẫn là monorepo desktop, dùng file JSON versioned làm port giữa hai adapter.

### 1.2 Một schema công khai, mở rộng cộng thêm

- `schemaVersion` bắt buộc.
- Field mới phải optional hoặc có default an toàn.
- Không đổi nghĩa field đã phát hành.
- Parser từ chối major version không hỗ trợ.
- Minor version mới được phép có field lạ nhưng phải giữ phần dữ liệu cốt lõi.
- JSON camelCase; số hình học dùng mm và degree.

### 1.3 Tách “plan” khỏi “apply”

`CadModelApply` không chạy lại inference. Nó chỉ:

1. đọc plan đã duyệt;
2. xác minh hash nguồn/profile/model;
3. resolve lại target id;
4. áp đúng candidate đã duyệt;
5. ghi binding trong cùng transaction.

Điều này ngăn preview một kết quả nhưng commit một kết quả khác.

### 1.4 Không đưa AI vào đường quyết định cuối

AI/Ollama có thể đề xuất mapping và giải thích diagnostics. Kết quả phải đi qua:

```text
AI proposal → schema parser → allowed target lookup → deterministic validator → review/ready
```

AI không được tự đặt `ready`, tự hạ tolerance hoặc tự xác nhận commit.

## 2. Cấu trúc module dự kiến

Tên cuối cùng có thể điều chỉnh để khớp convention, nhưng ranh giới phụ thuộc không đổi.

```text
src/
├── DhcbTools.Shared.Logic/
│   └── CadToBim/
│       ├── Contracts/
│       │   ├── CadSemanticManifest.cs
│       │   ├── CadEntityDto.cs
│       │   ├── CadMappingProfile.cs
│       │   ├── CadModelPlan.cs
│       │   └── SchemaVersion.cs
│       ├── Coordinates/
│       │   ├── CadTransform.cs
│       │   └── ControlPointSolver.cs
│       ├── Semantics/
│       │   ├── Evidence.cs
│       │   ├── ConfidencePolicy.cs
│       │   ├── DimensionConstraint.cs
│       │   └── CandidateClassifier.cs
│       ├── Planning/
│       │   ├── GridLevelPlanner.cs
│       │   ├── WallPlanner.cs
│       │   ├── BlockPlacementPlanner.cs
│       │   ├── MepRoutePlanner.cs
│       │   └── CadModelPlanBuilder.cs
│       ├── Reconcile/
│       │   ├── BindingFingerprint.cs
│       │   └── CadModelDiffer.cs
│       └── Reporting/
│           └── CadToBimReport.cs
├── DhcbTools.Core.AutoCAD/
│   └── CadToBim/
│       ├── CadSemanticExportCommand.cs
│       ├── AcadEntityExtractor.cs
│       ├── AcadDimensionExtractor.cs
│       └── AcadBlockWalker.cs
├── DhcbTools.Core/
│   └── CadToBim/
│       ├── CadModelPlanCommand.cs
│       ├── CadModelApplyCommand.cs
│       ├── CadModelReconcileCommand.cs
│       ├── RevitTargetResolver.cs
│       ├── RevitCandidateWriter.cs
│       └── CadBindingStore.cs
└── DhcbTools.Revit/
    └── Commands/
        └── CadToBimRibbonCommands.cs

tests/
├── DhcbTools.Shared.Logic.Tests/
│   └── CadToBim/
└── suites/
    ├── autocad-cad-to-bim.json
    └── revit-cad-to-bim.json
```

Không để `Autodesk.AutoCAD.*` hoặc `Autodesk.Revit.*` lọt vào `Shared.Logic`.

## 3. Contract và serialization

### 3.1 DTO

DTO dùng kiểu cụ thể, không dùng `JObject` trong domain:

```csharp
public sealed class CadSemanticManifest
{
    public required string SchemaVersion { get; init; }
    public required CadSourceInfo Source { get; init; }
    public required CadCoordinateInfo CoordinateSystem { get; init; }
    public List<CadEntityDto> Entities { get; init; } = new();
    public List<CadDimensionDto> Dimensions { get; init; } = new();
    public List<CadTextDto> Texts { get; init; } = new();
    public List<CadBlockDto> Blocks { get; init; } = new();
    public List<CadDiagnostic> Diagnostics { get; init; } = new();
}
```

Các variant geometry dùng discriminator `kind`, ví dụ `line`, `polyline`, `arc`, `circle`, thay vì dictionary không kiểu. Vì dự án chạy net48, tránh converter dựa vào API chỉ có ở .NET mới; dùng `Newtonsoft.Json` converter nhỏ có test round-trip.

### 3.2 Canonical hash

Hash dùng để chặn stale plan:

- UTF-8 không BOM.
- Object property theo thứ tự xác định.
- Collection entity sort theo `sourceKey`.
- Số normalize bằng `NumericText` với precision đã định nghĩa theo loại.
- Không đưa `extractedAtUtc`, đường dẫn report và diagnostics không quyết định mô hình vào hash nghiệp vụ.

Hash cần tách:

- `sourceContentHash`: SHA-256 file DWG gốc.
- `manifestSemanticHash`: canonical semantic content.
- `profileHash`: canonical mapping profile.
- `planHash`: semantic hash + profile hash + transform + candidate decisions.

### 3.3 Validation tại boundary

Mọi command thực hiện theo thứ tự:

1. file tồn tại và nằm trong phạm vi path policy;
2. kích thước file trong giới hạn;
3. JSON parse;
4. schema version;
5. required field;
6. số hữu hạn, tolerance dương, confidence `[0,1]`;
7. duplicate key/candidate id;
8. target lookup trong host hiện tại;
9. business invariant.

Lỗi trả `CommandResult.Fail` với mã máy đọc được; không ném stack trace qua Bridge.

## 4. AutoCAD adapter

### 4.1 `CadSemanticExportConfig`

```jsonc
{
  "outputPath": "D:/DHCB/cad/MEP-L02.manifest.json",
  "reportPath": "D:/DHCB/cad/MEP-L02.report.html",
  "layouts": ["Model"],
  "includeLayers": [],
  "excludeLayers": [],
  "includeXrefs": false,
  "maxNestedBlockDepth": 16,
  "maxEntities": 200000,
  "coordinateUnit": "auto"
}
```

Lệnh chỉ đọc drawing, không cần `dryRun`. `outputPath` và `reportPath` phải qua `AcadHelpers.EnsureParentDirectory` và path policy chung.

### 4.2 Duyệt block

`AcadBlockWalker`:

- Bắt đầu từ từng layout được chọn.
- Với `BlockReference`, resolve effective block name bằng `AcadHelpers.EffectiveBlockName`.
- Nhân transform theo thứ tự parent→child.
- Xuất insertion point, rotation, scale và attributes.
- Đi sâu block thường/dynamic tới `maxNestedBlockDepth`.
- Không đi sâu xref khi `includeXrefs=false`.
- Phát hiện cycle theo `BlockTableRecord.ObjectId` trên stack hiện tại.
- Anonymous block của DIM/hatch được đánh dấu `internal`, không phân loại thành thiết bị.

Không explode hoặc mở object `ForWrite`.

### 4.3 DIM

`AcadDimensionExtractor` dùng typed cases:

- `RotatedDimension`, `AlignedDimension`;
- `RadialDimension`, `DiametricDimension`;
- `AngularDimension` variants;
- `OrdinateDimension`.

Xuất measurement từ API sau khi bảo đảm dimension đã evaluate. Không gọi command-line `DIMREGEN`. Text override và tolerance/prefix/suffix được giữ riêng.

Nếu API không trả associative references ổn định, giữ definition points và đưa diagnostic `DIM_REFERENCE_UNAVAILABLE`; planner chỉ ghép spatial theo tolerance.

### 4.4 Đơn vị

- `INSUNITS` hợp lệ → dùng.
- `Unitless` → yêu cầu `coordinateUnit`; không đoán từ kích thước bản vẽ.
- Scale transform block được áp trước đổi đơn vị.
- Tất cả DTO ra mm.
- Góc ra degree chuẩn hóa `[0,360)`.

## 5. Shared.Logic

### 5.1 Hệ tọa độ

`CadTransform` là affine 2.5D cho lát đầu:

- scale từ unit;
- rotation quanh Z;
- translation XYZ;
- optional elevation offset theo level.

`ControlPointSolver`:

- một mốc: chỉ translation;
- hai mốc: translation + rotation + uniform scale kiểm chứng;
- từ ba mốc: least-squares residual, nhưng không cho non-uniform scale/shear ở bản đầu;
- residual vượt profile tolerance → `E-CAD-COORD`.

Mọi transform có round-trip test CAD→Revit→CAD.

### 5.2 Evidence và confidence

Mỗi rule trả `Evidence` có:

- `kind`;
- `sourceKeys`;
- `value`;
- `weight`;
- `hardConstraint`;
- `reason`.

`ConfidencePolicy` không cộng điểm tùy tiện. Nó nhận các bằng chứng chuẩn hóa và một bảng weight từ profile, sau đó:

1. hard conflict → `review/rejected`;
2. target không tồn tại → `rejected`;
3. tính score `[0,1]`;
4. áp ngưỡng status;
5. lưu breakdown để report giải thích.

Không dùng random hoặc model AI trong scorer.

### 5.3 Spatial index

Không so mọi DIM/text với mọi geometry O(n²). Dùng spatial hash/grid thuần:

- cell size cấu hình, mặc định theo search radius lớn nhất;
- index bounding box của geometry, DIM, text, block;
- query theo bbox mở rộng;
- kết quả luôn sort stable theo distance rồi `sourceKey`.

Có benchmark test dữ liệu sinh tất định để bắt hồi quy complexity.

### 5.4 Planner theo loại

Mỗi planner nhận cùng interface thuần:

```csharp
IReadOnlyList<CadCandidate> Plan(
    CadSemanticManifest manifest,
    CadMappingProfile profile,
    PlanningContext context,
    IList<CadDiagnostic> diagnostics);
```

`PlanningContext` chỉ chứa target catalog DTO đã đọc từ Revit: level, family/type, system/type, parameter metadata. Nó không chứa `Document`.

#### Grid/Level

Tái sử dụng `GridNaming`, không sao chép thuật toán. Output candidate chuyển được về input của `GridFromCsv` hoặc writer chung.

#### Wall

Hai strategy rõ ràng:

- `centerline`: line/polyline là location curve;
- `parallelPair`: tìm cặp gần song song, overlap đủ, khoảng cách khớp thickness type.

Không bật cả hai cho cùng selector nếu không có precedence; nếu hai candidate cạnh tranh → ambiguous.

#### Block placement

Dùng effective block name, transform và attributes. Target type phải ở `PlanningContext`. Parameter map validate storage type/unit trước khi candidate `ready`.

#### MEP route

Tái sử dụng `CadCurveFilter` và dữ liệu đầu vào của `RouteGraph`; planner không tự dựng fitting. Size/spec chỉ được resolve thành DTO plan.

## 6. Revit adapter

### 6.1 `CadModelPlanConfig`

```jsonc
{
  "manifestPath": "D:/DHCB/cad/MEP-L02.manifest.json",
  "profilePath": "D:/DHCB/profiles/office-mep-v1.json",
  "outputPlanPath": "D:/DHCB/plans/MEP-L02.plan.json",
  "reportPath": "D:/DHCB/plans/MEP-L02.plan.html",
  "targetKinds": ["Grid", "Level", "Wall", "Column", "Door", "Window", "MepDevice", "MepRoute"],
  "levels": ["Tầng 2"],
  "transform": {
    "rotationDeg": 0,
    "offsetXMm": 0,
    "offsetYMm": 0,
    "offsetZMm": 0,
    "controlPoints": []
  },
  "minimumReadyConfidence": 0.85,
  "reviewConfidence": 0.65,
  "maxCandidates": 10000
}
```

Lệnh chỉ đọc model và ghi file plan/report. Nó phải đưa `documentId` và revision vào plan khi chạy qua Bridge.

### 6.2 Target catalog

`RevitTargetResolver` đọc một lần:

- levels;
- wall/floor types;
- family symbols theo category;
- MEP curve/system types;
- writable parameter metadata;
- routing preference cần thiết.

Không collector trong vòng lặp candidate. Resolver tạo DTO nhẹ rồi giao cho Shared.Logic.

### 6.3 `CadModelApplyConfig`

```jsonc
{
  "planPath": "D:/DHCB/plans/MEP-L02.plan.json",
  "candidateIds": [],
  "includeStatuses": ["ready"],
  "maxCandidates": 2000,
  "failurePolicy": "rollbackAll",
  "dryRun": true
}
```

`dryRun=true` đọc lại plan và mô phỏng resolve/precondition nhưng không mở transaction. Chạy thật qua MCP còn cần context ngoài config: `documentId`, `previewToken`, `confirm=true`.

### 6.4 Writer

`RevitCandidateWriter` dispatch nội bộ theo `targetKind`, không thêm command Core cho từng primitive.

Thứ tự apply trong một lô:

1. Level/Grid.
2. Wall/Floor/Column.
3. Door/Window và family host-based.
4. MEP device.
5. MEP route/fitting.
6. Parameter sau tạo.
7. Binding.

Nếu có phụ thuộc giữa candidate, plan phải có `dependsOn`; topological sort thuần phát hiện cycle trước transaction.

`rollbackAll` là mặc định. Một candidate lỗi → rollback cả lô và report candidate gây lỗi. Chế độ partial không nằm trong bản đầu.

### 6.5 Binding store

- Extensible Storage schema có GUID cố định, versioned.
- Ghi entity-level binding trên phần tử do DHCB sở hữu.
- Ghi `DataStorage` index trong cùng transaction.
- Không ghi binding lên phần tử thủ công chỉ vì geometry giống.
- Khi update, so `lastAppliedTargetHash` với target state hiện tại; khác → conflict.
- Có lệnh rebuild index chỉ đọc element storage rồi tạo lại index; đây là maintenance nội bộ, chưa expose MCP ở lát đầu.

## 7. AutoRoute nhiều phương án

### 7.1 Tương thích ngược

Không đổi nghĩa field hiện có. Khi `alternativeCount` thiếu hoặc bằng 1, hành vi phải giống `AutoRoute` hiện tại.

### 7.2 Sinh phương án

Không chạy cùng A* ba lần với cùng input. Tạo candidate bằng biến thiên tất định:

- preferred elevation bands;
- hành lang trái/phải hoặc trên/dưới vật cản chính;
- penalty profile khác nhau trong phạm vi cấu hình;
- cấm cạnh/đoạn của phương án trước để tìm đường khác.

Dedupe theo canonical polyline trong tolerance. Không đủ số phương án thì trả số tìm được và lý do.

### 7.3 Score

```text
score = lengthWeight × normalizedLength
      + turnWeight × turns
      + verticalWeight × verticalChanges
      + nearObstacleWeight × nearObstacleSteps
      + preferredElevationWeight × elevationDeviation
```

Thấp hơn là tốt hơn. Response phải trả từng thành phần, không chỉ tổng điểm. Hard constraint clearance được kiểm riêng, không đổi thành penalty mềm.

### 7.4 Áp tuyến

Chỉ candidate route được người dùng chọn mới chuyển sang `RouteFromLines`. Mặc định tạo model line đề xuất; `buildRoute=true` vẫn cần preview/commit guard.

## 8. Reconcile

### 8.1 `CadModelReconcileConfig`

```jsonc
{
  "manifestPath": "D:/DHCB/cad/MEP-L02-r2.manifest.json",
  "profilePath": "D:/DHCB/profiles/office-mep-v1.json",
  "outputPath": "D:/DHCB/reconcile/MEP-L02-r2.json",
  "reportPath": "D:/DHCB/reconcile/MEP-L02-r2.html",
  "dryRun": true
}
```

Bản đầu chỉ lập diff; việc apply update đi lại qua `CadModelPlan`/`CadModelApply`, không tạo đường ghi thứ hai.

### 8.2 Trạng thái diff

- `unchanged`: source và target hash khớp.
- `sourceChanged`: nguồn đổi, target chưa bị sửa thủ công.
- `targetChanged`: nguồn không đổi, target bị sửa.
- `conflict`: cả hai đổi.
- `new`: nguồn mới.
- `orphaned`: nguồn mất.
- `missingTarget`: binding còn nhưng element đã bị xóa.

Không tự xóa orphan/missing source.

## 9. CommandCatalog, Ribbon và MCP

### 9.1 Lệnh mới

| Lệnh | App | writesModel | Ghi chú |
|---|---|---:|---|
| `CadSemanticExport` | AutoCAD | false | Xuất manifest/report |
| `CadModelPlan` | Revit | false | Đọc model, xuất plan/report |
| `CadModelApply` | Revit | true | Áp plan đã duyệt |
| `CadModelReconcile` | Revit | false ở bản đầu | Chỉ lập diff |

Mỗi lệnh phải có descriptor, field schema và alias trong `CommandCatalog`; `CommandCatalogTests` phải đối chiếu Core↔catalog↔dispatch.

### 9.2 AutoCAD MCP

Trước khi ship `CadSemanticExport`, phải bỏ giới hạn `autocad_execute` chỉ bốn lệnh hoặc tạo tool schema động từ `/tools` giống `scripts/dhcb_mcp_server.py`. Không duy trì hai allowlist thủ công.

Lệnh read-only không cần confirmation nhưng vẫn cần token/path validation.

### 9.3 Revit wizard

Wizard gọi cùng Core command, không chứa business logic. State wizard lưu đường dẫn/profile/lựa chọn candidate, nhưng không lưu `previewToken` qua phiên ứng dụng.

## 10. Bảo mật và an toàn

- Bridge chỉ loopback, Bearer token và rate limit giữ nguyên.
- Manifest/plan là input không tin cậy; validate trước deserialization vào workflow.
- Không cho path traversal hoặc ghi đè file DWG/RVT nguồn.
- Report escape HTML.
- Không ghi token, full prompt AI hoặc nội dung bí mật vào log.
- Lệnh ghi phải qua `BridgeCommitGuard`.
- `planHash` phải ràng buộc candidate decisions; sửa file plan sau preview làm token vô hiệu.
- Timeout có ba trạng thái: success/failure/unknown; async job id dùng cho lệnh dài.
- Apply giới hạn candidate để tránh một request chiếm Revit quá lâu.

## 11. Kế hoạch triển khai theo lát dọc

Mỗi lát phải build/test xanh và có thể merge độc lập sau review. Feature chưa hoàn chỉnh được ẩn khỏi Ribbon/catalog public bằng cờ build hoặc `Supported=false`; không expose đường ghi chưa có host test.

### Lát 0 — Contract và spike API (2–3 ngày)

**Mục tiêu:** chứng minh AutoCAD API đọc được DIM/block semantic cần thiết và Revit Extensible Storage đáp ứng binding.

- Viết contract v1 và canonical hash trong Shared.Logic.
- Spike đọc một fixture có nested dynamic block + DIM override.
- Spike ghi/đọc binding trên bản sao model Revit.
- Không thêm command public.

**Gate:** round-trip JSON; DIM measurement/override đúng; binding survive save/reopen; tài liệu cập nhật nếu API không cung cấp association.

### Lát 1 — `CadSemanticExport` (3–5 ngày)

- AutoCAD extractor cho geometry/text/block/attribute/DIM.
- Đơn vị và nested transform.
- Manifest/report.
- CommandCatalog + Bridge/MCP generic.

**Gate:** host test AutoCAD; chạy lại cùng DWG cho semantic hash giống nhau; drawing không đổi.

### Lát 2 — Tọa độ + Grid/Level plan/apply (3–5 ngày)

- ControlPointSolver, transform.
- `CadModelPlan` chỉ cho Grid/Level.
- `CadModelApply` + binding.
- Wizard tối thiểu và vòng MCP preview/apply/verify.

**Gate:** sai lệch fixture; rerun 0 create; stale plan bị chặn; save/reopen vẫn reconcile được.

### Lát 3 — Block → family/column/device (4–5 ngày)

- Block mapping và parameter map.
- Resolve family/type/level/host.
- Placement writer + ChangedIds.

**Gate:** dynamic/nested block, rotation/scale, parameter storage type, target bịa, host missing, rerun.

### Lát 4 — Wall/Door/Window/Floor (5 ngày)

- Centerline/parallel-pair wall planner.
- Door/window host relation.
- Closed polyline floor.

**Gate:** ambiguity, gap, thickness conflict, orientation, no free-floating hosted family, no duplicate.

### Lát 5 — MEP route từ CAD (4–5 ngày)

- Route candidate/spec resolution.
- Adapter sang `RouteFromLines`.
- Connector hậu kiểm.

**Gate:** size source precedence, short segment, branch/cycle, routing preference missing, no new open connector beyond expected.

### Lát 6 — AutoRoute đa phương án (4–5 ngày)

- Config additive, alternative generator, dedupe, scorer.
- UI/MCP chọn đúng candidate.

**Gate:** deterministic, ≥2 phương án trên fixture có hai hành lang, hard clearance, score breakdown, compatibility với config cũ.

### Lát 7 — Reconcile revision (3–5 ngày)

- Source/target hash, diff state, report.
- Update plan cho sourceChanged; conflict/orphan read-only.

**Gate:** ma trận source/target đổi độc lập; không xóa orphan; phần tử thủ công không bị nhận ownership.

### Lát 8 — Pilot và hardening (thời gian lịch phụ thuộc người dùng)

- Hai dự án, mỗi dự án ít nhất một DWG kiến trúc/kết cấu và một DWG MEP nếu có.
- Đo precision/review/reject, thời gian sửa, rerun, reconcile.
- Profile tuning dựa trên dữ liệu thật.
- Independent review và host regression toàn bộ.

Không gộp Lát 1–7 thành một PR lớn.

## 12. Chiến lược kiểm thử

### 12.1 Shared.Logic

Nhóm test bắt buộc:

- Contract/schema: round-trip, major version sai, field thiếu, số NaN/Infinity.
- Canonical hash: thứ tự JSON không làm đổi hash; semantic đổi phải đổi hash.
- Transform: translation/rotation/unit, round-trip, control point residual.
- Nested block transform và source key.
- DIM: override, tolerance, geometry conflict.
- Mapping: wildcard, precedence, target absent, parameter type mismatch.
- Confidence: biên 0.65/0.85, hard conflict thắng score.
- Spatial index: kết quả khớp brute-force trên dữ liệu nhỏ; tăng quy mô không O(n²).
- Planner từng loại và ambiguity.
- Dependency graph/cycle.
- Diff matrix và ownership.
- Route alternatives/dedupe/score/determinism.
- HTML/CSV escape và tiếng Việt.

### 12.2 AutoCAD host

Fixture được tạo trên bản vẽ tạm, không dùng file khách hàng:

- nested/dynamic block có attribute;
- line/polyline/arc/circle;
- các loại DIM hỗ trợ, ít nhất một override;
- UCS/WCS, insertion unit;
- xref bật/tắt;
- anonymous DIM/hatch block;
- 50.000 entity benchmark riêng, không chạy mọi PR nếu quá chậm.

Assertions đọc lại manifest bằng parser Shared.Logic, không so nguyên file vàng.

### 12.3 Revit host

Trên bản sao model mẫu:

1. plan dry-run;
2. apply thật;
3. query `ChangedIds`/geometry;
4. save/reopen;
5. apply lại = 0 create;
6. đổi DWG manifest → reconcile;
7. sửa tay một target → conflict;
8. verify connector/clash/snapshot.

Mọi test ghi phải dùng ba lớp khóa hiện có và bản sao model.

### 12.4 MCP/Bridge

- `/tools` khớp catalog.
- MCP read-only không gọi được `CadModelApply`.
- Apply không preview token bị chặn.
- Token của plan A không áp plan B.
- Document/model revision thay đổi sau preview bị chặn.
- Timeout không để lệnh abandoned chạy sau.
- Async progress trả cùng `CommandResult` cuối.

## 13. Cổng CI/build

Sau mỗi lát:

```text
pytest
Dotnet Shared.Logic tests
BatchRunner tests
scripts/check-build.sh cho matrix đang hỗ trợ
Windows WPF build
Python MCP tests nếu sửa server
AutoCAD/Revit host suite tương ứng trước khi bỏ nhãn thử nghiệm
```

Dùng đúng lệnh CI hiện hành trong repository khi triển khai; danh sách trên là loại gate, không thay thế workflow thật.

## 14. Quan sát và telemetry cục bộ

Usage log thêm field hoặc message có cấu trúc:

- command/version;
- source entity count;
- candidate count theo status/action/type;
- elapsed extract/normalize/plan/apply/verify;
- conflicts/orphans;
- route alternatives;
- success/failure/error code.

Không log path đầy đủ nếu report có thể chia sẻ; dùng tên file hoặc hash rút gọn. Không gửi telemetry ra ngoài.

## 15. Kế hoạch rollback

- Lát contract/extractor chỉ tạo file, rollback bằng bỏ command.
- Apply dùng transaction `rollbackAll`.
- Binding additive; không thay schema cũ tại chỗ. Schema mới dùng GUID/version mới và migration đọc cũ→ghi mới có test.
- Phần tử đã tạo chỉ được xóa qua lệnh riêng có preview; rollback release không tự xóa model.
- Config AutoRoute cũ tiếp tục hoạt động khi field mới không có.

## 16. Quyết định cần khóa trước khi code

| ID | Quyết định đề xuất | Trạng thái |
|---|---|---|
| D1 | Manifest JSON versioned là ranh giới AutoCAD→Revit | Đề xuất chấp nhận |
| D2 | DIM chỉ là constraint/evidence, không phải geometry authority tuyệt đối | Đề xuất chấp nhận |
| D3 | Mặc định chỉ candidate `ready` được chọn để apply; `review` cần duyệt riêng | Đề xuất chấp nhận |
| D4 | Extensible Storage + DataStorage index cho binding | Cần spike Lát 0 |
| D5 | Reconcile không tự xóa orphan | Đề xuất chấp nhận |
| D6 | AutoRoute mặc định tạo phương án/model line, không dựng MEP thật | Giữ quyết định roadmap hiện tại |
| D7 | `CadModelApply` là một command nhiều target kind, writer nội bộ | Đề xuất chấp nhận |
| D8 | AutoCAD MCP chuyển sang catalog động, không mở rộng allowlist thủ công | Đề xuất chấp nhận |
| D9 | Bản đầu chỉ uniform scale + rotation Z; không shear/non-uniform project transform | Đề xuất chấp nhận |
| D10 | Pilot hai dự án là điều kiện bỏ nhãn thử nghiệm | Bắt buộc |

## 17. Definition of Done toàn tính năng

- Contract và docs khớp code; không có field tài liệu không tồn tại.
- Mọi command mới có catalog/dispatch/schema/form hoặc wizard phù hợp.
- Shared.Logic không tham chiếu Autodesk.
- Host test chứng minh extract, apply, rerun, save/reopen và reconcile.
- Preview/commit guard chặn stale plan và nhầm document.
- Không candidate thấp/ambiguous được ghi im lặng.
- Không nhân đôi; không tự xóa orphan; không ghi đè phần tử thủ công.
- AutoRoute nhiều phương án deterministic, giải thích score và giữ tương thích cũ.
- CI, Windows build, AutoCAD/Revit host suites xanh.
- Independent review không còn blocking finding.
- Pilot đạt tiêu chí đã chốt và có người dùng thật xác nhận tiếp tục sử dụng.

Chỉ khi đủ các mục trên mới được gọi là sẵn sàng sản xuất; “build được” hoặc “chạy một model mẫu” chưa đủ.