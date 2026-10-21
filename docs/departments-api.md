# API quản lý phòng ban — Nhóm 3

Nguồn nghiệp vụ: [Nhóm 3, UC-PB-01…06](https://docs.google.com/spreadsheets/d/1xQg5ao1fWNIA5qC8qisSoVaUeUuRZ9UFa3_nse2ZHyA/edit#gid=877057480). Sheet có nhãn “chính sách đề xuất cần xác nhận”; bản triển khai này áp dụng các quy tắc hiện được ghi trong sheet để tiếp nối hai nhóm trước. Không tạo tài khoản mặc định hoặc thay đổi schema/migration.

## Endpoint và quyền

Tất cả endpoint yêu cầu `Authorization: Bearer <accessToken>` từ nhóm 2. Trưởng phòng QTTB là role `QTTB_MANAGER`; nhân viên QTTB và nhân viên phòng ban là `QTTB_STAFF`, `DEPARTMENT_STAFF`.

| Method / URL | Quyền | Thành công |
| --- | --- | --- |
| GET `/api/v1/departments` | Cả ba role | 200, danh mục phân trang |
| GET `/api/v1/departments/{id}` | Cả ba role | 200, chi tiết theo quyền |
| POST `/api/v1/departments` | Trưởng phòng | 201 + Location trỏ đến chi tiết |
| PUT `/api/v1/departments/{id}` | Trưởng phòng | 204 |
| PUT `/api/v1/departments/{id}/lock` | Trưởng phòng | 204 |
| DELETE `/api/v1/departments/{id}/lock` | Trưởng phòng | 204 |
| GET `/api/v1/departments/{id}/lock-impact` | Trưởng phòng | 200, cảnh báo trước khi khóa |
| GET `/api/v1/departments/{id}/history` | Trưởng phòng | 200, lịch sử riêng phòng ban, phân trang |
| DELETE `/api/v1/departments/{id}` | Luôn bị từ chối | 403 + audit DELETE_DENIED; không xóa dữ liệu |

UC-PB-06 là thao tác audit nội bộ. Endpoint history chỉ cho người có quyền **đọc**, không có API thêm/sửa/xóa bản ghi lịch sử. Route DELETE phòng ban chỉ ghi nhận yêu cầu xóa bị cấm theo BR-PB-06-04, không cung cấp chức năng xóa.

## Tạo và sửa

POST và PUT nhận cùng các trường nội dung:

```json
{
  "name": "Phòng Kế toán",
  "shortName": "KT",
  "description": "Quản lý tài chính"
}
```

Tên bắt buộc, tối đa 200 ký tự sau NFC/gộp khoảng trắng/trim; so sánh trùng không phân biệt hoa thường. Tên viết tắt tùy chọn tối đa 50 ký tự. Mô tả tùy chọn có giới hạn API 4000 ký tự để giới hạn payload (sheet không quy định độ dài; DB là text). Chuỗi trống ở trường tùy chọn được lưu null; các trường tùy chọn bị bỏ qua trong PUT cũng được xóa về null. PUT là thay toàn bộ thông tin cho phép sửa.

Mã phòng ban dài 32 ký tự, tiền tố PB + 30 ký tự hex ngẫu nhiên, do server sinh; trạng thái tạo mới luôn Active. DTO từ chối các trường ngoài hợp đồng như id/code/status/createdBy. AutoMapper chỉ sửa name/shortName/description; không thay id/code/status. Không cho sửa phòng ban Locked; cần mở khóa trước. Tên bị phòng ban Locked giữ chỗ như tên của phòng ban Active.

Tạo trả DTO chi tiết. Danh mục trả id/code/name/shortName/status, không kèm nhân sự/audit. Chi tiết cơ bản có thêm description. Chỉ Trưởng phòng có trường management:

```json
{
  "id": "<department-id>",
  "code": "PB<30-hex-characters>",
  "name": "Phòng Kế toán",
  "shortName": "KT",
  "description": "Quản lý tài chính",
  "status": "Active",
  "management": {
    "createdBy": "<actor-account-id>",
    "createdAt": "<UTC-timestamp>",
    "accountCount": 0
  }
}
```

Management bị loại khỏi JSON với hai role nhân viên. Thời gian/người tạo lấy từ DEPARTMENT_CREATED thành công sớm nhất; dữ liệu cũ/seed không có sự kiện tạo sẽ trả null, không tự tạo lịch sử giả. Không trả email, mật khẩu, token, danh sách tài khoản cá nhân hay thiết bị từ API này.

## Tìm kiếm, lọc, sắp xếp

```http
GET /api/v1/departments?search=kế%20toán&status=Active&sortBy=Name&sortDirection=Asc&page=1&pageSize=20
```

Search tìm chuỗi con theo tên đã chuẩn hóa hoặc tên viết tắt, không phân biệt hoa thường; ký tự `%`/`_` được coi là nội dung tìm, không phải quyền truyền SQL. Không tự bỏ dấu tiếng Việt. Status là Active/Locked, bỏ qua để lấy cả hai; phòng ban khóa vẫn được đọc. SortBy hỗ trợ Name/Code/Status, mặc định Name; sortDirection Asc/Desc, mặc định Asc. ID là khóa phụ để ổn định thứ tự phân trang. Các enum sai/không được định nghĩa bị trả 400.

Page từ 1 đến 1.000.000; pageSize 1–100, mặc định 20. Kết quả `{items,totalCount,page,pageSize}`, không tìm thấy trả 200 với items rỗng và totalCount=0. Lọc/sắp xếp/Count/Skip/Take/ProjectTo thực hiện trong PostgreSQL qua repository. Controller không truy cập DbContext. API đọc không tạo audit SUCCESS.

## Khóa, mở khóa và cảnh báo liên kết

Trước khi xác nhận khóa, client gọi GET `/{id}/lock-impact` và hiển thị warning/accountCount cho người quản lý. Số lượng là ảnh chụp tại thời điểm đọc, có thể thay đổi trước khi thao tác khóa được gửi.

```json
{
  "departmentId": "<department-id>",
  "status": "Active",
  "accountCount": 2,
  "deviceCount": null,
  "requestCount": null,
  "linkCountsComplete": false,
  "warning": "Locking prevents new assignments; existing accounts and links are preserved."
}
```

AccountCount đếm tài khoản hiện liên kết, kể cả tài khoản khóa. **Chưa có module/bảng thiết bị và phiếu**, nên hai số này là null và linkCountsComplete=false; không được hiển thị như 0. Yêu cầu thống kê thiết bị/phiếu trong BR-PB-03-07 cần tích hợp khi các module tương ứng có dữ liệu. Hiện API vẫn cho khóa theo chính sách và không thay đổi liên kết cũ.

Khóa phòng ban chỉ đổi trạng thái; không khóa tài khoản nhân viên, không thu hồi phiên và không chuyển dữ liệu sang phòng ban khác. Nhóm 1 đã từ chối tạo/chuyển tài khoản vào phòng ban Locked. Các nghiệp vụ thiết bị/phiếu triển khai sau phải kiểm tra trạng thái khi gán mới và cho phép xử lý phiếu cũ theo quyền. Gọi khóa/mở khóa khi trạng thái đã đúng trả 204, không thay đổi và không ghi thêm sự kiện. Sửa với toàn bộ nội dung đã giống nhau cũng không ghi sự kiện thay đổi.

## Audit, giao dịch và lỗi

Tạo/sửa/khóa/mở khóa ghi DEPARTMENT_CREATED / DEPARTMENT_UPDATED / DEPARTMENT_LOCKED / DEPARTMENT_UNLOCKED cùng dữ liệu nghiệp vụ trong một transaction. Snapshot chọn rõ id/code/name/shortName/description/status. Có request_id server sinh, actor, department_id và thời gian UTC; before của tạo là null. Không serialize toàn bộ entity/tài khoản.

DepartmentTransaction dùng cùng advisory lock với AccountTransaction. Quyền và phiên được kiểm tra lại trong transaction ghi; thao tác khóa không chạy đua với việc tạo/chuyển tài khoản vào phòng ban. Unique index tên chuẩn hóa vẫn là lớp bảo vệ DB. Không sửa transaction/migration của nhóm cũ.

Lỗi nghiệp vụ ghi DENIED hoặc FAILED trong transaction khác sau rollback, không có snapshot giả; DELETE bị cấm ghi DELETE_DENIED. Model binding/validation thất bại ở POST/PUT cũng ghi FAILED/VALIDATION_FAILED trước response 400, không lưu các giá trị request không hợp lệ. Request chưa xác thực bị chặn 401 trước controller. GET và lỗi tham số GET không ghi lịch sử thay đổi. Đích không tồn tại được audit với departmentId=null để không vi phạm FK. RequestId vẫn cho phép truy vết request bị từ chối.

Nếu không lưu được audit thành công, rollback dữ liệu và trả 503; cố ghi FAILED sau rollback. Nếu ngay audit thất bại cũng không lưu được, vẫn trả 503 và chỉ có log vận hành an toàn; không tuyên bố đã ghi audit. Chuyển phòng ban của tài khoản vẫn chỉ ghi account_history, không tạo sự kiện trùng trong department_history.

| HTTP | Code | Ý nghĩa |
| --- | --- | --- |
| 400 | VALIDATION_FAILED | Thiếu/sai trường, dài quá giới hạn, query không hợp lệ |
| 401 | AUTHENTICATION_REQUIRED hoặc phản hồi challenge | Thiếu/phiên không hợp lệ |
| 403 | DEPARTMENT_ACCESS_FORBIDDEN | Không đủ quyền thao tác |
| 403 | DEPARTMENT_DELETE_FORBIDDEN | Yêu cầu xóa phòng ban |
| 404 | DEPARTMENT_NOT_FOUND | Phòng ban không tồn tại |
| 409 | DEPARTMENT_NAME_CONFLICT | Trùng tên với phòng ban còn lưu |
| 409 | DEPARTMENT_LOCKED | Sửa thông tin phòng ban khóa |
| 503 | DEPARTMENT_UNAVAILABLE | Không lưu được dữ liệu/audit |

History nhận page/pageSize như danh mục, sắp xếp occurredAt giảm dần, ID làm khóa phụ. Mỗi item có id/requestId/actorAccountId/departmentId/actionCode/outcome/reasonCode/occurredAt/beforeData/afterData. BeforeData/afterData là chuỗi JSON snapshot đọc từ JSONB, có thể null. Chỉ Trưởng phòng đọc và chỉ nhận sự kiện của departmentId đang chọn.

## Kiểm thử

Tài liệu input/output từng testcase được lưu riêng bên ngoài project. Test tự động nằm trong `tests/CSVC.API.Tests/DepartmentApiTests.cs`. Test tích hợp dùng PostgreSQL UTF-8 riêng có quyền tạo DB test; không migrate hoặc xóa dữ liệu DB ứng dụng. Toàn bộ test nhóm trước được chạy lại để kiểm tra hồi quy. Không cần migration mới để dùng API phòng ban.
