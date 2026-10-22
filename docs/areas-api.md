# API quản lý khu vực — Nhóm 4

Nguồn: [Nhóm 4, UC-KV-01…06](https://docs.google.com/spreadsheets/d/1xQg5ao1fWNIA5qC8qisSoVaUeUuRZ9UFa3_nse2ZHyA/edit#gid=16845195). Áp dụng chính sách cây node trong sheet hiện tại. Khu vực là vị trí vật lý, độc lập với phòng ban tổ chức. Không thêm migration, tài khoản mặc định hoặc dữ liệu khu vực mẫu vào DB ứng dụng.

## Endpoint

Tất cả endpoint yêu cầu `Authorization: Bearer <accessToken>` từ nhóm 2. Các role: `QTTB_MANAGER` (Trưởng phòng), `QTTB_STAFF`, `DEPARTMENT_STAFF`.

| Method / URL | Quyền | Kết quả |
| --- | --- | --- |
| GET `/api/v1/areas` | Cả ba role | 200, tải gốc; có search thì tìm mọi cấp |
| GET `/api/v1/areas/{id}/children` | Cả ba role | 200, con trực tiếp, phân trang |
| GET `/api/v1/areas/{id}` | Cả ba role | 200, node, đường dẫn, trang đầu của con trực tiếp |
| POST `/api/v1/areas` | Trưởng phòng | 201 + Location |
| PUT `/api/v1/areas/{id}` | Trưởng phòng | 204, sửa thuộc tính và tùy chọn đổi cha |
| PUT `/api/v1/areas/{id}/lock` | Trưởng phòng | 204, khóa trạng thái riêng |
| DELETE `/api/v1/areas/{id}/lock` | Trưởng phòng | 204, mở khóa trạng thái riêng |
| GET `/api/v1/areas/{id}/lock-impact` | Trưởng phòng | 200, số con/hậu duệ và cảnh báo |
| GET `/api/v1/areas/{id}/history` | Trưởng phòng | 200, lịch sử chỉ đọc |
| DELETE `/api/v1/areas/{id}` | Luôn từ chối | 403 AREA_DELETE_FORBIDDEN + DELETE_DENIED |

Route DELETE chỉ ghi nhận yêu cầu xóa bị cấm theo UC-KV-06, không xóa dữ liệu. Không có API ghi/sửa/xóa lịch sử. Mã khu vực giữ nguyên khi chuyển nhánh.

## Tạo node và sửa thông tin

```http
POST /api/v1/areas
Content-Type: application/json

{
  "name": "Tòa A",
  "parentId": null,
  "typeLabel": "Tòa",
  "description": "Khu học tập"
}
```

ParentId=null hoặc bỏ qua tạo node gốc; gửi UUID tạo con của node đó. Mã tự sinh 32 ký tự, tiền tố KV + 30 ký tự hex ngẫu nhiên, không phụ thuộc đường dẫn. Trạng thái khởi tạo Active.

Name bắt buộc, tối đa 200 ký tự sau chuẩn hóa NFC/gộp khoảng trắng/trim. Tên không trùng trong cùng parentId, bao gồm các gốc null và node đã khóa; các cha khác nhau được có con cùng tên. TypeLabel tùy chọn tối đa 100 ký tự, không ép cứng cấp Tòa/Tầng/Phòng. Description tùy chọn tối đa 4000 ký tự ở API để giới hạn payload; sheet không quy định độ dài và DB dùng text. Chuỗi tùy chọn trắng được lưu null.

PUT nhận name/typeLabel/description và parentId tùy chọn. Các trường nội dung thay toàn bộ: typeLabel/description bị bỏ qua được xóa về null. ParentId có hợp đồng riêng:

| JSON gửi vào PUT | Ý nghĩa |
| --- | --- |
| Không có trường parentId | Giữ cha hiện tại |
| `"parentId": null` | Chủ động đưa node lên gốc |
| `"parentId": "<UUID>"` | Chuyển sang cha được chọn |

Ví dụ chỉ sửa tên, giữ cha:

```json
{"name":"Phòng mới","typeLabel":"Phòng"}
```

Ví dụ vừa sửa tên vừa chuyển nhánh:

```json
{"name":"Phòng mới","parentId":"<new-parent-id>","typeLabel":"Phòng"}
```

DTO từ chối các trường ngoài hợp đồng như id/code/status/departmentId/parentSpecified. Trạng thái xác định việc parentId có xuất hiện do server tính từ JSON, không nhận cờ do client gửi. Nếu client serialize bằng DTO riêng, phải bảo đảm không tự thêm `parentId:null` khi muốn giữ cha.

## Điều kiện cha, đổi nhánh và trạng thái hiệu lực

Cha được chọn phải tồn tại, chính cha và mọi tổ tiên Active. Không chấp nhận Guid.Empty, tự làm cha hoặc bất kỳ hậu duệ nào làm cha. Node có trạng thái riêng Locked không được sửa/di chuyển. Với node riêng Active nhưng tổ tiên Locked: sửa tại chỗ bị từ chối vì giữ cha không khả dụng; có thể chủ động chuyển ra gốc hoặc cha hiệu lực Active. Đây là cách áp dụng phân biệt trạng thái riêng và điều kiện cha đích trong BR-KV-02-06.

Đổi cha chỉ cập nhật hàng node được chọn; toàn bộ con/cháu đi theo thông qua quan hệ cha-con hiện hữu. Không đổi mã/ID, không cập nhật parentId của con, không ghi sự kiện giả cho từng con và không viết lại lịch sử cũ. Đường dẫn được tính từ cây hiện tại trong PostgreSQL, nên mọi hậu duệ thấy đường dẫn mới sau commit. Không lưu đường dẫn hiện tại thành dữ liệu dư thừa cần cập nhật hàng loạt.

Status là trạng thái riêng Active/Locked; EffectiveStatus là Active chỉ khi node và toàn bộ tổ tiên Active, còn lại Locked. EffectiveStatus=Locked được hiểu là không sử dụng được cho gán/phát sinh mới. Khóa cha không đổi status lưu của các con; mở khóa con không vượt qua cha khóa; mở khóa cha không tự mở các con đã khóa riêng. Thao tác khóa/mở khóa lặp lại trả 204 và không thêm sự kiện. PUT không đổi nội dung cũng không ghi sự kiện thay đổi.

Khi phát triển nghiệp vụ thiết bị/phiếu, kiểm tra **trạng thái hiệu lực** trước khi gán/phát sinh mới. Các thiết bị và phiếu đã có phải giữ liên kết và được xử lý theo quyền nghiệp vụ. Hiện chưa có các bảng/module này, nên chưa có kiểm tra phát sinh thiết bị thực tế.

## Tải cây, tìm kiếm và phân trang

```http
GET /api/v1/areas
GET /api/v1/areas/{id}/children?page=1&pageSize=20
GET /api/v1/areas?search=phòng&status=Active&effectiveStatus=Locked&sortBy=Name&sortDirection=Asc
```

GET danh mục không có search (hoặc search trắng) tải node gốc; có search tìm mã/tên ở mọi cấp. GET children luôn giới hạn con trực tiếp của id, search nếu có chỉ lọc trong các con đó. Không tìm thấy trả 200 với items rỗng; id cha không tồn tại trả 404. Tìm kiếm không phân biệt hoa/thường, chuẩn hóa khoảng trắng/NFC, không tự bỏ dấu tiếng Việt. `%`/`_` là ký tự nội dung, không phải wildcard của client.

Status lọc trạng thái riêng, effectiveStatus lọc trạng thái hiệu lực; mặc định không lọc và vẫn thấy node Locked. SortBy Name/Code, sortDirection Asc/Desc, mặc định Name/Asc, ID làm khóa phụ. Page 1–1.000.000, pageSize 1–100, mặc định 20. Enum không hợp lệ bị trả 400.

Response danh mục có `{items,totalCount,page,pageSize}`. Mỗi item gồm id/code/parentId/name/typeLabel/status/effectiveStatus/depth/childCount/pathIds/pathNames/path. PathIds và pathNames theo thứ tự gốc → node hiện tại, cho phép client mở các nhánh tổ tiên và định vị kết quả tìm kiếm. Path là chuỗi hiển thị nối bằng ` / `; dùng mảng ID để nhận diện vì tên có thể chứa dấu phân cách.

Chi tiết có thêm description, children (trang đầu 20 con) và management chỉ dành cho Trưởng phòng. Children có totalCount để tải tiếp qua endpoint children; không nhúng toàn bộ cây JSON. Không giới hạn số cấp nghiệp vụ; recursive CTE xử lý tại PostgreSQL, không đệ quy cây trong JSON. Query chịu command timeout hiện tại; dữ liệu rất lớn cần đo tải trước khi tối ưu tiếp.

Management gồm createdBy/createdAt và directDeviceCount/subtreeDeviceCount. Người/thời gian tạo suy ra từ AREA_CREATED Success; dữ liệu seed không có sự kiện trả null, không tạo lịch sử giả. Hai role nhân viên không nhận management hoặc danh sách thiết bị. Không có dữ liệu thiết bị nên các số thiết bị là null, không phải 0; dữ liệu chi tiết thiết bị và phân quyền tương ứng chờ module thiết bị.

## Cảnh báo trước khóa

Client gọi GET `/{id}/lock-impact` trước khi xác nhận khóa:

```json
{
  "areaId":"<UUID>",
  "status":"Active",
  "effectiveStatus":"Active",
  "childCount":2,
  "descendantCount":5,
  "directDeviceCount":null,
  "subtreeDeviceCount":null,
  "requestCount":null,
  "linkCountsComplete":false,
  "warning":"Locking prevents new use in this branch; child states and existing links are preserved."
}
```

ChildCount đếm con trực tiếp; descendantCount đếm mọi hậu duệ, không gồm node hiện tại. Các số là ảnh chụp tại thời điểm đọc, không khóa cấu trúc trong thời gian người dùng xác nhận. BR-KV-03-08 về số thiết bị/phiếu và BR-KV-05-04 về đếm thiết bị không trùng còn cần tích hợp khi có dữ liệu; không hiển thị null như 0.

## Audit và cạnh tranh

AREA_CREATED / AREA_UPDATED / AREA_REPARENTED / AREA_LOCKED / AREA_UNLOCKED được lưu nguyên tử cùng thay đổi cây. Vừa đổi thuộc tính vừa đổi cha chỉ có một AREA_REPARENTED, changedFields liệt kê tất cả trường đổi. Snapshot gồm ID/mã/tên/nhãn/mô tả, trạng thái riêng/hiệu lực, parentId, pathIds/pathNames và affectedDescendantCount. Không serialize toàn bộ entity/tài khoản. Thời gian UTC, actor và requestId lấy từ server.

AreaTransaction dùng READ COMMITTED, lấy advisory lock tài khoản rồi lock cây `(1129535043,1)` trước mọi đọc–kiểm tra–ghi; giá trị lock cây khớp trigger có sẵn trong migration. Kiểm tra lại tài khoản/phiên/quyền sau khi chờ lock. Chặn hai lần đổi cha đối nghịch tạo chu trình, hai lần tạo trùng tên và cuộc đua khóa cha/tạo con. SQL trực tiếp có trigger cũng phối hợp cùng lock cây. Các lần SaveChanges trung gian chỉ flush trong transaction; audit lỗi vẫn rollback cả cây.

Từ chối/lỗi nghiệp vụ ghi DENIED/FAILED riêng sau rollback, không có before/after/changedFields giả. Request DELETE ghi DELETE_DENIED. Validation POST/PUT thất bại cũng ghi FAILED trước automatic 400, không lưu nội dung không hợp lệ. Request chưa xác thực bị chặn 401 trước controller. Đọc không tạo lịch sử thay đổi. Đích không tồn tại có areaId=null để không vi phạm FK.

Lỗi lưu audit thành công trả 503 sau rollback; cố ghi FAILED ở transaction riêng. Nếu audit FAILED cũng không ghi được thì trả 503 và log vận hành an toàn, không tuyên bố đã có bản ghi audit.

History mặc định lọc đúng areaId. `includeDescendants=true` lấy sự kiện của các node thuộc **nhánh hiện tại**; node đã chuyển ra khỏi nhánh không còn được chọn. Đây không phải bộ lọc nhánh lịch sử tại một thời điểm. Bản thân snapshot đường dẫn cũ luôn bất biến. Sắp xếp occurredAt giảm dần, ID phụ; page/pageSize như danh mục. BeforeData/afterData/changedFields trả dưới dạng chuỗi JSON từ JSONB; dữ liệu thất bại có thể null. Chỉ Trưởng phòng đọc lịch sử, không có API sửa/xóa.

Mỗi item lịch sử trả sẵn `actorAccountId`, `actorAccountCode`, `actorFullName`, thống nhất với lịch sử tài khoản/xác thực/phòng ban. Frontend dùng trực tiếp `actorFullName` để hiển thị người thực hiện, không cần gọi thêm API tài khoản. Mã và họ tên lấy từ tài khoản/profile hiện tại; đổi tên người dùng không sửa snapshot trước/sau đã lưu. Sự kiện không xác định người thực hiện vẫn được trả về với các trường actor bằng null. Truy vấn nối trái dữ liệu người thực hiện trong PostgreSQL, không truy vấn riêng từng item; không thêm cột hay migration.

## Mã lỗi và kiểm thử

| HTTP | Code | Ý nghĩa |
| --- | --- | --- |
| 400 | VALIDATION_FAILED | Trường/query sai hợp đồng hoặc giới hạn |
| 400 | INVALID_AREA_PARENT | Guid.Empty thay vì cha hợp lệ/null |
| 401 | AUTHENTICATION_REQUIRED hoặc challenge | Phiên thiếu/không hợp lệ |
| 403 | AREA_ACCESS_FORBIDDEN | Không đủ quyền |
| 403 | AREA_DELETE_FORBIDDEN | Không cho xóa cứng |
| 404 | AREA_NOT_FOUND / AREA_PARENT_NOT_FOUND | Node/cha không tồn tại |
| 409 | AREA_NAME_CONFLICT | Tên trùng trong cùng cha đích |
| 409 | AREA_CYCLE | Tự trỏ hoặc chọn hậu duệ làm cha |
| 409 | AREA_LOCKED | Node nguồn đang khóa riêng |
| 409 | AREA_PARENT_UNAVAILABLE | Cha hoặc tổ tiên đích khóa |
| 409 | AREA_REFERENCE_CHANGED | Quan hệ thay đổi khi lưu |
| 503 | AREA_UNAVAILABLE | Không lưu được cây/audit |

Test tự động: `tests/CSVC.API.Tests/AreaApiTests.cs`. Tài liệu input/output được lưu bên ngoài backend theo cách tổ chức tài liệu testcase hiện tại. Các test dùng database PostgreSQL UTF-8 riêng có quyền tạo DB test, không thay đổi DB ứng dụng. Đặt CSVC_POSTGRES_TEST_CONNECTION để chạy test PostgreSqlFact; nếu không có, test tích hợp bị skip và không xác nhận ràng buộc DB. Chạy cả solution để kiểm tra hồi quy nhóm 1/2/3.
