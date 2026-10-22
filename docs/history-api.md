# API đọc lịch sử: tài khoản, xác thực và phòng ban

Tất cả endpoint yêu cầu phiên Bearer hợp lệ và vai trò **QTTB_MANAGER**. Hai role nhân viên không được đọc, kể cả lịch sử của chính mình. Chỉ có GET; không cung cấp API tạo/sửa/xóa lịch sử. Không cần migration mới.

## Endpoint

| GET | Dữ liệu |
| --- | --- |
| `/api/v1/history/accounts` | Danh sách thao tác tài khoản |
| `/api/v1/history/accounts/{id}` | Chi tiết một AccountHistory |
| `/api/v1/history/auth` | Danh sách đăng nhập, cấp/kết thúc phiên, thất bại và chặn đăng nhập |
| `/api/v1/history/auth/{id}` | Chi tiết một AuthHistory |
| `/api/v1/history/departments` | Danh sách thao tác trên mọi phòng ban |
| `/api/v1/history/departments/{id}` | Chi tiết một DepartmentHistory |

`id` trên các route chi tiết là **ID của item lịch sử**, không phải ID tài khoản/phòng ban/phiên.

Giữ route danh sách phòng ban hiện có: `GET /api/v1/departments/{id}/history`, cùng hợp đồng list mới và thêm `GET /api/v1/departments/{id}/history/{historyId}`. Scope department trên URL được kiểm tra tồn tại và luôn áp dụng; query departmentId khác scope chỉ cho kết quả rỗng. Chi tiết historyId thuộc phòng ban khác trả 404.

## Danh sách gọn

Response dùng `PagedResult<T>`: `items`, `totalCount`, `page`, `pageSize` theo kiểu phân trang hiện có. Frontend tính tổng số trang từ totalCount/pageSize.

Các trường chung mỗi item:

| Trường | Ý nghĩa |
| --- | --- |
| id | ID để mở chi tiết |
| occurredAt | Thời điểm UTC |
| actionCode | Loại thao tác |
| outcome | Success / Failed / Denied / NoChange |
| reasonCode | Lý do hoặc null |
| actorAccountId / actorAccountCode / actorFullName | Người thực hiện, có thể null nếu là hệ thống hoặc chưa xác thực |

Trường riêng từng danh sách:

| Nhóm | Thêm vào item |
| --- | --- |
| accounts | targetAccountId, targetAccountCode, targetFullName |
| auth | accountId, accountCode, fullName, emailSubmitted (giúp nhận biết lần đăng nhập thất bại không có accountId) |
| departments | departmentId, departmentCode, departmentName |

Danh sách không có requestId, snapshot, changedFields, sessionId, IP, user-agent hoặc blockedUntil. Repository lọc/sort/phân trang và `ProjectTo<ListItemDto>` tại PostgreSQL; các cột chi tiết không được tải khi lấy danh sách.

Từng item đã có sẵn `actorFullName` là tên người thực hiện, cùng `actorAccountId` và `actorAccountCode`; frontend không cần tải chi tiết hoặc gọi API tài khoản để lấy tên. Sự kiện không xác định người thực hiện trả actor null, không suy đoán từ tài khoản bị tác động hoặc email đăng nhập. Lịch sử khu vực tại `GET /api/v1/areas/{id}/history` cũng trả ba trường này. Tên hiển thị là tên profile hiện tại, không phải bản chụp tên tại thời điểm xảy ra sự kiện.

Tên và mã được join từ dữ liệu **hiện tại**, không phải giá trị cố định tại thời điểm thao tác. Khi đối chiếu thay đổi lịch sử, dùng snapshot ở chi tiết. Các quan hệ null được giữ lại bằng left join, không làm mất sự kiện thất bại/hệ thống.

## Chi tiết đầy đủ

Chi tiết bao gồm toàn bộ trường của item danh sách và các trường bổ sung:

| Nhóm | Trường chi tiết |
| --- | --- |
| accounts | requestId, changedFields, beforeData, afterData |
| auth | requestId, sessionId, blockedUntil, ipAddress, userAgent |
| departments | requestId, beforeData, afterData |

Snapshot và changedFields là chuỗi JSON đọc từ JSONB, có thể null; frontend parse JSON để hiển thị. PostgreSQL có thể chuẩn hóa khoảng trắng/thứ tự key của JSONB; nội dung JSON giữ nguyên. IP trả dạng chuỗi IPv4/IPv6 hoặc null. sessionId là UUID của phiên, không phải access token. Không truy vấn hoặc trả password hash, session token/hash hay mã reset.

## Bộ lọc chung

| Query | Quy tắc |
| --- | --- |
| from / to | DateTimeOffset, bao gồm hai biên; tự chuyển về UTC; from không được sau to. URL encode dấu `+` nếu gửi múi giờ +07:00 |
| actorAccountId | Người thực hiện; UUID không rỗng |
| requestId | UUID truy vết một request, không rỗng |
| actionCode / reasonCode | So khớp chính xác không phân biệt hoa thường, bỏ khoảng trắng đầu/cuối; tối đa 64/80 ký tự |
| outcome | Success / Failed / Denied / NoChange |
| search | Chuỗi con trên action/reason, mã/tên người thực hiện, mã/tên đối tượng; auth thêm emailSubmitted. NFC/trim/gộp khoảng trắng, không phân biệt hoa thường, không bỏ dấu; tối đa 200 ký tự. Không tìm trong snapshot/IP/user-agent |
| sortBy | OccurredAt (mặc định), ActionCode, Outcome, ActorAccountCode |
| sortDirection | Desc (mặc định) hoặc Asc |
| page / pageSize | Page 1–1.000.000; pageSize 1–100, mặc định 20 |

Sort luôn thêm Id tăng dần để ổn định khi trùng giá trị. Outcome Asc theo thứ tự enum PostgreSQL hiện có: Denied, Failed, NoChange, Success; Desc đảo lại. Sort ActorAccountCode theo collation DB; giá trị null nằm cuối khi Asc, đầu khi Desc. totalCount áp dụng tất cả bộ lọc; không tìm thấy trả items rỗng.

## Bộ lọc riêng

| Nhóm | Query bổ sung |
| --- | --- |
| accounts | targetAccountId |
| auth | accountId, sessionId, emailSubmitted (so khớp chính xác sau trim, không phân biệt hoa thường; tối đa 320 ký tự) |
| departments | departmentId |

UUID không rỗng; UUID hợp lệ không có dữ liệu khi dùng làm bộ lọc trả danh sách rỗng. Các bộ lọc được kết hợp bằng AND. actionCode/reasonCode hợp lệ nhưng chưa có trong dữ liệu trả danh sách rỗng.

```http
GET /api/v1/history/accounts?targetAccountId=<account-id>&outcome=Success&sortBy=OccurredAt&sortDirection=Desc&page=1&pageSize=20
GET /api/v1/history/auth?actionCode=SESSION_ENDED&reasonCode=LOGOUT&from=2026-10-21T00:00:00Z&to=2026-10-22T00:00:00Z
GET /api/v1/history/departments?departmentId=<department-id>&search=QTTB
GET /api/v1/history/accounts/<history-item-id>
```

Các lần đăng xuất mới của phiên còn hạn lưu `SESSION_ENDED` với reasonCode `LOGOUT`. Nếu phiên đã hết hạn, sự kiện vẫn ghi thời điểm hết hạn, actor/reason null theo cơ chế expiration hiện tại; không gắn nhầm LOGOUT. Sự kiện cũ và các luồng kết thúc phiên khác hiện có thể có reasonCode null; API không suy đoán hoặc ghi đè lịch sử cũ. Để xem mọi lần kết thúc phiên, chỉ lọc actionCode=SESSION_ENDED; thêm reasonCode=LOGOUT để lấy các lần đăng xuất đã được đánh dấu. Có thể có nhiều sự kiện cho cùng request (ví dụ SESSION_CREATED và LOGIN_SUCCESS); mỗi item là một sự kiện gốc, không tự gộp hoặc làm mất dữ liệu.

## Lỗi và tích hợp frontend

400 cho query sai định dạng, enum/giới hạn không hợp lệ hoặc from > to. 401 khi thiếu/phiên không hợp lệ. 403 `HISTORY_ACCESS_FORBIDDEN` cho người không có quyền. 404 `HISTORY_NOT_FOUND` cho historyId không tồn tại/không đúng scope; scope department không tồn tại trả `DEPARTMENT_NOT_FOUND`. POST/PUT/DELETE vào route lịch sử trả 405.

Khi mở bảng, frontend gọi endpoint danh sách. Khi người dùng chọn một item, gọi endpoint chi tiết bằng item.id. Đường dẫn `/departments/{id}/history` vẫn tồn tại, nhưng **không còn trả snapshot trong từng item**; panel lịch sử phòng ban cần gọi endpoint chi tiết mới để tải snapshot. Không thay đổi API lịch sử khu vực trong đợt này.
