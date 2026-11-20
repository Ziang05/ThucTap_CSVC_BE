# Task 4 — Tiếp nhận yêu cầu PMMS giả lập

**Cập nhật triển khai:** đã có phân công trực tiếp trên yêu cầu PMMS, chặn thao tác theo người được giao và bắt đầu sửa chữa từ nguồn; API local đã cập nhật. Xem [hướng dẫn, phạm vi và kiểm chứng hiện tại](source-request-assignment.md). CV-11 vẫn còn phần module chung cho nghiệp vụ nội bộ; các ghi chú rà soát bên dưới mô tả khoảng thiếu trước bản cập nhật này.

**Đính chính nghiệp vụ:** yêu cầu mua sắm/sửa chữa được đơn vị lập trên PMMS. PMQTTB kế thừa để theo dõi, phân công và thực hiện, không lập lại yêu cầu hoặc một phiếu sửa chữa nguồn khác. Code/giao diện mô tả phía dưới còn dùng luồng cũ và chưa tích hợp task chung; cần sửa ở CV-04/CV-11. Không coi các bước hiện có là luồng đích đã nghiệm thu.

## Luồng đích theo PMQTTB

1. PMMS có yêu cầu mua/sửa mới → PMQTTB tiếp nhận thông tin nguồn, giữ định danh, phiên bản và lịch sử. Do chưa có API, chỉ mô phỏng việc PMMS phát sinh/gửi sự kiện trong công cụ kiểm thử riêng và thông báo trước khi chạy.
2. Tạo/liên kết task chưa phân công; quản lý QTTB giao nhân viên phụ trách qua module của đồng đội. Không cần lập một yêu cầu mới trong PMQTTB để sinh việc.
3. Người được giao xử lý theo nhánh: mua thông dụng đủ tồn/thiếu tồn; mua chuyên dụng; sửa thông dụng; theo dõi sửa chuyên dụng. Chi tiết vai trò tại [hợp đồng nghiệp vụ](pmqttb-business-contract.md).
4. Ghi nhận các chứng từ thực hiện cần thiết: nhập, cấp phát, vật tư, bàn giao/nhận lại, can thiệp và kết quả dịch vụ; liên kết yêu cầu/task gốc. Bản ghi kỹ thuật sửa chữa không trở thành yêu cầu sửa chữa mới.
5. Đủ kết quả toàn bộ phạm vi mới đóng việc/yêu cầu; nhân viên phụ trách và báo cáo toàn phòng đọc được chứng từ/kết quả. Nghiệp vụ nội bộ khác cũng phải có người phụ trách, nhưng không cần tạo yêu cầu PMMS giả.

## Hiện trạng đã có trong code — còn phải chỉnh luồng

Backend và frontend có module **Yêu cầu PMMS** tại `/requests`. Nguồn cố định là `PMMS_SIMULATED`; chưa có kết nối, tài khoản tích hợp hay API của phần mềm mua sắm thật. Chủ project đã được thông báo trước khi triển khai giả lập. Không tự sinh dữ liệu khi khởi động, không gửi dữ liệu ra PMMS.

Màn hình giả lập tạo sự kiện mua sắm hoặc sửa chữa/nâng cấp với đơn vị đề xuất, liên hệ, tham chiếu hợp đồng KHTC&ĐT, nhà cung cấp và các dòng hàng/công việc. Người dùng chọn đơn vị, danh mục, thiết bị từ danh sách. Các phiếu được tạo sau đó là chứng từ PMQTTB thực trong database đang sử dụng; nhãn giả lập không có nghĩa mọi thao tác kho đều bị vô hiệu hóa.

Mỗi dòng giữ mã dòng nguồn, mô tả, loại thiết bị/vật tư/dịch vụ, số lượng, đơn vị tính, danh mục, thiết bị cần sửa và báo hỏng tùy chọn. Code còn mặc định chuyên dụng xử lý bên ngoài; đây là điểm phải sửa vì tài liệu phân biệt vai trò/chuyên môn, không bắt mọi thiết bị chuyên dụng phải xuất khỏi trường. PMQTTB không xây quy trình đấu thầu, hợp đồng hoặc thanh toán.

## Các bước giao diện hiện có — chưa phải luồng đích

1. Mở **Yêu cầu PMMS → Tạo yêu cầu giả lập**, điền nội dung và gửi. Màn hình hiển thị kết quả tiếp nhận hoặc thông tin cần đối chiếu.
2. Yêu cầu mua sắm: chọn **Chọn từ tồn / cấp phát toàn bộ** để xem thiết bị sẵn sàng ở QTTB, có tìm kiếm/phân trang và lọc theo danh mục từng dòng. Nếu đủ tồn, lập một phiếu cấp phát đủ toàn bộ yêu cầu.
3. Nếu thiếu tồn, lập một phiếu nhập cho phần cần mua. Từng thiết bị mới có hồ sơ riêng và số lượng `0/1`. Duyệt, hoàn tất phiếu nhập trong màn hình phiếu nhập để đưa hàng về QTTB. Sau đó chọn đủ cả hàng mới và hàng tồn, lập cấp phát riêng cho toàn bộ yêu cầu. Tất cả thiết bị nhập theo yêu cầu phải nằm trong phiếu cấp phát của yêu cầu đó.
4. Nếu QTTB là đơn vị sử dụng cuối, chọn đủ thiết bị từ tồn và ghi nhận sử dụng toàn bộ trong một giao dịch, chọn người chịu trách nhiệm. Không tạo phiếu điều chuyển cùng phòng. Quy tắc sẵn sàng và trạng thái `InStorage` vẫn áp dụng.
5. Yêu cầu sửa chữa/nâng cấp: **Lập phiếu sửa chữa** tự mang theo thiết bị, mã dòng, mô tả và báo hỏng nguồn nếu có. Duyệt, ghi kết quả, xuất/nhận lại bên ngoài qua module sửa chữa hiện có.
6. Chỉ **Hoàn tất toàn bộ yêu cầu** khi đủ chứng từ cuối: đơn vị đã thực nhận cấp phát, QTTB đã đưa vào sử dụng, hoặc tất cả dòng sửa chữa có kết quả và không còn giữ chỗ/thiết bị chưa nhận về. Không có API hoàn thành từng dòng hay bàn giao từng phần theo yêu cầu.
Các bước trên hiện mới kiểm tra vai trò và liên kết chứng từ. Chưa có bước giao task/kiểm soát người được giao, đồng thời còn bước “Lập phiếu sửa chữa”; đây là thiếu/sai luồng phải chỉnh theo phần Luồng đích. Module phân công do đồng đội phụ trách, tích hợp module đó là bắt buộc của PMQTTB.

Trường hợp không thực hiện được, phải ghi lý do và kết thúc toàn bộ với kết quả `Failed`; không được còn chứng từ mở hoặc giữ chỗ sửa chữa. Kết quả sửa chữa có dòng thất bại được lưu `CompletedWithIssues`, không đổi thành hủy nguồn.

## Gửi lại, cập nhật và hủy

- `eventId` là UUID sự kiện; định danh yêu cầu là cặp `(source, externalCode)`. `sourceVersion` là số nguyên tăng dần. Mã được chuẩn hóa, có giới hạn độ dài/ký tự và mã dòng không trùng. Lịch sử cho xem lại nội dung nguồn tại từng lần tiếp nhận, gồm cả đề xuất thay đổi không được áp dụng.
- Gửi lại cùng sự kiện hoặc cùng phiên bản và nội dung chuẩn hóa trả kết quả đã lưu, không tạo yêu cầu/phiếu/lịch sử nguồn trùng. Dùng lại định danh với nội dung khác bị `409 PMMS_EVENT_CONFLICT`.
- Phiên bản chưa từng nhận nhưng thấp hơn phiên bản hiện tại được ghi lịch sử `IgnoredStale`, không ghi đè phạm vi đang có. Yêu cầu đã kết thúc không được mở lại bởi sự kiện mới (`IgnoredTerminal`).
- Đơn vị, danh mục, thiết bị sửa hoặc báo hỏng không đối chiếu được vẫn có hồ sơ `NeedsMapping` để xử lý, nhưng không thể tạo phiếu. Sửa tham chiếu bằng sự kiện cập nhật mới.
- Chưa có chứng từ đang mở/đã thực hiện: cập nhật được phạm vi, giữ toàn bộ snapshot nguồn trong lịch sử. Đã có chứng từ: lưu snapshot chờ xem xét, không thay đổi các dòng đang thực hiện hoặc chứng từ. Quản lý QTTB có thể ghi lý do tiếp tục phạm vi đã thống nhất. Nếu muốn áp dụng phạm vi mới trước thực hiện, hủy các phiếu dự thảo rồi gửi phiên bản cập nhật mới.
- Hủy được áp dụng khi chưa thực hiện và không còn phiếu đang mở. Có dự thảo thì hủy dự thảo, giải phóng giữ chỗ qua luồng thông thường trước. Đã nhập hàng, cấp phát/sử dụng hoặc bắt đầu sửa chữa thì ghi `CancelRejected`; QTTB xử lý kết thúc nghiệp vụ. Không nhận hủy rồi hoàn tác dữ liệu kho.
- Liên kết yêu cầu, dòng nguồn và phiếu được ghi cùng giao dịch tạo phiếu. Không cho sửa phạm vi phiếu đã liên kết; hủy dự thảo và tạo lại từ yêu cầu để giữ đúng đối chiếu. Quyền duyệt, thực hiện và xác nhận nhận hàng của phiếu vẫn được kiểm tra như trước.

## API

QTTB được đọc module; nhân viên đơn vị chỉ xem yêu cầu/lịch sử của chính đơn vị mình và xác nhận kết quả. Nhân viên QTTB ghi bằng chứng dịch vụ; quản lý duyệt phiếu, xem ngoại lệ chuyên dụng và xử lý kết thúc không thành công khi đơn vị từ chối. Không có endpoint tích hợp PMMS thật.

| API | Công dụng |
| --- | --- |
| `GET /api/v1/procurement-requests` | Tìm kiếm, lọc loại/trạng thái/cần xem xét và phân trang |
| `GET /api/v1/procurement-requests/{code}` | Phạm vi đã tiếp nhận, liên kết phiếu và trạng thái thực tế |
| `GET /api/v1/procurement-requests/{code}/history` | Lịch sử nguồn/thao tác, phân trang |
| `POST /api/v1/procurement-requests/simulated-events` | Gửi sự kiện `Create`, `Update`, `Cancel` giả lập |
| `POST /api/v1/procurement-requests/{code}/status` | Cập nhật tiến độ với `version` hiện tại |
| `POST /api/v1/procurement-requests/{code}/review-source-update` | Quản lý giữ phạm vi hiện tại, bắt buộc lý do và `version` |
| `POST /api/v1/procurement-requests/{code}/complete` | Đóng toàn bộ sau đối chiếu kết quả; `failureReason` nếu không thực hiện được |
| `POST /api/v1/procurement-requests/{code}/service-evidence` | Nhân viên QTTB ghi đủ mọi dòng dịch vụ, có `requestId`, `version`, bên thực hiện, chứng từ và kết quả |
| `POST /api/v1/procurement-requests/{code}/accept-results` | Nhân viên đơn vị yêu cầu xác nhận hoặc từ chối toàn bộ kết quả; từ chối bắt buộc lý do |
| `POST /api/v1/devices/put-into-use` | QTTB tự dùng toàn bộ thiết bị của một yêu cầu trong cùng giao dịch |

Ví dụ payload gửi giả lập, thay các mã tham chiếu bằng dữ liệu trong môi trường:

```json
{
  "eventId": "80634a35-4e40-4651-9245-449b73ca4f3b",
  "externalCode": "PMMS-DEMO-001",
  "sourceVersion": 1,
  "action": "Create",
  "request": {
    "requestType": "Purchase",
    "title": "Trang bị thiết bị cho đơn vị",
    "requestingDepartmentCode": "PB_DONVI",
    "sourceStatus": "Submitted",
    "createdAt": "2026-01-01T00:00:00Z",
    "updatedAt": "2026-01-01T00:00:00Z",
    "lines": [
      {
        "sourceLineCode": "L1",
        "kind": "Equipment",
        "description": "Máy tính làm việc",
        "quantity": 2,
        "unit": "Cái",
        "categoryCode": "CAT_MAYTINH",
        "specialization": "Common"
      }
    ]
  }
}
```

Phiếu nhập/cấp phát/sửa chữa nhận thêm `sourceRequestCode` (mã nội bộ `YC...`) và `items[].sourceLineCode`. Frontend tự truyền từ yêu cầu, không yêu cầu người dùng nhập lại. `requestCode` dạng chữ trên phiếu nhập cũ vẫn là thông tin tham chiếu, không thay cho liên kết có kiểm tra. Phiếu cũ không có nguồn tiếp tục dùng như trước.

## Ranh giới adapter và phần phụ thuộc

`IPmmsRequestSource` hiện được cài bởi `SimulatedPmmsSource` để chuẩn hóa/kiểm tra payload. `ProcurementRequestService` xử lý nhận lại, phiên bản, mapping và lịch sử; `ProcurementWorkflow` kiểm tra liên kết/kết quả chứng từ. Khi PMMS có API, bổ sung adapter và cơ chế xác thực/đồng bộ theo hợp đồng thật, kiểm thử mapping, namespace nguồn và định danh sự kiện trước khi thay đăng ký DI. Không đổi nguồn dữ liệu giả lập thành dữ liệu thật bằng cách đổi nhãn.

Task 6 đã bổ sung [chứng từ vật tư và truy vết can thiệp có cấu trúc](material-inventory.md). Dòng vật tư có nhập/xuất riêng, liên kết mã dòng nguồn, kiểm tra đơn vị và số lượng; không biểu diễn bằng thiết bị. Yêu cầu vật tư hoàn tất sau khi đơn vị xác nhận nhận đủ. Với yêu cầu có cả thiết bị và vật tư, xác nhận thực nhận cả hai trong cùng giao dịch tại phiếu xuất vật tư; chặn bàn giao riêng từng nhóm. Dịch vụ độc lập dùng bằng chứng và bước xác nhận dưới đây. Task 12 hợp nhất timeline toàn vòng đời. Không tạo thiết bị con cho vật tư thay thế.

## Bằng chứng dịch vụ và xác nhận kết quả

- Mỗi lần ghi phải bao phủ đúng mọi dòng dịch vụ với toàn bộ số lượng nguồn: `sourceLineCode`, `outcome=Completed|Failed|NotExecuted`, `provider`, `evidenceReference`, `description`. `performedAt` có thể bỏ trống để lấy thời điểm hiện tại; nếu cung cấp phải nằm giữa lúc PMQTTB tiếp nhận và hiện tại. Không ghi nhận hoàn tất một phần.
- Sau khi đủ chứng từ, kết quả sửa chữa và bàn giao, nhân viên thuộc đơn vị đề xuất xác nhận toàn bộ kết quả. Với yêu cầu của chính QTTB, nhân viên QTTB của phòng đó được xác nhận. Quản lý không xác nhận thay đơn vị sử dụng. Bước này áp dụng cho yêu cầu có dịch vụ và yêu cầu sửa chữa mới (`workflowVersion=2`); yêu cầu sửa chữa cũ giữ phiên bản 1, không tự bổ sung nghĩa vụ nghiệm thu vào hồ sơ đang thực hiện.
- Xác nhận gắn với phạm vi/kết quả cụ thể, có tài khoản và thời điểm. Không đóng yêu cầu đã thực hiện bằng cách ghi lý do thất bại để bỏ qua nghiệm thu. Kết quả dịch vụ không đạt vẫn cho xác nhận đã xem xét; khi đóng trả `CompletedWithIssues`, không biến thành thành công.
- Đơn vị từ chối phải ghi lý do. QTTB có thể ghi lại bằng chứng dịch vụ, giữ bản cũ trong lịch sử và yêu cầu xác nhận lại; bản đã được chấp nhận không được ghi đè. Chỉ quản lý được kết thúc kết quả bị từ chối thành `Failed` kèm lý do. Kết quả phiếu sửa chữa đã kết thúc không ghi đè; phát sinh xử lý tiếp theo dùng yêu cầu mới.
- `requestId` UUID chống ghi lặp cho bằng chứng/xác nhận; gửi lại cùng nội dung và tài khoản không thêm sự kiện. Dùng lại cho nội dung/tài khoản khác bị từ chối. Ghi kết quả, phiên bản và sự kiện cùng giao dịch; lỗi audit rollback toàn bộ.
- Có bằng chứng thực hiện thì hủy nguồn bị từ chối. Cập nhật phạm vi được lưu chờ quản lý xem xét, không đổi nội dung đã làm.

Hiện code còn dùng `specializedHandlingReason` khi chọn chuyên dụng/tại chỗ và có vòng duyệt phiếu sửa chữa cục bộ. Phần này cần rà lại cùng CV-04: chuyên dụng là QTTB theo dõi/phối hợp với bên có chuyên môn, có thể làm tại chỗ hoặc bên ngoài; không suy ra thủ tục ngoại lệ chỉ từ địa điểm. Phê duyệt nghiệp vụ phát sinh cần phân biệt với việc lập/duyệt lại yêu cầu sửa chữa nguồn.

## Triển khai

Migration `SimulatedPmmsRequests` chỉ bổ sung bốn bảng `procurement_requests`, `procurement_request_lines`, `procurement_request_events`, `procurement_document_links` và các ràng buộc. Không biến phiếu cũ thành yêu cầu giả lập. Cần triển khai migration task 3 (`ReceiptEquipmentCustody`) trước theo thứ tự EF.

Migration mới `ProcurementFulfillmentAcceptance` bổ sung dữ liệu bằng chứng, xác nhận, định danh lệnh và lý do chuyên dụng. Giữ nguyên yêu cầu/phiếu/lịch sử cũ. Trạng thái môi trường và kết quả nâng cấp hiện tại theo [biên bản CV-02–CV-05](cv02-05-validation.md). Không tự seed dữ liệu giả lập vào database đang sử dụng.

## Kiểm tra

Các test `ProcurementRequestTests`, `ProcurementFulfillmentTests` và kiểm thử vật tư bao phủ phiên bản/sự kiện/đồng thời, snapshot, quyền đơn vị, tiếp nhận/cấp phát đủ, dịch vụ, sửa chữa cũ/mới, ngoại lệ chuyên dụng, gửi lặp và rollback. Kết quả tổng và trình duyệt ở [biên bản CV-02–CV-05](cv02-05-validation.md).
- 62 kiểm thử hợp đồng frontend đạt, gồm nhãn giả lập, quyền truy cập, payload tự liên kết dòng nguồn, lựa chọn từ tồn đủ số lượng, sửa chữa kế thừa thiết bị/báo hỏng, lịch sử nguồn và mở chi tiết chứng từ từ liên kết. Build đạt; lint không có lỗi, còn một cảnh báo Fast Refresh có sẵn ở màn hình danh mục.


## Trạng thái Docker local

Migration và image API local đã được cập nhật; xem [biên bản đồng bộ và kiểm tra](local-runtime-sync.md). Ghi chú chưa triển khai ở phần trước mô tả thời điểm hoàn thành mã nguồn; trạng thái môi trường hiện tại theo mục này.
