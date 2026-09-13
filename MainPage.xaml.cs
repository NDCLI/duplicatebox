using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.Security.Credentials;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using WinRT.Interop;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace CvatDuplicateChecker;

public sealed partial class MainPage : Page
{
    private string? _selectedPath;
    private string? _xml;
    private bool _useIoU = true;
    private bool _showAllBoxes = true;
    private (string Server, bool WholeTask, int Id)? _activeCvat;
    private string? _activePat;
    private (Box A, Box B, double Overlap)? _previewRow;
    private readonly List<(Box A, Box B, double Overlap)> _duplicateRows = new();
    private List<Box> _allBoxes = new();
    private int? _frameStart;
    private int? _frameEnd;
    private int _currentPage = 1;
    private const int PageSize = 20;
    private readonly List<CvatTask> _tasks = new();
    private readonly List<CvatJob> _jobs = new();
    private long _cvatRequestVersion;
    private long _previewRequestVersion;
    private bool _previewFitPending;
    private long _previewFitRequestVersion;
    private bool _isPreviewDragging;
    private bool _previewClickCandidate;
    private double _previewPressX;
    private double _previewPressY;
    private uint _previewDragPointerId;
    private double _previewDragX;
    private double _previewDragY;
    private double _previewScale = 1;
    private double _previewPanX;
    private double _previewPanY;
    private const double PreviewMinScale = 0.05;
    private const double PreviewMaxScale = 50;
    private static readonly PasswordVault CredentialVault = new();
    private static readonly HttpClient Http = new();
    private readonly HashSet<string> _selectedLabels = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressSelectionChanged;
    private (Box A, Box B, double Overlap)? _selectedRow;
    private UIElement? _quickReviewVisual;
    private bool _quickReviewLoading;
    private double _quickViewScale = 1;
    private double _quickPanX;
    private double _quickPanY;
    private bool _quickDragging;
    private uint _quickDragPointerId;
    private double _quickDragX;
    private double _quickDragY;
    private CompositeTransform? _quickActiveTransform;
    private double _quickCurrentScale = 1;
    private double _quickCurrentTX;
    private double _quickCurrentTY;
    private double _quickTargetScale = 1;
    private double _quickTargetTX;
    private double _quickTargetTY;
    private bool _quickZoomAnimating;
    private long _quickReviewRequestVersion;
    private readonly Dictionary<string, (ImageSource Source, double Width, double Height)> _frameImageCache = new();
    private Dictionary<XElement, int> _globalIndexByNode = new();
    private int _analyzeDebounceVersion;
    private readonly HashSet<int> _selectionByBoxId = new();
    private string _overlapFilterKey = "all";
    private readonly Dictionary<string, List<ShapeCandidate>> _shapeCandidates = new(StringComparer.OrdinalIgnoreCase);
    private string _annotationVersion = "0";

    // Color palette matching the React app
    private static readonly string[] Palette =
    {
        "#ef4444", "#f97316", "#f59e0b", "#84cc16", "#22c55e", "#10b981", "#14b8a6",
        "#06b6d4", "#0ea5e9", "#3b82f6", "#6366f1", "#8b5cf6", "#a855f7", "#d946ef", "#f43f5e"
    };
    private static readonly Dictionary<string, string> HardcodedLabelColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["person"] = "#c06060", ["car"] = "#2080c0", ["motorbike"] = "#00a0a0", ["bicycle"] = "#004040",
        ["bus"] = "#204080", ["truck"] = "#906080", ["train"] = "#50a080", ["face"] = "#90e8ce",
        ["head"] = "#ba9109", ["_skip"] = "#766433", ["negative"] = "#e6213a", ["licenseplate"] = "#e277ef"
    };

    public MainPage()
    {
        InitializeComponent();
        IoUSlider.Value = 90;
        ToleranceSlider.Value = 0;
        RebuildOverlapChips();
        LoadDefaultServersAndTokens();
        // Hàng cấu hình: 4 khối nằm ngang khi cửa sổ rộng, xếp dọc khi hẹp (thay cho VisualState kém ổn định).
        SizeChanged += (_, e) => ApplySettingsLayout(e.NewSize.Width >= 1000);
        Loaded += (_, _) => ApplySettingsLayout(ActualWidth >= 1000);

        if (Environment.GetEnvironmentVariable("CVAT_SCREENSHOT_DEMO") == "1" ||
            File.Exists(Path.Combine(Path.GetTempPath(), "cvat-duplicate-checker-screenshot-demo.flag")))
        {
            Loaded += async (_, _) =>
            {
                _xml = DemoXml;
                SelectedFileText.Text = "demo-duplicate-annotation.xml";
                await AnalyzeCurrentAsync();
            };
        }
    }

    private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".xml");
        picker.FileTypeFilter.Add(".zip");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        ResetResults();
        _selectedPath = file.Path;
        SelectedFileText.Text = file.Name;
        StatusText.Text = "Đang phân tích annotation...";
        await AnalyzeCurrentAsync();
    }

    private async void OpenTaskButton_Click(object sender, RoutedEventArgs e) => await LoadCvatAsync(true);

    private async void OpenJobButton_Click(object sender, RoutedEventArgs e) => await LoadCvatAsync(false);

    private async void ListTasksButton_Click(object sender, RoutedEventArgs e) => await ListTasksAsync();

    private async void RefreshCvatButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCvat is { } source)
            await LoadCvatAsync(source.WholeTask, source.Id, _activePat);
    }

    private async Task LoadCvatAsync(bool wholeTask, int? requestedId = null, string? knownPat = null)
    {
        try
        {
            var id = requestedId ?? GetSelectedCvatId(wholeTask);
            if (id <= 0)
                throw new InvalidOperationException(wholeTask ? "Chọn hoặc nhập Task ID hợp lệ." : "Chọn hoặc nhập Job ID hợp lệ.");

            var serverUrl = SelectedServer();
            var pat = knownPat ?? GetPatForServer(serverUrl);
            await SavePatIfRequestedAsync(serverUrl, pat);

            StatusText.Text = "Đang xuất annotation từ CVAT...";
            PatStatusText.Text = "Đang xuất annotation từ CVAT...";

            var body = await ExportCvatAnnotationsAsync(serverUrl, pat, wholeTask, id);

            // Chỉ chuyển sang workspace khi đã lấy được dữ liệu; lỗi thì ở lại landing kèm thông báo.
            CvatErrorText.Visibility = Visibility.Collapsed;
            LandingPanel.Visibility = Visibility.Collapsed;
            WorkspacePanel.Visibility = Visibility.Visible;
            SelectedFileText.Text = $"CVAT {(wholeTask ? "Task" : "Job")} #{id}";

            ResetResults();
            _xml = body;
            _activePat = pat;
            _activeCvat = (serverUrl, wholeTask, id);
            await RefreshShapeIdsAsync();
            DeleteSelectedButton.Visibility = wholeTask ? Visibility.Collapsed : Visibility.Visible;
            PreviewDeleteButton.Visibility = DeleteSelectedButton.Visibility;
            RefreshCvatButton.Content = wholeTask ? "Tải lại Task" : "Tải lại Job";
            RefreshCvatButton.Visibility = Visibility.Visible;
            SelectedFileText.Text = $"CVAT {(wholeTask ? "Task" : "Job")} #{id}";
            StatusText.Text = "Đã tải annotation từ CVAT. Đang tìm dữ liệu trùng...";
            await AnalyzeCurrentAsync();
        }
        catch (Exception ex)
        {
            ShowError("Không thể tải annotation từ CVAT", FriendlyCvatError(ex));
        }
    }

    private async Task ListTasksAsync()
    {
        try
        {
            var requestVersion = ++_cvatRequestVersion;
            var serverUrl = SelectedServer();
            var pat = GetPatForServer(serverUrl);
            await SavePatIfRequestedAsync(serverUrl, pat);
            ListTasksButton.IsEnabled = false;
            PatStatusText.Text = "Đang tải danh sách Task...";
            var tasks = await GetPagedCvatItemsAsync(serverUrl, pat, "/api/tasks?page_size=100");
            if (requestVersion != _cvatRequestVersion) return;

            _tasks.Clear();
            _tasks.AddRange(tasks.Select(item => new CvatTask(
                item.GetProperty("id").GetInt32(),
                item.TryGetProperty("name", out var name) ? name.GetString() ?? "Không tên" : "Không tên")));
            TaskComboBox.ItemsSource = _tasks;
            TaskComboBox.SelectedIndex = _tasks.Count > 0 ? 0 : -1;
            TaskComboBox.Visibility = _tasks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            TaskIdBox.Visibility = _tasks.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            PatStatusText.Text = _tasks.Count == 0 ? "Không có Task để hiển thị" : $"Đã tải {_tasks.Count} Task";
        }
        catch (Exception ex)
        {
            PatStatusText.Text = "Không thể tải danh sách Task";
            ShowError("Không thể tải danh sách Task", FriendlyCvatError(ex));
        }
        finally
        {
            ListTasksButton.IsEnabled = true;
        }
    }

    private async void TaskComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TaskComboBox.SelectedItem is not CvatTask task) return;
        TaskIdBox.Value = task.Id;
        await ListJobsAsync(task.Id);
    }

    private void JobComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (JobComboBox.SelectedItem is CvatJob job) JobIdBox.Value = job.Id;
    }

    private async Task ListJobsAsync(int taskId)
    {
        try
        {
            var requestVersion = ++_cvatRequestVersion;
            var serverUrl = SelectedServer();
            var pat = GetPatForServer(serverUrl);
            JobComboBox.IsEnabled = false;
            JobComboBox.ItemsSource = null;
            JobComboBox.Visibility = Visibility.Collapsed;
            JobIdBox.Visibility = Visibility.Visible;
            var jobs = await GetPagedCvatItemsAsync(serverUrl, pat, $"/api/jobs?task_id={taskId}&page_size=100");
            if (requestVersion != _cvatRequestVersion) return;

            _jobs.Clear();
            _jobs.AddRange(jobs.Select(item => new CvatJob(
                item.GetProperty("id").GetInt32(),
                item.TryGetProperty("start_frame", out var start) ? start.GetInt32() : 0,
                item.TryGetProperty("stop_frame", out var stop) ? stop.GetInt32() : 0)));
            JobComboBox.ItemsSource = _jobs;
            JobComboBox.SelectedIndex = _jobs.Count > 0 ? 0 : -1;
            JobComboBox.Visibility = _jobs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            JobIdBox.Visibility = _jobs.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            ShowError("Không thể tải Job của Task", FriendlyCvatError(ex));
        }
        finally
        {
            JobComboBox.IsEnabled = true;
        }
    }

    private int GetSelectedCvatId(bool wholeTask)
    {
        if (wholeTask && TaskComboBox.SelectedItem is CvatTask task) return task.Id;
        if (!wholeTask && JobComboBox.SelectedItem is CvatJob job) return job.Id;
        var value = wholeTask ? TaskIdBox.Value : JobIdBox.Value;
        return double.IsNaN(value) ? 0 : (int)value;
    }

    private async Task AnalyzeCurrentAsync()
    {
        try
        {
            if (_xml is null && _selectedPath is not null) _xml = await ReadXmlAsync(_selectedPath);
            if (_xml is null) return;

            var allBoxes = XDocument.Parse(_xml).Descendants("box").Select(ReadBox).ToList();
            _allBoxes = allBoxes;
            _globalIndexByNode = new Dictionary<XElement, int>(allBoxes.Count);
            for (var i = 0; i < allBoxes.Count; i++) _globalIndexByNode[allBoxes[i].Node] = i + 1;
            var frameIds = allBoxes.Select(box => box.Frame).Where(frame => frame >= 0).Distinct().OrderBy(frame => frame).ToList();
            var minFrame = frameIds.FirstOrDefault();
            var maxFrame = frameIds.LastOrDefault();
            var startFrame = _frameStart ?? minFrame;
            var endFrame = _frameEnd ?? maxFrame;
            if (endFrame < startFrame) (startFrame, endFrame) = (endFrame, startFrame);

            var rangeBoxes = allBoxes.Where(box => box.Frame >= startFrame && box.Frame <= endFrame).ToList();
            var skipFrames = rangeBoxes
                .Where(box => box.Label.Contains("skip", StringComparison.OrdinalIgnoreCase))
                .Select(box => box.Frame)
                .ToHashSet();
            var candidates = rangeBoxes
                .Where(box => !box.Outside)
                .Where(box => !skipFrames.Contains(box.Frame))
                .ToList();

            var threshold = IoUSlider.Value / 100;
            var tolerance = ToleranceSlider.Value;
            _duplicateRows.Clear();
            var processed = new HashSet<XElement>();

            foreach (var frameBoxes in candidates.GroupBy(box => box.Frame))
            {
                var frameCandidates = frameBoxes.ToList();
                for (var i = 0; i < frameCandidates.Count; i++)
                {
                    var a = frameCandidates[i];
                    if (processed.Contains(a.Node)) continue;
                    for (var j = i + 1; j < frameCandidates.Count; j++)
                    {
                        var b = frameCandidates[j];
                        if (processed.Contains(b.Node)) continue;
                        if (MatchLabelCheckBox.IsChecked.GetValueOrDefault() && !string.Equals(a.Label, b.Label, StringComparison.OrdinalIgnoreCase)) continue;

                        var iou = IoU(a, b);
                        var duplicate = _useIoU
                            ? iou >= threshold
                            : Math.Abs(a.X1 - b.X1) <= tolerance &&
                              Math.Abs(a.Y1 - b.Y1) <= tolerance &&
                              Math.Abs(a.X2 - b.X2) <= tolerance &&
                              Math.Abs(a.Y2 - b.Y2) <= tolerance;
                        if (!duplicate) continue;
                        _duplicateRows.Add((a, b, iou * 100));
                        processed.Add(b.Node);
                    }
                }
            }

            var duplicateFrames = _duplicateRows.Select(row => row.A.Frame).Distinct().ToHashSet();
            _currentPage = 1;

            DuplicateCountText.Text = $"{_duplicateRows.Count} box trùng";
            DuplicateFrameCountText.Text = $"{duplicateFrames.Count} frame";
            DuplicateListTitle.Text = "Danh sách trùng lặp";
            FrameRangeTitleText.Text = $"LỌC THEO VÙNG FRAME (GIỚI HẠN: {minFrame} – {maxFrame})";
            _selectedRow = null;
            _quickReviewVisual = null;
            _quickReviewLoading = false;
            _quickViewScale = 1;
            _quickPanX = 0;
            _quickPanY = 0;
            StopQuickZoomAnimation();
            _quickActiveTransform = null;
            _quickCurrentScale = _quickTargetScale = 1;
            _quickCurrentTX = _quickTargetTX = 0;
            _quickCurrentTY = _quickTargetTY = 0;
            _quickReviewRequestVersion++;
            RebuildLabelChips(resetSelection: true);
            RefreshList();
            ResultInfoBar.IsOpen = false;
            StatusText.Text = $"Phân tích hoàn tất · {_allBoxes.Count} box · {frameIds.Count} frame";
            LandingPanel.Visibility = Visibility.Collapsed;
            WorkspacePanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ShowError("Không thể phân tích annotation", ex.Message);
        }
    }

    private void UseIoUButton_Click(object sender, RoutedEventArgs e)
    {
        _useIoU = true;
        IoUSlider.Visibility = Visibility.Visible;
        ThresholdText.Visibility = Visibility.Visible;
        TolerancePanel.Visibility = Visibility.Collapsed;
        SetModeColors();
        if (_xml is not null) _ = AnalyzeCurrentAsync();
    }

    private void UseToleranceButton_Click(object sender, RoutedEventArgs e)
    {
        _useIoU = false;
        IoUSlider.Visibility = Visibility.Collapsed;
        ThresholdText.Visibility = Visibility.Collapsed;
        TolerancePanel.Visibility = Visibility.Visible;
        SetModeColors();
        if (_xml is not null) _ = AnalyzeCurrentAsync();
    }

    private void ApplySettingsLayout(bool wide)
    {
        FrameColumn.Width = wide ? new GridLength(280) : new GridLength(1, GridUnitType.Star);
        CriteriaColumn.Width = wide ? new GridLength(300) : new GridLength(1, GridUnitType.Star);
        ThresholdColumn.Width = wide ? new GridLength(360) : new GridLength(1, GridUnitType.Star);
        LabelColumn.Width = wide ? new GridLength(330) : new GridLength(1, GridUnitType.Star);
        var panels = new[] { FramePanel, CriteriaPanel, ThresholdPanel, LabelPanel };
        for (var i = 0; i < panels.Length; i++)
        {
            Grid.SetRow(panels[i], wide ? 0 : i);
            Grid.SetColumn(panels[i], wide ? i : 0);
            Grid.SetColumnSpan(panels[i], wide ? 1 : 5);
        }
    }

    private void SetModeColors()
    {
        var active = new SolidColorBrush(Color.FromArgb(255, 61, 99, 217));
        var inactive = new SolidColorBrush(Color.FromArgb(255, 25, 35, 52));
        UseIoUButton.Background = _useIoU ? active : inactive;
        UseToleranceButton.Background = _useIoU ? inactive : active;
    }

    private void MatchLabelCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_xml is not null) _ = AnalyzeCurrentAsync();
    }

    private void IoUSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ThresholdText is not null) ThresholdText.Text = $"{e.NewValue:0}% trùng nhau";
        if (_xml is not null && _useIoU) ScheduleAnalyze();
    }

    private void ToleranceSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ToleranceText is not null) ToleranceText.Text = e.NewValue == 0 ? "0.0 px · Khớp 100%" : $"{e.NewValue:0.0} px";
        if (_xml is not null && !_useIoU) ScheduleAnalyze();
    }

    // Kéo slider/gõ vùng frame bắn sự kiện liên tục — gom lại thành một lần phân tích sau khi ngừng tay 160ms.
    private async void ScheduleAnalyze()
    {
        var version = ++_analyzeDebounceVersion;
        await Task.Delay(160);
        if (version == _analyzeDebounceVersion && _xml is not null) _ = AnalyzeCurrentAsync();
    }

    private void LoadDefaultServersAndTokens()
    {
        foreach (var server in new[] { "https://app.cvat.ai", "http://10.43.2.147:8080", "http://10.43.2.12:8080" })
            CvatServerComboBox.Items.Add(server);
        CvatServerComboBox.SelectedIndex = 0;
        UpdatePatStatus();
    }

    private void CvatServerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CvatServerComboBox.SelectedItem is string server && CustomServerTextBox is not null)
            CustomServerTextBox.Text = server;
        LoadStoredPatForCurrentServer();
    }

    private void CustomServerTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _cvatRequestVersion++;
        LoadStoredPatForCurrentServer();
    }

    private void PatPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => UpdatePatStatus();

    private void LoadStoredPatForCurrentServer()
    {
        if (CustomServerTextBox is null || PatPasswordBox is null) return;
        try
        {
            var server = SelectedServer();
            var credential = SafeFindCredential(CredentialResource(server));
            if (credential is null)
            {
                PatPasswordBox.Password = DefaultPatForServer(server) ?? string.Empty;
                RememberPatCheckBox.IsChecked = false;
            }
            else
            {
                credential.RetrievePassword();
                PatPasswordBox.Password = credential.Password;
                RememberPatCheckBox.IsChecked = true;
            }
        }
        catch
        {
            PatPasswordBox.Password = string.Empty;
            RememberPatCheckBox.IsChecked = false;
        }
        UpdatePatStatus();
    }

    private void UpdatePatStatus()
    {
        if (PatStatusText is null || PatPasswordBox is null) return;
        var length = PatPasswordBox.Password.Length;
        PatStatusText.Text = length > 0
            ? $"Đã nhập: {length} ký tự"
            : "Chưa nhập PAT";
    }

    private string SelectedServer()
    {
        var server = CustomServerTextBox.Text;
        if (string.IsNullOrWhiteSpace(server)) server = CvatServerComboBox.SelectedItem as string;
        return !string.IsNullOrWhiteSpace(server)
            ? NormalizeServer(server)
            : throw new InvalidOperationException("Chọn hoặc nhập server CVAT.");
    }

    private string GetPatForServer(string server)
    {
        var pat = PatPasswordBox.Password.Trim();
        if (pat.Length == 0)
            pat = DefaultPatForServer(server) ?? string.Empty;
        if (pat.Length == 0)
            throw new InvalidOperationException("Nhập Personal Access Token (PAT) của CVAT trước khi tiếp tục.");
        return pat;
    }

    private static string? DefaultPatForServer(string server)
    {
        try
        {
            return GeneratedDefaultCvatTokens.Values.TryGetValue(NormalizeServer(server), out var token) &&
                   !string.IsNullOrWhiteSpace(token)
                ? token
                : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task SavePatIfRequestedAsync(string server, string pat)
    {
        if (!RememberPatCheckBox.IsChecked.GetValueOrDefault()) return;
        await Task.Run(() =>
        {
            try
            {
                var resource = CredentialResource(server);
                foreach (var credential in SafeFindCredentials(resource)) CredentialVault.Remove(credential);
                CredentialVault.Add(new PasswordCredential(resource, "cvat-pat", pat));
            }
            catch
            {
                // Credential Locker is optional. Manual PAT authentication must still work.
            }
        });
    }

    private static PasswordCredential? SafeFindCredential(string resource) => SafeFindCredentials(resource).FirstOrDefault();

    private static IReadOnlyList<PasswordCredential> SafeFindCredentials(string resource)
    {
        try
        {
            return CredentialVault.FindAllByResource(resource);
        }
        catch
        {
            return Array.Empty<PasswordCredential>();
        }
    }

    private void ClearSavedPatButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var resource = CredentialResource(SelectedServer());
            foreach (var credential in SafeFindCredentials(resource)) CredentialVault.Remove(credential);
            PatPasswordBox.Password = string.Empty;
            RememberPatCheckBox.IsChecked = false;
            UpdatePatStatus();
        }
        catch
        {
            ShowError("Không thể xóa PAT đã lưu", "Không thể truy cập Windows Credential Locker cho server này.");
        }
    }

    private static string CredentialResource(string server) => "CvatDuplicateChecker/" + NormalizeServer(server);

    private static string NormalizeServer(string value)
    {
        var server = value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("URL CVAT phải bắt đầu bằng http:// hoặc https://.");
        }
        return server.ToLowerInvariant();
    }

    private static string FriendlyCvatError(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden } =>
            "PAT không hợp lệ hoặc tài khoản không có quyền truy cập dữ liệu CVAT này.",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } =>
            "Không tìm thấy Task/Job hoặc endpoint trên server CVAT.",
        HttpRequestException => "Không thể kết nối tới CVAT. Kiểm tra URL server, mạng hoặc VPN.",
        _ => ex.Message
    };

    private async Task<string> ExportCvatAnnotationsAsync(string server, string pat, bool wholeTask, int id)
    {
        var resource = wholeTask ? "tasks" : "jobs";
        var exportEndpoint = $"/api/{resource}/{id}/dataset/export?save_images=False&format={Uri.EscapeDataString("CVAT for images 1.1")}";
        using var exportRequest = new HttpRequestMessage(HttpMethod.Post, server + exportEndpoint);
        exportRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pat);
        using var exportResponse = await Http.SendAsync(exportRequest);
        if (!exportResponse.IsSuccessStatusCode)
            throw new HttpRequestException("CVAT không thể bắt đầu xuất annotation.", null, exportResponse.StatusCode);

        using var exportDocument = JsonDocument.Parse(await exportResponse.Content.ReadAsStringAsync());
        if (!exportDocument.RootElement.TryGetProperty("rq_id", out var requestIdElement) ||
            string.IsNullOrWhiteSpace(requestIdElement.GetString()))
        {
            throw new InvalidDataException("CVAT không trả về mã yêu cầu xuất annotation.");
        }

        var requestId = requestIdElement.GetString()!;
        string? resultUrl = null;
        for (var attempt = 0; attempt < 120; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            var statusEndpoint = "/api/requests/" + Uri.EscapeDataString(requestId);
            var statusText = await GetCvatTextAsync(server, pat, statusEndpoint);
            using var statusDocument = JsonDocument.Parse(statusText);
            var root = statusDocument.RootElement;
            var status = root.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString()
                : null;

            if (string.Equals(status, "finished", StringComparison.OrdinalIgnoreCase))
            {
                resultUrl = root.TryGetProperty("result_url", out var resultUrlElement)
                    ? resultUrlElement.GetString()
                    : null;
                break;
            }

            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("CVAT không thể xuất annotation cho Task/Job này.");
        }

        if (string.IsNullOrWhiteSpace(resultUrl))
            throw new TimeoutException("CVAT mất quá lâu để chuẩn bị annotation. Hãy thử lại sau.");

        using var downloadRequest = new HttpRequestMessage(HttpMethod.Get, resultUrl);
        downloadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pat);
        using var downloadResponse = await Http.SendAsync(downloadRequest);
        if (!downloadResponse.IsSuccessStatusCode)
            throw new HttpRequestException("CVAT không thể tải annotation đã xuất.", null, downloadResponse.StatusCode);

        var bytes = await downloadResponse.Content.ReadAsByteArrayAsync();
        if (bytes.Length == 0) throw new InvalidDataException("CVAT trả về annotation rỗng.");
        return ReadExportedAnnotationXml(bytes);
    }

    private static string ReadExportedAnnotationXml(byte[] bytes)
    {
        if (bytes.Length > 0 && bytes[0] == '<') return System.Text.Encoding.UTF8.GetString(bytes);
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(candidate =>
            string.Equals(Path.GetFileName(candidate.FullName), "annotations.xml", StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault(candidate => candidate.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("File CVAT đã xuất không chứa annotation XML.");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private async Task<string> GetCvatTextAsync(string server, string pat, string endpoint)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, server + endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pat);
        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("CVAT không chấp nhận yêu cầu.", null, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<List<JsonElement>> GetPagedCvatItemsAsync(string server, string pat, string firstEndpoint)
    {
        var items = new List<JsonElement>();
        var endpoint = firstEndpoint;
        while (!string.IsNullOrEmpty(endpoint))
        {
            var body = await GetCvatTextAsync(server, pat, endpoint);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                items.AddRange(document.RootElement.EnumerateArray().Select(item => item.Clone()));
                break;
            }

            if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("CVAT trả về danh sách không hợp lệ.");
            items.AddRange(results.EnumerateArray().Select(item => item.Clone()));
            endpoint = document.RootElement.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String
                ? ToEndpoint(server, next.GetString())
                : null;
        }
        return items;
    }

    private static string? ToEndpoint(string server, string? next)
    {
        if (string.IsNullOrWhiteSpace(next)) return null;
        if (Uri.TryCreate(next, UriKind.Absolute, out var nextUri))
            return nextUri.PathAndQuery;
        return next.StartsWith('/') ? next : "/" + next;
    }

    private void FrameRange_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        _frameStart = ToFrameNumber(FrameStartBox.Value);
        _frameEnd = ToFrameNumber(FrameEndBox.Value);
        if (_xml is not null) ScheduleAnalyze();
    }

    private void ResetFrameRangeButton_Click(object sender, RoutedEventArgs e)
    {
        _frameStart = null;
        _frameEnd = null;
        FrameStartBox.Value = double.NaN;
        FrameEndBox.Value = double.NaN;
        if (_xml is not null) _ = AnalyzeCurrentAsync();
    }

    private void PreviousPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage <= 1) return;
        _currentPage--;
        RefreshList();
    }

    private void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        _currentPage++;
        RefreshList();
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _currentPage = 1;
        RefreshList();
    }

    private void RefreshList()
    {
        var query = SearchTextBox?.Text.Trim() ?? "";
        var rows = _duplicateRows
            .Where(row => OverlapPasses(row.Overlap))
            .Where(row => _selectedLabels.Count == 0 || _selectedLabels.Contains(row.A.Label) || _selectedLabels.Contains(row.B.Label))
            .Where(row => string.IsNullOrEmpty(query) ||
                row.A.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.B.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.A.Frame.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.A.FrameName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var pageCount = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)PageSize));
        _currentPage = Math.Clamp(_currentPage, 1, pageCount);
        var pageRows = rows.Skip((_currentPage - 1) * PageSize).Take(PageSize).ToList();

        DuplicateListView.ItemsSource = pageRows.Select((row, index) =>
        {
            var ordinal = (_currentPage - 1) * PageSize + index + 1;
            var indexA = GetGlobalIndex(row.A);
            var indexB = GetGlobalIndex(row.B);
            var isSelected = _selectedRow == row;
            // Ô xem nhanh chỉ dựng cho card đang chọn (ảnh thật nếu đã tải, minimap tọa độ trong lúc chờ);
            // các card khác không chứa cây UIElement nào để danh sách refresh nhẹ.
            UIElement? quickReview = null;
            if (isSelected)
            {
                quickReview = _quickReviewVisual
                    ?? (_quickReviewLoading
                        ? BuildQuickReviewLoading()
                        : BuildQuickReview(
                            null,
                            Math.Max(1, row.A.FrameWidth > 0 ? row.A.FrameWidth : Math.Ceiling(Math.Max(row.A.X2, row.B.X2))),
                            Math.Max(1, row.A.FrameHeight > 0 ? row.A.FrameHeight : Math.Ceiling(Math.Max(row.A.Y2, row.B.Y2))),
                            row));
            }
            return new DuplicateItem(
                row,
                ordinal,
                $"#{ordinal}",
                $"Frame {row.A.Frame}",
                row.A.Label,
                row.A.FrameName,
                $"IoU {row.Overlap:0.##}%",
                $"A: #{indexA} · B: #{indexB}",
                new SolidColorBrush(ParseColor(GetLabelColor(row.A.Label))),
                MakeBoxRow(row.A, indexA, "A"),
                MakeBoxRow(row.B, indexB, "B"),
                "2 box trùng",
                quickReview,
                isSelected ? Visibility.Visible : Visibility.Collapsed);
        }).ToList();
        PageText.Text = $"Trang {_currentPage} / {pageCount} · {rows.Count} kết quả";
        UpdateDeleteButtons();
        RestoreListSelection();
    }

    // ── Lọc theo % trùng (IoU): các khoảng rời nhau để mỗi mốc chỉ hiện đúng nhóm thuộc khoảng đó ──
    private static readonly (string Key, string Text, string Hint)[] OverlapPresets =
    {
        ("all", "Tất cả", "Không lọc theo % trùng"),
        ("100", "100%", "Chỉ nhóm trùng khít 100% — bản sao y hệt do copy"),
        ("90-99", "90–99%", "Nhóm trùng từ 90% đến dưới 100%"),
        ("70-89", "70–89%", "Nhóm trùng từ 70% đến dưới 90%"),
        ("50-69", "50–69%", "Nhóm trùng từ 50% đến dưới 70%"),
        ("lt50", "< 50%", "Nhóm trùng dưới 50% — chỉ chạm nhẹ nhau"),
    };

    private bool OverlapPasses(double overlap) => _overlapFilterKey switch
    {
        "100" => overlap >= 99.95,
        "90-99" => overlap >= 90 && overlap < 99.95,
        "70-89" => overlap >= 70 && overlap < 90,
        "50-69" => overlap >= 50 && overlap < 70,
        "lt50" => overlap < 50,
        _ => true,
    };

    private void RebuildOverlapChips()
    {
        if (OverlapChipsView is null) return;
        OverlapChipsView.ItemsSource = OverlapPresets.Select(preset =>
        {
            var selected = preset.Key == _overlapFilterKey;
            return new OverlapChipItem(
                preset.Key,
                preset.Text,
                preset.Hint,
                new SolidColorBrush(selected ? ParseColor("#2563EB") : Color.FromArgb(255, 13, 21, 36)),
                new SolidColorBrush(selected ? Microsoft.UI.Colors.White : Color.FromArgb(255, 154, 169, 191)),
                new SolidColorBrush(selected ? ParseColor("#3B82F6") : Color.FromArgb(255, 42, 58, 82)));
        }).ToList();
    }

    private void OverlapChip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not OverlapChipItem chip) return;
        if (chip.Key == _overlapFilterKey) return;
        _overlapFilterKey = chip.Key;
        _currentPage = 1;
        RebuildOverlapChips();
        RefreshList();
    }

    // Số box đang đánh dấu "SẼ XÓA" hiện ngay trên nút xóa (cả danh sách lẫn preview).
    private void UpdateDeleteButtons()
    {
        var text = _selectionByBoxId.Count == 0
            ? "Xóa box đã chọn"
            : $"Xóa {_selectionByBoxId.Count} box đã chọn";
        if (DeleteSelectedButton is not null) DeleteSelectedButton.Content = text;
        if (PreviewDeleteText is not null) PreviewDeleteText.Text = text;
    }

    private BoxRowItem MakeBoxRow(Box box, int index, string letter)
    {
        var marked = _selectionByBoxId.Contains(index);
        return new BoxRowItem(
            index,
            $"{letter} · #{index} {box.Label}: [{box.X1:0.#}, {box.Y1:0.#}, {box.X2:0.#}, {box.Y2:0.#}]" + (marked ? "  ·  SẼ XÓA" : ""),
            new SolidColorBrush(marked ? ParseColor("#FB7185") : ParseColor("#94A3B8")));
    }

    private void BoxRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not BoxRowItem row) return;
        if (row.Key >= 1 && row.Key <= _allBoxes.Count) ToggleMarkSet(_allBoxes[row.Key - 1]);
        RefreshList();
    }

    // Chỉ xóa hàng loạt khi nhóm là bản sao trùng 100% (box A và B trùng khít tọa độ) và
    // các frame khác cũng có cặp trùng khít cùng tọa độ đó (lỗi copy nhanh); mỗi nhóm nhận tối đa một box.
    private void ToggleMarkSet(Box clicked)
    {
        var clickedIndex = GetGlobalIndex(clicked);
        var clickedPos = _duplicateRows.FindIndex(row =>
            ReferenceEquals(row.A.Node, clicked.Node) || ReferenceEquals(row.B.Node, clicked.Node));

        var set = new List<int> { clickedIndex };
        if (clickedPos >= 0)
        {
            var clickedRow = _duplicateRows[clickedPos];
            if (CoordsMatch(clickedRow.A, clickedRow.B))
            {
                var clickedIsA = ReferenceEquals(clickedRow.A.Node, clicked.Node);
                for (var i = 0; i < _duplicateRows.Count; i++)
                {
                    if (i == clickedPos) continue;
                    var row = _duplicateRows[i];
                    if (!CoordsMatch(row.A, row.B) || !CoordsMatch(row.A, clicked)) continue;
                    set.Add(GetGlobalIndex(clickedIsA ? row.A : row.B));
                }
            }
        }

        if (_selectionByBoxId.Contains(clickedIndex))
            foreach (var index in set) _selectionByBoxId.Remove(index);
        else
            foreach (var index in set) _selectionByBoxId.Add(index);
    }

    private static bool CoordsMatch(Box a, Box b) =>
        string.Equals(a.Label, b.Label, StringComparison.OrdinalIgnoreCase) &&
        Math.Abs(a.X1 - b.X1) <= 0.5 && Math.Abs(a.Y1 - b.Y1) <= 0.5 &&
        Math.Abs(a.X2 - b.X2) <= 0.5 && Math.Abs(a.Y2 - b.Y2) <= 0.5;

    // ── Xóa box trùng trên CVAT (giống bản web: chọn box → xác nhận → partial_delete → backup) ──
    private async Task RefreshShapeIdsAsync()
    {
        _shapeCandidates.Clear();
        _annotationVersion = "0";
        if (_activeCvat is not { WholeTask: false } cvat || _activePat is null) return;

        using var jobDocument = JsonDocument.Parse(await GetCvatTextAsync(cvat.Server, _activePat, $"/api/jobs/{cvat.Id}"));
        var taskId = jobDocument.RootElement.GetProperty("task_id").GetInt32();

        var labelNames = new Dictionary<int, string>();
        foreach (var label in await GetPagedCvatItemsAsync(cvat.Server, _activePat, $"/api/labels?task_id={taskId}&page_size=100"))
        {
            labelNames[label.GetProperty("id").GetInt32()] = label.GetProperty("name").GetString() ?? "";
        }

        using var document = JsonDocument.Parse(await GetCvatTextAsync(cvat.Server, _activePat, $"/api/jobs/{cvat.Id}/annotations"));
        var root = document.RootElement;
        if (root.TryGetProperty("version", out var version)) _annotationVersion = version.ToString();
        if (!root.TryGetProperty("shapes", out var shapes)) return;
        foreach (var shape in shapes.EnumerateArray())
        {
            if (shape.TryGetProperty("outside", out var outside) && outside.ValueKind == JsonValueKind.True) continue;
            if (!shape.TryGetProperty("points", out var points) || points.GetArrayLength() < 4) continue;
            var labelId = shape.GetProperty("label_id").GetInt32();
            var key = $"{shape.GetProperty("frame").GetInt32()}|{(labelNames.TryGetValue(labelId, out var name) ? name : "")}";
            if (!_shapeCandidates.TryGetValue(key, out var list))
            {
                list = new List<ShapeCandidate>();
                _shapeCandidates[key] = list;
            }
            list.Add(new ShapeCandidate(
                shape.GetProperty("id").GetInt32(),
                shape.TryGetProperty("type", out var type) ? type.GetString() ?? "rectangle" : "rectangle",
                labelId,
                shape.GetProperty("frame").GetInt32(),
                points[0].GetDouble(), points[1].GetDouble(), points[2].GetDouble(), points[3].GetDouble()));
        }
    }

    // Khớp box trong XML với shape trên CVAT theo frame + nhãn + tọa độ (sai số 0.5px vì XML làm tròn số).
    private bool TryFindShape(Box box, out ShapeCandidate found)
    {
        found = default!;
        if (!_shapeCandidates.TryGetValue($"{box.Frame}|{box.Label}", out var list)) return false;
        foreach (var candidate in list)
        {
            if (Math.Abs(candidate.X1 - box.X1) <= 0.5 && Math.Abs(candidate.Y1 - box.Y1) <= 0.5 &&
                Math.Abs(candidate.X2 - box.X2) <= 0.5 && Math.Abs(candidate.Y2 - box.Y2) <= 0.5)
            {
                found = candidate;
                return true;
            }
        }
        return false;
    }

    private string? WriteBackupXml()
    {
        if (_xml is null || _activeCvat is not { } cvat) return null;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CvatDuplicateChecker", "backups");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"job_{cvat.Id}_{DateTime.Now:yyyyMMdd-HHmmss}.xml");
        File.WriteAllText(path, _xml);
        return path;
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCvat is not { } cvat)
        {
            ShowError("Chưa kết nối CVAT", "Xóa box chỉ dùng được khi mở dữ liệu từ CVAT.");
            return;
        }
        if (cvat.WholeTask)
        {
            ShowError("Không hỗ trợ xóa theo Task", "Hãy mở một Job cụ thể của Task để xóa box trùng.");
            return;
        }

        var deletes = new List<ShapeCandidate>();
        foreach (var row in _duplicateRows)
        {
            var marked = new[] { row.A, row.B }.Where(box => _selectionByBoxId.Contains(GetGlobalIndex(box))).ToList();
            if (marked.Count == 0) continue;
            if (marked.Count == 2)
            {
                ShowError("Không thể xóa", $"Không thể xóa hết box trong nhóm Frame {row.A.Frame} — hãy giữ lại ít nhất một box.");
                return;
            }
            foreach (var box in marked)
            {
                if (!TryFindShape(box, out var candidate))
                {
                    ShowError("Thiếu ID shape", $"Không tìm thấy shape trên CVAT cho box ở Frame {box.Frame}. Hãy bấm Tải lại Job rồi thử lại.");
                    return;
                }
                deletes.Add(candidate);
            }
        }
        var uniqueDeletes = deletes.DistinctBy(delete => delete.Id).ToList();
        if (uniqueDeletes.Count == 0)
        {
            ShowError("Chưa chọn box", "Bấm vào dòng tọa độ của box trong card để đánh dấu 'SẼ XÓA' trước.");
            return;
        }

        var frameList = uniqueDeletes.Select(delete => delete.Frame).Distinct().OrderBy(frame => frame).ToList();
        // Danh sách frame dài thì tóm tắt khoảng thay vì liệt kê tràn hộp thoại.
        var frames = frameList.Count <= 12
            ? string.Join(", ", frameList)
            : $"{frameList.Count} frame ({frameList.First()} → {frameList.Last()})";
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Xóa box trùng trên CVAT",
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBlock
                {
                    Text = $"Job #{cvat.Id}\nFrame: {frames}\nSẽ xóa {uniqueDeletes.Count} shape.\n\nXML hiện tại sẽ được sao lưu trước khi xóa. Tiếp tục?",
                    TextWrapping = TextWrapping.Wrap
                }
            },
            PrimaryButtonText = "Xóa",
            CloseButtonText = "Hủy",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (PreviewOverlay.Visibility == Visibility.Visible) ClosePreview();

        var backupPath = WriteBackupXml();
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                version = int.TryParse(_annotationVersion, out var parsedVersion) ? parsedVersion : 0,
                shapes = uniqueDeletes.Select(delete => new
                {
                    id = delete.Id,
                    type = delete.Type,
                    label_id = delete.LabelId,
                    frame = delete.Frame,
                    points = new[] { delete.X1, delete.Y1, delete.X2, delete.Y2 }
                }).ToList()
            });
            using var request = new HttpRequestMessage(HttpMethod.Patch, $"{cvat.Server}/api/jobs/{cvat.Id}/annotations?action=delete");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _activePat);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"CVAT từ chối xóa shape (HTTP {(int)response.StatusCode}): {detail}");
            }

            StatusText.Text = "Đã xóa trên CVAT. Đang tải lại annotation...";
            _xml = await ExportCvatAnnotationsAsync(cvat.Server, _activePat!, false, cvat.Id);
            await RefreshShapeIdsAsync();
            _selectionByBoxId.Clear();
            await AnalyzeCurrentAsync();
            if (PreviewOverlay.Visibility == Visibility.Visible) ClosePreview();

            ResultInfoBar.Title = $"Đã xóa {uniqueDeletes.Count} shape trên CVAT";
            ResultInfoBar.Message = backupPath is null ? "Đã tải lại annotation sau khi xóa." : $"Bản sao trước khi xóa: {backupPath}";
            ResultInfoBar.Severity = InfoBarSeverity.Success;
            ResultInfoBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            ShowError("Không thể xóa shape trên CVAT", FriendlyCvatError(ex));
        }
    }

    private void RestoreListSelection()
    {
        if (_selectedRow is not { } selected) return;
        var match = DuplicateListView.Items.OfType<DuplicateItem>().FirstOrDefault(item => item.Row == selected);
        if (match is null || ReferenceEquals(DuplicateListView.SelectedItem, match)) return;
        _suppressSelectionChanged = true;
        DuplicateListView.SelectedItem = match;
        _suppressSelectionChanged = false;
    }

    // ── Lọc theo nhãn (port từ DuplicateList web) ──
    private void RebuildLabelChips(bool resetSelection)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _duplicateRows)
        {
            foreach (var label in new[] { row.A.Label, row.B.Label })
                counts[label] = counts.TryGetValue(label, out var count) ? count + 1 : 1;
        }

        if (resetSelection)
        {
            _selectedLabels.Clear();
            foreach (var label in counts.Keys) _selectedLabels.Add(label);
        }
        else
        {
            _selectedLabels.RemoveWhere(label => !counts.ContainsKey(label));
        }

        LabelChipsView.ItemsSource = counts.Select(pair => CreateLabelChip(pair.Key, pair.Value)).ToList();
        SelectAllLabelsButton.Visibility = counts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Nút chỉ có tác dụng khi đang bỏ chọn ít nhất một nhãn — tránh trạng thái bấm không phản hồi.
        var allSelected = counts.Count > 0 && _selectedLabels.Count >= counts.Count;
        SelectAllLabelsButton.IsEnabled = !allSelected;
        SelectAllLabelsButton.Opacity = allSelected ? 0.5 : 1.0;
        ToolTipService.SetToolTip(SelectAllLabelsButton, allSelected
            ? "Đang chọn đủ mọi nhãn có trùng lặp — bỏ chọn chip nhãn để lọc, nút này sẽ bật lại để chọn đủ lại"
            : "Chọn lại toàn bộ nhãn có trùng lặp");
    }

    private LabelChipItem CreateLabelChip(string label, int count)
    {
        var selected = _selectedLabels.Contains(label);
        var color = ParseColor(GetLabelColor(label));
        return new LabelChipItem(
            label,
            count.ToString(CultureInfo.InvariantCulture),
            new SolidColorBrush(selected ? color : Color.FromArgb(255, 25, 35, 52)),
            new SolidColorBrush(selected ? Microsoft.UI.Colors.White : Color.FromArgb(255, 154, 169, 191)),
            new SolidColorBrush(selected ? Color.FromArgb(70, color.R, color.G, color.B) : Color.FromArgb(255, 13, 21, 36)),
            new SolidColorBrush(selected ? color : Color.FromArgb(255, 42, 58, 82)));
    }

    private void LabelChip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not LabelChipItem chip) return;
        if (_selectedLabels.Contains(chip.Label))
        {
            if (_selectedLabels.Count == 1) return; // luôn giữ ít nhất một nhãn như bản web
            _selectedLabels.Remove(chip.Label);
        }
        else
        {
            _selectedLabels.Add(chip.Label);
        }
        _currentPage = 1;
        RebuildLabelChips(resetSelection: false);
        RefreshList();
    }

    private void SelectAllLabels_Click(object sender, RoutedEventArgs e)
    {
        _currentPage = 1;
        RebuildLabelChips(resetSelection: true);
        RefreshList();
    }

    // ── Tab nguồn ở landing ──
    private void SourceTabCvat_Click(object sender, RoutedEventArgs e) => SetSourceTab(cvat: true);

    private void SourceTabFile_Click(object sender, RoutedEventArgs e) => SetSourceTab(cvat: false);

    private void SetSourceTab(bool cvat)
    {
        CvatSourcePanel.Visibility = cvat ? Visibility.Visible : Visibility.Collapsed;
        FileSourcePanel.Visibility = cvat ? Visibility.Collapsed : Visibility.Visible;
        var active = new SolidColorBrush(Color.FromArgb(255, 61, 99, 217));
        var inactive = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
        SourceTabCvatButton.Background = cvat ? active : inactive;
        SourceTabFileButton.Background = cvat ? inactive : active;
    }

    private static int? ToFrameNumber(double value) => double.IsNaN(value) ? null : Math.Max(0, (int)value);

    private void DuplicateListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged) return;
        if (DuplicateListView.SelectedItem is not DuplicateItem item) return;
        _selectedRow = item.Row;
        _quickReviewVisual = null;
        _quickReviewLoading = true;
        _quickViewScale = 1;
        _quickPanX = 0;
        _quickPanY = 0;
        StopQuickZoomAnimation();
        _quickActiveTransform = null;
        _quickCurrentScale = _quickTargetScale = 1;
        _quickCurrentTX = _quickTargetTX = 0;
        _quickCurrentTY = _quickTargetTY = 0;
        var row = item.Row;
        DispatcherQueue.TryEnqueue(() =>
        {
            RefreshList();
            _ = LoadQuickReviewAsync(row);
        });
    }

    private async void OpenPreviewDetail_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not DuplicateItem item) return;
        if (!ReferenceEquals(DuplicateListView.SelectedItem, item)) DuplicateListView.SelectedItem = item;
        _selectedRow = item.Row;
        _previewRow = item.Row;
        await OpenPreviewAsync(item.Row);
    }

    // ── Ô xem nhanh ảnh frame ngay trong card đang chọn ──
    private string QuickReviewCacheKey(Box box) => _activeCvat is { } cvat
        ? $"cvat|{cvat.Server}|{(cvat.WholeTask ? "task" : "job")}|{cvat.Id}|{box.Frame}"
        : $"file|{_selectedPath}|{box.FrameName}";

    private async Task LoadQuickReviewAsync((Box A, Box B, double Overlap) row)
    {
        var requestVersion = ++_quickReviewRequestVersion;
        try
        {
            (ImageSource Source, double Width, double Height)? image =
                _frameImageCache.TryGetValue(QuickReviewCacheKey(row.A), out var cached) ? cached : null;

            if (image is null)
            {
                var result = await LoadPreviewImageAsync(row.A);
                if (requestVersion != _quickReviewRequestVersion || _selectedRow != row) return;
                if (result.Bytes is not null)
                {
                    using var stream = new InMemoryRandomAccessStream();
                    using (var writer = new DataWriter(stream))
                    {
                        writer.WriteBytes(result.Bytes);
                        await writer.StoreAsync();
                        writer.DetachStream();
                    }
                    stream.Seek(0);
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);
                    if (bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
                    {
                        image = (bitmap, bitmap.PixelWidth, bitmap.PixelHeight);
                        _frameImageCache[QuickReviewCacheKey(row.A)] = image.Value;
                    }
                }
            }

            if (requestVersion != _quickReviewRequestVersion || _selectedRow != row) return;

            _quickReviewLoading = false;
            _quickReviewVisual = image is { } loaded
                ? BuildQuickReview(loaded.Source, loaded.Width, loaded.Height, row)
                : BuildQuickReview(
                    null,
                    Math.Max(1, row.A.FrameWidth > 0 ? row.A.FrameWidth : Math.Ceiling(Math.Max(row.A.X2, row.B.X2))),
                    Math.Max(1, row.A.FrameHeight > 0 ? row.A.FrameHeight : Math.Ceiling(Math.Max(row.A.Y2, row.B.Y2))),
                    row);
            RefreshList();
        }
        catch
        {
            // Quick review chỉ là phần bổ trợ; lỗi tải ảnh thì card hiển thị không kèm ảnh.
            if (requestVersion == _quickReviewRequestVersion && _selectedRow == row)
            {
                _quickReviewLoading = false;
                RefreshList();
            }
        }
    }

    // Ô giữ chỗ có vòng xoay trong lúc chờ tải ảnh frame, thay cho nền đen.
    private static UIElement BuildQuickReviewLoading()
    {
        return new Border
        {
            Width = 300,
            Height = 190,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(ParseColor("#050810")),
            BorderBrush = new SolidColorBrush(ParseColor("#263750")),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 10,
                Children =
                {
                    new ProgressRing
                    {
                        IsActive = true,
                        Width = 34,
                        Height = 34,
                        Foreground = new SolidColorBrush(ParseColor("#38BDF8"))
                    },
                    new TextBlock
                    {
                        Text = "Đang tải ảnh…",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(ParseColor("#94A3B8"))
                    }
                }
            }
        };
    }

    private UIElement BuildQuickReview(ImageSource? source, double imageWidth, double imageHeight, (Box A, Box B, double Overlap) row)
    {
        const double viewWidth = 300;
        const double viewHeight = 190;
        var pad = Math.Max(24, Math.Max(row.A.X2 - row.A.X1, row.A.Y2 - row.A.Y1) * 0.3);
        var left = Math.Max(0, Math.Min(row.A.X1, row.B.X1) - pad);
        var top = Math.Max(0, Math.Min(row.A.Y1, row.B.Y1) - pad);
        var right = Math.Min(imageWidth, Math.Max(row.A.X2, row.B.X2) + pad);
        var bottom = Math.Min(imageHeight, Math.Max(row.A.Y2, row.B.Y2) + pad);
        var cropWidth = Math.Max(1, right - left);
        var cropHeight = Math.Max(1, bottom - top);
        var scale = Math.Min(viewWidth / cropWidth, viewHeight / cropHeight);

        var canvas = new Canvas { Width = imageWidth, Height = imageHeight };
        AddQuickReviewBox(canvas, row.A, "A", scale, 0);
        AddQuickReviewBox(canvas, row.B, "B", scale, 1);

        var surface = new Grid { Width = imageWidth, Height = imageHeight };
        if (source is not null) surface.Children.Add(new Image { Source = source, Stretch = Stretch.Fill });
        surface.Children.Add(canvas);
        surface.RenderTransform = new CompositeTransform
        {
            ScaleX = scale,
            ScaleY = scale,
            TranslateX = (viewWidth - cropWidth * scale) / 2 - left * scale,
            TranslateY = (viewHeight - cropHeight * scale) / 2 - top * scale
        };

        // Lớp ngoài nhận zoom/pan bằng chuột; trạng thái zoom giữ qua các lần refresh card.
        var outer = new Grid { Width = viewWidth, Height = viewHeight };
        outer.Children.Add(surface);
        var userTransform = new CompositeTransform
        {
            ScaleX = _quickViewScale,
            ScaleY = _quickViewScale,
            TranslateX = _quickPanX,
            TranslateY = _quickPanY
        };
        outer.RenderTransform = userTransform;
        _quickActiveTransform = userTransform;
        _quickCurrentScale = _quickTargetScale = _quickViewScale;
        _quickCurrentTX = _quickTargetTX = _quickPanX;
        _quickCurrentTY = _quickTargetTY = _quickPanY;

        var border = new Border
        {
            Width = viewWidth,
            Height = viewHeight,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(ParseColor("#050810")),
            BorderBrush = new SolidColorBrush(ParseColor("#263750")),
            BorderThickness = new Thickness(1),
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, viewWidth, viewHeight) },
            Child = outer,
            Tag = userTransform
        };
        border.PointerWheelChanged += QuickView_PointerWheelChanged;
        border.PointerPressed += QuickView_PointerPressed;
        border.PointerMoved += QuickView_PointerMoved;
        border.PointerReleased += QuickView_PointerReleased;
        border.PointerCanceled += QuickView_PointerCanceled;
        return border;
    }

    // Zoom quick preview bằng wheel và kéo chuột để di chuyển ảnh; con trỏ trong khung thì khóa cuộn trang.
    private void QuickView_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border border || border.Tag is not CompositeTransform t) return;
        var point = e.GetCurrentPoint(border);
        var newScale = Math.Clamp(_quickTargetScale * Math.Exp(point.Properties.MouseWheelDelta * 0.0018), 1, 10);
        if (Math.Abs(newScale - _quickTargetScale) < 0.0001) return;
        var p = point.Position;
        _quickTargetTX = p.X - (p.X - _quickTargetTX) * (newScale / _quickTargetScale);
        _quickTargetTY = p.Y - (p.Y - _quickTargetTY) * (newScale / _quickTargetScale);
        _quickTargetScale = newScale;
        if (newScale <= 1.0001)
        {
            _quickTargetTX = 0;
            _quickTargetTY = 0;
        }
        _quickViewScale = _quickTargetScale;
        _quickPanX = _quickTargetTX;
        _quickPanY = _quickTargetTY;
        _quickActiveTransform = t;
        StartQuickZoomAnimation();
    }

    // Mỗi frame render tiến dần về mốc zoom đích thay vì nhảy cóc → zoom mượt, điểm dưới con trỏ giữ cố định.
    private void StartQuickZoomAnimation()
    {
        if (_quickZoomAnimating) return;
        _quickZoomAnimating = true;
        CompositionTarget.Rendering += QuickZoomFrame;
    }

    private void StopQuickZoomAnimation()
    {
        if (!_quickZoomAnimating) return;
        _quickZoomAnimating = false;
        CompositionTarget.Rendering -= QuickZoomFrame;
    }

    private void QuickZoomFrame(object? sender, object e)
    {
        const double ease = 0.3;
        _quickCurrentScale += (_quickTargetScale - _quickCurrentScale) * ease;
        _quickCurrentTX += (_quickTargetTX - _quickCurrentTX) * ease;
        _quickCurrentTY += (_quickTargetTY - _quickCurrentTY) * ease;
        if (Math.Abs(_quickTargetScale - _quickCurrentScale) < 0.002 &&
            Math.Abs(_quickTargetTX - _quickCurrentTX) < 0.4 &&
            Math.Abs(_quickTargetTY - _quickCurrentTY) < 0.4)
        {
            _quickCurrentScale = _quickTargetScale;
            _quickCurrentTX = _quickTargetTX;
            _quickCurrentTY = _quickTargetTY;
            StopQuickZoomAnimation();
        }
        if (_quickActiveTransform is { } t)
        {
            t.ScaleX = _quickCurrentScale;
            t.ScaleY = _quickCurrentScale;
            t.TranslateX = _quickCurrentTX;
            t.TranslateY = _quickCurrentTY;
        }
    }

    private void QuickView_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border) return;
        var point = e.GetCurrentPoint(border);
        if (!point.Properties.IsLeftButtonPressed) return;
        _quickDragging = true;
        _quickDragPointerId = point.PointerId;
        _quickDragX = point.Position.X;
        _quickDragY = point.Position.Y;
        border.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void QuickView_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_quickDragging || sender is not Border border || border.Tag is not CompositeTransform t) return;
        var point = e.GetCurrentPoint(border);
        if (point.PointerId != _quickDragPointerId) return;
        // Chỉ cập nhật mốc đích; vòng lặp render tự lướt theo để pan mượt không khựng.
        _quickTargetTX += point.Position.X - _quickDragX;
        _quickTargetTY += point.Position.Y - _quickDragY;
        _quickPanX = _quickTargetTX;
        _quickPanY = _quickTargetTY;
        _quickDragX = point.Position.X;
        _quickDragY = point.Position.Y;
        _quickActiveTransform = t;
        StartQuickZoomAnimation();
        e.Handled = true;
    }

    private void QuickView_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_quickDragging && sender is Border border && e.Pointer.PointerId == _quickDragPointerId)
        {
            _quickDragging = false;
            border.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }
    }

    private void QuickView_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_quickDragging && sender is Border border)
        {
            _quickDragging = false;
            border.ReleasePointerCapture(e.Pointer);
        }
    }

    private static void AddQuickReviewBox(Canvas canvas, Box box, string letter, double scale, int slot)
    {
        var color = ParseColor(GetLabelColor(box.Label));
        var rectangle = new Rectangle
        {
            Width = Math.Max(1, box.X2 - box.X1),
            Height = Math.Max(1, box.Y2 - box.Y1),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2.5 / scale,
            Fill = new SolidColorBrush(Color.FromArgb(30, color.R, color.G, color.B))
        };
        Canvas.SetLeft(rectangle, box.X1);
        Canvas.SetTop(rectangle, box.Y1);
        canvas.Children.Add(rectangle);

        var text = new TextBlock
        {
            Text = letter,
            FontSize = 13 / scale,
            FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
        };
        // Hai box trùng khít thì chữ A/B nằm cùng góc — xếp B sang phải chữ A theo slot.
        Canvas.SetLeft(text, box.X1 + (3 + slot * 15) / scale);
        Canvas.SetTop(text, box.Y1 + 1 / scale);
        canvas.Children.Add(text);
    }

    private async Task OpenPreviewAsync((Box A, Box B, double Overlap) row)
    {
        var requestVersion = ++_previewRequestVersion;
        PreviewFrameText.Text = $"Preview · Frame {row.A.Frame} · {row.A.Label} · IoU {row.Overlap:0.##}%";
        PreviewImage.Source = null;
        PreviewErrorBanner.Visibility = Visibility.Collapsed;
        PreviewLoadingOverlay.Visibility = Visibility.Visible;
        PreviewLoadingFrameText.Text = $"Frame {row.A.Frame}";
        PreviewOverlay.Visibility = Visibility.Visible;
        PreviewOverlay.Focus(FocusState.Programmatic);

        var result = await LoadPreviewImageAsync(row.A);
        if (requestVersion != _previewRequestVersion || _previewRow != row) return;

        var width = Math.Max(1, row.A.FrameWidth > 0 ? row.A.FrameWidth : Math.Ceiling(Math.Max(row.A.X2, row.B.X2)));
        var height = Math.Max(1, row.A.FrameHeight > 0 ? row.A.FrameHeight : Math.Ceiling(Math.Max(row.A.Y2, row.B.Y2)));
        var hasImage = false;

        if (result.Bytes is not null)
        {
            try
            {
                using var stream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(stream))
                {
                    writer.WriteBytes(result.Bytes);
                    await writer.StoreAsync();
                    writer.DetachStream();
                }
                stream.Seek(0);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                PreviewImage.Source = bitmap;
                if (bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
                {
                    width = bitmap.PixelWidth;
                    height = bitmap.PixelHeight;
                    hasImage = true;
                }
                else
                {
                    result = result with { UserMessage = "Ảnh đã tải nhưng không đọc được kích thước frame." };
                }
            }
            catch
            {
                result = result with { Bytes = null, UserMessage = "Ảnh đã tải nhưng không thể giải mã. Hãy kiểm tra định dạng ảnh của frame." };
            }
        }

        PreviewLoadingOverlay.Visibility = Visibility.Collapsed;
        if (!string.IsNullOrEmpty(result.UserMessage))
        {
            PreviewErrorText.Text = result.UserMessage;
            PreviewErrorBanner.Visibility = Visibility.Visible;
        }

        PreviewDimensionsText.Text = $"Kích thước: {width:0} × {height:0} px";
        PreviewImageStatusText.Text = hasImage
            ? "✦ Đã tải ảnh thực tế"
            : result.Attempted ? $"Không tải được ảnh · {result.Source}" : "Chỉ có file XML (Vẽ mô phỏng)";
        PreviewImageStatusText.Foreground = new SolidColorBrush(hasImage
            ? Color.FromArgb(255, 52, 211, 153)
            : result.Attempted ? Color.FromArgb(255, 251, 113, 133) : Color.FromArgb(255, 100, 116, 139));

        PreviewSurface.Width = width;
        PreviewSurface.Height = height;
        PreviewCanvas.Width = width;
        PreviewCanvas.Height = height;
        DrawPreviewBoxes(row);

        _previewFitRequestVersion = requestVersion;
        _previewFitPending = true;
        RequestPreviewFit(requestVersion);
    }

    private async Task<ImageLoadResult> LoadPreviewImageAsync(Box box)
    {
        if (_activeCvat is { } cvat)
        {
            var resource = cvat.WholeTask ? "tasks" : "jobs";
            var source = $"CVAT {resource[..^1]} #{cvat.Id}";
            if (box.Frame < 0)
                return new ImageLoadResult(source, true, null, "Frame không hợp lệ để tải từ CVAT.");

            try
            {
                var url = $"{cvat.Server}/api/{resource}/{cvat.Id}/data?type=frame&number={box.Frame}&quality=compressed";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _activePat);
                request.Headers.Accept.ParseAdd("*/*");
                using var response = await Http.SendAsync(request);
                var bytes = await response.Content.ReadAsByteArrayAsync();
                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (!response.IsSuccessStatusCode)
                {
                    var detail = response.StatusCode switch
                    {
                        System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => "Kiểm tra PAT hoặc quyền xem dữ liệu task/job.",
                        System.Net.HttpStatusCode.NotFound => "Không tìm thấy frame hoặc Task/Job đã chọn.",
                        _ => "CVAT không trả về ảnh cho frame này."
                    };
                    return new ImageLoadResult(source, true, null, $"{detail} (HTTP {(int)response.StatusCode})");
                }
                if (bytes.Length == 0)
                    return new ImageLoadResult(source, true, null, "CVAT trả về ảnh rỗng cho frame này.");
                if (!string.IsNullOrWhiteSpace(contentType) && !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    return new ImageLoadResult(source, true, null, $"CVAT trả về dữ liệu không phải ảnh ({contentType}).");
                return new ImageLoadResult(source, true, bytes, null);
            }
            catch (HttpRequestException)
            {
                return new ImageLoadResult(source, true, null, "Không kết nối được tới CVAT. Kiểm tra mạng, VPN hoặc URL server.");
            }
            catch (TaskCanceledException)
            {
                return new ImageLoadResult(source, true, null, "Yêu cầu tải ảnh từ CVAT đã hết thời gian chờ.");
            }
        }

        if (_selectedPath?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) != true)
            return new ImageLoadResult("XML", false, null, null);

        var targetName = Path.GetFileName(box.FrameName);
        if (string.IsNullOrWhiteSpace(targetName))
            return new ImageLoadResult("ZIP", true, null, "Không xác định được tên ảnh của frame trong XML.");

        try
        {
            using var archive = ZipFile.OpenRead(_selectedPath);
            var entry = archive.Entries.FirstOrDefault(x => !string.IsNullOrEmpty(x.Name) &&
                string.Equals(Path.GetFileName(x.FullName), targetName, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                return new ImageLoadResult("ZIP", true, null, $"Không tìm thấy ảnh ‘{targetName}’ trong ZIP.");
            if (entry.Length == 0)
                return new ImageLoadResult("ZIP", true, null, $"Ảnh ‘{targetName}’ trong ZIP đang rỗng.");
            await using var source = entry.Open();
            using var memory = new MemoryStream();
            await source.CopyToAsync(memory);
            return new ImageLoadResult("ZIP", true, memory.ToArray(), null);
        }
        catch (InvalidDataException)
        {
            return new ImageLoadResult("ZIP", true, null, "Không thể đọc archive ZIP chứa ảnh.");
        }
        catch (IOException)
        {
            return new ImageLoadResult("ZIP", true, null, "Không thể đọc ảnh từ ZIP. File có thể đang được ứng dụng khác sử dụng.");
        }
    }

    private void DrawPreviewBoxes((Box A, Box B, double Overlap) row)
    {
        PreviewCanvas.Children.Clear();

        // Collect all duplicate box nodes on this frame for highlighting
        var duplicateNodeSet = new HashSet<XElement>(
            _duplicateRows.Where(r => r.A.Frame == row.A.Frame)
                .SelectMany(r => new[] { r.A.Node, r.B.Node }));

        // Draw all regular (non-duplicate) boxes on the same frame — dimmed with dashed stroke
        if (_showAllBoxes)
        {
            foreach (var box in _allBoxes.Where(b => b.Frame == row.A.Frame && !b.Outside && !duplicateNodeSet.Contains(b.Node)))
            {
                var labelColor = ParseColor(GetLabelColor(box.Label));
                AddPreviewBox(box, labelColor, 1.5, true, opacity: 0.3);
                AddLabelBadge(box, labelColor, opacity: 0.4);
            }
        }

        // Draw all duplicate boxes on this frame — highlighted
        if (_showAllBoxes)
        {
            foreach (var dup in _duplicateRows.Where(r => r.A.Frame == row.A.Frame))
            {
                foreach (var box in new[] { dup.A, dup.B })
                {
                    if (ReferenceEquals(box.Node, row.A.Node) || ReferenceEquals(box.Node, row.B.Node)) continue;
                    var labelColor = ParseColor(GetLabelColor(box.Label));
                    AddPreviewBox(box, labelColor, 2.5, false, opacity: 0.6);
                    AddLabelBadge(box, labelColor, opacity: 0.7);
                }
            }
        }

        // Draw the selected pair on top using semantic label colors; selection is conveyed by weight.
        var colorA = ParseColor(GetLabelColor(row.A.Label));
        var colorB = ParseColor(GetLabelColor(row.B.Label));
        var markedA = _selectionByBoxId.Contains(GetGlobalIndex(row.A));
        var markedB = _selectionByBoxId.Contains(GetGlobalIndex(row.B));
        AddPreviewBox(row.A, colorA, 4, false);
        if (markedA) AddMarkedOverlay(row.A);
        AddLabelBadge(row.A, colorA, yOffset: 0, globalIndex: GetGlobalIndex(row.A), letter: "A", marked: markedA);
        AddPreviewBox(row.B, colorB, 3, true);
        if (markedB) AddMarkedOverlay(row.B);
        AddLabelBadge(row.B, colorB, yOffset: -18, globalIndex: GetGlobalIndex(row.B), letter: "B", marked: markedB);
    }

    // Khung nét đứt đỏ phủ lên box đã đánh dấu xóa trong preview.
    private void AddMarkedOverlay(Box box)
    {
        var rectangle = new Rectangle
        {
            Width = Math.Max(1, box.X2 - box.X1),
            Height = Math.Max(1, box.Y2 - box.Y1),
            Stroke = new SolidColorBrush(ParseColor("#FB7185")),
            StrokeThickness = 3,
            StrokeDashArray = new DoubleCollection { 6, 4 },
            Fill = new SolidColorBrush(Color.FromArgb(40, 0xFB, 0x71, 0x85))
        };
        Canvas.SetLeft(rectangle, box.X1);
        Canvas.SetTop(rectangle, box.Y1);
        PreviewCanvas.Children.Add(rectangle);
    }

    private void AddLabelBadge(Box box, Color color, double opacity = 1.0, double yOffset = 0, int? globalIndex = null, string? letter = null, bool marked = false)
    {
        var label = globalIndex is { } index ? $"#{index} {box.Label}" : box.Label;
        if (letter is not null) label = $"{letter} · {label}";
        if (marked) label += " · SẼ XÓA";
        var badgeWidth = Math.Max(label.Length * 7 + 30, 95);
        const double badgeHeight = 16;

        var badgeRect = new Rectangle
        {
            Width = badgeWidth,
            Height = badgeHeight,
            Fill = new SolidColorBrush(marked ? ParseColor("#FB7185") : color),
            RadiusX = 3,
            RadiusY = 3,
            Opacity = opacity
        };
        Canvas.SetLeft(badgeRect, box.X1);
        Canvas.SetTop(badgeRect, box.Y1 - badgeHeight + yOffset);
        PreviewCanvas.Children.Add(badgeRect);

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 9.5,
            FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            Opacity = opacity
        };
        Canvas.SetLeft(labelText, box.X1 + 5);
        Canvas.SetTop(labelText, box.Y1 - badgeHeight + yOffset + 1);
        PreviewCanvas.Children.Add(labelText);
    }

    private void AddPreviewBox(Box box, Color color, double thickness, bool dashed, double opacity = 1.0)
    {
        var rectangle = new Rectangle
        {
            Width = Math.Max(1, box.X2 - box.X1),
            Height = Math.Max(1, box.Y2 - box.Y1),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            Fill = new SolidColorBrush(Color.FromArgb(28, color.R, color.G, color.B)),
            Opacity = opacity
        };
        if (dashed) rectangle.StrokeDashArray = new DoubleCollection { 8, 5 };
        Canvas.SetLeft(rectangle, box.X1);
        Canvas.SetTop(rectangle, box.Y1);
        PreviewCanvas.Children.Add(rectangle);
    }

    private void PreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_previewFitPending) RequestPreviewFit(_previewFitRequestVersion);
    }

    private void RequestPreviewFit(long requestVersion)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (requestVersion != _previewRequestVersion ||
                requestVersion != _previewFitRequestVersion ||
                !_previewFitPending ||
                PreviewOverlay.Visibility != Visibility.Visible)
            {
                return;
            }

            if (FitPreview()) _previewFitPending = false;
        });
    }

    private bool FitPreview()
    {
        var contentWidth = PreviewSurface.Width;
        var contentHeight = PreviewSurface.Height;
        var viewportWidth = PreviewViewport.ActualWidth;
        var viewportHeight = PreviewViewport.ActualHeight;
        if (contentWidth <= 0 || contentHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0 ||
            double.IsNaN(viewportWidth) || double.IsNaN(viewportHeight))
        {
            return false;
        }

        var scale = Math.Clamp(Math.Min(Math.Min(viewportWidth / contentWidth, viewportHeight / contentHeight), 1.0), PreviewMinScale, PreviewMaxScale);
        SetPreviewTransform(scale,
            (viewportWidth - contentWidth * scale) / 2,
            (viewportHeight - contentHeight * scale) / 2,
            constrain: false);
        return true;
    }

    private void PreviewViewport_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (PreviewSurface.Width <= 0 || PreviewSurface.Height <= 0) return;
        var point = e.GetCurrentPoint(PreviewViewport).Position;
        var delta = e.GetCurrentPoint(PreviewViewport).Properties.MouseWheelDelta;
        var newScale = Math.Clamp(_previewScale * Math.Exp(delta * 0.0018), PreviewMinScale, PreviewMaxScale);
        if (Math.Abs(newScale - _previewScale) < 0.0001) return;

        var imageX = (point.X - _previewPanX) / _previewScale;
        var imageY = (point.Y - _previewPanY) / _previewScale;
        SetPreviewTransform(newScale, point.X - imageX * newScale, point.Y - imageY * newScale, constrain: true);
        e.Handled = true;
    }

    private void PreviewViewport_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(PreviewViewport);
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && point.Properties.IsLeftButtonPressed)
        {
            _isPreviewDragging = true;
            _previewDragPointerId = point.PointerId;
            _previewDragX = point.Position.X;
            _previewDragY = point.Position.Y;
            _previewClickCandidate = true;
            _previewPressX = point.Position.X;
            _previewPressY = point.Position.Y;
            PreviewViewport.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void PreviewViewport_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPreviewDragging || e.Pointer.PointerId != _previewDragPointerId) return;
        var point = e.GetCurrentPoint(PreviewViewport);
        if (_previewClickCandidate && Math.Sqrt(Math.Pow(point.Position.X - _previewPressX, 2) + Math.Pow(point.Position.Y - _previewPressY, 2)) > 5)
            _previewClickCandidate = false;
        SetPreviewTransform(_previewScale,
            _previewPanX + point.Position.X - _previewDragX,
            _previewPanY + point.Position.Y - _previewDragY,
            constrain: true);
        _previewDragX = point.Position.X;
        _previewDragY = point.Position.Y;
        e.Handled = true;
    }

    private void PreviewViewport_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(PreviewViewport);
        var wasClick = _previewClickCandidate && e.Pointer.PointerId == _previewDragPointerId;
        _previewClickCandidate = false;
        if (_isPreviewDragging && e.Pointer.PointerId == _previewDragPointerId)
        {
            StopPreviewDragging(e.Pointer);
            e.Handled = true;
        }
        if (wasClick) TogglePreviewBoxAt(point.Position);
    }

    // Click (không kéo) lên preview: rơi vào box A/B thì đảo đánh dấu "SẼ XÓA" giống bấm dòng tọa độ trên card.
    private void TogglePreviewBoxAt(Point position)
    {
        if (_previewRow is not { } row) return;
        var imageX = (position.X - _previewPanX) / _previewScale;
        var imageY = (position.Y - _previewPanY) / _previewScale;
        Box? hit = null;
        foreach (var box in new[] { row.B, row.A })
        {
            if (imageX >= box.X1 - 4 && imageX <= box.X2 + 4 && imageY >= box.Y1 - 4 && imageY <= box.Y2 + 4)
            {
                hit = box;
                break;
            }
        }
        if (hit is null) return;
        // Trong preview chỉ đánh dấu đúng box đã bấm; xóa hàng loạt theo bản sao trùng tọa độ chỉ dùng ở danh sách card.
        var index = GetGlobalIndex(hit);
        if (!_selectionByBoxId.Remove(index)) _selectionByBoxId.Add(index);
        DrawPreviewBoxes(row);
        RefreshList();
    }

    private void PreviewViewport_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_isPreviewDragging && e.Pointer.PointerId == _previewDragPointerId)
            StopPreviewDragging(e.Pointer);
    }

    private void StopPreviewDragging(Pointer pointer)
    {
        _isPreviewDragging = false;
        PreviewViewport.ReleasePointerCapture(pointer);
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ZoomAtViewportCenter(1.25);

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ZoomAtViewportCenter(0.8);

    private void FitPreviewButton_Click(object sender, RoutedEventArgs e) => FitPreview();

    private void ZoomAtViewportCenter(double factor)
    {
        var center = new Point(PreviewViewport.ActualWidth / 2, PreviewViewport.ActualHeight / 2);
        var newScale = Math.Clamp(_previewScale * factor, PreviewMinScale, PreviewMaxScale);
        var imageX = (center.X - _previewPanX) / _previewScale;
        var imageY = (center.Y - _previewPanY) / _previewScale;
        SetPreviewTransform(newScale, center.X - imageX * newScale, center.Y - imageY * newScale, constrain: true);
    }

    private void FocusPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_previewRow is not { } row) return;
        var viewportWidth = PreviewViewport.ActualWidth;
        var viewportHeight = PreviewViewport.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        var padding = ZoomPaddingSlider.Value;
        var left = Math.Max(0, Math.Min(row.A.X1, row.B.X1) - padding);
        var top = Math.Max(0, Math.Min(row.A.Y1, row.B.Y1) - padding);
        var right = Math.Min(PreviewSurface.Width, Math.Max(row.A.X2, row.B.X2) + padding);
        var bottom = Math.Min(PreviewSurface.Height, Math.Max(row.A.Y2, row.B.Y2) + padding);
        var targetWidth = right - left;
        var targetHeight = bottom - top;
        if (targetWidth <= 0 || targetHeight <= 0) return;

        var scale = Math.Clamp(Math.Min(Math.Min(viewportWidth / targetWidth, viewportHeight / targetHeight), 5.0), PreviewMinScale, PreviewMaxScale);
        SetPreviewTransform(scale,
            viewportWidth / 2 - ((left + right) / 2) * scale,
            viewportHeight / 2 - ((top + bottom) / 2) * scale,
            constrain: true);
    }

    private void SetPreviewTransform(double scale, double panX, double panY, bool constrain)
    {
        if (constrain) ConstrainPreviewPan(scale, ref panX, ref panY);
        _previewScale = scale;
        _previewPanX = panX;
        _previewPanY = panY;
        PreviewTransform.ScaleX = scale;
        PreviewTransform.ScaleY = scale;
        PreviewTransform.TranslateX = panX;
        PreviewTransform.TranslateY = panY;
        PreviewZoomText.Text = $"{scale * 100:0}%";
    }

    private void ConstrainPreviewPan(double scale, ref double panX, ref double panY)
    {
        var viewportWidth = PreviewViewport.ActualWidth;
        var viewportHeight = PreviewViewport.ActualHeight;
        var scaledWidth = PreviewSurface.Width * scale;
        var scaledHeight = PreviewSurface.Height * scale;
        if (scaledWidth <= viewportWidth)
            panX = (viewportWidth - scaledWidth) / 2;
        else
            panX = Math.Clamp(panX, viewportWidth - scaledWidth, 0);
        if (scaledHeight <= viewportHeight)
            panY = (viewportHeight - scaledHeight) / 2;
        else
            panY = Math.Clamp(panY, viewportHeight - scaledHeight, 0);
    }

    private void ZoomPaddingSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ZoomPaddingText is not null) ZoomPaddingText.Text = $"±{e.NewValue:0}px";
    }

    private void ToggleAllBoxesButton_Click(object sender, RoutedEventArgs e)
    {
        _showAllBoxes = !_showAllBoxes;
        if (_previewRow is { } row) DrawPreviewBoxes(row);
    }

    private void ClosePreviewButton_Click(object sender, RoutedEventArgs e) => ClosePreview();

    private void PreviewOverlay_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape) ClosePreview();
    }

    private void ClosePreview()
    {
        _previewRequestVersion++;
        _previewFitPending = false;
        _isPreviewDragging = false;
        PreviewOverlay.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        PreviewCanvas.Children.Clear();
        _previewRow = null;
    }

    private void CloseWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        ResetResults();
        LandingPanel.Visibility = Visibility.Visible;
        WorkspacePanel.Visibility = Visibility.Collapsed;
    }

    private void ResetResults()
    {
        _selectedPath = null;
        _xml = null;
        _activeCvat = null;
        _allBoxes = new List<Box>();
        _globalIndexByNode = new Dictionary<XElement, int>();
        _frameStart = null;
        _frameEnd = null;
        _currentPage = 1;
        ClosePreview();
        _duplicateRows.Clear();
        _selectedRow = null;
        _quickReviewVisual = null;
        _quickReviewLoading = false;
        _quickViewScale = 1;
        _quickPanX = 0;
        _quickPanY = 0;
        StopQuickZoomAnimation();
        _quickActiveTransform = null;
        _quickCurrentScale = _quickTargetScale = 1;
        _quickCurrentTX = _quickTargetTX = 0;
        _quickCurrentTY = _quickTargetTY = 0;
        _quickReviewRequestVersion++;
        _frameImageCache.Clear();
        _selectedLabels.Clear();
        _selectionByBoxId.Clear();
        _shapeCandidates.Clear();
        DeleteSelectedButton.Visibility = Visibility.Collapsed;
        PreviewDeleteButton.Visibility = Visibility.Collapsed;
        LabelChipsView.ItemsSource = null;
        _overlapFilterKey = "all";
        RebuildOverlapChips();
        UpdateDeleteButtons();
        DuplicateListView.ItemsSource = null;
        SearchTextBox.Text = "";
        FrameStartBox.Value = double.NaN;
        FrameEndBox.Value = double.NaN;
        DuplicateCountText.Text = "0 box trùng";
        DuplicateFrameCountText.Text = "0 frame";
        DuplicateListTitle.Text = "Danh sách trùng lặp";
        PageText.Text = "Trang 1 / 1";
        ResultInfoBar.IsOpen = false;
    }

    private void ShowError(string title, string message)
    {
        ResultInfoBar.Title = title;
        ResultInfoBar.Message = message;
        ResultInfoBar.Severity = InfoBarSeverity.Error;
        ResultInfoBar.IsOpen = true;
        // Đang ở landing thì báo lỗi ngay tại landing, không chuyển sang giao diện có dữ liệu.
        if (WorkspacePanel.Visibility == Visibility.Collapsed && CvatErrorText is not null)
        {
            CvatErrorText.Text = $"{title}: {message}";
            CvatErrorText.Visibility = Visibility.Visible;
        }
    }



    private static async Task<string> ReadXmlAsync(string path)
    {
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return await File.ReadAllTextAsync(path);
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.Entries.FirstOrDefault(x => x.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("ZIP không chứa file CVAT XML.");
        using var reader = new StreamReader(entry.Open());
        return await reader.ReadToEndAsync();
    }

    private static Box ReadBox(XElement element)
    {
        var parent = element.Parent;
        var frame = int.TryParse(element.Attribute("frame")?.Value ?? parent?.Attribute("id")?.Value, out var parsedFrame) ? parsedFrame : -1;
        return new Box(
            frame,
            parent?.Attribute("name")?.Value ?? $"Frame {frame}",
            double.TryParse(parent?.Attribute("width")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var width) ? width : 0,
            double.TryParse(parent?.Attribute("height")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var height) ? height : 0,
            element.Attribute("label")?.Value ?? "",
            Number(element, "xtl"), Number(element, "ytl"), Number(element, "xbr"), Number(element, "ybr"),
            element.Attribute("outside")?.Value == "1", element);
    }

    private static double Number(XElement e, string name) => double.Parse(e.Attribute(name)?.Value ?? "0", CultureInfo.InvariantCulture);

    private static double IoU(Box a, Box b)
    {
        var left = Math.Max(a.X1, b.X1);
        var top = Math.Max(a.Y1, b.Y1);
        var right = Math.Min(a.X2, b.X2);
        var bottom = Math.Min(a.Y2, b.Y2);
        var intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        var union = (a.X2 - a.X1) * (a.Y2 - a.Y1) + (b.X2 - b.X1) * (b.Y2 - b.Y1) - intersection;
        return union <= 0 ? 0 : intersection / union;
    }


    private static string GetLabelColor(string label)
    {
        if (HardcodedLabelColors.TryGetValue(label, out var hardcoded)) return hardcoded;
        var hash = 0;
        foreach (var c in label) hash = c + ((hash << 5) - hash);
        return Palette[Math.Abs(hash) % Palette.Length];
    }

    private static Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        return hex.Length switch
        {
            6 => Color.FromArgb(255,
                byte.Parse(hex[..2], NumberStyles.HexNumber),
                byte.Parse(hex[2..4], NumberStyles.HexNumber),
                byte.Parse(hex[4..6], NumberStyles.HexNumber)),
            8 => Color.FromArgb(
                byte.Parse(hex[..2], NumberStyles.HexNumber),
                byte.Parse(hex[2..4], NumberStyles.HexNumber),
                byte.Parse(hex[4..6], NumberStyles.HexNumber),
                byte.Parse(hex[6..8], NumberStyles.HexNumber)),
            _ => Color.FromArgb(255, 150, 150, 150)
        };
    }


    private int GetGlobalIndex(Box box) => _globalIndexByNode.TryGetValue(box.Node, out var index) ? index : 0;

    private sealed record Box(int Frame, string FrameName, double FrameWidth, double FrameHeight, string Label, double X1, double Y1, double X2, double Y2, bool Outside, XElement Node);

    private sealed record DuplicateItem(
        (Box A, Box B, double Overlap) Row,
        int Ordinal,
        string OrdinalText,
        string FrameTitle,
        string Label,
        string Details,
        string IoUText,
        string PairText,
        SolidColorBrush AccentBrush,
        BoxRowItem BoxARow,
        BoxRowItem BoxBRow,
        string BoxCountText,
        UIElement? QuickReview,
        Visibility QuickReviewVisibility);

    private sealed record BoxRowItem(int Key, string CoordText, SolidColorBrush TextBrush);
    private sealed record OverlapChipItem(string Key, string Text, string Hint, SolidColorBrush ChipBrush, SolidColorBrush TextBrush, SolidColorBrush BorderColorBrush);

    private sealed record ShapeCandidate(int Id, string Type, int LabelId, int Frame, double X1, double Y1, double X2, double Y2);

    private sealed record LabelChipItem(
        string Label,
        string CountText,
        SolidColorBrush ChipBrush,
        SolidColorBrush TextBrush,
        SolidColorBrush BadgeBrush,
        SolidColorBrush BorderColorBrush);

    private sealed record ImageLoadResult(string Source, bool Attempted, byte[]? Bytes, string? UserMessage);

    private sealed record CvatTask(int Id, string Name)
    {
        public string Display => $"#{Id} — {Name}";
    }

    private sealed record CvatJob(int Id, int StartFrame, int StopFrame)
    {
        public string Display => $"#{Id} — Frame {StartFrame}–{StopFrame}";
    }

    private const string DemoXml = """
        <annotations>
          <image id="0" name="frame_000001.jpg"><box label="person" xtl="120" ytl="80" xbr="340" ybr="510"/><box label="person" xtl="120" ytl="80" xbr="340" ybr="510"/></image>
          <image id="1" name="frame_000002.jpg"><box label="vehicle" xtl="500" ytl="220" xbr="820" ybr="610"/><box label="vehicle" xtl="500" ytl="220" xbr="820" ybr="610"/></image>
          <image id="2" name="frame_000003.jpg"><box label="person" xtl="160" ytl="90" xbr="350" ybr="500"/></image>
          <image id="3" name="frame_000004.jpg"><box label="helmet" xtl="50" ytl="40" xbr="130" ybr="120"/></image>
          <image id="4" name="frame_000005.jpg"><box label="vehicle" xtl="480" ytl="230" xbr="800" ybr="620"/></image>
        </annotations>
        """;
}
