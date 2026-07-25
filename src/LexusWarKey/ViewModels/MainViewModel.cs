using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LexusWarKey.Core;
using LexusWarKey.Views;
using LexusWarKey.Windows;

namespace LexusWarKey.ViewModels;

/// <summary>What the window is currently waiting for the user to press.</summary>
public sealed record CaptureRequest(Action<int> Assign, Action Cancel);

public sealed partial class KeyMapRow : ObservableObject
{
    private readonly KeyMap _model;
    private readonly Action _onChanged;
    private readonly Action<KeyMapRow, bool> _beginCapture;

    public KeyMapRow(string label, KeyMap model, Action onChanged, Action<KeyMapRow, bool> beginCapture)
    {
        Label = label;
        _model = model;
        _onChanged = onChanged;
        _beginCapture = beginCapture;
        _enabled = model.Enabled;
    }

    public KeyMap Model => _model;
    public string Label { get; }

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _isCapturingFrom;
    [ObservableProperty] private bool _isCapturingTo;

    public string FromDisplay => IsCapturingFrom ? "дар…" : _model.FromVk == 0 ? "—" : VirtualKeys.NameOf(_model.FromVk);
    public string ToDisplay => IsCapturingTo ? "дар…" : _model.ToVk == 0 ? "—" : VirtualKeys.NameOf(_model.ToVk);

    partial void OnEnabledChanged(bool value) { _model.Enabled = value; _onChanged(); }
    partial void OnIsCapturingFromChanged(bool value) => OnPropertyChanged(nameof(FromDisplay));
    partial void OnIsCapturingToChanged(bool value) => OnPropertyChanged(nameof(ToDisplay));

    [RelayCommand] private void CaptureFrom() => _beginCapture(this, true);
    [RelayCommand] private void CaptureTo() => _beginCapture(this, false);

    [RelayCommand]
    private void ClearRow()
    {
        _model.FromVk = 0;
        _model.Enabled = false;
        Enabled = false;
        OnPropertyChanged(nameof(FromDisplay));
        _onChanged();
    }

    /// <summary>The in-game overlay edits the same model objects, so the list needs a nudge.</summary>
    public void NotifyModelChanged()
    {
        Enabled = _model.Enabled;
        OnPropertyChanged(nameof(FromDisplay));
        OnPropertyChanged(nameof(ToDisplay));
    }

    public void SetKey(bool isFrom, int vk)
    {
        if (isFrom)
        {
            _model.FromVk = vk;
            // A slot with a key is live; clearing the key switches it off.
            _model.Enabled = vk != 0;
            Enabled = _model.Enabled;
            OnPropertyChanged(nameof(FromDisplay));
        }
        else
        {
            _model.ToVk = vk;
            OnPropertyChanged(nameof(ToDisplay));
        }
        _onChanged();
    }
}

public sealed partial class ChatMacroRow : ObservableObject
{
    private readonly ChatMacro _model;
    private readonly Action _onChanged;
    private readonly Action<ChatMacroRow> _beginCapture;

    public ChatMacroRow(ChatMacro model, Action onChanged, Action<ChatMacroRow> beginCapture)
    {
        _model = model;
        _onChanged = onChanged;
        _beginCapture = beginCapture;
        _messagesText = string.Join(Environment.NewLine, model.Messages);
        _enabled = model.Enabled;
        _alliesOnly = model.AlliesOnly;
    }

    public ChatMacro Model => _model;

    [ObservableProperty] private string _messagesText = "";
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _alliesOnly;
    [ObservableProperty] private bool _isCapturing;

    public string HotkeyDisplay => IsCapturing ? "дар…" : _model.HotkeyVk == 0 ? "—" : VirtualKeys.NameOf(_model.HotkeyVk);
    public int MessageCount => _model.Messages.Count;

    partial void OnEnabledChanged(bool value) { _model.Enabled = value; _onChanged(); }
    partial void OnAlliesOnlyChanged(bool value) { _model.AlliesOnly = value; _onChanged(); }
    partial void OnIsCapturingChanged(bool value) => OnPropertyChanged(nameof(HotkeyDisplay));

    partial void OnMessagesTextChanged(string value)
    {
        _model.Messages = value.Split('\n').Select(l => l.Trim('\r', ' ')).Where(l => l.Length > 0).ToList();
        OnPropertyChanged(nameof(MessageCount));
        _onChanged();
    }

    [RelayCommand] private void CaptureHotkey() => _beginCapture(this);

    public void SetHotkey(int vk)
    {
        _model.HotkeyVk = vk;
        OnPropertyChanged(nameof(HotkeyDisplay));
        _onChanged();
    }
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ProfileStore _store;
    private readonly GameWindowWatcher _watcher;
    private readonly KeyboardHookService _hook;
    private readonly WarKeyProfile _profile;
    private readonly System.Windows.Threading.DispatcherTimer _statusTimer;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _onlyWhenGameFocused;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _statusDetail = "";
    [ObservableProperty] private bool _statusIsLive;
    [ObservableProperty] private string _conflictText = "";
    [ObservableProperty] private bool _hasConflicts;
    [ObservableProperty] private bool _isCapturing;

    private CaptureRequest? _capture;
    private readonly OverlayConfigSession _overlaySession;
    private OverlayWindow? _overlay;
    private readonly MouseCaptureService _mouse = new();

    [ObservableProperty] private bool _skillsUsePosition;
    [ObservableProperty] private bool _moveCursorForClicks;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _minimiseToTray;

    private readonly StartupService _startup = new();

    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateText = "";
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private double _updateProgress;
    private UpdateInfo? _pendingUpdate;

    public string VersionText => $"v{UpdateChecker.CurrentVersion}";
    [ObservableProperty] private string _calibrationText = "";
    [ObservableProperty] private bool _isCalibrated;
    [ObservableProperty] private bool _isCalibrating;

    public MainViewModel()
    {
        _store = new ProfileStore();
        _profile = _store.Load();
        _watcher = new GameWindowWatcher();

        var engine = new RemapEngine(() => _profile, _watcher.IsGameFocused);
        _hook = new KeyboardHookService(engine, _watcher.GameWindowHandle, () => _profile.MoveCursorForClicks);
        _hook.ToggleRequested += () => Application.Current?.Dispatcher.BeginInvoke(() => IsEnabled = !IsEnabled);
        _hook.OverlayToggleRequested += () => Application.Current?.Dispatcher.BeginInvoke(ToggleOverlay);
        _hook.ConfigKeyPressed += vk => Application.Current?.Dispatcher.BeginInvoke(() => OnOverlayKey(vk));
        _overlaySession = new OverlayConfigSession(_profile, () => { Save(); RefreshRowsFromProfile(); });

        _isEnabled = _profile.Enabled;
        _onlyWhenGameFocused = _profile.OnlyWhenGameFocused;
        _skillsUsePosition = _profile.SkillsUsePosition;
        _moveCursorForClicks = _profile.MoveCursorForClicks;
        _minimiseToTray = _profile.MinimiseToTray;
        _startWithWindows = _startup.IsEnabled();
        if (_startWithWindows)
            _startup.RepairPathIfNeeded();

        InventoryRows = new ObservableCollection<KeyMapRow>(
            _profile.Inventory.Select((m, i) => new KeyMapRow($"{i + 1}", m, Save, BeginKeyCapture)));
        SkillRows = new ObservableCollection<KeyMapRow>(
            _profile.Skills.Select((m, i) => new KeyMapRow($"{i + 1}", m, Save, BeginKeyCapture)));
        ChatRows = new ObservableCollection<ChatMacroRow>(
            _profile.ChatMacros.Select(m => new ChatMacroRow(m, Save, BeginChatCapture)));

        _hook.Install();

        _statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
        RefreshStatus();
        RefreshConflicts();
        RefreshCalibration();

        UpdateInstaller.CleanupAfterUpdate();
        AskAboutStartupOnce();
        _ = CheckForUpdateAsync();
    }

    /// <summary>Asked once, on the very first run — the closest a portable app gets to an
    /// installer question. The answer is remembered so it never nags again.</summary>
    private void AskAboutStartupOnce()
    {
        if (_profile.StartWithWindows is not null)
            return;

        var answer = MessageBox.Show(
            "Windows асахад Lexus WarKey автоматаар ажиллаж эхлэх үү?\n\n" +
            "Ингэснээр тоглохын өмнө бүр сануулгагүйгээр товчнууд чинь бэлэн байна.\n" +
            "Дараа нь Тохиргоо хэсгээс хэдийд ч өөрчилж болно.",
            "Lexus WarKey", MessageBoxButton.YesNo, MessageBoxImage.Question);

        var enable = answer == MessageBoxResult.Yes;
        _profile.StartWithWindows = enable;
        if (enable && !_startup.TrySet(true))
        {
            MessageBox.Show("Автомат эхлүүлэлтийг бүртгэж чадсангүй. Тохиргоо хэсгээс дахин оролдож болно.",
                "Lexus WarKey", MessageBoxButton.OK, MessageBoxImage.Warning);
            _profile.StartWithWindows = false;
        }
        StartWithWindows = _startup.IsEnabled();
        Save();
    }

    // ---- update check (asks first, never installs silently) ----

    private async Task CheckForUpdateAsync()
    {
        var info = await new UpdateChecker().CheckAsync().ConfigureAwait(true);
        if (info is null)
            return;
        _pendingUpdate = info;
        UpdateAvailable = true;
        UpdateText = $"Шинэ хувилбар гарсан: v{info.Version} ({info.SizeBytes / (1024 * 1024)} MB)";
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdate is null || IsUpdating)
            return;

        var confirm = MessageBox.Show(
            $"v{_pendingUpdate.Version} татаж суулгах уу?\n\nАпп татаж дуусаад автоматаар дахин нээгдэнэ. Таны тохиргоо хэвээр үлдэнэ.",
            "Lexus WarKey", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        IsUpdating = true;
        UpdateProgress = 0;
        try
        {
            var installer = new UpdateInstaller();
            var progress = new Progress<double>(p => UpdateProgress = p);
            var staged = await installer.DownloadAsync(_pendingUpdate, progress).ConfigureAwait(true);
            Save();
            Shutdown();
            installer.ApplyAndRestart(staged);
        }
        catch (Exception ex)
        {
            IsUpdating = false;
            MessageBox.Show($"Шинэчлэлт амжилтгүй боллоо:\n{ex.Message}\n\nАпп хэвийн ажилласаар байна.",
                "Lexus WarKey", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (_pendingUpdate is null)
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_pendingUpdate.ReleaseUrl) { UseShellExecute = true });
        }
        catch { /* browser refused */ }
    }

    [RelayCommand]
    private void DismissUpdate() => UpdateAvailable = false;

    public ObservableCollection<KeyMapRow> InventoryRows { get; }
    public ObservableCollection<KeyMapRow> SkillRows { get; }
    public ObservableCollection<ChatMacroRow> ChatRows { get; }

    partial void OnSkillsUsePositionChanged(bool value) { _profile.SkillsUsePosition = value; Save(); RefreshCalibration(); }
    partial void OnMoveCursorForClicksChanged(bool value) { _profile.MoveCursorForClicks = value; Save(); }
    partial void OnMinimiseToTrayChanged(bool value) { _profile.MinimiseToTray = value; Save(); }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!_startup.TrySet(value) && value)
        {
            MessageBox.Show("Автомат эхлүүлэлтийг бүртгэж чадсангүй.", "Lexus WarKey",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            StartWithWindows = false;
            return;
        }
        _profile.StartWithWindows = value;
        Save();
    }

    // ---- command-card calibration (position-based skills) ----

    private void RefreshCalibration()
    {
        var card = _profile.CommandCard;
        IsCalibrated = card.IsCalibrated;
        CalibrationText = !SkillsUsePosition
            ? "Байрлалын горим унтраалттай — товч зүгээр л өөр товч руу шилждэг."
            : card.IsCalibrated
                ? $"Тохируулсан: 1-р нүд ({card.TopLeftX}, {card.TopLeftY}) → 12-р нүд ({card.BottomRightX}, {card.BottomRightY})"
                : "Тохируулаагүй — доорх товчийг дараад тоглоом дотроо 2 удаа дар.";
    }

    [RelayCommand]
    private void Calibrate()
    {
        if (IsCalibrating)
            return;
        IsCalibrating = true;
        CalibrationText = "1/2 — Тоглоом руугаа орж, командын картны ЗҮҮН ДЭЭД чадварын товчийг дар.";

        _mouse.CaptureNextClick((x1, y1) => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            CalibrationText = "2/2 — Одоо БАРУУН ДООД (12-р) нүдийг дар.";
            _mouse.CaptureNextClick((x2, y2) => Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                var card = _profile.CommandCard;
                card.TopLeftX = Math.Min(x1, x2);
                card.TopLeftY = Math.Min(y1, y2);
                card.BottomRightX = Math.Max(x1, x2);
                card.BottomRightY = Math.Max(y1, y2);
                IsCalibrating = false;
                Save();
                RefreshCalibration();
            }));
        }));
    }

    [RelayCommand]
    private void ClearCalibration()
    {
        _mouse.Stop();
        IsCalibrating = false;
        _profile.CommandCard.Clear();
        Save();
        RefreshCalibration();
    }

    partial void OnIsEnabledChanged(bool value) { _profile.Enabled = value; Save(); RefreshStatus(); }
    partial void OnOnlyWhenGameFocusedChanged(bool value) { _profile.OnlyWhenGameFocused = value; Save(); RefreshStatus(); }

    // ---- key capture ----

    private void BeginKeyCapture(KeyMapRow row, bool isFrom)
    {
        CancelCapture();
        if (isFrom) row.IsCapturingFrom = true; else row.IsCapturingTo = true;
        IsCapturing = true;
        _capture = new CaptureRequest(
            vk => { row.SetKey(isFrom, vk); ClearFlags(); },
            ClearFlags);

        void ClearFlags()
        {
            row.IsCapturingFrom = false;
            row.IsCapturingTo = false;
            IsCapturing = false;
            _capture = null;
            RefreshConflicts();
        }
    }

    private void BeginChatCapture(ChatMacroRow row)
    {
        CancelCapture();
        row.IsCapturing = true;
        IsCapturing = true;
        _capture = new CaptureRequest(
            vk => { row.SetHotkey(vk); Clear(); },
            Clear);

        void Clear()
        {
            row.IsCapturing = false;
            IsCapturing = false;
            _capture = null;
            RefreshConflicts();
        }
    }

    /// <summary>Called from the window's PreviewKeyDown. Returns true when the press was consumed.</summary>
    public bool HandleCaptureKey(int vk)
    {
        if (_capture is null)
            return false;
        if (vk == VirtualKeys.Escape)
        {
            _capture.Cancel();
            return true;
        }
        // Backspace empties the slot rather than binding Backspace itself.
        _capture.Assign(vk == VirtualKeys.Back ? 0 : vk);
        return true;
    }

    public void CancelCapture() => _capture?.Cancel();

    // ---- in-game overlay (Ctrl+F6) ----

    private void ToggleOverlay()
    {
        if (_overlay is { IsVisible: true })
        {
            CloseOverlay();
            return;
        }

        _overlaySession.Reset();
        if (_overlay is null)
        {
            _overlay = new OverlayWindow();
            _overlay.SlotClicked += (group, index) =>
            {
                _overlaySession.SelectSlot(group, index);
                RenderOverlay();
            };
            _overlay.Moved += (left, top) =>
            {
                _profile.OverlayLeft = left;
                _profile.OverlayTop = top;
                Save();
            };
        }
        _hook.ConfigMode = true;
        RenderOverlay();
        _overlay.PlaceAt(_profile.OverlayLeft, _profile.OverlayTop);
    }

    private void CloseOverlay()
    {
        _hook.ConfigMode = false;
        _overlay?.Hide();
    }

    private void OnOverlayKey(int vk)
    {
        if (!_overlaySession.HandleKey(vk))
        {
            CloseOverlay();
            return;
        }
        RenderOverlay();
    }

    private void RenderOverlay()
    {
        if (_overlay is null)
            return;

        _overlay.ShowSlots(
            BuildSlots(SlotGroup.Inventory, _profile.Inventory),
            BuildSlots(SlotGroup.Skill, _profile.Skills),
            _overlaySession.Prompt);
        RefreshRowsFromProfile();
    }

    private List<OverlaySlot> BuildSlots(SlotGroup group, IReadOnlyList<KeyMap> maps)
    {
        var selected = _overlaySession.Step == OverlayStep.WaitingForKey && _overlaySession.SelectedGroup == group
            ? _overlaySession.SelectedIndex
            : -1;

        return maps.Select((m, i) => new OverlaySlot(
            group,
            i,
            m.FromVk == 0 ? "—" : VirtualKeys.NameOf(m.FromVk),
            Background: i == selected ? "#4A2E22" : "#181615",
            Border: i == selected ? "#D97757" : "#3A3632")).ToList();
    }

    // ---- rows ----

    private void RefreshRowsFromProfile()
    {
        foreach (var row in InventoryRows.Concat(SkillRows))
            row.NotifyModelChanged();
    }

    [RelayCommand]
    private void AddChat()
    {
        var model = new ChatMacro();
        _profile.ChatMacros.Add(model);
        ChatRows.Add(new ChatMacroRow(model, Save, BeginChatCapture));
        Save();
    }

    [RelayCommand]
    private void RemoveChat(ChatMacroRow? row)
    {
        if (row is null) return;
        _profile.ChatMacros.Remove(row.Model);
        ChatRows.Remove(row);
        Save();
    }

    private void Save()
    {
        try { _store.Save(_profile); } catch { /* keep running even if the disk says no */ }
        RefreshConflicts();
    }

    private void RefreshConflicts()
    {
        var conflicts = RemapEngine.FindConflicts(_profile);
        HasConflicts = conflicts.Count > 0;
        ConflictText = HasConflicts
            ? "Давхардсан товч: " + string.Join(", ", conflicts.Select(VirtualKeys.NameOf))
            : "";
    }

    private void RefreshStatus()
    {
        var focused = _watcher.IsGameFocused();
        StatusIsLive = IsEnabled && (focused || !OnlyWhenGameFocused);

        if (!IsEnabled)
        {
            StatusText = "Унтраалттай";
            StatusDetail = "Ctrl + F5 дарж асаана";
        }
        else if (focused)
        {
            StatusText = "Идэвхтэй";
            StatusDetail = "Warcraft III фокуст байна";
        }
        else if (OnlyWhenGameFocused)
        {
            StatusText = "Хүлээж байна";
            StatusDetail = $"Warcraft III эхлэхийг хүлээж байна ({_watcher.ForegroundProcessName() ?? "?"})";
        }
        else
        {
            StatusText = "Идэвхтэй";
            StatusDetail = "Бүх программд — болгоомжтой";
        }
    }

    public void Shutdown()
    {
        _statusTimer.Stop();
        _mouse.Dispose();
        _overlay?.Close();
        _hook.Dispose();
        Save();
    }
}
