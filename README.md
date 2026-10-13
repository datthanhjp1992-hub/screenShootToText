# ScreenOcr — Chụp màn hình → Text (offline)

App portable, rất nhẹ (~1 MB), dùng engine OCR có sẵn của Windows 10/11 (`Windows.Media.Ocr`).
Tối ưu cho việc chụp **code trong Eclipse**: giữ thụt lề, bỏ số dòng, giữ dòng trống, hỗ trợ comment tiếng Nhật.

## 0. Tải bản build sẵn (không cần cài gì)

Vào trang **Releases** của repo → tải `ScreenOcr-x.y.z.zip`.

1. Chuột phải file zip → **Properties** → tick **Unblock** → OK (tránh Windows chặn file tải từ internet).
2. Giải nén vào thư mục bất kỳ → chạy `ScreenOcr.exe`.
3. Lần đầu chạy, nếu SmartScreen báo *"Windows protected your PC"* → **More info** → **Run anyway**
   (vì exe chưa ký số).

Tạo bản phát hành mới: push một tag dạng `v1.0.1`, GitHub Actions sẽ tự build và đính kèm file zip vào Release.

## 1. Build

**Yêu cầu:** Windows 10/11, Visual Studio 2022 (workload *.NET desktop development*) **hoặc** .NET SDK 8 trở lên.

- **Cách 1 – Visual Studio:** mở `ScreenOcr.csproj` → chọn `Release` → Build (Ctrl+Shift+B).
- **Cách 2 – Dòng lệnh:** chạy `build.bat` (hoặc `dotnet build -c Release`).

Kết quả nằm ở `bin\Release\net48\`. **Copy cả thư mục này** sang máy khác là chạy được
(.NET Framework 4.8 đã có sẵn trên Windows 10/11, không cần cài gì thêm).

> Lần build đầu cần internet để NuGet tải 2 package: `Microsoft.Windows.SDK.Contracts`
> (để gọi Windows OCR) và `Microsoft.NETFramework.ReferenceAssemblies`. App chạy thì hoàn toàn offline.

## 2. Gói ngôn ngữ OCR

Kiểm tra các gói đã cài (PowerShell):

```powershell
Get-WindowsCapability -Online | Where-Object Name -like 'Language.OCR*'
```

Cài thêm nếu thiếu (PowerShell **Run as Administrator**):

```powershell
Add-WindowsCapability -Online -Name "Language.OCR~~~en-US~0.0.1.0"
Add-WindowsCapability -Online -Name "Language.OCR~~~ja-JP~0.0.1.0"
```

## 3. Sử dụng

| Thao tác | Cách làm |
|---|---|
| Chụp vùng màn hình | Hotkey **Ctrl+Shift+Q** (ở bất kỳ đâu) hoặc nút 📷. Kéo chuột chọn vùng, Esc/chuột phải để huỷ |
| Nhập ảnh | Nút 📂 (chọn nhiều file), **kéo thả** file vào cửa sổ, hoặc **Ctrl+V** dán ảnh (VD sau khi chụp bằng Win+Shift+S) |
| OCR | Tự chạy sau khi chụp. Hoặc **F5** = OCR ảnh đang chọn, **Ctrl+F5** = OCR tất cả |
| Copy | Tự copy sau OCR, hoặc nút ⧉ / **Ctrl+Shift+C** |
| Lưu 1 file | 💾 / **Ctrl+S** → chọn thư mục + đặt tên file (.txt / .java) |
| Xuất nhiều file | 🗂 → chọn thư mục, mỗi ảnh ra 1 file `.txt` (UTF-8) |
| Chọn nhiều ảnh | Ctrl/Shift + click, Ctrl+A. Khung text sẽ hiển thị gộp kết quả |
| Sửa kết quả | Chọn 1 ảnh → sửa trực tiếp trong khung text trước khi copy/lưu |
| Đổi hotkey | Click ô *Hotkey* ở thanh tuỳ chọn → nhấn tổ hợp phím mới |
| Thoát hẳn | Nút X chỉ thu xuống khay hệ thống. Chuột phải icon ở khay → **Thoát** |

Cài đặt được lưu vào `ScreenOcr.ini` cạnh file exe (nếu thư mục không ghi được thì lưu vào `%APPDATA%\ScreenOcr`).

## 4. Mẹo để OCR code Eclipse chính xác hơn

- **Ngôn ngữ:** code thuần tiếng Anh → chọn `English (en-US)` (nhận ký hiệu `{ } ; ( )` tốt hơn).
  Code có comment tiếng Nhật → chọn `日本語 (ja)`.
- **Phóng to font Eclipse** (Ctrl + `=`) trước khi chụp; chữ càng to OCR càng chuẩn. Tuỳ chọn *Phóng to ×2–3* trong app cũng giúp với chữ nhỏ.
- Chụp **gọn vùng code**, tránh dính thanh cuộn, icon breakpoint, dấu gập code (+/−).
- *Bỏ số dòng* chỉ kích hoạt khi đa số dòng bắt đầu bằng số, nên để bật cũng an toàn.
- *Thụt lề* được làm tròn theo bội số 4 cột (cấu hình `TabSize` trong file ini) vì Eclipse thụt lề theo tab.
- Eclipse **Dark theme**: để bật *Tự đảo màu nền tối*.
- Các lỗi OCR hay gặp với code: `l` ↔ `1` ↔ `|`, `O` ↔ `0`, `;` ↔ `:`. Nên xem lại nhanh trước khi dùng.

## 5. Cấu trúc mã nguồn

| File | Vai trò |
|---|---|
| `Program.cs` | Điểm vào, chống chạy 2 instance |
| `MainForm.cs` | Giao diện, khay hệ thống, hotkey, nhập/xuất |
| `RegionSelectForm.cs` | Màn hình chọn vùng (hỗ trợ nhiều màn hình, DPI 125%/150%) |
| `OcrService.cs` | Tiền xử lý ảnh + gọi Windows OCR |
| `TextLayout.cs` | Dựng lại text dạng code từ toạ độ chữ (thụt lề, dòng trống, số dòng) |
| `AppSettings.cs` | Đọc/ghi `ScreenOcr.ini` |
| `NativeMethods.cs` | Win32 `RegisterHotKey`, parse hotkey |
