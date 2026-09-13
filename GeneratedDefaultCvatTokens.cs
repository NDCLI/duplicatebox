// BẢN MẪU — KHÔNG CHỨA PAT THẬT.
// File thật được sinh cục bộ từ token.txt của bạn:
//   powershell -File Generate-DefaultTokens.ps1 -TokenFile <đường-dẫn-token.txt> -OutputFile GeneratedDefaultCvatTokens.cs
// Sau khi sinh, chạy lệnh sau để PAT không bao giờ lọt vào commit:
//   git update-index --skip-worktree GeneratedDefaultCvatTokens.cs
using System.Collections.Generic;

namespace CvatDuplicateChecker;

internal static class GeneratedDefaultCvatTokens
{
    internal static IReadOnlyDictionary<string, string> Values { get; } =
        new Dictionary<string, string>();
}
