# Phân công xử lý yêu cầu PMMS

Yêu cầu mua sắm/sửa chữa được lập tại PMMS. PMQTTB kế thừa yêu cầu để theo dõi và giao nhân viên QTTB xử lý. Hiện nguồn PMMS vẫn là giả lập, chưa có API thật.

## Cấp phát từ kho hoặc theo dõi mua sắm thiết bị

Sau khi được phân công, nhân viên xử lý yêu cầu mua sắm thiết bị theo hai lựa chọn:

1. **Cấp phát từ kho:** hệ thống kiểm tra toàn bộ số lượng theo từng dòng. Các dòng cùng danh mục dùng chung số tồn, không tính một thiết bị nhiều lần. Chỉ tính thiết bị đã tiếp nhận, tình trạng tốt, đang lưu kho tại QTTB và không bị giữ chỗ. Chỉ mở tạo cấp phát khi đủ toàn bộ; chứng từ vẫn bắt buộc chọn đủ mọi dòng và backend kiểm tra lại thiết bị khi lưu.
2. **Theo dõi yêu cầu mua sắm:** dùng khi thiếu tồn. Bấm nút sẽ hỏi “Đưa phiếu yêu cầu mua sắm này vào danh sách theo dõi?”. Hủy hộp xác nhận không thay đổi dữ liệu; xác nhận mới lưu trạng thái `TrackingPurchase` (**Đang theo dõi**) và lịch sử. Không tạo phiếu nhập hoặc biến động kho ở bước này.

Việc mua hàng thực hiện bên ngoài PMQTTB. Khi hàng được giao tới, bấm **Lập phiếu nhập** trong yêu cầu đang theo dõi để mở cùng form đầy đủ của trang Phiếu nhập: nhà cung cấp, đơn vị vận chuyển, mã yêu cầu, ngày/đợt giao, hóa đơn, thiết bị và ghi chú. Phiếu nhập liên kết yêu cầu chỉ được tạo sau khi đã xác nhận theo dõi. Phiếu nhập hiện có tiếp tục được duyệt và hoàn tất theo luồng của trang Phiếu nhập.

Yêu cầu mua sắm thiết bị có sáu trạng thái hiển thị: **Mới tiếp nhận**, **Đang theo dõi**, **Chờ bàn giao**, **Chờ xác nhận thực nhận**, **Hoàn tất**, **Đã hủy**. Trạng thái được cập nhật từ chứng từ, không đổi thủ công.

- **Đủ tồn:** Mới tiếp nhận → lưu phiếu cấp phát đầy đủ → Chờ xác nhận thực nhận → đơn vị yêu cầu xác nhận thiết bị đã đến phòng → Hoàn tất.
- **Thiếu tồn:** Mới tiếp nhận → xác nhận theo dõi → Đang theo dõi → hoàn tất tiếp nhận hàng vào kho QTTB → Chờ bàn giao → lập phiếu cấp phát đầy đủ → Chờ xác nhận thực nhận → đơn vị xác nhận đã nhận đủ tại phòng → Hoàn tất.

Nút **Lập phiếu cấp phát** mở chính form đầy đủ của trang Điều chuyển, điền sẵn bên giao là QTTB, bên nhận là đơn vị đề xuất, lý do và ghi chú. Người dùng chọn người chịu trách nhiệm tại bên nhận và đủ thiết bị cho từng dòng; không được đổi hai đơn vị hoặc cấp phát một phần. Phiếu cấp phát từ yêu cầu giữ chỗ toàn bộ thiết bị ngay khi lưu, không thay đổi đơn vị đang giữ cho tới khi bên nhận xác nhận thực nhận. Luồng này không yêu cầu một bước đồng ý tiếp nhận trước khi thiết bị được giao.

Đơn vị yêu cầu dùng **Xác nhận thực nhận**, kiểm tra tình trạng từng thiết bị và đánh dấu đã nhận đủ tại phòng. Backend kiểm tra quyền đơn vị nhận và toàn bộ các dòng. Xác nhận đồng thời hoàn tất phiếu cấp phát và yêu cầu trong một giao dịch; không có bước QTTB bấm hoàn tất yêu cầu sau đó. Chứng từ sửa chữa/dịch vụ và việc bàn giao hỗn hợp thiết bị–vật tư tiếp tục dùng các kiểm tra riêng tương ứng.

API bổ sung: `GET /api/v1/procurement-requests/{code}/stock` trả khả năng cấp phát và số cần/có/thiếu cho từng dòng; `POST /api/v1/procurement-requests/{code}/track-purchase` nhận `requestId`, `version`, chỉ cho nhân viên đang được giao xác nhận. `POST /api/v1/procurement-requests/{code}/receive` dành cho đơn vị nhận, có `requestId`, `version`, `receivedInFull` và đầy đủ `items` (mã thiết bị, tình trạng, ghi chú).

Migration dữ liệu `20261127020000_PurchaseRequestLifecycle` chuyển các yêu cầu mua sắm thiết bị chưa đóng sang trạng thái đúng theo phiếu nhập/cấp phát thực tế. Không thêm cột, không đổi quyền giữ thiết bị và không mở lại các yêu cầu đã hoàn tất hoặc hủy. Lịch sử chuyển trạng thái được lưu lại.

## Cách giao nhân viên

1. Đăng nhập bằng tài khoản **Trưởng phòng QTTB** (`QTTB_MANAGER`).
2. Mở **Yêu cầu PMMS**, chọn **Phân công** trong cột **Người phụ trách**. Trang cuộn đến chi tiết yêu cầu.
3. Trong **Nhân viên phụ trách yêu cầu**, chọn nhân viên từ danh sách **Nhân viên QTTB**, rồi bấm **Lưu phân công**.
4. Khi đổi hoặc gỡ người phụ trách, nhập lý do. Yêu cầu đã kết thúc không đổi phân công.

Tài khoản Nhân viên QTTB chỉ thấy yêu cầu/task hiện được giao cho mình, không thấy việc chưa phân công hoặc giao người khác. Quy tắc này áp dụng cả danh sách, tìm kiếm, phân trang, chi tiết và lịch sử; mở trực tiếp mã yêu cầu không được giao trả về 404. Khi đổi hoặc gỡ phân công, người cũ không còn quyền xem task. Nhân viên không tự nhận việc; quản lý xem toàn bộ để giao việc. Đơn vị đề xuất vẫn xem yêu cầu thuộc đơn vị mình để xác nhận kết quả.

Danh sách chọn người chỉ có nhân viên QTTB thuộc phòng QTTB đã cấu hình và tài khoản không bị khóa. Nếu danh sách trống, quản lý kiểm tra tài khoản/phòng ban; nếu tải lỗi, dùng nút tải lại danh sách.

Nếu vừa cập nhật phần mềm, tải lại trang. Giao diện mới cần API và migration `SourceRequestAssignments`; chạy API cũ sẽ thiếu thông tin phân công.

Màn yêu cầu chỉ hiển thị thông tin nghiệp vụ chính. Danh sách ẩn mã nguồn và phiên bản PMMS; chi tiết ẩn mã nội bộ, mã dòng và thông tin giao việc kỹ thuật. Lịch sử, thông tin bổ sung và liên kết thiết bị/vật tư trong chứng từ được thu gọn để mở khi cần; các trường bổ sung trống không hiển thị.

## Phạm vi liên kết

- Yêu cầu mới mặc định chưa phân công. Một yêu cầu nguồn là một công việc, dùng chung định danh yêu cầu; gửi lại sự kiện không tạo công việc thứ hai. Không khôi phục bảng task cũ.
- Trưởng phòng giao/đổi/gỡ người phụ trách; thay đổi có phiên bản, định danh lệnh chống lặp và lịch sử trước–sau.
- Chỉ nhân viên hiện được giao thực hiện yêu cầu và các chứng từ liên quan: nhập, cấp phát, sử dụng tại QTTB, vật tư, sửa chữa, xuất/nhận lại và can thiệp. Đổi phân công không đổi người đã thực hiện trong lịch sử.
- Quyền duyệt của quản lý và quyền xác nhận thực nhận/nghiệm thu của đơn vị được giữ riêng.
- Nhập hàng về QTTB trước, sau đó cấp phát riêng; chỉ người được giao đóng yêu cầu khi đủ toàn bộ kết quả.
- **Lập phiếu nhập** sau khi xác nhận theo dõi mở ngay form Phiếu nhập dùng chung trong màn yêu cầu. Mã yêu cầu, đơn vị đề xuất và các dòng thiết bị được kế thừa; có thể chọn thiết bị đã khai báo hoặc thêm thiết bị mới, ghi vận chuyển/hóa đơn và in mã vạch sau khi lưu. Mỗi dòng giữ liên kết nguồn riêng, kể cả khi cùng loại thiết bị; số nhập không vượt số lượng yêu cầu. Lưu phiếu chưa làm tăng tồn, vẫn cần duyệt và hoàn tất tiếp nhận.
- Sửa chữa bắt đầu bằng **Bắt đầu xử lý sửa chữa** ngay trên yêu cầu nguồn. Thiết bị, nội dung và báo hỏng được kế thừa; tạo bản ghi thực hiện đang xử lý, không lập/duyệt thêm yêu cầu sửa chữa cục bộ. Giữ nguyên các phiếu và lịch sử cũ.
- Chuyên dụng là phân loại chuyên môn/phối hợp. Chọn tại chỗ hoặc đưa thiết bị ra ngoài theo thực tế; không ép chuyên dụng thành xuất ngoài hoặc bắt lý do ngoại lệ chỉ vì xử lý tại chỗ.
- Tổng hợp tại trang yêu cầu thống kê theo người phụ trách hiện tại: chưa kết thúc, đang chờ, hoàn tất, đã hủy. Nhóm đang chờ nằm trong nhóm chưa kết thúc.

Phân công cho nghiệp vụ nội bộ không gắn yêu cầu PMMS và báo cáo toàn bộ nghiệp vụ vẫn thuộc phần phối hợp module chung của team ở CV-11. Không tạo yêu cầu PMMS giả cho các nghiệp vụ này.

## API liên quan

| API | Quyền / tác dụng |
| --- | --- |
| `GET /api/v1/procurement-requests/assignees` | Quản lý: nhân viên đủ điều kiện |
| `POST /api/v1/procurement-requests/{code}/assignment` | Quản lý: `requestId`, `version`, `assigneeAccountCode`, `reason` |
| `GET /api/v1/procurement-requests?assignedToMe=true` | Việc giao cho tài khoản hiện tại |
| `GET /api/v1/procurement-requests?unassigned=true` | Yêu cầu chưa phân công |
| `POST /api/v1/procurement-requests/{code}/start-repair` | Nhân viên được giao: bắt đầu thực hiện các dòng thiết bị nguồn |
| `GET /api/v1/procurement-requests/workload` | Quản lý: tổng hợp yêu cầu theo người phụ trách |

Màn tác nghiệp `/requests` không tạo/cập nhật/hủy yêu cầu nguồn. Công cụ giả lập `/pmms-simulator` tách riêng và chỉ dành cho quản lý, được chặn quyền ở cả API; phải thông báo trước khi chạy giả lập.

## Kiểm chứng và triển khai local

- 40 test backend liên quan đạt: phân công/quyền, đổi người, nhận hàng, vật tư, yêu cầu nguồn, sửa chữa và báo hỏng; 74 test giao diện đạt. Build backend/frontend đạt, lint frontend không lỗi.
- Trình duyệt với API thật trong database test riêng xác nhận mở form nhập dùng chung từ yêu cầu, điền sẵn thông tin nguồn, nhập cả thiết bị đã khai báo và thiết bị mới, lưu đúng liên kết, in mã vạch và đóng form để quay lại yêu cầu.
- Trình duyệt/API đạt thao tác phân công từ danh sách và năm nhánh: mua từ tồn, mua phần thiếu, mua chuyên dụng, sửa thông dụng, sửa chuyên dụng tại chỗ; kiểm tra kết quả/nghiệm thu, đóng yêu cầu và báo cáo nhân viên.
- PMMS giả lập chỉ chạy trong database test riêng; không seed dữ liệu vào database local đang sử dụng.
- Đã sao lưu, restore/nâng cấp trên bản sao và áp migration local; dữ liệu cũ trong 81 bảng được giữ nguyên. API local đã khởi động lại và live/ready đạt.
- Kiểm tra API/trình duyệt bổ sung đạt: nhân viên chỉ thấy task được giao, không vượt giới hạn qua bộ lọc/mã yêu cầu/lịch sử, đổi/gỡ phân công thu hồi quyền xem. Bản sửa này không đổi schema hoặc tạo dữ liệu mẫu.
- Các kết quả trên nằm trong phạm vi yêu cầu PMMS; không thay cho nghiệm thu API PMMS thật hoặc module phân công chung của team.
