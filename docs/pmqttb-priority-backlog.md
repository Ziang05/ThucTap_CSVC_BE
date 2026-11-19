# Danh sách việc cần làm theo PMQTTB

Nguồn: `Documents/PMQTTB.docx`, [hợp đồng nghiệp vụ](pmqttb-business-contract.md), quyết định của chủ project và code backend/frontend hiện tại. Giữ số task để đối chiếu trao đổi cũ; thứ tự thực hiện mới ở cuối tài liệu.

`[x]` là phần đã có tài liệu/code và đã đối chiếu hoặc có kiểm thử liên quan; không đồng nghĩa đã triển khai lên database/API đang chạy hay nghiệm thu với khách hàng. `[ ]` là phần còn cần xử lý. Các ghi nhận Docker local cũ không xác nhận trạng thái triển khai phiên bản sau merge.

## Quyết định phạm vi giữ nguyên

- In/quét **mã vạch**, không dùng QR. Backend có barcode/lookup; UI đã in Code 128 theo mã đã lưu, còn nghiệm thu máy in/máy quét vật lý.
- Mỗi thiết bị/bộ có mã riêng, số lượng `0/1`; vật tư/dụng cụ có kho riêng, số lượng nhiều.
- Thu hẹp cha–con theo nghiệp vụ đích; truy vết thay thế/nâng cấp/tháo bỏ bằng can thiệp trước–sau và vật tư, không bắt tạo thiết bị con.
- Giữ mượn trả. Điều chuyển giữa các đơn vị giữ hành vi hiện tại cho đến khi thống nhất khác.
- Nhập về QTTB trước, cấp phát riêng; hoàn tất toàn bộ yêu cầu một lần; không áp dụng hủy sau phát sinh thực hiện.
- PMMS chưa có API: chỉ giả lập, gắn nhãn và **thông báo trước khi tạo/chạy giả lập**. Không xây quy trình hợp đồng/thanh toán của KHTC&ĐT.
- Phân công nhân viên/kiểm kê do đồng đội phụ trách; không tạo lại module hoặc áp điều kiện được giao task vào phần việc này.
- Giữ code đã pull làm nền; chỉ sửa/bỏ phần từ stash. Thay đổi nghiệp vụ nằm trong code đã pull phải thống nhất với team trước khi triển khai.

## Tổng quan sau rà soát

| Task | Tình trạng thực tế | Phần còn lại |
| --- | --- | --- |
| 0 — Đồng bộ sau merge | **CV-01 hoàn tất:** backend 336/336, UI 63/63, trình duyệt/API đạt; local đã nâng cấp | Tiếp tục nghiệm thu nghiệp vụ chi tiết ở các CV còn lại |
| 1 — Hợp đồng | Các quyết định cốt lõi đã chốt; đã cập nhật tài liệu trạng thái | Theo dõi điểm cần thống nhất ở task 2, 4, 9 |
| 2 — Thu hẹp cha–con | **CV-02 đã triển khai**: tham chiếu cũ chỉ đọc, thiết bị xử lý độc lập | Giữ dữ liệu/phiếu/lịch sử cũ; [quy tắc](device-reference-policy.md) |
| 3 — Nhập → QTTB → cấp phát | Có backend/UI, kiểm thử backend | Nghiệm thu lại sau merge; không suy ra đã triển khai local |
| 4 — PMMS giả lập | **CV-04 đã triển khai**: dịch vụ, nghiệm thu của đơn vị, ngoại lệ chuyên dụng | Chưa có API PMMS thật; [hướng dẫn](pmms-simulation.md) |
| 5 — Phân công | Ngoài phần việc này; module cũ đã gỡ/lưu trữ dữ liệu | Tích hợp khi đồng đội cung cấp module/hợp đồng |
| 6 — Kho vật tư | Có backend/UI, tồn, chứng từ, can thiệp và đối soát | Nghiệm thu sau merge; chốt danh mục trước dùng dữ liệu thật |
| 7 — Thanh lý | Chưa có module | Chứng từ, vòng đời, chặn nghiệp vụ, giữ lịch sử |
| 8 — Mã vạch | **CV-03 đã triển khai phần mềm**: Code 128, PDF, in lại và tra cứu | Còn nghiệm thu máy in/máy quét vật lý |
| 9 — Kiểm kê | Đã có module backend/UI từ bản pull | Đồng đội nghiệm thu, đối chiếu tình trạng/vị trí/quét |
| 10 — Báo cáo thiết bị toàn trường | Chưa có báo cáo theo kỳ | Không nhầm với báo cáo vật tư; phụ thuộc thanh lý |
| 11 — Lịch bảo trì | Có phiếu thủ công, chưa có lịch/chu kỳ/cảnh báo | Khai báo từ hợp đồng, nhắc hạn, tạo phiếu |
| 12 — Timeline | Có DEVICE/RECEIPT/LOAN/TRANSFER; can thiệp xem riêng | Hợp nhất sự kiện dịch vụ/vật tư và các module khác, tránh trùng |

## P0 — Đồng bộ phiên bản và sửa lệch nghiệp vụ

### 0. Đồng bộ sau merge và kiểm chứng trước demo

- [x] Ghép backend/UI từ stash, giữ kiểm kê, bộ sinh mã, quan hệ thiết bị, phần in tem và phân quyền của bản pull.
- [x] Backend 336/336, UI 63/63 test, build/EF model đạt; lint UI 0 lỗi/1 cảnh báo có sẵn. Khôi phục route/menu Mượn trả theo xác nhận, không sửa kỳ vọng test từ bản pull.
- [x] Backup, diễn tập restore/nâng cấp và áp sáu migration local theo xác nhận; API tại `http://localhost:8080` live/ready đạt, không còn migration nguồn chờ áp dụng.
- [x] Gỡ riêng hai guard của stash cũ bằng `ReconcileLegacyComponentGuards`, giữ tham chiếu/lịch sử và hành vi bản pull; sửa lỗi khởi tạo UUID ở UI vật tư từ stash.
- [x] Bảy nhóm tình huống trình duyệt/API đạt trên PostgreSQL riêng, không mock nghiệp vụ; giả lập PMMS đã thông báo và không chạy trên database chính. [Báo cáo và giới hạn CV-01](cv01-validation.md).

### 1. Hợp đồng nghiệp vụ và tài liệu

- [x] Có hợp đồng hai nhóm kho, định danh, vai trò, PMMS giả lập, nhập trước/cấp phát riêng, hoàn tất toàn bộ và chặn hủy sau thực hiện.
- [x] Giữ mượn trả; phân công/kiểm kê do đồng đội phụ trách; truy vết can thiệp áp dụng chung cho nhiều loại thiết bị.
- [x] Cập nhật trạng thái backlog/hợp đồng sau merge; bỏ tham chiếu tới tài liệu/migration thu hẹp cha–con không còn trong source.

### 2. Thu hẹp cha–con — CV-02 đã triển khai

- [x] Chủ project giao triển khai và tự quyết phương án trong phạm vi CV-02–CV-05.
- [x] API/DB chặn ghi liên kết mới hoặc sửa tham chiếu cũ; UI chỉ đọc tham chiếu lịch sử.
- [x] Chọn/khóa/mượn/điều chuyển/thu hồi độc lập, giữ đủ dòng phiếu đã lưu, định danh, giữ chỗ và lịch sử.
- [x] Kiểm chứng migration dữ liệu cũ, phân quyền và rollback. [Quy tắc](device-reference-policy.md), [biên bản](cv02-05-validation.md).

### 3. Nhập kho → QTTB giữ → cấp phát

- [x] Phiếu mới tách đơn vị đề xuất và QTTB tiếp nhận từ Settings; nhập về QTTB/InStorage. Phiếu phiên bản 1 giữ nghĩa cũ.
- [x] Cấp phát riêng chỉ đổi nơi giữ/InUse khi thực nhận; QTTB tự dùng có thao tác/người/vị trí/lịch sử riêng. Có kiểm thử quyền, rollback, migration, giữ metadata.
- [x] Có UI phòng nhận chỉ đọc, danh sách chọn hồ sơ chưa nhận và dùng tại QTTB; đã ghép với phần tìm kiếm có sẵn.
- [x] CV-05 kiểm chứng lại sau merge và CV-02: tiếp nhận, cấp phát, tự dùng, dữ liệu cũ và rollback. Hướng dẫn: [nhập và cấp phát](receipt-custody-release.md).

### 4. Yêu cầu mua sắm/sửa chữa từ PMMS giả lập

- [x] Có adapter `PMMS_SIMULATED`, API/UI Create/Update/Cancel; không tự seed, chưa kết nối PMMS thật.
- [x] Có định danh nguồn/sự kiện/phiên bản, chống trùng, lịch sử, đối soát cập nhật và chặn hủy sau thực hiện.
- [x] Liên kết từng dòng với nhập/cấp phát/sửa chữa/vật tư; kiểm tra đủ phạm vi trước đóng. Yêu cầu hỗn hợp bàn giao thiết bị/vật tư cùng giao dịch.
- [x] Lưu thông dụng/chuyên dụng, tham chiếu KHTC&ĐT/nhà cung cấp; UI đề xuất bên ngoài cho chuyên dụng. Tham chiếu danh mục theo giới hạn 64 ký tự của bản pull.
- [x] Dịch vụ độc lập có bằng chứng đủ mọi dòng, người/bên thực hiện, chứng từ, thời điểm và kết quả; ghi lặp/rollback được kiểm thử.
- [x] Đơn vị yêu cầu xác nhận toàn bộ kết quả sửa chữa mới và dịch vụ trước khi QTTB đóng; giữ luồng sửa chữa phiên bản 1 cho hồ sơ cũ.
- [x] Chuyên dụng mặc định bên ngoài; tại chỗ phải có lý do hiển thị cho quản lý duyệt. Không tự ép mọi trường hợp ra ngoài.
- [x] CV-04 kiểm chứng API/trình duyệt và dữ liệu cũ. Hướng dẫn: [PMMS giả lập](pmms-simulation.md).

### 8. In tem mã vạch — CV-03 đã triển khai phần mềm

- [x] Backend giữ barcode/lookup; UI in Code 128, bỏ QR và thư viện QR.
- [x] Tem 100 × 50 mm, in A4 nhiều tem; in lại không tạo định danh mới.
- [x] Kiểm chứng trình duyệt/PDF, giải mã độc lập, tra cứu Enter, quét lặp, mã không tồn tại và quyền đọc. [Hướng dẫn](barcode-labels.md).
- [ ] In và quét bằng máy thật của khách hàng; phối hợp nghiệm thu quét kiểm kê với đồng đội. Chưa có thiết bị vật lý để đánh dấu hoàn tất phần này.

## P1 — Truy vết vòng đời và hoàn thiện kho

### 6. Kho vật tư dụng cụ với số lượng nhiều

- [x] Có danh mục/đơn vị tính/kho/vị trí, tồn, nhập/xuất/điều chỉnh/chuyển, sổ biến động và báo cáo; thiết bị vẫn `quantity=0/1`.
- [x] Nhân viên ghi nhận trực tiếp; điều chỉnh cần quản lý duyệt. Có trả chưa dùng/thu hồi còn dùng được và đối chiếu nguồn; dụng cụ không bắt buộc trả.
- [x] Có nhiều can thiệp trước–sau, vật tư lắp/tháo, người/thời điểm và hiệu chỉnh giữ bản gốc; không bắt tạo thiết bị con.
- [x] Có kiểm tra tồn âm, chống lặp, đồng thời, rollback và đối soát vật tư trước kết quả dịch vụ. Kết quả tổng kết dùng lịch sử dịch vụ có sẵn, không tạo can thiệp trùng.
- [x] Có UI kho/chứng từ/tồn/báo cáo/can thiệp; đã ghép lại với bản pull.
- [ ] Nghiệm thu UI/API và chốt danh mục thực tế; không đưa giấy/mực tiêu hao thường ngày vào phạm vi khi chưa chốt. Hướng dẫn: [vật tư và can thiệp](material-inventory.md).

### 12. Hợp nhất nhật ký vòng đời — nâng từ P2 lên P1

- [x] Có timeline DEVICE/RECEIPT/LOAN/TRANSFER với lọc/phân trang/quyền đọc; can thiệp trước–sau xem ở module vật tư.
- [ ] Thêm báo hỏng, bảo trì/sửa chữa, xuất/nhận lại, can thiệp/vật tư, kiểm kê và thanh lý khi nguồn đã có. Dùng lịch sử nguồn, không ghi thêm bản sao sự kiện để dựng timeline.
- [ ] Từ hồ sơ xem được ai làm gì, khi nào, trước–sau, vật tư lắp/tháo và mã phiếu; giữ hiệu chỉnh.
- [ ] Phân biệt lịch sử và thông tin hiện tại đã xác nhận; không suy ra cấu hình hiện tại tùy tiện từ ghi chú gần nhất.

### 7. Thanh lý hoặc đưa thiết bị ra khỏi trường

- [ ] Chốt chứng từ/quy trình/thẩm quyền/bằng chứng thanh lý hoặc xuất vĩnh viễn; khác xuất tạm dịch vụ.
- [ ] Lưu thiết bị, quyết định, người/thời điểm/kết quả; chặn nghiệp vụ sử dụng mới, loại khỏi tồn hiện hữu, giữ hồ sơ/lịch sử.
- [ ] Kiểm thử tác động timeline/báo cáo; không dùng riêng `quantity=0` để biểu diễn thanh lý.

### 10. Báo cáo nhập–xuất–tồn thiết bị toàn trường

- [ ] Định nghĩa nhập vào trường/xuất vĩnh viễn/tồn đầu-cuối kỳ; cấp phát/thu hồi nội bộ không đổi tổng sở hữu, xuất tạm không phải thanh lý.
- [ ] Tổng hợp theo kỳ/nhóm/danh mục/đơn vị từ chứng từ/lịch sử, không chỉ đếm hồ sơ. Liên kết dữ liệu task 7.
- [ ] Đối chiếu chuỗi nhập → cấp phát → thu hồi → sửa bên ngoài → nhận lại → thanh lý.

Báo cáo vật tư đã có ở task 6 không thay báo cáo thiết bị toàn trường.

## P2 — Công việc định kỳ

### 11. Lịch bảo trì, bảo dưỡng và cảnh báo

- [ ] Khai báo lịch/chu kỳ từ hợp đồng khi tạo/nhập thiết bị, tách khỏi hạn bảo hành.
- [ ] Danh sách sắp đến hạn/quá hạn, cảnh báo, tạo phiếu từ lịch và cập nhật kỳ sau khi có kết quả.

Phiếu bảo trì thủ công hiện có chưa đáp ứng lịch và cảnh báo.

## Phần việc của đồng đội — không triển khai trùng

### 5. Phân công và báo cáo công việc

Ngoài phần việc này. Module cũ đã gỡ; migration lưu dữ liệu tại `retired_assignment_context`. Không dùng lại module cũ hoặc tự bắt buộc được giao việc. Khi tích hợp cần thống nhất định danh chứng từ/trạng thái/quyền với đồng đội.

### 9. Kiểm kê

Đã có backend/UI từ bản pull: kế hoạch/phiên bản, phân người kiểm kê, duyệt, thực hiện/hiệu chỉnh, tiến độ, biên bản, lịch sử và thông báo. Giữ module này, không coi là phải xây lại.

Phối hợp nghiệm thu: API kết quả hiện ghi `Exists`/`NewExists`; chưa thể coi là bao phủ cập nhật tình trạng/vị trí theo PMQTTB. Phân người trong **đợt kiểm kê** không đồng nghĩa đã có phân công mọi công việc ở task 5. Quét/in tem phải khớp quy tắc mã vạch.

## Thứ tự thực hiện đề xuất

1. CV-01 và phần mềm CV-02–CV-05 đã hoàn tất; kết quả/giới hạn theo [biên bản CV-02–CV-05](cv02-05-validation.md).
2. CV-03 còn nghiệm thu máy in/máy quét vật lý; thực hiện khi có thiết bị.
3. Tiếp tục CV-06: nghiệm thu kho vật tư/can thiệp và thống nhất danh mục thực tế.
4. CV-07: hợp nhất timeline; CV-08/CV-09: thanh lý và báo cáo thiết bị theo kỳ.
5. CV-10: lịch bảo trì/cảnh báo. CV-11/CV-12 chỉ phối hợp với người phụ trách phân công/kiểm kê.
