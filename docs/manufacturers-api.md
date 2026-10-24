# API Nhà sản xuất và Manufacturer History — Nhóm 7

Cập nhật: 24/10/2026. Nguồn: [Danh sách nghiệp vụ, tab Nhóm 7-1 và Nhóm 7-2, UC-HSX-01…08](https://docs.google.com/spreadsheets/d/1xQg5ao1fWNIA5qC8qisSoVaUeUuRZ9UFa3_nse2ZHyA/edit).

## 1. Quyền và định danh

Mọi API cần `Authorization: Bearer <accessToken>` của phiên còn hiệu lực. Trưởng phòng QTTB (`QTTB_MANAGER`) và Nhân viên QTTB (`QTTB_STAFF`) được tạo/sửa/khóa/mở khóa và xem lịch sử. Nhân viên phòng ban (`DEPARTMENT_STAFF`) chỉ đọc mã/tên/mô tả/trạng thái; không nhận country/website/shortName/contactInfo/notes/version/management. Đây là cách phân chia thông tin cơ bản và thông tin quản lý đang dùng ở các module danh mục.

UUID nhà sản xuất chỉ dùng nội bộ để liên kết. `{displayCode}` là mã hiển thị; mã có `/` phải encode, ví dụ `HSX/2026.01` → `HSX%2F2026.01`. `{historyId}` là UUID sự kiện lấy từ trường `id` của danh sách lịch sử, không phải UUID nhà sản xuất.

## 2. API Manufacturer

| Method | URL | Tác dụng | Quyền | Phản hồi thông thường |
| --- | --- | --- | --- | --- |
| GET | `/api/v1/manufacturers` | Tra cứu danh sách, tìm/lọc/sắp xếp/phân trang. | Cả ba role | `200` + danh sách phân trang |
| GET | `/api/v1/manufacturers/{displayCode}` | Xem chi tiết theo mã, kể cả bản ghi Locked. | Cả ba role, dữ liệu theo quyền | `200`; không tồn tại `404` |
| POST | `/api/v1/manufacturers` | Tạo nhà sản xuất mới ở trạng thái Active. | Hai role QTTB | `201` + chi tiết + header `Location` |
| PUT | `/api/v1/manufacturers/{displayCode}` | Sửa thông tin, giữ nguyên ID/mã/trạng thái. | Hai role QTTB | `204` |
| PUT | `/api/v1/manufacturers/{displayCode}/lock` | Khóa, đổi trạng thái sang Locked. | Hai role QTTB | `204` |
| DELETE | `/api/v1/manufacturers/{displayCode}/lock` | Mở khóa, đổi trạng thái về Active. | Hai role QTTB | `204` |
| GET | `/api/v1/manufacturers/{displayCode}/lock-impact` | Xem trạng thái và cảnh báo trước khi khóa. | Hai role QTTB | `200` |
| DELETE | `/api/v1/manufacturers/{displayCode}` | Ghi nhận yêu cầu xóa bị cấm; không xóa dữ liệu. | Kiểm tra quyền QTTB; thao tác xóa luôn bị từ chối | `403 MANUFACTURER_DELETE_FORBIDDEN`; không tồn tại `404` |

Chỉ GET danh sách có query lọc/sắp xếp. Lock/unlock/delete không cần body. Khóa/mở khóa lặp hoặc sửa không đổi nội dung trả `204` với audit NoChange, không tạo Success giả.

### Bộ lọc, sắp xếp và phân trang

Áp dụng cho `GET /api/v1/manufacturers`. Các tham số đều tùy chọn; các bộ lọc khác nhau kết hợp AND.

| Query | Ý nghĩa / giá trị hợp lệ | Mặc định |
| --- | --- | --- |
| `search` | Tìm chuỗi con theo mã hoặc tên, không phân biệt hoa thường; NFC/trim/gộp khoảng trắng; tối đa 200 ký tự, không tự bỏ dấu tiếng Việt. | Không tìm kiếm |
| `status` | `Active`, `Locked`. | Cả hai |
| `sortBy` | `Name`, `DisplayCode`, `Status`. | `Name` |
| `sortDirection` | `Asc`, `Desc`. | `Asc` |
| `page` | Số nguyên 1–1.000.000. | `1` |
| `pageSize` | Số nguyên 1–100. | `20` |

Kết quả `{items,totalCount,page,pageSize}`. Danh sách trả displayCode/name/description/status/deviceCount, không có UUID hoặc metadata quản lý. Không có kết quả trả `200`, `items=[]`, `totalCount=0`. ID nội bộ tăng dần là khóa sắp xếp phụ để ổn định phân trang; ID này không được trả ra ngoài. Lọc/count/sort/phân trang và projection thực hiện tại PostgreSQL.

Chưa có bộ lọc quốc gia/website/tên viết tắt hoặc sort theo số thiết bị. Search chỉ tìm mã/tên chính thức, không tìm theo shortName.

```http
GET /api/v1/manufacturers?search=HSX&status=Active&sortBy=DisplayCode&sortDirection=Asc&page=1&pageSize=20
```

### Body tạo và sửa

| Trường JSON | POST | PUT | Quy tắc |
| --- | --- | --- | --- |
| `displayCode` | Tùy chọn | Không gửi | Tối đa 64 ký tự; trim/viết hoa, bắt đầu A–Z hoặc số, phần sau cho phép A–Z/0–9 và `.`, `_`, `/`, `-`. Bỏ qua/null/trắng khi tạo sinh `HSX` + 32 ký tự hex. Mã bất biến sau tạo, không trùng kể cả bản ghi khóa. |
| `name` | Bắt buộc | Bắt buộc | Tối đa 200 ký tự sau NFC/trim/gộp khoảng trắng. Không trùng sau chuẩn hóa/không phân biệt hoa thường kể cả bản ghi khóa. |
| `description` | Tùy chọn | Tùy chọn | Mô tả tối đa 4000 ký tự, trim. |
| `country` | Tùy chọn | Tùy chọn | Quốc gia/khu vực, tối đa 120 ký tự sau NFC/trim/gộp khoảng trắng. Là chuỗi tự do, không dùng danh sách mã ISO. |
| `website` | Tùy chọn | Tùy chọn | Tối đa 2048 ký tự; trim, URL tuyệt đối HTTP hoặc HTTPS có host, không có username/password nhúng. Giữ nguyên hoa/thường trong path và query. |
| `shortName` | Tùy chọn | Tùy chọn | Tên viết tắt/thương hiệu, tối đa 120 ký tự sau NFC/trim/gộp khoảng trắng; không thay thế name. |
| `contactInfo` | Tùy chọn | Tùy chọn | Thông tin liên hệ dạng văn bản, tối đa 4000 ký tự, trim; không phải object phone/email. |
| `notes` | Tùy chọn | Tùy chọn | Ghi chú quản lý, tối đa 4000 ký tự, trim; độc lập description/contactInfo. |
| `version` | Không gửi | Tùy chọn | 64 ký tự hex từ chi tiết. Nếu nội dung hiện tại khác lúc đọc, PUT trả `409 MANUFACTURER_VERSION_CONFLICT`. Bỏ qua thì không kiểm tra version. |

Trường tùy chọn null/trắng thành null; bỏ qua trong PUT xóa nội dung cũ về null. Country/website/shortName/contactInfo không dùng làm khóa duy nhất: hai hãng khác tên/mã có thể dùng chung các giá trị này. Quốc gia của hãng không tự xác định nơi lắp ráp/xuất xứ của từng thiết bị.

Website chỉ được kiểm tra cú pháp, không được backend truy cập hoặc kiểm tra quyền sở hữu. Ví dụ hợp lệ: `https://example.com/Products?Lang=vi`; không nhận URL tương đối, `javascript:`, `ftp:`, `file:` hoặc `https://user:password@example.com`.

```json
{
  "displayCode": "HSX_ACME",
  "name": "ACME Manufacturing",
  "description": "Nhà sản xuất thiết bị văn phòng",
  "country": "Việt Nam",
  "website": "https://example.com/Products",
  "shortName": "ACME",
  "contactInfo": "Bộ phận hỗ trợ kỹ thuật",
  "notes": "Ghi chú nội bộ QTTB"
}
```

Trường ngoài hợp đồng như id/status/actorAccountId hoặc displayCode trong PUT bị từ chối `400`. Nhà sản xuất Locked phải mở khóa trước khi sửa. Không tạo nhà cung cấp, chứng từ hoặc thiết bị khi tạo nhà sản xuất.

Chi tiết cho hai role QTTB có thêm các trường bổ sung, version và management. Trường tùy chọn null được bỏ khỏi JSON. Management gồm mã/tên người tạo, thời gian tạo, mã/tên người cập nhật cuối và thời gian; lấy từ audit thành công. Bản ghi cũ chưa có audit trả metadata null, không tạo lịch sử giả.

## 3. API Manufacturer History

Cả hai role QTTB được đọc; role phòng ban bị từ chối. Các endpoint lịch sử chỉ hỗ trợ đọc.

| Method | URL | Tác dụng | Lọc/sắp xếp |
| --- | --- | --- | --- |
| GET | `/api/v1/history/manufacturers` | Tra cứu toàn bộ lịch sử, gồm sự kiện target/actor null. | Có |
| GET | `/api/v1/history/manufacturers/{historyId}` | Xem chi tiết một sự kiện. | Không |
| GET | `/api/v1/manufacturers/{displayCode}/history` | Tra cứu lịch sử trong phạm vi hãng được chọn. | Có |
| GET | `/api/v1/manufacturers/{displayCode}/history/{historyId}` | Chi tiết sự kiện thuộc đúng hãng; khác phạm vi trả `404`. | Không |

### Bộ lọc, sắp xếp và phân trang lịch sử

Áp dụng cho cả hai endpoint danh sách lịch sử. Mọi tham số tùy chọn và kết hợp AND.

| Query | Ý nghĩa / giới hạn | Mặc định |
| --- | --- | --- |
| `search` | Tìm chuỗi con mã/tên trong snapshot lúc phát sinh event, không phân biệt hoa thường; tối đa 200 ký tự. | Không tìm kiếm |
| `displayCode` | Lọc chính xác mã trong snapshot; trim, không phân biệt hoa thường; tối đa 64 ký tự. | Không lọc thêm mã |
| `actorAccountCode` | Lọc chính xác mã tài khoản thao tác; trim, không phân biệt hoa thường; tối đa 32 ký tự. | Không lọc actor |
| `actionCode` | Lọc chính xác action; trim/viết hoa, tối đa 64 ký tự. Các action hiện có: `MANUFACTURER_CREATED`, `MANUFACTURER_UPDATED`, `MANUFACTURER_LOCKED`, `MANUFACTURER_UNLOCKED`, `DELETE_DENIED`. | Mọi action |
| `outcome` | `Success`, `Failed`, `Denied`, `NoChange`. | Mọi kết quả |
| `from` | ISO 8601, chuẩn hóa UTC; `occurredAt >= from`, bao gồm mốc đầu. | Không giới hạn đầu |
| `to` | ISO 8601, chuẩn hóa UTC; `occurredAt <= to`, bao gồm mốc cuối. `from <= to` nếu có cả hai. | Không giới hạn cuối |
| `sortBy` | `OccurredAt`, `ActionCode`, `Outcome`, `ActorAccountCode`. | `OccurredAt` |
| `sortDirection` | `Asc`, `Desc`. | `Desc` |
| `page` | Số nguyên 1–1.000.000. | `1` |
| `pageSize` | Số nguyên 1–100. | `20` |

ID sự kiện tăng dần là khóa sắp xếp phụ. Sort Outcome theo mã chữ trong DB. Không có query reasonCode/requestId/UUID nội bộ. Query không thể mở rộng scope của route theo mã.

```http
GET /api/v1/history/manufacturers?displayCode=HSX_ACME&actionCode=MANUFACTURER_UPDATED&outcome=Success&actorAccountCode=TK_STAFF&from=2026-10-01T00:00:00Z&to=2026-10-31T23:59:59.999999Z&sortBy=OccurredAt&sortDirection=Desc&page=1&pageSize=20
```

Offset `+07:00` cần encode dấu `+` thành `%2B`. List trả ID event, mã/tên hãng từ snapshot, action/outcome/thời gian và mã/tên actor; không có UUID hãng/actor, reasonCode, snapshot hoặc RequestId. Đổi tên hãng không làm đổi nhãn event cũ; actor không xác định được giữ null.

Chi tiết có reasonCode an toàn, beforeData/afterData/changedFields đã lọc. Snapshot cho phép display_code/name/description/country/website/short_name/contact_info/notes/status, chỉ chuỗi/null đúng giới hạn. Trường lạ, object lồng, URL website không hợp lệ bị loại khỏi dữ liệu trả về. Chỉ Trưởng phòng QTTB nhận `requestId` trong chi tiết.

## 4. Giao dịch, audit và lỗi

Theo cấu trúc hiện tại: Controller → ManufacturerService → UnitOfWork/repository; AutoMapper quản lý mapping, ManufacturerTransaction giữ nhất quán thay đổi và audit. Quyền/phiên được kiểm tra lại trong giao dịch ghi; dùng cùng advisory lock với AccountTransaction để có thứ tự với thu hồi quyền/phiên.

Audit lưu action đã thử và outcome riêng. Update/lock/unlock Success ghi changed_fields đúng tên trường CSDL thực sự đổi, bao gồm country/website/short_name/contact_info/notes; không ghi trường giả. Create có before=null; NoChange không tạo diff thành công. Validation POST/PUT ghi Failed/VALIDATION_FAILED trước `400`, không lưu payload sai; từ chối quyền ghi Denied. GET không ghi audit thay đổi.

Audit cho thay đổi thành công lỗi thì rollback nghiệp vụ, trả `503` và thử lưu Failed sau rollback. Nếu cả audit Failed cũng lỗi, vẫn `503`, không tuyên bố đã ghi lịch sử. Unique index tên/mã là lớp bảo vệ DB. `(request_id,action_code)` duy nhất cho mỗi event trong request; RequestId do server sinh, không phải khóa idempotency do client gửi.

| HTTP | Code / ý nghĩa |
| --- | --- |
| 400 | `VALIDATION_FAILED`: payload/query sai, vượt giới hạn hoặc ngoài hợp đồng |
| 401 | `AUTHENTICATION_REQUIRED` / challenge: thiếu phiên, phiên bị thu hồi/hết hạn hoặc tài khoản khóa |
| 403 | `MANUFACTURER_ACCESS_FORBIDDEN`, `MANUFACTURER_DELETE_FORBIDDEN` |
| 404 | `MANUFACTURER_NOT_FOUND`, `MANUFACTURER_HISTORY_NOT_FOUND` |
| 409 | `MANUFACTURER_NAME_CONFLICT`, `MANUFACTURER_CODE_CONFLICT`, `MANUFACTURER_LOCKED`, `MANUFACTURER_VERSION_CONFLICT` |
| 503 | `MANUFACTURER_UNAVAILABLE`: không thể đọc/lưu dữ liệu hoặc audit |

## 5. Thiết bị liên kết và triển khai

Chưa có module thiết bị/chứng từ; `deviceCount=null`, `linkCountsComplete=false`, không trả số 0 giả hoặc danh sách thiết bị không có nguồn dữ liệu. Khóa hãng chỉ đổi trạng thái và giữ nguyên định danh/quan hệ cũ. Module thiết bị triển khai sau phải kiểm tra hãng Active trong giao dịch gán mới; không tự thay đổi hồ sơ thiết bị đã phát sinh.

API Nhà sản xuất dùng các bảng đã có trong AddCatalogModules, **không thêm migration mới**. Database vẫn cần áp dụng các migration hiện có của backend, gồm UniqueSupplierTaxCode của module Nhà cung cấp nếu chưa cập nhật.

Test tự động ở `ManufacturerApiTests.cs` và `ManufacturerDetailsApiTests.cs`, dùng PostgreSQL UTF-8 riêng; báo cáo testcase/input/output/kết quả nằm ngoài project. Không chạy migrate trên database ứng dụng khi kiểm thử.
