# API đọc lịch sử: tài khoản, xác thực, phòng ban và khu vực

Tất cả endpoint yêu cầu phiên Bearer hợp lệ và vai trò **QTTB_MANAGER**. Hai role nhân viên không được đọc, kể cả lịch sử của chính mình. Chỉ có GET; không cung cấp API tạo/sửa/xóa lịch sử. Không cần migration mới.

## Device timeline (Phase 6)

These device timeline routes are a scoped exception to the QTTB-only rule for the other history resources documented below.

- `GET /api/v1/history/devices` returns the global device timeline for QTTB roles only.
- `GET /api/v1/devices/{deviceCode}/history` returns the same timeline after device department-scope authorization.
- Query filters: `eventType` (`DEVICE|RECEIPT|LOAN|TRANSFER`), `deviceCode`, `departmentCode` (the device's current department), `actorAccountCode`, `fromDate`, `toDate`, `page`, and `pageSize`.
- Events include `eventType`, `actionCode`, the related receipt/loan/transfer `businessCode` when available, device code/name, current department code, actor, outcome, and timestamp. Results are ordered by timestamp descending with stable tie-breakers.
- Department Staff cannot use the global route and do not receive snapshots or changed-field details from the scoped route. QTTB can read the audit snapshots.

## Endpoint

| GET | Dữ liệu |
| --- | --- |
| `/api/v1/history/accounts` | Danh sách thao tác tài khoản |
| `/api/v1/history/accounts/{id}` | Chi tiết một AccountHistory |
| `/api/v1/history/auth` | Danh sách đăng nhập, cấp/kết thúc phiên, thất bại và chặn đăng nhập |
| `/api/v1/history/auth/{id}` | Chi tiết một AuthHistory |
| `/api/v1/history/departments` | Danh sách thao tác trên mọi phòng ban |
| `/api/v1/history/departments/{id}` | Chi tiết một DepartmentHistory |
| `/api/v1/history/areas` | Danh sách thao tác trên mọi khu vực |
| `/api/v1/history/areas/{id}` | Chi tiết một AreaHistory |

`id` trên các route chi tiết là **ID của item lịch sử**, không phải ID tài khoản/phòng ban/khu vực/phiên.

Giữ route danh sách phòng ban hiện có: `GET /api/v1/departments/{id}/history`, cùng hợp đồng list mới và thêm `GET /api/v1/departments/{id}/history/{historyId}`. Scope department trên URL được kiểm tra tồn tại và luôn áp dụng; query departmentId khác scope chỉ cho kết quả rỗng. Chi tiết historyId thuộc phòng ban khác trả 404.

Khu vực cũng có `GET /api/v1/areas/{id}/history` trả danh sách gọn theo hợp đồng chung, và `GET /api/v1/areas/{id}/history/{historyId}` trả chi tiết. Xem quy tắc phạm vi cây bên dưới; route `{id}` khu vực phải tồn tại.

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
| areas | areaId, areaCode, areaName |

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
| areas | requestId, changedFields, beforeData, afterData |

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
| areas | areaId, includeDescendants (mặc định false) |

UUID không rỗng; UUID hợp lệ không có dữ liệu khi dùng làm bộ lọc trả danh sách rỗng. Các bộ lọc được kết hợp bằng AND. actionCode/reasonCode hợp lệ nhưng chưa có trong dữ liệu trả danh sách rỗng.

### Phạm vi khu vực và cây con

- `/history/areas` không có `areaId` trả sự kiện của mọi khu vực, kể cả sự kiện không xác định khu vực/người thực hiện.
- `areaId=<UUID>` lọc đúng khu vực; thêm `includeDescendants=true` lấy cả các node con/cháu. Trên route toàn cục, bật includeDescendants mà thiếu areaId trả 400. areaId hợp lệ nhưng không tồn tại trả danh sách rỗng, kể cả khi tìm cả nhánh.
- `/areas/{id}/history` mặc định chỉ lấy đúng khu vực trên URL; `includeDescendants=true` mở rộng sang cây con. Nếu gửi thêm areaId, backend lấy giao của phạm vi URL và bộ lọc areaId; khi bật includeDescendants, cả hai phạm vi được hiểu là cây con. Query không thể làm lộ sự kiện nằm ngoài nhánh URL.
- Chi tiết `/areas/{id}/history/{historyId}` mặc định yêu cầu sự kiện thuộc đúng `{id}`. Muốn mở sự kiện của con/cháu từ danh sách nhánh, gửi `?includeDescendants=true` hoặc dùng `/history/areas/{historyId}`. Sự kiện ngoài phạm vi trả 404.
- Phạm vi nhánh dựa trên **cây hiện tại**, không phải cây tại thời điểm thao tác. Chuyển một node sang nhánh khác sẽ thay đổi tập lịch sử của các nhánh; snapshot trước/sau vẫn bất biến.

`search` khu vực tìm trong mã thao tác/lý do, mã/tên người thực hiện và mã/tên khu vực hiện tại. Không tìm trong snapshot hoặc changedFields. Bộ lọc thời gian, kết quả, người thực hiện, request, sort và phân trang dùng chung như các nhóm khác. Mặc định `OccurredAt/Desc`, thêm ID tăng dần khi bằng nhau; danh sách không tải các cột snapshot/requestId/changedFields từ DB.

```http
GET /api/v1/history/accounts?targetAccountId=<account-id>&outcome=Success&sortBy=OccurredAt&sortDirection=Desc&page=1&pageSize=20
GET /api/v1/history/auth?actionCode=SESSION_ENDED&reasonCode=LOGOUT&from=2026-10-21T00:00:00Z&to=2026-10-22T00:00:00Z
GET /api/v1/history/departments?departmentId=<department-id>&search=QTTB
GET /api/v1/history/areas?areaId=<area-id>&includeDescendants=true&outcome=Success&sortBy=OccurredAt&sortDirection=Desc
GET /api/v1/areas/<area-id>/history/<history-item-id>?includeDescendants=true
GET /api/v1/history/accounts/<history-item-id>
```

Các lần đăng xuất mới của phiên còn hạn lưu `SESSION_ENDED` với reasonCode `LOGOUT`. Nếu phiên đã hết hạn, sự kiện vẫn ghi thời điểm hết hạn, actor/reason null theo cơ chế expiration hiện tại; không gắn nhầm LOGOUT. Sự kiện cũ và các luồng kết thúc phiên khác hiện có thể có reasonCode null; API không suy đoán hoặc ghi đè lịch sử cũ. Để xem mọi lần kết thúc phiên, chỉ lọc actionCode=SESSION_ENDED; thêm reasonCode=LOGOUT để lấy các lần đăng xuất đã được đánh dấu. Có thể có nhiều sự kiện cho cùng request (ví dụ SESSION_CREATED và LOGIN_SUCCESS); mỗi item là một sự kiện gốc, không tự gộp hoặc làm mất dữ liệu.

## Lỗi và tích hợp frontend

400 cho query sai định dạng, enum/giới hạn không hợp lệ, from > to hoặc includeDescendants=true thiếu areaId trên route toàn cục. 401 khi thiếu/phiên không hợp lệ. 403 `HISTORY_ACCESS_FORBIDDEN` cho người không có quyền. 404 `HISTORY_NOT_FOUND` cho historyId không tồn tại/không đúng scope; scope department không tồn tại trả `DEPARTMENT_NOT_FOUND`, scope khu vực không tồn tại trả `AREA_NOT_FOUND`. POST/PUT/DELETE vào route lịch sử trả 405.

Khi mở bảng, frontend gọi endpoint danh sách. Khi người dùng chọn một item, gọi endpoint chi tiết bằng item.id. Các đường dẫn `/departments/{id}/history` và `/areas/{id}/history` vẫn tồn tại, nhưng **không còn trả snapshot/requestId/changedFields trong từng item**; panel lịch sử cần gọi endpoint chi tiết mới để tải dữ liệu đầy đủ. Mã/tên người thực hiện và đối tượng đã có sẵn trong danh sách. Không thêm migration hay ghi lại dữ liệu lịch sử cũ.
