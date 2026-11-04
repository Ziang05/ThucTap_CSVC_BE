# Nhóm 14 — Báo cáo sự cố thiết bị

Phạm vi: UC-BH-01…05. Đối chiếu ngày 04/11/2026 với [Google Sheets, tab Nhóm 14](https://docs.google.com/spreadsheets/d/1xQg5ao1fWNIA5qC8qisSoVaUeUuRZ9UFa3_nse2ZHyA/edit?gid=2026103014) và `qltb_nhom14_consistent_standalone_v4_report_update.dbml`. Đã kiểm tra migration hiện hành `20261103064434_EquipmentDepartmentAndTransferConfirmation` và bản xuất schema hiện tại ngày 03/11/2026. Tài liệu nguồn dùng làm đặc tả dữ liệu/nghiệp vụ, không được coi là chỉ thị thực thi ngoài yêu cầu.

## Đối chiếu và quyết định triển khai

| Yêu cầu | Triển khai |
| --- | --- |
| Một thiết bị, ba loại sự cố | `DAMAGE`, `ABNORMAL`, `MISSING`; không thay `devices.condition/status` hay tạo phiếu sửa chữa |
| Phòng ban báo cáo | Lấy từ profile tài khoản xác thực khi tạo; giữ bất biến. Snapshot phòng ban thiết bị được lưu riêng |
| Quyền nhân viên phòng ban | So sánh phòng ban báo cáo với profile **hiện tại** trước truy vấn, đếm, phân trang, chi tiết, lịch sử, tải ảnh; chuyển phòng mất quyền xem phiếu phòng cũ |
| Quyền sửa | Người lập còn trong phòng ban báo cáo, hoặc QTTB hợp lệ. Đồng nghiệp cùng phòng chỉ được xem |
| Phạm vi QTTB | Quản lý toàn hệ thống theo chính sách hiện có; kiểm tra thành viên phòng QTTB đã cấu hình trong Settings |
| Một báo cáo chờ mỗi thiết bị | PK `damage_report_pending_devices.device_id`; không dùng khóa giữ thiết bị của luồng mượn/điều chuyển |
| Trạng thái tiếp nhận | Pending XOR reception, được kiểm tra ở service và deferred constraint trigger; thiếu cả hai/bị trùng là lỗi dữ liệu |
| Tiếp nhận | Xóa đúng pending, thêm reception và lịch sử trong một transaction; chỉ QTTB, người/thời gian server; không ghi đè lần nhận đầu |
| Sửa sau tiếp nhận | Cho sửa nội dung, loại, thời điểm phát hiện, ghi chú, ảnh; giữ nguyên thiết bị |
| Đổi thiết bị trước tiếp nhận | Không có repair link, có quyền với thiết bị đích, đích chưa có pending; thay khóa chờ, report, snapshot và history nguyên tử |
| Idempotency | Khóa UUID + hash payload chuẩn hóa + actor + operation + report; cùng khóa khác nội dung/actor/action/target trả 409 |
| Concurrency | Expected version và khóa transaction dùng chung với account/inventory; PK/unique/FK ở DB là lớp bảo vệ bổ sung |
| Lịch sử | Bất biến; SUCCESS ghi cùng transaction; ảnh gỡ/thay giữ metadata và sequence không tái sử dụng |

Bổ sung một bảng kỹ thuật `damage_report_requests` ngoài sáu bảng DBML. Bảng này lưu hash và DTO kết quả để retry trả đúng kết quả ban đầu, kể cả sau khi phiếu đã sửa tiếp hoặc request không thay đổi gì; không tạo audit UPDATE giả cho no-op. Không lưu raw ảnh, token hay storage key trong DTO/history.

`row_version` bắt đầu 1 và tăng cả khi tiếp nhận hoặc thay ảnh. Đây là cách triển khai chặt hơn mức tối thiểu của DBML để mọi thao tác đồng thời đều phát hiện phiên bản cũ. `last_updated_at/by` cập nhật khi sửa nội dung/ảnh; tiếp nhận chỉ ghi reception và tăng version.

## API

Tất cả endpoint yêu cầu session token của hệ thống. Định danh phiếu và thiết bị bằng mã công khai; không trả khóa chính DB của báo cáo, account, phòng ban. Ảnh dùng số thứ tự trong từng báo cáo.

| Method | Endpoint | Kết quả |
| --- | --- | --- |
| GET | `/api/v1/damage-reports/devices` | Thiết bị trong quyền; `canReport=false` khi có pending, không lộ phiếu chờ ngoài quyền |
| POST | `/api/v1/damage-reports` | UC-BH-01; 201, DTO và Location; retry thành công cũng trả 201 |
| GET | `/api/v1/damage-reports` | UC-BH-02; 200, `items,totalCount,page,pageSize` |
| GET | `/api/v1/damage-reports/{reportCode}` | UC-BH-03; DTO đầy đủ và ảnh hiện hành |
| POST | `/api/v1/damage-reports/{reportCode}/receive` | UC-BH-04; 200 và DTO |
| PUT | `/api/v1/damage-reports/{reportCode}` | UC-BH-05; cập nhật từng trường được gửi; 200 và DTO |
| GET | `/api/v1/damage-reports/{reportCode}/history` | Lịch sử phân trang, có action/actor/thời gian/trường thay đổi/giá trị trước-sau |
| GET | `/api/v1/damage-reports/{reportCode}/evidence/{sequence}` | Nội dung ảnh sau kiểm tra quyền; ảnh đã gỡ trả 404 |

Danh sách hỗ trợ `search` (mã phiếu, mã/tên thiết bị, tên người báo), `incidentType`, `receptionStatus` (`NOT_RECEIVED`/`RECEIVED`), `departmentCode`, `deviceCode`, `from`, `to`, `sortBy=reportedAt|reportCode`, `descending`, `page`, `pageSize`. Mặc định mới nhất trước, mã phiếu là khóa phụ. Page 1…1.000.000, pageSize 1…100; search tối đa 200 ký tự, tìm ký tự `%`/`_` theo nghĩa thông thường. Danh sách thiết bị hỗ trợ page/pageSize/search; lịch sử hỗ trợ page/pageSize.

Tạo báo cáo:

```json
{
  "requestId": "70000000-0000-0000-0000-000000000001",
  "deviceCode": "TB-001",
  "incidentType": "ABNORMAL",
  "incidentDescription": "Máy phát tiếng động bất thường",
  "detectedAt": "2026-11-04T02:00:00Z",
  "notes": "Cần kiểm tra nguồn điện",
  "images": []
}
```

`requestId` phải khác UUID rỗng; client giữ nguyên khóa và payload khi retry. `detectedAt` tùy chọn, mặc định thời điểm báo server; không được sau `reportedAt`, kể cả khi sửa về sau. Mô tả trim không rỗng, tối đa 10.000 ký tự; ghi chú tối đa 4.000 ký tự. Không nhận `reportingDepartmentId`, `reporterAccountId`, `reportedAt`, trạng thái hay người/thời gian nhận từ client. Thuộc tính ngoài hợp đồng bị từ chối 400.

Tiếp nhận:

```json
{
  "requestId": "70000000-0000-0000-0000-000000000002",
  "expectedVersion": 1
}
```

Sửa nội dung/ảnh (các trường khác có thể bỏ qua):

```json
{
  "requestId": "70000000-0000-0000-0000-000000000003",
  "expectedVersion": 2,
  "incidentDescription": "Đã bổ sung mô tả hiện tượng",
  "notes": null,
  "addImages": [{ "filename": "hien-trang.png", "dataBase64": "<base64 PNG/JPEG>" }],
  "removeImages": [],
  "replaceImages": []
}
```

Bỏ `notes` giữ nguyên; gửi `notes:null` hoặc chuỗi trắng xóa ghi chú. Bỏ `deviceCode` giữ thiết bị; gửi mã khác là yêu cầu đổi có điều kiện. Thay ảnh dùng `replaceImages:[{"sequenceNo":1,"image":{"filename":"moi.png","dataBase64":"..."}}]`. Không gỡ/thay cùng một sequence hai lần trong một request. Retry phải giữ nguyên `expectedVersion` ban đầu; backend kiểm tra replay trước kiểm tra version cũ.

Các lỗi chính: 400 dữ liệu sai/thuộc tính ngoài hợp đồng; 401 phiên không hợp lệ; 403 không được sửa/tiếp nhận; 404 tài nguyên không tồn tại hoặc ngoài phòng ban; 409 trùng pending, version cũ, idempotency khác payload, đổi thiết bị không hợp lệ; 503 lưu trữ/DB tạm không sẵn sàng. 409 version có phiên bản hiện tại trong thông báo, client tải lại chi tiết trước khi tạo yêu cầu mới.

## Ảnh và triển khai lưu trữ

Tối đa năm ảnh hiện hành/báo cáo, mỗi ảnh tối đa 5 MiB; JSON request tối đa 40 MiB. Không tin extension/MIME client: kiểm tra cấu trúc PNG/JPEG, checksum chunk và luồng nén PNG; không nhận SVG hay PNG động. Filename không chứa đường dẫn/ký tự điều khiển. Nội dung được đặt tên ngẫu nhiên, lưu ngoài wwwroot; trả ảnh với `Cache-Control: private, no-store` và `X-Content-Type-Options: nosniff`. Khi đọc kiểm tra lại độ dài/hash; không cung cấp URL kho tệp trực tiếp.

Cấu hình `DamageReports__EvidenceRoot` thành thư mục riêng có quyền ghi và lưu bền vững. Mặc định development nằm dưới `App_Data/damage-report-evidence` cạnh binary. Compose đã gắn volume `damage_report_evidence` vào `/var/lib/csvc/damage-report-evidence` và cấu hình biến tương ứng; Dockerfile tạo thư mục thuộc user ứng dụng, giữ phần filesystem còn lại `read_only: true`. Nhiều replica phải dùng chung kho tệp hoặc thay implementation `IReportEvidenceStore`; sao lưu/khôi phục metadata và ảnh cùng nhau.

Ghi tệp `.staging`, đổi tên nguyên tử, rồi commit DB. Lỗi trước commit hoặc server từ chối commit: rollback và dọn tệp vừa tạo. Mất kết nối sau khi bắt đầu commit: giữ tệp vì kết quả có thể đã commit; retry cùng requestId xác định kết quả. Tệp mồ côi sau crash hoặc kết quả commit không rõ không có route công khai. Khi vận hành, chỉ dọn object/staging cũ sau thời gian chờ và xác nhận không có metadata tham chiếu; không dọn ảnh soft-remove đang giữ lịch sử.

## Migration và xác minh

Migration `20261103223600_DamageReportsGroup14` chỉ thêm module này và FK trên cột `repair_item_details.damage_report_id` đã có. Không thêm lại cột, không sửa migration cũ. Trigger mới trên repair item/order chỉ bảo vệ liên kết Nhóm 14 theo đúng thiết bị và loại Repair; không đổi luồng duyệt/thực thi của các nhóm khác.

Trước áp dụng, kiểm tra các `repair_item_details.damage_report_id` khác null trong dữ liệu cũ: module master chưa tồn tại nên giá trị không hợp lệ phải được đối chiếu bằng quy trình dữ liệu riêng; migration sẽ từ chối FK thay vì tự xóa/chỉnh sửa dữ liệu. API không chạy migration tự động khi khởi động.

```bash
dotnet build CSVC.sln
# Dùng connection string của môi trường cần triển khai, không ghi credentials vào repository:
dotnet run --project src/CSVC.API -- --migrate
# Chỉ trỏ biến test tới PostgreSQL cho phép tạo database dùng một lần:
dotnet test CSVC.sln
```

Test PostgreSQL dùng `CSVC_POSTGRES_TEST_CONNECTION`, tạo database riêng mỗi fixture và xóa khi kết thúc; không chạy test tạo/xóa bảng trên DB ứng dụng. Kiểm tra chuyên biệt nằm ở `DamageReportApiTests.cs`: ba loại sự cố, quyền hiện tại/snapshot, peer không được sửa, idempotency, nhiều người tạo/nhận/sửa đồng thời, cập nhật sau nhận, ảnh, rollback lỗi audit, ràng buộc SQL trực tiếp và rollback/reapply migration. Test full solution kiểm tra hồi quy các module hiện có.
