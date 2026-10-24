# Danh sách API Category và Category History

Cập nhật: 24/10/2026. Nội dung đối chiếu với controller, DTO và repository hiện tại.

## 1. Quy ước chung

| Nội dung | Quy định |
| --- | --- |
| Xác thực | Mọi API cần header `Authorization: Bearer <accessToken>` của phiên còn hiệu lực. |
| Quyền quản lý | Trưởng phòng QTTB (`QTTB_MANAGER`) và Nhân viên QTTB (`QTTB_STAFF`) được tạo/sửa/khóa/mở khóa, xem cảnh báo và lịch sử. |
| Quyền đọc cơ bản | Nhân viên phòng ban (`DEPARTMENT_STAFF`) chỉ được xem danh sách và chi tiết Category; không nhận `notes`, `version`, `management`. |
| `{displayCode}` | Mã hiển thị của danh mục, ví dụ `DM_PC`; không phải UUID. Mã có `/` phải encode: `DM/2026.01` → `DM%2F2026.01`. |
| `{historyId}` | UUID của bản ghi lịch sử, lấy từ trường `id` trong danh sách lịch sử. |

Hai API danh sách trả cấu trúc `{ items, totalCount, page, pageSize }`. Không tìm thấy kết quả lọc trả `200` với `items: []` và `totalCount: 0`.

## 2. Category — Quản lý danh mục thiết bị

Đường dẫn gốc: `/api/v1/device-categories`.

### 2.1. Danh sách endpoint

| Method | Endpoint | Tác dụng | Quyền | Phản hồi thông thường | Lọc / sắp xếp |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/v1/device-categories` | Lấy danh sách danh mục, có tìm kiếm và phân trang. | Cả ba role | `200` + danh sách phân trang | Có, xem mục 2.2. |
| GET | `/api/v1/device-categories/{displayCode}` | Xem chi tiết một danh mục theo mã. Danh mục đã khóa vẫn đọc được. | Cả ba role; nội dung theo quyền | `200` + chi tiết; `404` nếu không tồn tại | Không. |
| POST | `/api/v1/device-categories` | Tạo danh mục mới, trạng thái ban đầu `Active`. | Hai role QTTB | `201` + chi tiết và header `Location` | Không; nhận JSON body ở mục 2.3. |
| PUT | `/api/v1/device-categories/{displayCode}` | Sửa tên, mô tả, ghi chú; có thể kiểm tra version để tránh ghi đè dữ liệu đã thay đổi. | Hai role QTTB | `204`; `409` nếu bị khóa, trùng tên hoặc version cũ | Không; nhận JSON body ở mục 2.3. |
| PUT | `/api/v1/device-categories/{displayCode}/lock` | Khóa danh mục bằng cách đổi trạng thái sang `Locked`. | Hai role QTTB | `204` | Không; không cần body. |
| DELETE | `/api/v1/device-categories/{displayCode}/lock` | Mở khóa danh mục, đổi trạng thái về `Active`. | Hai role QTTB | `204` | Không; không cần body. |
| GET | `/api/v1/device-categories/{displayCode}/lock-impact` | Xem trạng thái và cảnh báo ảnh hưởng trước khi khóa. | Hai role QTTB | `200` + `displayCode`, `status`, `deviceCount`, `linkCountsComplete`, `warning` | Không. |
| DELETE | `/api/v1/device-categories/{displayCode}` | Ghi nhận yêu cầu xóa bị cấm; không thực hiện xóa danh mục. | Yêu cầu được kiểm tra quyền QTTB nhưng thao tác xóa luôn bị từ chối | `403 CATEGORY_DELETE_FORBIDDEN` với danh mục tồn tại; `404` nếu không tồn tại | Không. |

Khóa/mở khóa lặp hoặc sửa không thay đổi nội dung trả `204` và ghi audit `NoChange`. Các thao tác làm thay đổi dữ liệu ghi audit `Success` trong cùng giao dịch.

Hiện chưa có module thiết bị: `deviceCount = null`, `linkCountsComplete = false`; không hiểu `null` là 0. Khi tích hợp thiết bị, thao tác gán mới cần từ chối danh mục khóa; liên kết cũ phải được giữ nguyên. API hiện tại chỉ thay đổi trạng thái danh mục.

### 2.2. Bộ lọc, sắp xếp và phân trang Category

Chỉ áp dụng cho `GET /api/v1/device-categories`. Mọi tham số đều tùy chọn.

| Trường query | Tác dụng | Giá trị / giới hạn | Mặc định nếu bỏ qua |
| --- | --- | --- | --- |
| `search` | Tìm chuỗi con trong **tên hoặc mã hiển thị** danh mục, không phân biệt hoa thường. | Chuỗi tối đa 200 ký tự; chuẩn hóa NFC, trim, gộp khoảng trắng; không tự bỏ dấu tiếng Việt. | Không tìm kiếm, lấy mọi danh mục phù hợp các điều kiện còn lại. |
| `status` | Lọc chính xác trạng thái danh mục. | `Active` hoặc `Locked`. | Lấy cả hai trạng thái. |
| `sortBy` | Chọn trường sắp xếp. | `Name`, `DisplayCode`, `Status`. | `Name`. |
| `sortDirection` | Chọn chiều sắp xếp. | `Asc` = tăng dần; `Desc` = giảm dần. | `Asc`. |
| `page` | Chọn trang kết quả. | Số nguyên từ 1 đến 1.000.000. | `1`. |
| `pageSize` | Số danh mục tối đa mỗi trang. | Số nguyên từ 1 đến 100. | `20`. |

Mỗi request chọn một trường `sortBy`. Khi giá trị trường sắp xếp bằng nhau, backend dùng ID nội bộ tăng dần làm khóa phụ để ổn định thứ tự; ID này không được trả trong DTO Category. Chưa hỗ trợ lọc hoặc sắp xếp theo số thiết bị.

Ví dụ tìm danh mục đang hoạt động có mã/tên chứa `máy`, sắp xếp theo mã giảm dần:

```http
GET /api/v1/device-categories?search=m%C3%A1y&status=Active&sortBy=DisplayCode&sortDirection=Desc&page=1&pageSize=20
```

### 2.3. Body tạo và sửa Category

| Trường JSON | POST tạo mới | PUT cập nhật | Ý nghĩa / quy tắc |
| --- | --- | --- | --- |
| `displayCode` | Tùy chọn | Không được gửi | Mã tối đa 64 ký tự, trim/viết hoa. Bắt đầu bằng chữ A–Z hoặc số; phần sau cho phép A–Z/0–9 và `.`, `_`, `/`, `-`. Bỏ qua/null/trắng khi tạo thì tự sinh mã `DM` + 32 ký tự hex. Bất biến sau khi tạo. |
| `name` | Bắt buộc | Bắt buộc | Tên tối đa 200 ký tự sau chuẩn hóa NFC, trim và gộp khoảng trắng; không được trùng tên không phân biệt hoa thường, kể cả danh mục khóa. |
| `description` | Tùy chọn | Tùy chọn | Mô tả, tối đa 4000 ký tự; trim, trắng/null thành null. Bỏ qua trong PUT sẽ xóa mô tả cũ về null. |
| `notes` | Tùy chọn | Tùy chọn | Ghi chú nội bộ, độc lập với mô tả; tối đa 4000 ký tự; xử lý chuỗi trắng và bỏ qua trong PUT giống `description`. |
| `version` | Không được gửi | Tùy chọn | Chuỗi 64 ký tự hex lấy từ GET chi tiết. Nếu nội dung đã khác lúc đọc, PUT trả `409 CATEGORY_VERSION_CONFLICT`. Bỏ qua thì không kiểm tra version. |

Không gửi thêm các trường như `id`, `status`, `actorAccountId`; body ngoài hợp đồng bị từ chối `400`.

Ví dụ POST tạo mới:

```json
{
  "displayCode": "DM_PC",
  "name": "Máy tính",
  "description": "Thiết bị văn phòng",
  "notes": "Ghi chú nội bộ QTTB"
}
```

## 3. Category History — Lịch sử danh mục thiết bị

Cả Trưởng phòng và Nhân viên QTTB được đọc lịch sử. Nhân viên phòng ban không có quyền đọc. Các endpoint lịch sử chỉ hỗ trợ đọc; audit được ghi nội bộ khi thao tác Category.

### 3.1. Danh sách endpoint

| Method | Endpoint | Tác dụng | Phản hồi thông thường | Lọc / sắp xếp |
| --- | --- | --- | --- | --- |
| GET | `/api/v1/history/device-categories` | Tra cứu lịch sử trên toàn bộ danh mục, gồm cả event không còn thông tin target/actor. | `200` + danh sách phân trang | Có, xem mục 3.2. |
| GET | `/api/v1/history/device-categories/{historyId}` | Xem chi tiết một sự kiện lịch sử theo ID. | `200` + chi tiết; `404` nếu không tồn tại | Không. |
| GET | `/api/v1/device-categories/{displayCode}/history` | Tra cứu lịch sử riêng của một danh mục. | `200` + danh sách phân trang; `404` nếu danh mục không tồn tại | Có, dùng cùng các trường ở mục 3.2, trong phạm vi danh mục được chọn. |
| GET | `/api/v1/device-categories/{displayCode}/history/{historyId}` | Xem chi tiết sự kiện thuộc đúng danh mục được chọn. | `200`; `404` nếu event không tồn tại hoặc thuộc danh mục khác | Không. |

Danh sách trả các trường `id`, `displayCode`, `name`, `actionCode`, `outcome`, `occurredAt`, `actorAccountCode`, `actorFullName`. Mã/tên danh mục lấy từ snapshot tại thời điểm sự kiện; đổi tên sau đó không sửa nhãn lịch sử cũ.

Chi tiết trả thêm `reasonCode`, `changedFields`, `beforeData`, `afterData` đã lọc các trường cho phép. Chỉ Trưởng phòng QTTB nhận thêm `requestId` trong chi tiết. UUID danh mục và UUID người thao tác không được trả trong DTO lịch sử.

### 3.2. Bộ lọc, sắp xếp và phân trang Category History

Áp dụng cho **cả hai endpoint danh sách lịch sử** tại mục 3.1. Mọi tham số đều tùy chọn; các bộ lọc khác nhau kết hợp theo điều kiện AND.

| Trường query | Tác dụng | Giá trị / giới hạn | Mặc định nếu bỏ qua |
| --- | --- | --- | --- |
| `search` | Tìm chuỗi con theo **mã hoặc tên danh mục trong snapshot sự kiện**. | Chuỗi tối đa 200 ký tự; chuẩn hóa NFC/trim/gộp khoảng trắng, không phân biệt hoa thường; không tự bỏ dấu. | Không tìm kiếm. |
| `displayCode` | Lọc chính xác mã danh mục trong snapshot sự kiện. | Chuỗi tối đa 64 ký tự; trim, không phân biệt hoa thường. | Không lọc thêm theo mã. |
| `actorAccountCode` | Lọc chính xác mã tài khoản người thao tác. | Chuỗi tối đa 32 ký tự; trim, không phân biệt hoa thường. | Không lọc theo người thao tác. |
| `actionCode` | Lọc chính xác loại hành động đã thử. | Chuỗi tối đa 64 ký tự; trim/viết hoa. Các action hiện dùng: `CATEGORY_CREATED`, `CATEGORY_UPDATED`, `CATEGORY_LOCKED`, `CATEGORY_UNLOCKED`, `DELETE_DENIED`. | Lấy mọi hành động. |
| `outcome` | Lọc kết quả thực hiện hành động. | `Success`, `Failed`, `Denied`, `NoChange`. | Lấy mọi kết quả. |
| `from` | Chỉ lấy sự kiện có `occurredAt >= from`, bao gồm mốc đầu. | Thời gian ISO 8601, nên có `Z` hoặc UTC offset; backend chuẩn hóa về UTC. | Không giới hạn mốc đầu. |
| `to` | Chỉ lấy sự kiện có `occurredAt <= to`, bao gồm mốc cuối. | Thời gian ISO 8601; phải thỏa `from <= to` nếu truyền cả hai. | Không giới hạn mốc cuối. |
| `sortBy` | Chọn trường sắp xếp lịch sử. | `OccurredAt`, `ActionCode`, `Outcome`, `ActorAccountCode`. | `OccurredAt`. |
| `sortDirection` | Chọn chiều sắp xếp. | `Asc` hoặc `Desc`. | `Desc`, tức sự kiện mới nhất trước. |
| `page` | Chọn trang kết quả. | Số nguyên từ 1 đến 1.000.000. | `1`. |
| `pageSize` | Số sự kiện tối đa mỗi trang. | Số nguyên từ 1 đến 100. | `20`. |

Khi giá trị trường sắp xếp bằng nhau, backend dùng ID sự kiện tăng dần làm khóa phụ. `sortBy=Outcome` sắp xếp theo mã kết quả dạng chữ trong database, không theo thứ tự khai báo enum. Không có query lọc theo `reasonCode`, `requestId`, UUID danh mục hoặc UUID người thao tác trong API Category History hiện tại.

Với route theo `{displayCode}`, mọi query chỉ lọc bên trong danh mục đó. Ví dụ route `DM_PC/history` kết hợp `displayCode=DM_OTHER` trả danh sách rỗng, không mở rộng phạm vi sang danh mục khác.

Ví dụ xem các lần sửa thành công do `TK_STAFF` thực hiện trong tháng 10/2026:

```http
GET /api/v1/history/device-categories?displayCode=DM_PC&actorAccountCode=TK_STAFF&actionCode=CATEGORY_UPDATED&outcome=Success&from=2026-10-01T00:00:00Z&to=2026-10-31T23:59:59.999999Z&sortBy=OccurredAt&sortDirection=Desc&page=1&pageSize=20
```

Ví dụ xem các lần khóa riêng danh mục `DM_PC`:

```http
GET /api/v1/device-categories/DM_PC/history?actionCode=CATEGORY_LOCKED&sortBy=OccurredAt&sortDirection=Desc&page=1&pageSize=10
```

Nếu dùng offset `+07:00` trong query, encode dấu `+` thành `%2B`, ví dụ `from=2026-10-24T00:00:00%2B07:00`.

## 4. Mã phản hồi cần xử lý

| HTTP | Ý nghĩa |
| --- | --- |
| `400` | Payload/query không hợp lệ: trường ngoài hợp đồng, vượt độ dài, enum sai, phân trang vượt giới hạn hoặc `from > to`. |
| `401` | Thiếu phiên, phiên hết hạn/bị thu hồi hoặc tài khoản bị khóa. |
| `403` | Không đủ quyền; hoặc yêu cầu xóa danh mục bị cấm. |
| `404` | Danh mục/lịch sử không tồn tại hoặc event nằm ngoài phạm vi danh mục. |
| `409` | Trùng tên/mã; sửa danh mục khóa; hoặc version cũ. |
| `503` | Không thể đọc/lưu dữ liệu hoặc ghi audit. |

Hợp đồng dữ liệu và quy tắc nghiệp vụ đầy đủ: [API quản lý danh mục thiết bị](device-categories-api.md).
