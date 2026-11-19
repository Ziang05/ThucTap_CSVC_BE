# Nhóm 19 — Quản lý kiểm kê thiết bị

## Phạm vi và nguồn đặc tả

Triển khai UC-KK-01 đến UC-KK-16 từ [Danh sách nghiệp vụ, Nhóm 19 V7](https://docs.google.com/spreadsheets/d/1xQg5ao1fWNIA5qC8qisSoVaUeUuRZ9UFa3_nse2ZHyA/edit#gid=2026111919).
Đã đối chiếu [DBML extension V3](https://drive.google.com/file/d/1qJiE2XAyy5OS8rnzXadc1ZOldq-Tfu44/view),
[DBML standalone V3](https://drive.google.com/file/d/1Zxi7oca26jI3Z_L0h1VDzsH3MYWU_T3y/view),
[SQL rules V3](https://drive.google.com/file/d/1qxPONF8SUlLVQYkt2DWKWcBxVqwGdSZi/view),
[notes V3](https://drive.google.com/file/d/1JLxFQvbs_praUaIXkXJ_WrgOFBvvrGTR/view) và [ZIP V3](https://drive.google.com/file/d/1EbGU0P2s0gDXWPQH7ORZ0IZWaWe1kZMO/view).
Bốn file trong ZIP khớp nội dung các file rời (khác biệt duy nhất khi so byte là newline cuối file tải về).

Kiến trúc: Core chứa entity; UseCases chứa service/DTO/validation và repository interface; Infrastructure chứa EF mapping, truy vấn và SQL integrity; API chứa controller và reminder worker.
EF Core 10/Npgsql PostgreSQL, `CsvcDbContext`, khóa chính UUID của thiết bị/account/phòng ban/khu vực đã được xác minh từ source. Authentication dùng opaque bearer session hiện có, không phải JWT độc lập của module.

## Điều chỉnh V3 để tích hợp

| Thành phần | Tích hợp thực tế |
|---|---|
| Hạ tầng chung | Tham chiếu `accounts`, `user_profiles`, `roles`, `departments`, `devices`, `areas`, `equipment_settings`; không tạo lại các bảng này. |
| RBAC | Tra tài khoản/session/role/phòng ban đang có hiệu lực ở server. QTTB phải thuộc phòng QTTB đang cấu hình. Manager muốn ghi item cũng phải được phân công. |
| Transaction | Tái sử dụng `IInventoryTransaction` của dự án: advisory lock account trước, inventory sau; mọi nghiệp vụ thiết bị hiện có dùng chung khóa. Snapshot lấy khóa device theo thứ tự ID. Trigger result lấy row lock phiếu để chống race với completion, kể cả ghi SQL trực tiếp. |
| Thông báo | Không tìm thấy notification/outbox dùng chung. Dùng đúng bảng `inventory_notifications` V3 cho in-app; lưu cùng transaction với publish/update/cancel. Không tạo thêm hệ thống outbox hoặc bảng UX. |
| Idempotency | Bổ sung `request_hash`, `response_data` vào ba bảng lịch sử. Các lịch sử là ledger phản hồi đã commit; không tạo bảng request mới. Bỏ approval/cancel request ID dư thừa ở header. Request ID được tra cả ba nguồn dưới khóa transaction chung. |
| Snapshot | Bổ sung tên/mã người lập, assignee và người kiểm đầu tiên; tên actor trên lịch sử item, `quantity_snapshot`. Báo cáo không phụ thuộc tên/thiết bị live sau chốt. `devices.quantity` thực tế chỉ 0/1; snapshot cho phép cả hai. Một item tương ứng một device ID. |
| Status/action | Lưu text có CHECK thay vì thêm native PostgreSQL enum; tránh phải thay cấu hình enum của hạ tầng chung. API trả nhãn V7 viết hoa. |
| Chính sách | Singleton riêng của nghiệp vụ kiểm kê, mặc định 3 ngày, cho phép 1–365 ngày; chỉ manager sửa, bắt buộc version/request ID/lý do và audit. Không áp hồi tố lên revision đã công bố. |

`inventory-schema.dbml` là extension được sinh từ migration thực tế: 10 bảng, 30 FK, unique/index/partial index. Bảng nền không nằm trong extension; muốn xem độc lập cần ghép schema nền của dự án. SQL guard/view nằm trong `InventoryIntegritySql.cs` và được migration gọi tự động. **Không chạy lại SQL V3 trên Drive sau migration**, vì tên/type/ràng buộc đã được tích hợp.

## Quyền và trạng thái

`PENDING_APPROVAL → PREPARING → IN_PROGRESS → COMPLETED`; chỉ hủy trước START. Mọi mutation header có `expectedVersion` của phiếu. Tạo có version 0, mỗi thay đổi header/plan tăng 1. Ghi/sửa item chỉ tăng version riêng của result, không tăng version phiếu.

Manager duyệt/công bố, duyệt hoặc từ chối revision, sửa policy và chốt. Staff tạo phiếu, sửa/hủy nháp do mình lập và đề nghị revision trước START. Chỉ QTTB được phân công trên revision published hiệu lực được bắt đầu, ghi lần đầu hoặc sửa kết quả.

PB chỉ đọc kế hoạch published của PB mình trong `PREPARING`. `GET /{id}` trả trực tiếp `InventoryPlanDto` cho PB; QTTB nhận `InventorySlipDto` với plan/revisions/results/progress. Endpoint `/plan` luôn trả DTO kế hoạch; PB không nhận trường results, inspector, item history hoặc progress. API report/progress/history từ chối PB. Phiếu không thuộc scope trả 404; thao tác bị cấm trả 403.

Bản published mới nhất là baseline hiệu lực. Propose tạo DRAFT mới, giữ nguyên bản published cũ; sau publish mới thay baseline. Không cho START nếu còn revision DRAFT chưa xử lý. Manager có thể reject revision đề nghị và tiếp tục dùng baseline cũ. Không đổi PB hoặc ghi chú header đã công bố. Tất cả revision/items/assignees bị khóa sau START.

## Endpoint và đối chiếu UC

Base path: `/api/v1/inventory-slips`; `{id}`, `{revisionId}`, `{itemId}` là UUID từ API.

| UC | Endpoint / xử lý |
|---|---|
| 01 | `POST /` tạo phiếu, nháp, item snapshot, assignment, CREATE history. |
| 02 | `PUT /{id}` sửa nháp theo chủ sở hữu/manager và version. |
| 03 | `POST /{id}/cancel` có lý do; hủy bản published chỉ manager và tạo notice. |
| 04 | `POST /{id}/revisions/{revisionId}/publish` duyệt và công bố nguyên tử. |
| 05 | `POST /{id}/items/{itemId}/results` INSERT kết quả đầu tiên và RESULT_RECORDED. |
| 06 | `GET /{id}/progress` đếm kết quả hiện hành và DISTINCT checked_by gốc. |
| 07 | `POST /{id}/complete` chốt khi total > 0 và mọi item đã có kết quả. |
| 08 | `GET /` có `page`, `pageSize`, `search`, `departmentId`, `status`, `from`, `to`. |
| 09 | `GET /{id}` chi tiết theo quyền. |
| 10 | `GET /{id}/history`, `GET /{id}/items/{itemId}/history`; hai nguồn lịch sử tách biệt. |
| 11 | In-app transaction + reminder worker; policy/inbox endpoints bên dưới. |
| 12 | `GET /{id}/plan` published preview cho PB đúng scope. |
| 13 | `POST /{id}/revisions` đề nghị, publish như UC04; `POST /{id}/revisions/{revisionId}/reject`. |
| 14 | `POST /{id}/start` đến lịch, đủ notice, đúng assignee và không pending revision. |
| 15 | `GET /{id}/report` JSON DRAFT/OFFICIAL; `GET /{id}/report.csv` UTF-8 BOM, chống CSV formula injection. |
| 16 | `PATCH /{id}/items/{itemId}/result` sửa Có↔Không có version/lý do/idempotency. |

Endpoint policy: `GET/PUT /api/v1/inventory-notice-policy`, `GET /api/v1/inventory-notice-policy/history`.
Inbox: `GET /api/v1/inventory-notifications`, `POST /api/v1/inventory-notifications/{id}/read`.
Các endpoint danh sách/lịch sử dùng page mặc định 1, pageSize 20, tối đa 100. Read chỉ đánh dấu đã đọc, không phải ACK/xác nhận kiểm kê.

## Ví dụ request

Tạo phiếu:

```json
{
  "requestId": "<new-uuid>",
  "departmentId": "<department-uuid>",
  "plannedStartAt": "2026-12-01T08:00:00+07:00",
  "plannedEndAt": "2026-12-01T10:00:00+07:00",
  "deviceIds": ["<device-uuid>"],
  "assigneeAccountIds": ["<qttb-account-uuid>"],
  "notes": "Kiểm kê cuối kỳ"
}
```

Bỏ `deviceIds` hoặc gửi null: server lấy các device đang thuộc phòng ban. Gửi `[]`: nháp rỗng hợp lệ nhưng không thể duyệt. Có thể chọn danh sách con, tối đa 2.000 device; assignees tối đa 100. Duplicate/empty UUID bị từ chối. Snapshot vẫn có thể chứa thiết bị đang BT/SC/PX/TH; module không kiểm/ghi holds hoặc tình trạng hoạt động của thiết bị.

Sửa nháp/propose nhận các trường như trên, thêm `expectedVersion`. Propose bắt buộc `reason`; lịch trong tương lai, end nếu có phải sau start. Mutation publish/start/complete nhận:

```json
{ "requestId": "<new-uuid>", "expectedVersion": 0 }
```

Ghi kết quả đầu tiên:

```json
{ "requestId": "<new-uuid>", "exists": true }
```

Sửa kết quả:

```json
{
  "requestId": "<new-uuid>",
  "expectedVersion": 0,
  "newExists": false,
  "reason": "Đối chiếu lại danh sách tại phòng"
}
```

`exists`/`newExists` bắt buộc là boolean, không có kết quả thứ ba. NOT_CHECKED được suy ra khi chưa có result. Không nhận actor/checked_by/checked_at do client khai báo. Version result bắt đầu 0. Sửa giữ nguyên checked_by/checked_at, tăng version 1 và ghi last_corrected_by/at cùng RESULT_CORRECTED. Người chỉ sửa không xuất hiện trong roster thực tế.

Frontend chịu trách nhiệm bảng đối chiếu và hộp thoại xác nhận lần hai. Hủy modal không gửi request. Backend có một API sửa, không có confirmation token, challenge, `confirmed` hoặc bảng xác nhận.

Retry phải dùng nguyên request ID, actor, endpoint và payload. Replay trả đúng phản hồi ban đầu ngay cả khi result đã được sửa tiếp hoặc phiếu đã chốt; replay không thực hiện mutation mới. ID dùng lại với payload/actor/đích khác trả `IDEMPOTENCY_CONFLICT`. Validation vẫn được áp dụng; không dùng request ID cũ để yêu cầu thay đổi mới.

## Publication và thông báo

Công bố kiểm lại PB active, thiết bị vẫn đúng PB và snapshot nhận dạng/quantity/area không lệch, assignees còn hợp lệ, có ít nhất một người nhận active ở PB. Thiếu điều kiện thì rollback cả publication/history/notice. Nếu nguồn lệch lúc duyệt, cần refresh bằng sửa nháp; với revision đề nghị đã lệch, reject và đề nghị lại để không âm thầm đổi snapshot.

Lead time đo từ giờ server publish đến start, chính xác `minimumNoticeDays × 24 giờ`; lịch bằng ngưỡng được chấp nhận. Policy được chụp vào revision. Chặn overlap với bản published hiệu lực của phiếu PREPARING/IN_PROGRESS cùng PB; nếu không có end, coi lịch là một thời điểm; khoảng có end dùng quy tắc [start,end).

In-app là kênh đã triển khai. `delivery_status=SENT`, `sent_at=published_at` chỉ việc notice đã có trong inbox; không giả lập việc gửi email/push. Dự án chưa có kênh push/email chung cho notification, nên không cấu hình retry kênh ngoài. `event_key` unique theo phiếu/revision/người nhận/loại, do đó retry không nhân đôi notice.

`InventoryReminderWorker` quét mỗi 5 phút; gửi một reminder trong khoảng 24 giờ trước start cho baseline published đang PREPARING. `Inventory:RemindersEnabled=false` tắt worker. Khi tạm lỗi, worker ghi log và thử lại lượt sau; notice đã commit không tạo trùng. Worker không tự START, không đòi PB nhấn đọc và không sửa device.

## Lỗi thường gặp

| HTTP / code | Ý nghĩa |
|---|---|
| 400 / VALIDATION_FAILED, REASON_REQUIRED, VALUE_UNCHANGED | Thiếu trường, lý do hoặc kết quả mới không đổi; dự án thống nhất Validation = HTTP 400. |
| 401 / AUTHENTICATION_REQUIRED | Session thiếu, hết hạn, bị thu hồi hoặc account khóa. |
| 403 / INVENTORY_FORBIDDEN | Sai role, không được phân công hoặc không phải chủ nháp/manager. |
| 404 / INVENTORY_NOT_FOUND | Không tồn tại hoặc ngoài scope; item revision cũ không thể ghi. |
| 409 / VERSION_CONFLICT, RESULT_CHANGED | Version phiếu/result đã thay đổi. |
| 409 / ITEM_ALREADY_CHECKED | UC05 chỉ INSERT; cần chuyển qua UC16 nếu muốn đổi kết quả. |
| 409 / INVENTORY_COMPLETED | Phiếu đã chốt, không thể ghi/sửa/hủy/mở lại. |
| 409 / NOTICE_TOO_SHORT, PLAN_SOURCE_CHANGED, SCHEDULE_CONFLICT, NO_NOTICE_RECIPIENTS | Điều kiện công bố chưa đáp ứng. |
| 409 / INVENTORY_HAS_UNCHECKED_ITEMS, PLAN_CHANGE_PENDING | Chưa đủ kết quả để chốt hoặc revision nháp chưa được xử lý trước START. |
| 503 | Hạ tầng database/audit tạm lỗi; transaction rollback toàn bộ. Tái sử dụng transaction chung nên một số mã lỗi hạ tầng mang tiền tố DEVICE_WORKFLOW của dự án. |

## Migration và kiểm tra

Migration: `20261119051005_EquipmentInventoryGroup19`, tiếp sau `20261105032457_EquipmentInitiatedRecovery`. Chỉ tạo 10 bảng inventory, index/FK/CHECK, policy mặc định, hai view và các trigger module. Không ALTER các bảng nghiệp vụ khác. Down gỡ view/function/trigger và 10 bảng; chỉ dùng trên database thử hoặc theo kế hoạch rollback đã duyệt vì Down xóa dữ liệu kiểm kê.

Không chạy migration tự động khi API startup. Áp dụng theo quy trình triển khai hiện có của dự án. Không áp dụng thử vào database đang sử dụng; test dùng database riêng có tên ngẫu nhiên rồi tự xóa.

```bash
dotnet restore CSVC.sln
dotnet build CSVC.sln --no-restore
CSVC_POSTGRES_TEST_CONNECTION='<disposable-database-admin-connection>' \
  DOTNET_PROCESSOR_COUNT=2 DOTNET_USE_POLLING_FILE_WATCHER=1 \
  dotnet test CSVC.sln --no-restore
```

`InventoryApiTests` kiểm chứng lifecycle đủ UC, PB scope, owner/manager/assigned RBAC, revision, notice/policy, export snapshot, idempotency, stale version, concurrent insert/correction/completion, rollback khi audit/notification lỗi, SQL guards và reminders. Test schema chung kiểm đúng 62 entity gồm 10 bảng mới, FK Restrict; không nới logic của nhóm khác.

## Các quyết định chưa chốt / giới hạn giữ theo V7

1. Không thay assignee sau IN_PROGRESS; cần UC cấp/thu hồi quyền giữa kỳ nếu mở rộng.
2. Item chưa xác minh được giữ NOT_CHECKED và chặn completion; không tự thêm UNKNOWN hoặc ngoại lệ chốt.
3. PB chưa được xem biên bản sau completion; quyền hiện tại chỉ preview published khi PREPARING.
4. Email/push ngoài ứng dụng chưa được cấu hình; in-app và reminder đã triển khai đầy đủ.
