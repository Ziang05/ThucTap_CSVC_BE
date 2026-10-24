# API Nhà cung cấp và Supplier History — Nhóm 6

Cập nhật: 24/10/2026. Nguồn: [Danh sách nghiệp vụ, tab Nhóm 6-1 và Nhóm 6-2, UC-NCC-01…08](https://docs.google.com/spreadsheets/d/1xQg5ao1fWNIA5qC8qisSoVaUeUuRZ9UFa3_nse2ZHyA/edit). Mã số thuế đã được chủ dự án xác nhận: kiểm tra định dạng và không cho trùng khi có nhập.

## 1. Quyền và quy ước

Mọi API yêu cầu `Authorization: Bearer <accessToken>` của phiên còn hiệu lực. Trưởng phòng QTTB (`QTTB_MANAGER`) và Nhân viên QTTB (`QTTB_STAFF`) được quản lý nhà cung cấp và đọc lịch sử. Nhân viên phòng ban (`DEPARTMENT_STAFF`) chỉ đọc mã/tên/mô tả/trạng thái; không nhận thông tin mã số thuế, địa chỉ, điện thoại, email, ghi chú, version hoặc metadata quản lý.

UUID nhà cung cấp được giữ nội bộ. `{displayCode}` là mã hiển thị; mã có `/` phải encode thành `%2F`, ví dụ `NCC/2026.01` → `NCC%2F2026.01`. `{historyId}` là UUID sự kiện, lấy từ `id` của danh sách lịch sử. Không dùng UUID nhà cung cấp trong URL API công khai.

## 2. Danh sách API Supplier

| Method | URL | Tác dụng | Quyền | Phản hồi thông thường |
| --- | --- | --- | --- | --- |
| GET | `/api/v1/suppliers` | Tra cứu danh sách nhà cung cấp, tìm kiếm/lọc/sắp xếp/phân trang. | Cả ba role | `200` + danh sách phân trang |
| GET | `/api/v1/suppliers/{displayCode}` | Xem chi tiết theo mã; nhà cung cấp khóa vẫn đọc được. | Cả ba role, nội dung theo quyền | `200` hoặc `404` |
| POST | `/api/v1/suppliers` | Tạo nhà cung cấp mới ở trạng thái `Active`. | Hai role QTTB | `201` + chi tiết + `Location` |
| PUT | `/api/v1/suppliers/{displayCode}` | Cập nhật tên, mô tả, ghi chú và thông tin liên hệ; giữ mã/ID/trạng thái. | Hai role QTTB | `204` |
| PUT | `/api/v1/suppliers/{displayCode}/lock` | Khóa, đổi trạng thái sang `Locked`. | Hai role QTTB | `204` |
| DELETE | `/api/v1/suppliers/{displayCode}/lock` | Mở khóa, đổi trạng thái về `Active`. | Hai role QTTB | `204` |
| GET | `/api/v1/suppliers/{displayCode}/lock-impact` | Xem trạng thái và cảnh báo trước khi khóa. | Hai role QTTB | `200` |
| DELETE | `/api/v1/suppliers/{displayCode}` | Ghi nhận yêu cầu xóa bị cấm; không xóa dữ liệu. | Kiểm tra quyền QTTB, luôn cấm xóa | `403 SUPPLIER_DELETE_FORBIDDEN`; đích không tồn tại `404` |

Ngoài GET danh sách, các endpoint trên không có trường lọc/sắp xếp. Các thao tác lock/unlock/delete không cần body.

### Bộ lọc, sắp xếp và phân trang danh sách

Chỉ áp dụng cho `GET /api/v1/suppliers`. Các trường đều tùy chọn, bộ lọc khác nhau kết hợp AND.

| Query | Tác dụng / giá trị hợp lệ | Mặc định |
| --- | --- | --- |
| `search` | Tìm chuỗi con theo tên hoặc mã hiển thị, không phân biệt hoa thường; NFC/trim/gộp khoảng trắng; tối đa 200 ký tự, không tự bỏ dấu. | Không tìm kiếm |
| `status` | `Active` hoặc `Locked`. | Cả hai |
| `sortBy` | `Name`, `DisplayCode`, `Status`. | `Name` |
| `sortDirection` | `Asc` hoặc `Desc`. | `Asc` |
| `page` | Số nguyên 1–1.000.000. | `1` |
| `pageSize` | Số nguyên 1–100. | `20` |

Dữ liệu trả `{items,totalCount,page,pageSize}`. Không có kết quả trả `200`, `items=[]`, `totalCount=0`. ID nội bộ tăng dần là khóa sắp xếp phụ để ổn định phân trang nhưng không được trả ra ngoài. Chưa hỗ trợ tìm theo mã số thuế/liên hệ hoặc sắp xếp theo số phiếu nhập. Count/lọc/sort/phân trang được thực hiện ở PostgreSQL; controller không truy cập DbContext.

```http
GET /api/v1/suppliers?search=NCC&status=Active&sortBy=DisplayCode&sortDirection=Asc&page=1&pageSize=20
```

### Body tạo/sửa

| Trường JSON | POST | PUT | Quy tắc |
| --- | --- | --- | --- |
| `displayCode` | Tùy chọn | Không gửi | Tối đa 64 ký tự, trim/viết hoa; bắt đầu bằng A–Z hoặc số, phần sau cho phép A–Z/0–9 và `.`, `_`, `/`, `-`. Bỏ qua/null/trắng khi tạo thì sinh `NCC` + 32 ký tự hex. Mã bất biến sau tạo, không trùng kể cả bản ghi khóa. |
| `name` | Bắt buộc | Bắt buộc | Tối đa 200 ký tự sau NFC/trim/gộp khoảng trắng, không trùng không phân biệt hoa thường kể cả bản ghi khóa. |
| `description` | Tùy chọn | Tùy chọn | Mô tả, tối đa 4000 ký tự. |
| `notes` | Tùy chọn | Tùy chọn | Ghi chú nội bộ, tối đa 4000 ký tự, độc lập với mô tả. |
| `taxCode` | Tùy chọn | Tùy chọn | Tối đa 50 ký tự; trim/viết hoa; chỉ A–Z/0–9 và dấu `-` hoặc `.` ngăn giữa các nhóm. Regex `^[A-Z0-9]+(?:[-.][A-Z0-9]+)*$`. Không chấp nhận khoảng trắng ở giữa, dấu liên tiếp hoặc dấu đầu/cuối. Khi có nhập phải duy nhất sau trim/viết hoa, kể cả nhà cung cấp khóa. |
| `address` | Tùy chọn | Tùy chọn | Địa chỉ, tối đa 4000 ký tự. |
| `phone` | Tùy chọn | Tùy chọn | Tối đa 32 ký tự; có thể bắt đầu bằng `+`, phần số cho phép chữ số/khoảng trắng/ngoặc/dấu chấm/gạch ngang và phải kết thúc bằng chữ số; không nhận chữ cái. Ví dụ `+84 (28) 1234-5678`. |
| `email` | Tùy chọn | Tùy chọn | Tối đa 320 ký tự, kiểm tra EmailAddress, trim/viết thường. |
| `version` | Không gửi | Tùy chọn | 64 ký tự hex lấy từ chi tiết; PUT từ chối `409 SUPPLIER_VERSION_CONFLICT` nếu nội dung hiện tại khác lúc đọc. Bỏ qua thì không kiểm tra version. |

Các trường tùy chọn được trim; trắng/null thành null. Bỏ qua trường tùy chọn trong PUT sẽ xóa nội dung cũ về null. API kiểm tra cú pháp mã số thuế theo quy tắc ở trên, không áp dụng quy tắc độ dài riêng của từng quốc gia. Có thể có nhiều nhà cung cấp không nhập mã số thuế. Trường ngoài hợp đồng như `id`, `status`, `actorAccountId` hoặc `displayCode` trong PUT bị từ chối `400`.

```json
{
  "displayCode": "NCC_ABC",
  "name": "Công ty ABC",
  "description": "Cung ứng thiết bị văn phòng",
  "taxCode": "0101234567-001",
  "address": "123 Nguyễn Trãi",
  "phone": "+84 (28) 1234-5678",
  "email": "contact@example.com",
  "notes": "Liên hệ trước khi đặt hàng"
}
```

Chi tiết cho hai role QTTB có thêm `version`, `management` và các trường liên hệ trên; trường tùy chọn null được bỏ khỏi JSON. Management gồm mã/tên người tạo, thời gian tạo, mã/tên người cập nhật cuối và thời gian, lấy từ audit thành công. Bản ghi cũ chưa có audit trả metadata null, không tự tạo lịch sử giả.

Nhà cung cấp Locked không được sửa; cần mở khóa trước. Khóa/mở khóa lặp hoặc PUT không đổi dữ liệu trả `204` và ghi `NoChange`, không ghi Success giả.

## 3. Danh sách API Supplier History

Các endpoint này chỉ cho hai role QTTB đọc. Không có API tạo/sửa/xóa lịch sử trực tiếp.

| Method | URL | Tác dụng | Lọc/sắp xếp |
| --- | --- | --- | --- |
| GET | `/api/v1/history/suppliers` | Tra cứu toàn bộ lịch sử nhà cung cấp, kể cả event có target/actor null. | Có, dùng bảng dưới |
| GET | `/api/v1/history/suppliers/{historyId}` | Xem chi tiết một sự kiện. | Không |
| GET | `/api/v1/suppliers/{displayCode}/history` | Tra cứu lịch sử trong phạm vi một nhà cung cấp. | Có, dùng bảng dưới |
| GET | `/api/v1/suppliers/{displayCode}/history/{historyId}` | Xem chi tiết sự kiện thuộc đúng nhà cung cấp; khác phạm vi trả `404`. | Không |

### Bộ lọc, sắp xếp và phân trang lịch sử

Áp dụng cho cả hai endpoint danh sách lịch sử. Các trường tùy chọn và kết hợp AND.

| Query | Tác dụng / giới hạn | Mặc định |
| --- | --- | --- |
| `search` | Tìm chuỗi con mã/tên từ snapshot lúc phát sinh sự kiện; tối đa 200 ký tự, không phân biệt hoa thường. | Không tìm kiếm |
| `displayCode` | Lọc chính xác mã hiển thị trong snapshot; trim, không phân biệt hoa thường, tối đa 64 ký tự. | Không lọc thêm theo mã |
| `actorAccountCode` | Lọc chính xác mã tài khoản người thao tác; trim, không phân biệt hoa thường, tối đa 32 ký tự. | Không lọc theo actor |
| `actionCode` | Lọc chính xác hành động; trim/viết hoa, tối đa 64 ký tự. Action hiện có: `SUPPLIER_CREATED`, `SUPPLIER_UPDATED`, `SUPPLIER_LOCKED`, `SUPPLIER_UNLOCKED`, `DELETE_DENIED`. | Mọi action |
| `outcome` | `Success`, `Failed`, `Denied`, `NoChange`. | Mọi kết quả |
| `from` | ISO 8601, chuẩn hóa UTC; `occurredAt >= from`, gồm mốc đầu. | Không giới hạn đầu |
| `to` | ISO 8601, chuẩn hóa UTC; `occurredAt <= to`, gồm mốc cuối; phải `from <= to`. | Không giới hạn cuối |
| `sortBy` | `OccurredAt`, `ActionCode`, `Outcome`, `ActorAccountCode`. | `OccurredAt` |
| `sortDirection` | `Asc` hoặc `Desc`. | `Desc` |
| `page` | Số nguyên 1–1.000.000. | `1` |
| `pageSize` | Số nguyên 1–100. | `20` |

ID sự kiện tăng dần là khóa sắp xếp phụ. Outcome được sắp xếp theo mã kết quả dạng chữ tại DB. Không có bộ lọc `reasonCode`, `requestId` hay UUID nội bộ. Route theo mã luôn giới hạn phạm vi nhà cung cấp; query không thể mở rộng scope đó. List không tải toàn bộ JSON snapshot, chỉ trích nhãn để lọc/sắp xếp/phân trang tại DB.

```http
GET /api/v1/history/suppliers?displayCode=NCC_ABC&actionCode=SUPPLIER_UPDATED&outcome=Success&actorAccountCode=TK_STAFF&from=2026-10-01T00:00:00Z&to=2026-10-31T23:59:59.999999Z&sortBy=OccurredAt&sortDirection=Desc&page=1&pageSize=20
```

Nếu dùng offset `+07:00`, encode `+` thành `%2B`. Danh sách trả ID sự kiện, mã/tên nhà cung cấp từ snapshot, action/outcome/thời gian và mã/tên actor; không có UUID nhà cung cấp/actor, reasonCode, raw JSON hay RequestId. Đổi tên nhà cung cấp không đổi nhãn lịch sử cũ.

Chi tiết có thêm reasonCode an toàn và beforeData/afterData/changedFields đã lọc. Snapshot chỉ cho phép `display_code`, `name`, `description`, `tax_code`, `address`, `phone`, `email`, `notes`, `status`; không nhận trường lạ hoặc đối tượng lồng. Chỉ Trưởng phòng được nhận `requestId` trong chi tiết. Actor null được giữ null để frontend thể hiện Hệ thống/Không xác định.

## 4. Giao dịch và mã lỗi

Service → UnitOfWork/repository, AutoMapper và transaction theo cách tổ chức của Category. SupplierTransaction dùng cùng advisory lock với AccountTransaction; quyền/phiên được kiểm tra lại trong giao dịch ghi. Thành công nghiệp vụ và audit lưu cùng giao dịch; audit lỗi thì rollback và trả `503`, thử ghi Failed sau rollback. Nếu cả audit lỗi cũng không lưu được, vẫn trả `503`, không báo đã lưu lịch sử.

ActionCode luôn là hành động đã thử, Outcome lưu riêng. Update/lock/unlock Success ghi changed_fields đúng các trường thực sự thay đổi, gồm notes và các trường liên hệ; Create có before=null. NoChange không có diff giả. Validation POST/PUT ghi Failed/VALIDATION_FAILED nhưng không lưu payload sai; yêu cầu trái quyền ghi Denied. Đọc dữ liệu không tạo audit thay đổi.

| HTTP | Code | Ý nghĩa |
| --- | --- | --- |
| 400 | `VALIDATION_FAILED` | Payload/query sai hoặc trường ngoài hợp đồng |
| 401 | `AUTHENTICATION_REQUIRED` / challenge | Phiên thiếu/không hợp lệ hoặc tài khoản khóa |
| 403 | `SUPPLIER_ACCESS_FORBIDDEN` / `SUPPLIER_DELETE_FORBIDDEN` | Thiếu quyền / xóa bị cấm |
| 404 | `SUPPLIER_NOT_FOUND` / `SUPPLIER_HISTORY_NOT_FOUND` | Đích thiếu hoặc khác scope |
| 409 | `SUPPLIER_NAME_CONFLICT` / `SUPPLIER_CODE_CONFLICT` / `SUPPLIER_TAX_CODE_CONFLICT` | Trùng tên/mã/mã số thuế |
| 409 | `SUPPLIER_LOCKED` / `SUPPLIER_VERSION_CONFLICT` | Sửa bản ghi khóa hoặc version cũ |
| 503 | `SUPPLIER_UNAVAILABLE` | Lỗi đọc/lưu hoặc audit |

## 5. Migration và tích hợp phiếu nhập

Migration mới: `20261024085049_UniqueSupplierTaxCode`. Chỉ thêm cột chuẩn hóa mã số thuế tính toán và unique index có điều kiện khác null; không xóa bảng, bản ghi hoặc thay đổi tax_code gốc. Index áp dụng cả nhà cung cấp khóa. Nếu dữ liệu cũ đã trùng sau trim/viết hoa, migration dừng và rollback, không tự xóa hoặc sửa dữ liệu trùng.

Có thể kiểm tra trùng bằng truy vấn chỉ đọc trước khi cập nhật database:

```sql
SELECT NULLIF(upper(btrim(tax_code)), '') AS normalized_tax_code, count(*)
FROM suppliers
WHERE NULLIF(upper(btrim(tax_code)), '') IS NOT NULL
GROUP BY NULLIF(upper(btrim(tax_code)), '')
HAVING count(*) > 1;
```

Từ thư mục gốc backend, cập nhật database trước khi sử dụng API mới:

```bash
dotnet run --project src/CSVC.API --launch-profile http -- --migrate
```

Chưa có module phiếu nhập/hồ sơ thiết bị nên không cung cấp danh sách chứng từ hoặc số lượng giả. `receiptCount=null`, `linkCountsComplete=false`; API khóa chỉ đổi trạng thái, giữ nguyên ID/quan hệ hiện có. Module phiếu nhập triển khai sau phải kiểm tra supplier Active trong giao dịch gán mới. Tạo nhà cung cấp không tự tạo phiếu nhập hoặc hãng sản xuất.

## 6. Kiểm thử

Test tự động ở `SupplierApiTests.cs` và `SupplierContactApiTests.cs`. Test tích hợp dùng PostgreSQL UTF-8 riêng, bao gồm migration với dữ liệu cũ hợp lệ và dữ liệu cũ trùng mã số thuế; không chạy migration trên database ứng dụng. Báo cáo MD với input/output mong muốn/kết quả và TRX nằm bên ngoài project theo yêu cầu.
