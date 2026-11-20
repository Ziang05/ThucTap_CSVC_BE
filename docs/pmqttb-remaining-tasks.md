# PMQTTB — Danh sách công việc còn phải làm

**Cập nhật triển khai:** đã có phân công trực tiếp trên yêu cầu PMMS, chặn thao tác theo người được giao và bắt đầu sửa chữa từ nguồn; API local đã cập nhật. Xem [hướng dẫn, phạm vi và kiểm chứng hiện tại](source-request-assignment.md). CV-11 vẫn còn phần module chung cho nghiệp vụ nội bộ; các ghi chú rà soát bên dưới mô tả khoảng thiếu trước bản cập nhật này.

Tổng hợp từ [backlog sau rà soát](pmqttb-priority-backlog.md) và [hợp đồng nghiệp vụ](pmqttb-business-contract.md). Danh sách bắt đầu từ các việc còn lại: sửa lệch, bổ sung, tích hợp và nghiệm thu; mục đã hoàn tất được giữ để theo dõi kết quả, không yêu cầu xây lại phần đã có.

Dùng mã **CV-01 → CV-12** cho danh sách mới. Cột “Task cũ” dùng đối chiếu các trao đổi trước. Tất cả checkbox ban đầu để trống; chỉ đánh dấu khi có kết quả kiểm chứng tương ứng. Không suy ra đã triển khai từ việc có code hoặc build thành công.

**Đính chính sau khi đọc lại PMQTTB:** yêu cầu mua sắm/sửa chữa được lập tại PMMS, PMQTTB kế thừa để theo dõi và phân task. Phân công là luồng chính của toàn phần mềm dù đồng đội phụ trách module. CV-04 chưa đạt nghiệp vụ đích; CV-11 nâng lên P0 và cần xử lý cùng CV-04 trước khi nghiệm thu tiếp các nhánh. Các báo cáo test trước đây giữ làm bằng chứng kỹ thuật của mô hình đã chạy, không khẳng định mô hình đó đúng toàn bộ yêu cầu nguồn.

## Quy tắc áp dụng cho mọi công việc

- Giữ nguyên các module ngoài phạm vi. Chủ project đã giao triển khai CV-02–CV-05 và tự quyết phương án, bao gồm thay đổi hành vi liên quan của code đã pull; phân công và kiểm kê của đồng đội vẫn giữ nguyên.
- Giữ hai file `pmqttb-business-contract.md` và `pmqttb-priority-backlog.md`, dữ liệu và lịch sử hiện hữu. Không xóa/gộp thiết bị cũ để giải quyết khác biệt mô hình.
- Dùng mã vạch, không QR; thiết bị có mã riêng và số lượng `0/1`, vật tư có số lượng nhiều.
- Nhập về QTTB trước rồi cấp phát riêng; yêu cầu chỉ hoàn tất toàn bộ một lần, không nhận hủy sau phát sinh thực hiện.
- PMMS chỉ giả lập; **thông báo cho chủ project trước khi tạo/chạy giả lập**. Không tự seed vào database đang sử dụng hoặc mô tả là kết nối API thật.
- Giữ mượn trả. Phân công/kiểm kê do đồng đội phụ trách triển khai nhưng là nghiệp vụ PMQTTB; phải tích hợp trách nhiệm nhân viên, không loại bỏ phân công khỏi luồng hoặc khôi phục hệ thống task cũ song song.
- Trên các form cần chọn hồ sơ đã có, ưu tiên danh sách tìm/chọn thay vì bắt nhập mã thủ công.

## Thứ tự ưu tiên

| Mã | Công việc | Ưu tiên | Task cũ | Phụ thuộc / phối hợp |
| --- | --- | --- | --- | --- |
| CV-01 | **Hoàn tất:** kiểm chứng và đồng bộ sau merge | P0 | 0 | [Báo cáo CV-01](cv01-validation.md) |
| CV-02 | Đã triển khai: thu hẹp quan hệ thiết bị cha–con | P0 | 2 | [Biên bản CV-02–CV-05](cv02-05-validation.md) |
| CV-03 | Đã triển khai phần mềm: in/quét mã vạch | P0 | 8 | Còn nghiệm thu máy in/máy quét vật lý |
| CV-04 | Cần sửa: kế thừa yêu cầu PMMS → task → thực hiện | P0 | 4 | Làm cùng CV-11; không tạo lại yêu cầu mua/sửa trong PMQTTB |
| CV-05 | Đã kiểm chứng: nhập → QTTB → cấp phát | P0 | 3 | Kiểm tra lại sau thay đổi chọn thiết bị ở CV-02 |
| CV-06 | Hoàn thiện nghiệm thu kho vật tư/can thiệp | P1 | 6 | CV-01; chốt danh mục sử dụng thực tế |
| CV-07 | Hợp nhất timeline vòng đời thiết bị | P1 | 12 | Dùng lịch sử sẵn có; mở rộng khi nguồn mới hoàn tất |
| CV-08 | Bổ sung thanh lý/xuất vĩnh viễn | P1 | 7 | Chốt quy trình và thẩm quyền |
| CV-09 | Báo cáo thiết bị toàn trường theo kỳ | P1 | 10 | CV-08 cho phần xuất vĩnh viễn |
| CV-10 | Lịch bảo trì/bảo dưỡng và cảnh báo | P2 | 11 | Chốt cách tính lịch và đối tượng nhận cảnh báo |
| CV-11 | Tích hợp phân công và báo cáo nhân viên vào luồng chính | P0 | 5 | Module do đồng đội phụ trách; là phụ thuộc của CV-04 và các nghiệp vụ |
| CV-12 | Phối hợp nghiệm thu kiểm kê | Theo team | 9 | Module đã pull; phối hợp CV-03 về mã vạch |

Thứ tự hiện tại: thống nhất/tích hợp hợp đồng CV-11 → sửa luồng nguồn và xử lý các nhánh CV-04 → nghiệm thu lại CV-05/CV-06 với người phụ trách → các phần vòng đời tiếp theo. CV-12 phối hợp module kiểm kê đang có; CV-07 có thể làm các nguồn đã sẵn sàng.

## P0 — Đồng bộ và sửa lệch nghiệp vụ

### CV-01. Kiểm chứng phiên bản sau merge

**Đã hoàn tất:** backend 336/336 test, UI 63/63 test; build đạt, lint UI 0 lỗi/1 cảnh báo có sẵn. Bảy nhóm tình huống trình duyệt/API đạt; local đã nâng cấp và API chạy tại `http://localhost:8080`. Phạm vi và giới hạn tại [báo cáo CV-01](cv01-validation.md).

- [x] Khôi phục riêng route/menu Mượn trả theo xác nhận, giữ nguyên test đã pull; hai ca lỗi trước đây đã đạt.
- [x] Đối chiếu database/image; áp sáu migration và khởi động API theo xác nhận, live/ready đạt, không còn migration nguồn chờ áp dụng.
- [x] Backup, diễn tập restore/nâng cấp và kiểm tra lưu trữ dữ liệu cũ; gỡ riêng hai guard của stash không còn phù hợp bản pull.
- [x] Kiểm chứng luồng chính bằng API/trình duyệt, gồm PMMS giả lập trên database riêng, vật tư/can thiệp, mượn trả, kiểm kê; sửa lỗi UUID của UI vật tư từ stash.
- [x] Ghi kết quả, giới hạn, bản sao và trạng thái triển khai tại báo cáo CV-01; nghiệm thu chi tiết/khách hàng ở các CV sau vẫn giữ riêng.

**Đạt khi:** biết chính xác phiên bản đang chạy, các luồng đã kiểm chứng và lỗi còn lại; không lẫn lỗi có sẵn với lỗi phần mới.

### CV-02. Thu hẹp quan hệ cha–con — đã triển khai

Phạm vi CV-02–CV-05 được chủ project giao triển khai và tự quyết phương án. Tham chiếu cũ giữ chỉ đọc; mỗi thiết bị/bộ vận hành độc lập. Xem [quy tắc dữ liệu cũ](device-reference-policy.md) và [kiểm chứng](cv02-05-validation.md).

- [x] Giữ hồ sơ, định danh, tham chiếu, phiếu và lịch sử cũ; không gộp/xóa/chuyển thiết bị thành vật tư.
- [x] API từ chối gắn/đổi cha, DB chặn ghi liên kết mới/sửa liên kết cũ, UI chỉ tra cứu tham chiếu.
- [x] Nhập mới không tạo liên kết; sửa phiếu cũ giữ tham chiếu đã lưu; hoàn tất phiếu không gắn lại quan hệ.
- [x] Chọn/khóa/mượn/điều chuyển/thu hồi từng thiết bị, không mở rộng cây hoặc tự chặn vì trạng thái thiết bị liên quan.
- [x] Phiếu nhiều dòng vẫn xử lý đủ; kiểm thử giữ chỗ, quyền, migration dữ liệu cũ và rollback.

### CV-03. In và quét mã vạch — đã triển khai phần mềm

- [x] Thay QR bằng Code 128, bỏ thư viện QR; giữ API lookup và định danh có sẵn.
- [x] Mẫu tem 100 × 50 mm, mã/tên thiết bị và chuỗi barcode; in nhiều tem trên A4 hai cột, in lại không đổi mã.
- [x] Kiểm tra trình duyệt/PDF, giải mã độc lập, tra cứu Enter, mã không tồn tại, quét lặp và quyền đọc theo đơn vị.
- [x] Giữ nguyên module kiểm kê của đồng đội; không xây thêm quy trình quét kiểm kê.
- [ ] Nghiệm thu máy in/máy quét vật lý của khách hàng. Chưa có thiết bị phần cứng trong môi trường làm việc; không coi kiểm thử phần mềm là đã nghiệm thu phần cứng.

Hướng dẫn: [in và quét mã vạch](barcode-labels.md). Kết quả kỹ thuật tại [biên bản](cv02-05-validation.md).

### CV-04. Sửa luồng kế thừa PMMS và xử lý theo task — chưa hoàn tất

- [ ] Màn tác nghiệp chỉ tiếp nhận/theo dõi yêu cầu mua/sửa do PMMS lập; tách tạo/cập nhật/hủy nguồn giả lập thành công cụ kiểm thử nguồn, không coi QTTB là bên lập yêu cầu.
- [ ] Liên kết yêu cầu nguồn với task chưa phân công, quản lý giao nhân viên qua module CV-11; gửi lặp không sinh thêm việc.
- [ ] Bỏ bước bắt lập lại phiếu/yêu cầu sửa chữa cục bộ và vòng duyệt yêu cầu trùng. Tận dụng bản ghi kỹ thuật/chứng từ có sẵn để ghi thực hiện dưới đúng yêu cầu/task; giữ dữ liệu và lịch sử cũ.
- [ ] Nghiệm thu mua thông dụng đủ tồn → cấp phát; thiếu tồn → phối hợp KHTC&ĐT → nhận về QTTB → cấp phát riêng; mua chuyên dụng → KHTC&ĐT/đơn vị làm việc nhà cung cấp, QTTB ghi nhận/bàn giao.
- [ ] Nghiệm thu sửa thông dụng → QTTB xử lý và dùng vật tư nếu cần; sửa chuyên dụng → QTTB theo dõi phối hợp, ghi kết quả bên thực hiện, xuất/nhận lại chỉ khi thực tế có di chuyển.
- [ ] Tách chuyên môn/đơn vị thực hiện khỏi nơi xử lý. Rà bỏ mặc định ép chuyên dụng ra ngoài hoặc coi tại chỗ là ngoại lệ chuyên môn của QTTB.
- [ ] Chỉ nhân viên được giao thực hiện; quản lý duyệt theo quyền; đóng task/yêu cầu khi đủ toàn bộ kết quả. Đồng bộ kết quả cho báo cáo nhân viên/phòng QTTB.

Các thành phần kỹ thuật đang có dưới đây được giữ để tái sử dụng; chưa đại diện cho việc đã đáp ứng luồng nguồn/task:

- [x] Nhân viên QTTB ghi đủ kết quả dịch vụ, bên thực hiện, chứng từ/bằng chứng và thời điểm; không hoàn thành từng phần.
- [x] Nhân viên đơn vị đề xuất đọc yêu cầu/lịch sử của đơn vị mình và xác nhận/từ chối toàn bộ kết quả. QTTB không xác nhận thay đơn vị khác.
- [x] Yêu cầu sửa chữa mới cần nghiệm thu riêng trước đóng; yêu cầu sửa chữa cũ giữ luồng phiên bản 1.
- [ ] Sửa quy tắc chuyên dụng hiện có: vai trò QTTB là theo dõi/phối hợp; không suy ra bắt buộc `External` hoặc bắt lý do chỉ vì `OnSite`.
- [x] Yêu cầu thiết bị/vật tư/dịch vụ/hỗn hợp chỉ đóng khi đủ kết quả, thực nhận và giải phóng giữ chỗ. Dịch vụ không đạt không bị tính thành công.
- [x] Từ chối có lý do; sửa bằng chứng giữ bản cũ và yêu cầu nghiệm thu lại; bản được chấp nhận không ghi đè. Quản lý có thể kết thúc kết quả bị từ chối thành thất bại kèm lý do.
- [x] Giữ kiểm tra phiên bản/sự kiện nguồn, hủy/cập nhật sau thực hiện, chống ghi lặp theo lệnh/tài khoản và rollback khi lỗi audit.
- [x] Chỉ chạy PMMS giả lập trên database kiểm thử riêng và đã thông báo; không seed vào database local đang sử dụng.

Hướng dẫn/API: [PMMS giả lập](pmms-simulation.md); kết quả tại [biên bản](cv02-05-validation.md).

### CV-05. Nhập → QTTB → cấp phát — đã kiểm chứng sau thay đổi

Các kiểm tra dưới đây xác nhận xử lý kho/cấp phát. Vẫn cần nghiệm thu lại liên kết nguồn và người được giao sau CV-04/CV-11; không tạo lại yêu cầu nguồn để sử dụng các chứng từ này.

- [x] Duyệt chưa làm tăng số lượng/đổi trách nhiệm; hoàn tất nhập đưa thiết bị về QTTB/InStorage.
- [x] Cấp phát riêng chỉ đổi đơn vị giữ/InUse khi thực nhận; dùng thiết bị từ tồn không tạo lần nhập mới.
- [x] QTTB tự dùng ghi người/vị trí/lịch sử riêng; không tạo phiếu nguồn và đích cùng phòng.
- [x] Kiểm tra phiếu phiên bản 1, chọn hồ sơ chưa nhận, giữ metadata/định danh, quyền/cấu hình và rollback.
- [x] Chạy lại các ca tiếp nhận/cấp phát sau CV-02; xác nhận không mở rộng cây, phiếu nhiều dòng xử lý đủ.

Hướng dẫn: [nhập và cấp phát](receipt-custody-release.md); kết quả backend/trình duyệt tại [biên bản](cv02-05-validation.md).


## P1 — Kho và vòng đời thiết bị

### CV-06. Nghiệm thu kho vật tư và truy vết can thiệp

**Đã có:** danh mục loại vật tư/dụng cụ, đơn vị tính, tồn tại QTTB, nhập–xuất–tồn, trả/thu hồi và can thiệp trước–sau. Luồng hiện tại không yêu cầu tạo kho hoặc chia kệ/ngăn/vị trí.

- [x] Danh mục loại vật tư/đơn vị tính dùng chung cho nhập/xuất; nhân viên QTTB được thêm mới, có tạo nhanh khi nhập. Không quản lý trạng thái trên giao diện hai danh mục, dropdown chỉ hiển thị tên và không kèm textbox tìm kiếm.

- [ ] Chốt danh mục dùng thật: vật tư/dụng cụ sửa chữa, thay thế; ranh giới với giấy/mực tiêu hao sử dụng thường ngày đang ngoài phạm vi.
- [ ] Nghiệm thu toàn bộ UI/API nhập, xuất, điều chỉnh, trả chưa dùng và nhập thu hồi; chỉ điều chỉnh cần quản lý duyệt. Luồng chọn/tạo nhanh loại và ghi nhận tồn QTTB đã được kiểm tra riêng, kết quả tại [hướng dẫn vật tư](material-inventory.md).
- [ ] Kiểm tra ví dụ tồn 20, xuất 3, còn 17; thiết bị vẫn có số lượng `0/1`. Đối chiếu báo cáo và sổ biến động.
- [ ] Kiểm tra nhiều can thiệp trên nhiều loại thiết bị, cả trường hợp không dùng vật tư; đúng người/thời điểm, trước–sau, nguồn lắp và xử lý phần tháo.
- [ ] Kiểm tra đối soát số dùng/trả trước kết quả sửa chữa, hiệu chỉnh giữ bản gốc, giới hạn thu hồi, tồn âm/đồng thời/gửi lặp/rollback.
- [ ] Xác nhận kết quả tổng kết dùng lịch sử dịch vụ hiện có, không tạo thêm can thiệp tổng kết trùng; cập nhật [hướng dẫn vật tư](material-inventory.md).

**Đạt khi:** đối chiếu được tồn và vật tư sử dụng, lần yêu cầu sau tra được thiết bị đã từng can thiệp gì.

### CV-07. Hợp nhất timeline vòng đời thiết bị

- [ ] Bổ sung báo hỏng, bảo trì/sửa chữa, xuất/nhận lại và can thiệp/vật tư vào timeline đang có DEVICE/RECEIPT/LOAN/TRANSFER.
- [ ] Phối hợp đọc kết quả kiểm kê; bổ sung thanh lý sau CV-08. Dùng lịch sử nguồn, không ghi thêm bản sao sự kiện để dựng timeline.
- [ ] Hiển thị thời gian, loại sự kiện, mã chứng từ, người thực hiện, kết quả, trước–sau và vật tư liên quan; mở được chứng từ nguồn.
- [ ] Giữ phân trang/lọc/phân quyền; tránh nhân đôi sự kiện khi một thao tác ghi nhiều bảng lịch sử.
- [ ] Chốt cách thể hiện thông tin hiện tại đã xác nhận, khác với lịch sử hiệu chỉnh; không suy ra cấu hình thiết bị tùy tiện từ ghi chú gần nhất.

**Đạt khi:** tra một mã thiết bị xem được chuỗi can thiệp và vòng đời trên cùng một luồng lịch sử.

### CV-08. Thanh lý hoặc xuất vĩnh viễn khỏi trường

- [ ] Chốt hình thức chứng từ, trạng thái, thẩm quyền duyệt và bằng chứng/quyết định; phân biệt xuất vĩnh viễn với xuất tạm sửa chữa.
- [ ] Bổ sung quy trình, API/UI ghi thiết bị, người/thời điểm, lý do và kết quả; xử lý thiết bị còn phiếu/giữ chỗ theo quy tắc được chốt.
- [ ] Loại khỏi tồn hiện hữu và chặn cấp phát/mượn/sửa chữa mới sau kết thúc vòng đời, vẫn giữ mã, hồ sơ và lịch sử.
- [ ] Không dùng riêng `quantity=0` để biểu diễn thanh lý; kiểm tra tác động đến timeline và báo cáo.

**Đạt khi:** thiết bị đã ra khỏi trường vĩnh viễn không còn được sử dụng trong nghiệp vụ mới nhưng vẫn tra cứu đầy đủ.

### CV-09. Báo cáo nhập–xuất–tồn thiết bị toàn trường

- [ ] Chốt công thức đầu kỳ/nhập/xuất/cuối kỳ từ chứng từ/lịch sử; cấp phát/thu hồi nội bộ không làm đổi tổng sở hữu.
- [ ] Phân biệt thiết bị chưa tiếp nhận, tài sản đang giữ/sử dụng, xuất tạm dịch vụ và đã xuất vĩnh viễn; lấy dữ liệu thanh lý từ CV-08.
- [ ] Có API/UI lọc theo kỳ, nhóm/danh mục/đơn vị giữ; không chỉ đếm hồ sơ đang có hoặc dùng báo cáo vật tư để thay thế.
- [ ] Đối chiếu chuỗi nhập → cấp phát → thu hồi → sửa bên ngoài → nhận lại → thanh lý và các mốc đầu/cuối kỳ.

**Đạt khi:** số liệu toàn trường đúng theo kỳ, không tăng/giảm vì chuyển nội bộ hoặc xuất tạm đi sửa.

## P2 — Lịch và cảnh báo

### CV-10. Lịch bảo trì/bảo dưỡng

- [ ] Chốt cách tính chu kỳ, mốc bắt đầu, việc bỏ lỡ kỳ, cập nhật kỳ tiếp theo và đối tượng nhận cảnh báo.
- [ ] Khai báo lịch từ hợp đồng khi tạo/nhập thiết bị; tách khỏi hạn bảo hành và phiếu bảo trì thủ công.
- [ ] Có danh sách sắp đến hạn/quá hạn, lọc khoảng thời gian và cảnh báo phù hợp; dùng cơ chế thông báo có sẵn nếu phù hợp.
- [ ] Tạo phiếu bảo trì từ lịch, liên kết lần thực hiện và cập nhật kỳ sau theo kết quả; tránh tạo phiếu/cảnh báo lặp.

**Đạt khi:** nhận biết thiết bị cần bảo trì trước khi có hỏng hóc và truy lại được lịch thực hiện.

## Tích hợp với đồng đội — phân công là nghiệp vụ cốt lõi

### CV-11. Tích hợp phân công công việc — P0, chưa hoàn tất

- [ ] Tích hợp hợp đồng của module đồng đội: định danh yêu cầu PMMS, task/phần việc, chứng từ, người được giao, người thực hiện, trạng thái/phiên bản và quyền.
- [ ] Yêu cầu PMMS kế thừa vào có task chưa phân công, quản lý giao nhân viên; không cần QTTB tự tạo yêu cầu mua/sửa để sinh việc.
- [ ] Chặn thực hiện khi chưa được giao hoặc giao người khác; giữ quyền duyệt riêng của quản lý. Hiện code mới kiểm tra vai trò nên phần này còn thiếu.
- [ ] Mọi nghiệp vụ cần nhân viên phụ trách: tiếp nhận/cấp phát/thu hồi/kiểm kê/bảo trì/sửa chữa và phần vật tư liên quan. Liên kết theo hợp đồng module; không sinh việc trùng hoặc yêu cầu PMMS giả cho nghiệp vụ nội bộ.
- [ ] Đồng bộ kết quả, hủy, đổi người phụ trách và nhận lặp, giữ lịch sử; người được giao xác nhận hoàn tất khi đủ kết quả.
- [ ] Báo cáo theo từng nhân viên và toàn phòng QTTB, truy được yêu cầu nguồn và chứng từ thực hiện. Không khôi phục module task cũ để thay phần của team.

**Đạt khi:** yêu cầu từ PMMS và các nghiệp vụ thực sự có người phụ trách, quyền thực hiện và kết quả/báo cáo liên thông; không có hai hệ thống task song song.

### CV-12. Nghiệm thu kiểm kê và giao diện tích hợp

**Đã có:** module kiểm kê backend/UI từ bản pull. Việc phân người trong đợt kiểm kê không thay module phân công mọi công việc ở CV-11.

- [ ] Phối hợp nghiệm thu kế hoạch/duyệt/thực hiện/hiệu chỉnh/tiến độ/biên bản/thông báo và quyền đọc của từng bên.
- [ ] Đối chiếu yêu cầu hiện diện/tình trạng/vị trí: API kết quả hiện dùng `Exists`/`NewExists`; thống nhất phần còn thiếu với người phụ trách, không tự sửa module đã pull.
- [ ] Phối hợp quét mã vạch theo CV-03 và đưa sự kiện phù hợp vào timeline CV-07.
- [ ] Kiểm tra kết quả kiểm kê không tự ghi đè metadata hoặc thay đổi tồn khi chưa có quy trình đối soát được thống nhất.

**Đạt khi:** module kiểm kê được nghiệm thu theo phạm vi team chốt và tích hợp đúng với hồ sơ, mã vạch, lịch sử thiết bị.

## Các điểm cần chốt trước khi thực hiện phần phụ thuộc

- **Với chủ project:** các chi tiết còn mở về danh mục dùng thật, thanh lý và cách tính lịch bảo trì; không hỏi lại ranh giới nguồn/task đã được làm rõ.
- **Với team:** hợp đồng module phân công, liên kết yêu cầu/task/chứng từ, quyền và đồng bộ kết quả; phạm vi tình trạng/vị trí/quét kiểm kê.
- Không chốt lại: PMMS lập yêu cầu mua/sửa; PMQTTB kế thừa, phân công và theo dõi; mọi nghiệp vụ có người phụ trách; mã vạch, nhập trước/cấp phát riêng, hoàn tất toàn bộ một lần, chặn hủy sau thực hiện và giữ mượn trả.

Hợp đồng cũ đã được đính chính theo PMQTTB và yêu cầu mới nhất. Giữ các phần kỹ thuật phù hợp để sửa đúng luồng; không coi việc có code/test là đã hoàn tất nghiệp vụ nguồn/task.
