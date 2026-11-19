# In và quét mã vạch

Tại chi tiết phiếu nhập hoặc sau khi tạo phiếu, chọn **In tem mã vạch**. Mỗi dòng thiết bị có một tem Code 128, chứa mã vạch đã lưu; in lại không gọi API tạo định danh và không đổi mã thiết bị.

Mẫu mặc định là tem 100 × 50 mm, có mã thiết bị, tên và chuỗi mã vạch đọc được bằng mắt. In A4 hai cột, lề 5 mm, tỷ lệ 100%, tắt header/footer của trình duyệt. Tem dùng SVG, có vùng trắng hai bên, không phụ thuộc tải ảnh từ máy chủ. Thư viện mã hóa: [JsBarcode](https://github.com/lindell/JsBarcode).

Máy quét dạng bàn phím: mở ô tra cứu mã vạch, quét và gửi Enter. API kiểm tra định dạng, mã không tồn tại và quyền đọc theo đơn vị. Quét lại chỉ đọc hồ sơ, không tạo thiết bị hoặc thay đổi trạng thái. Cơ chế quét của module kiểm kê do đồng đội phụ trách vẫn giữ nguyên.

Kiểm tra phần mềm gồm render Code 128, in lại, PDF, giải mã bằng bộ đọc độc lập, tra cứu Enter, mã sai/không tồn tại và phân quyền. Cần in thử trên máy in thật và quét bằng máy của khách hàng để xác nhận chất lượng ở độ phân giải, giấy và tỷ lệ thực tế; kiểm tra phần mềm không thay thế nghiệm thu phần cứng.
