# API quản lý danh mục thiết bị — Nhóm 5

Nguồn nghiệp vụ: [Danh sách nghiệp vụ, tab Nhóm 5-1 và Nhóm 5-2, UC-DM-01…08](https://docs.google.com/spreadsheets/d/1xQg5ao1fWNIA5qC8qisSoVaUeUuRZ9UFa3_nse2ZHyA/edit). Quyền tạo/sửa/khóa được áp dụng theo xác nhận của chủ dự án: **cả Trưởng phòng và Nhân viên QTTB**, giải quyết chênh lệch giữa các tab.

## Endpoint và quyền

Tất cả endpoint cần `Authorization: Bearer <accessToken>` của phiên đang hoạt động. `QTTB_MANAGER` và `QTTB_STAFF` được quản lý danh mục, đọc lịch sử và xem cảnh báo khóa. `DEPARTMENT_STAFF` chỉ được đọc danh mục/chi tiết cơ bản. Quyền và phiên được kiểm tra từ database; không chỉ dựa vào thông tin xác thực cũ.

| Method / URL | Quyền | Thành công |
| --- | --- | --- |
| GET `/api/v1/device-categories` | Cả ba role | 200, phân trang |
| GET `/api/v1/device-categories/{displayCode}` | Cả ba role | 200, chi tiết theo quyền |
| POST `/api/v1/device-categories` | Hai role QTTB | 201 + Location |
| PUT `/api/v1/device-categories/{displayCode}` | Hai role QTTB | 204 |
| PUT `/api/v1/device-categories/{displayCode}/lock` | Hai role QTTB | 204 |
| DELETE `/api/v1/device-categories/{displayCode}/lock` | Hai role QTTB | 204 |
| GET `/api/v1/device-categories/{displayCode}/lock-impact` | Hai role QTTB | 200 |
| GET `/api/v1/device-categories/{displayCode}/history` | Hai role QTTB | 200, lịch sử của danh mục |
| GET `/api/v1/device-categories/{displayCode}/history/{historyId}` | Hai role QTTB | 200, chi tiết sự kiện trong danh mục |
| GET `/api/v1/history/device-categories` | Hai role QTTB | 200, lịch sử toàn bộ danh mục |
| GET `/api/v1/history/device-categories/{historyId}` | Hai role QTTB | 200, chi tiết sự kiện |
| DELETE `/api/v1/device-categories/{displayCode}` | Luôn từ chối | 403 + audit, không xóa |

UUID danh mục chỉ dùng nội bộ cho liên kết/FK. API tra cứu bằng `displayCode`, không nhận hoặc trả UUID danh mục. UUID `historyId` chỉ định danh sự kiện lịch sử. Với mã chứa `/`, phải encode từng giá trị đường dẫn: `DM/2026.01` thành `DM%2F2026.01`, không nối dấu `/` nguyên bản. Location của POST đã được encode. Đường dẫn lịch sử toàn bộ được tách riêng để mã danh mục `HISTORY` vẫn dùng được.

## Tạo và sửa

POST mẫu:

```json
{
  "displayCode": "DM_PC",
  "name": "Máy tính",
  "description": "Thiết bị văn phòng",
  "notes": "Ghi chú nội bộ QTTB"
}
```

Tên bắt buộc, tối đa 200 ký tự sau chuẩn hóa Unicode NFC, trim và gộp khoảng trắng. Tên không được trùng sau chuẩn hóa/không phân biệt hoa thường, kể cả danh mục Locked. Description và Notes là hai trường độc lập; tùy chọn, tối đa 4000 ký tự mỗi trường, trim, chuỗi trắng thành null. Giới hạn 4000 là giới hạn payload API, DB vẫn là text. Khi PUT, bỏ qua/null/chuỗi trắng xóa trường tùy chọn về null.

DisplayCode tùy chọn khi tạo. Bỏ qua/null/chuỗi trắng sẽ sinh `DM` + 32 ký tự hex viết hoa, kiểm tra trùng trước khi lưu và có unique index bảo vệ. Bộ cấp mã không dùng sequence hoặc thêm migration; không cam kết số thứ tự liên tiếp. Mã nhập tay được trim/viết hoa, tối đa 64 ký tự, bắt đầu bằng A–Z hoặc số, phần sau cho phép A–Z/0–9 và `.`, `_`, `/`, `-`. Mã của danh mục Locked vẫn giữ chỗ.

**DisplayCode bất biến sau khi tạo.** PUT chỉ nhận name/description/notes và version tùy chọn. Trường ngoài hợp đồng như id/displayCode/status/actorAccountId bị từ chối 400. AutoMapper chỉ map nội dung; ID/mã/trạng thái được bảo toàn. Danh mục mới luôn Active; danh mục Locked phải mở khóa trước khi sửa.

GET chi tiết và POST cho hai role QTTB trả:

```json
{
  "displayCode": "DM_PC",
  "name": "Máy tính",
  "description": "Thiết bị văn phòng",
  "status": "Active",
  "deviceCount": null,
  "notes": "Ghi chú nội bộ QTTB",
  "version": "<64 ký tự hex>",
  "management": {
    "createdByAccountCode": "TK_MANAGER",
    "createdByFullName": "Nguyễn Văn An",
    "createdAt": "2026-10-24T08:00:00+00:00",
    "updatedByAccountCode": "TK_MANAGER",
    "updatedByFullName": "Nguyễn Văn An",
    "updatedAt": "2026-10-24T08:00:00+00:00"
  }
}
```

Notes null được bỏ khỏi JSON. Nhân viên phòng ban không nhận notes/version/management. Mọi danh sách chỉ trả displayCode/name/description/status/deviceCount. Thông tin tạo/cập nhật lấy từ các sự kiện thành công, không lấy từ thất bại/NoChange. Danh mục cũ không có audit tương ứng trả metadata null, không tạo lịch sử giả. Người thao tác được hiển thị bằng mã/tên tài khoản, không công khai UUID tài khoản.

Version là SHA-256 của snapshot nội dung hiện tại; client có thể gửi lại vào PUT để tránh ghi đè dữ liệu đã thay đổi. Nếu khác version hiện tại, trả 409 CATEGORY_VERSION_CONFLICT. Bỏ qua version thì PUT áp dụng nội dung mới lên trạng thái hiện tại. Đây là kiểm tra nội dung tùy chọn, không phải bộ đếm phiên bản hoặc khóa DB kéo dài qua nhiều request.

## Danh sách, lọc và sắp xếp

```http
GET /api/v1/device-categories?search=máy&status=Active&sortBy=Name&sortDirection=Asc&page=1&pageSize=20
```

Search tìm chuỗi con theo tên hoặc mã chuẩn hóa, không phân biệt hoa thường; không tự bỏ dấu tiếng Việt. Status Active/Locked, bỏ qua để lấy cả hai. SortBy Name/DisplayCode/Status, mặc định Name; SortDirection Asc/Desc, mặc định Asc. Khóa phụ ID nội bộ giúp phân trang ổn định khi giá trị bằng nhau; không trả ID này.

Page 1–1.000.000; pageSize 1–100, mặc định 20. Kết quả `{items,totalCount,page,pageSize}`; không có kết quả trả 200 với items rỗng. Count/lọc/sắp xếp/Skip/Take/ProjectTo thực hiện tại PostgreSQL. GET không ghi audit thay đổi. Enum hoặc giới hạn không hợp lệ trả 400. Chưa hỗ trợ sắp xếp theo số thiết bị.

## Khóa và liên kết thiết bị

GET `/{displayCode}/lock-impact` trả displayCode/status/deviceCount=null/linkCountsComplete=false và warning. Frontend cần thể hiện số lượng chưa xác định, không hiển thị như 0.

Khóa/mở khóa chỉ đổi trạng thái danh mục, không xóa danh mục hoặc thay đổi FK hiện có. Chưa có module/bảng thiết bị trong backend nên chưa thể đếm, liệt kê thiết bị hoặc kiểm thử gán thiết bị thực tế. Khi triển khai module thiết bị, phải kiểm tra danh mục Active trong transaction gán mới; liên kết cũ của danh mục Locked được giữ nguyên. Cảnh báo khóa là hợp đồng cho tích hợp này, không khẳng định luồng gán thiết bị đã được triển khai.

Gửi khóa/mở khóa khi trạng thái đã đúng, hoặc PUT không đổi nội dung, trả 204 và audit Outcome=NoChange. Không tạo Success giả. Xóa luôn bị cấm; với danh mục không tồn tại trả 404.

## Lịch sử và an toàn dữ liệu

ActionCode luôn là hành động đã thử: CATEGORY_CREATED/CATEGORY_UPDATED/CATEGORY_LOCKED/CATEGORY_UNLOCKED/DELETE_DENIED. Outcome tách riêng Success/Failed/Denied/NoChange. Không dùng FAILED hoặc DENIED thay cho tên hành động.

Các thao tác thành công và audit nằm trong cùng transaction. Ghi audit lỗi thì rollback nghiệp vụ, trả 503 và thử ghi Failed trong transaction mới. Nếu cả audit thất bại cũng không lưu được, trả 503, chỉ có log vận hành chứa RequestId/loại lỗi; không tuyên bố đã lưu lịch sử. Cùng advisory lock với AccountTransaction giúp kiểm tra vai trò/phiên và thao tác ghi có thứ tự với thu hồi quyền. Unique index giữ ràng buộc tên/mã; unique `(request_id,action_code)` tránh sự kiện trùng trong cùng request. RequestId do server sinh cho mỗi HTTP request, không phải khóa idempotency do client cung cấp.

Snapshot chỉ gồm `display_code`, `name`, `description`, `notes`, `status`. ChangedFields của sửa/khóa/mở khóa thành công ghi chính xác trường thay đổi, gồm notes. Tạo có before=null; NoChange có snapshot giống nhau và changedFields=null. Lỗi với đích đã biết chỉ lưu snapshot trước thao tác; lỗi tạo hoặc đích không tồn tại có target/snapshot null. Validation POST/PUT được audit trước 400, không lưu payload sai. Request trái quyền được ghi Denied; anonymous/phiên không hợp lệ có thể bị middleware chặn 401 trước controller.

Lịch sử danh sách trả id sự kiện, displayCode/name **tại thời điểm sự kiện**, actionCode/outcome/occurredAt/actorAccountCode/actorFullName. Không trả snapshot, changedFields, reasonCode, requestId hoặc UUID mục tiêu/người thao tác. Đổi tên danh mục sau đó không đổi nhãn của lịch sử cũ; không ghép nhãn từ danh mục hiện tại. Actor/target null vẫn đọc được trong lịch sử toàn bộ.

Chi tiết trả thêm beforeData/afterData/changedFields và reasonCode an toàn. JSON snapshot được lọc theo danh sách trường cho phép, chỉ chuỗi/null; trường lạ, đối tượng lồng, định danh nội bộ bị loại. ChangedFields được tính lại từ snapshot đã lọc cho Success. Chỉ Trưởng phòng nhận RequestId trong chi tiết; Nhân viên QTTB vẫn đọc được nội dung nghiệp vụ nhưng không nhận trường này. Không có endpoint ghi/sửa/xóa lịch sử.

Query lịch sử dùng được trên cả route toàn bộ và route theo mã:

```http
GET /api/v1/history/device-categories?displayCode=DM_PC&actionCode=CATEGORY_UPDATED&outcome=Success&actorAccountCode=TK_STAFF&from=2026-10-01T00:00:00Z&to=2026-10-31T23:59:59Z&sortBy=OccurredAt&sortDirection=Desc&page=1&pageSize=20
```

Search tìm theo nhãn mã/tên snapshot. DisplayCode và ActorAccountCode so sánh chính xác không phân biệt hoa thường; ActionCode viết hoa. From/To gồm cả mốc biên, chuẩn hóa UTC; From > To bị từ chối. SortBy OccurredAt/ActionCode/Outcome/ActorAccountCode; mặc định OccurredAt Desc; ID sự kiện là khóa phụ. Giới hạn phân trang giống danh mục. Lọc/count/phân trang thực hiện ở PostgreSQL, list chỉ trích nhãn JSON thay vì tải snapshot. Route theo mã luôn giới hạn categoryId nội bộ; event của danh mục khác trả 404 dù historyId hợp lệ.

## Mã lỗi

| HTTP | Code | Ý nghĩa |
| --- | --- | --- |
| 400 | VALIDATION_FAILED | Payload/query sai, quá giới hạn hoặc trường ngoài hợp đồng |
| 401 | AUTHENTICATION_REQUIRED / challenge | Thiếu phiên, phiên bị thu hồi/hết hạn hoặc tài khoản khóa |
| 403 | CATEGORY_ACCESS_FORBIDDEN | Không được thao tác hoặc đọc lịch sử |
| 403 | CATEGORY_DELETE_FORBIDDEN | Không cho phép xóa danh mục |
| 404 | CATEGORY_NOT_FOUND / CATEGORY_HISTORY_NOT_FOUND | Không tìm thấy đích trong phạm vi yêu cầu |
| 409 | CATEGORY_NAME_CONFLICT / CATEGORY_CODE_CONFLICT | Trùng tên hoặc mã, kể cả danh mục khóa |
| 409 | CATEGORY_LOCKED | Sửa danh mục đang khóa |
| 409 | CATEGORY_VERSION_CONFLICT | Nội dung đã thay đổi từ lần đọc trước |
| 503 | CATEGORY_UNAVAILABLE | Không thể đọc/lưu dữ liệu hoặc audit |

## Kiểm thử và triển khai

Test tự động: `tests/CSVC.API.Tests/CategoryApiTests.cs`, dùng PostgreSQL UTF-8 riêng và database tạm. Báo cáo input/output mong muốn/kết quả từng testcase nằm bên ngoài project theo yêu cầu chủ dự án.

API này sử dụng sáu bảng đã được thêm ở migration AddCatalogModules; không đổi schema và không cần migration mới. Không chạy migrate trên DB ứng dụng khi kiểm thử. Nhà cung cấp và hãng sản xuất hiện vẫn có entity/repository, chưa mở API trong phạm vi nhóm danh mục thiết bị này.
