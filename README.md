# CVAT Duplicate Checker

App Windows (WinUI 3 / .NET 10) để kiểm tra annotation CVAT: phát hiện box trùng lặp
theo IoU hoặc sai số tọa độ, xem nhanh ảnh frame, và xóa box trùng trực tiếp trên CVAT
kèm sao lưu XML trước khi xóa.

## Tính năng

- Mở dữ liệu từ file XML/ZIP cục bộ hoặc trực tiếp từ CVAT (Task/Job) bằng PAT.
- Phát hiện trùng lặp theo hai tiêu chí: chỉ số IoU hoặc dung sai tọa độ (px), tùy chọn
  chỉ tính khi cùng nhãn.
- Lọc danh sách theo vùng frame, theo % trùng (các khoảng 100% / 90–99 / 70–89 / 50–69 /
  < 50) và theo nhãn.
- Ô xem nhanh ảnh frame (crop vùng box) ngay trong card đang chọn: zoom bằng wheel,
  kéo để di chuyển ảnh, khóa cuộn trang khi con trỏ trong khung.
- Preview chi tiết: zoom/pan, đến vị trí lỗi, hiện tất cả box trên frame; bấm box A/B
  để đánh dấu xóa.
- Xóa box trùng trên CVAT (PATCH annotations action=delete) sau khi xác nhận; tự sao lưu
  XML vào `%LOCALAPPDATA%\CvatDuplicateChecker\backups\`; không bao giờ xóa hết cả cặp
  trong một nhóm.
- Xóa hàng loạt: bấm một box thuộc cặp trùng 100% sẽ đánh dấu cả các bản sao trùng khít
  cùng tọa độ ở frame khác (lỗi copy nhanh), mỗi nhóm giữ lại ít nhất một box.

## Build & chạy

Yêu cầu: Windows 10/11 x64, .NET SDK 10, Windows App SDK (tự restore qua NuGet).

```bash
dotnet build CvatDuplicateChecker.csproj -p:Platform=x64
dotnet run --project CvatDuplicateChecker.csproj -p:Platform=x64
```

Lưu ý: đây là app đóng gói (packaged WinUI) — chạy bằng `dotnet run` hoặc cài appx,
không double-click thẳng exe trong `bin`.

## PAT mặc định (tùy chọn)

Repo KHÔNG chứa PAT thật. `GeneratedDefaultCvatTokens.cs` chỉ là bản mẫu rỗng.
Để app có PAT mặc định nội bộ, sinh file thật cục bộ:

```bash
powershell -File Generate-DefaultTokens.ps1 -TokenFile <đường-dẫn-token.txt> -OutputFile GeneratedDefaultCvatTokens.cs
git update-index --skip-worktree GeneratedDefaultCvatTokens.cs
```

Định dạng `token.txt`: mỗi dòng `serverUrl=PAT`, dòng `#` là chú thích.
Người dùng app vẫn có thể nhập PAT riêng trong ô kết nối; PAT chỉ lưu vào
Windows Credential Locker khi bấm "Ghi nhớ PAT".

## Chính sách bảo mật

Xem [PRIVACY.md](PRIVACY.md) (bản tiếng Anh dùng cho Microsoft Store).
