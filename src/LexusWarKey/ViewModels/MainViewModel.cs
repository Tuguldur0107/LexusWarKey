using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LexusWarKey.Core;
using LexusWarKey.Views;
using LexusWarKey.Windows;

namespace LexusWarKey.ViewModels;

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
    }

    public KeyMap Model => _model;
    public string Label { get; }

    [ObservableProperty] private bool _isCapturingFrom;
    [ObservableProperty] private bool _isCapturingTo;

    public string FromDisplay => IsCapturingFrom ? "press..." : _model.FromVk == 0 ? "-" : VirtualKeys.NameOf(_model.FromVk);
    public string ToDisplay => IsCapturingTo ? "press..." : _model.ToVk == 0 ? "-" : VirtualKeys.NameOf(_model.ToVk);

    partial void OnIsCapturingFromChanged(bool value) => OnPropertyChanged(nameof(FromDisplay));
    partial void OnIsCapturingToChanged(bool value) => OnPropertyChanged(nameof(ToDisplay));

    [RelayCommand] private void CaptureFrom() => _beginCapture(this, true);
    [RelayCommand] private void CaptureTo() => _beginCapture(this, false);

    public void NotifyModelChanged()
    {
        OnPropertyChanged(nameof(FromDisplay));
        OnPropertyChanged(nameof(ToDisplay));
    }

    public void SetKey(bool isFrom, int vk)
    {
        if (isFrom)
        {
            _model.FromVk = vk;
            if (vk == 0)
                _model.ToVk = 0;
            _model.Enabled = vk != 0;
            OnPropertyChanged(nameof(FromDisplay));
            OnPropertyChanged(nameof(ToDisplay));
        }
        else
        {
            _model.ToVk = vk;
            _model.Enabled = _model.FromVk != 0;
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

    public ChatMacroRow(string label, ChatMacro model, Action onChanged, Action<ChatMacroRow> beginCapture)
    {
        Label = label;
        _model = model;
        _onChanged = onChanged;
        _beginCapture = beginCapture;
        _messageText = model.Message;
    }

    public ChatMacro Model => _model;
    public string Label { get; }

    [ObservableProperty] private string _messageText = "";
    [ObservableProperty] private bool _isCapturing;

    public string HotkeyDisplay => IsCapturing ? "press..." : _model.HotkeyVk == 0 ? "-" : VirtualKeys.NameOf(_model.HotkeyVk);

    partial void OnIsCapturingChanged(bool value) => OnPropertyChanged(nameof(HotkeyDisplay));

    partial void OnMessageTextChanged(string value)
    {
        _model.Message = value;
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
    private readonly RemapEngine _engine;
    private readonly KeyboardHookService _hook;
    private readonly WarKeyProfile _profile;
    private readonly System.Windows.Threading.DispatcherTimer _statusTimer;
    private readonly OverlayConfigSession _overlaySession;

    private static readonly TimeSpan StuckChatLine = TimeSpan.FromSeconds(20);

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _statusDetail = "";
    [ObservableProperty] private bool _statusIsLive;
    [ObservableProperty] private bool _hasStatus;
    [ObservableProperty] private string _problemText = "";
    [ObservableProperty] private bool _hasProblems;
    [ObservableProperty] private bool _isCapturing;

    private CaptureRequest? _capture;
    private OverlayWindow? _overlay;

    public ObservableCollection<KeyMapRow> SkillRows { get; }
    public ObservableCollection<ChatMacroRow> ChatRows { get; }

    public string VersionText
    {
        get
        {
            var v = typeof(MainViewModel).Assembly.GetName().Version;
            return v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public MainViewModel()
    {
        _store = new ProfileStore(log: DiagnosticLog.Write);
        _profile = _store.Load();
        _profile.NormaliseSlots();

        DiagnosticLog.Write($"startup; skill binds={_profile.Skills.Count(m => m.ClaimsKey)}, warning={_store.LoadWarning ?? "none"}");

        _watcher = new GameWindowWatcher();
        _engine = new RemapEngine(() => _profile, _watcher.IsGameFocused);
        _engine.ChatOpenChanged += open =>
            DiagnosticLog.Write(open
                ? "chat line opened; remapping suspended"
                : "chat line closed; remapping live");

        _hook = new KeyboardHookService(_engine);
        _hook.OverlayToggleRequested += () => Application.Current?.Dispatcher.BeginInvoke(new Action(ToggleOverlay));
        _hook.ConfigKeyPressed += vk => Application.Current?.Dispatcher.BeginInvoke(new Action(() => OnOverlayKey(vk)));

        _overlaySession = new OverlayConfigSession(_profile, () => { Save(); RefreshRowsFromProfile(); });

        _isEnabled = _profile.Enabled;
        SkillRows = new ObservableCollection<KeyMapRow>(
            _profile.Skills.Select((m, i) => new KeyMapRow($"{i + 1}", m, Save, BeginKeyCapture)));
        ChatRows = new ObservableCollection<ChatMacroRow>(
            _profile.ChatMacros.Take(WarKeyProfile.QuickChatSlots)
                .Select((m, i) => new ChatMacroRow($"QuickChat {i + 1}", m, Save, BeginChatCapture)));

        try
        {
            _hook.Install();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"keyboard hook refused: {ex.GetType().Name}: {ex.Message}");
        }

        _statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();

        Save();
        RefreshStatus();
        RefreshConflicts();
    }

    partial void OnIsEnabledChanged(bool value)
    {
        _profile.Enabled = value;
        _engine.ResetChatState();
        Save();
        RefreshStatus();
    }

    private void BeginKeyCapture(KeyMapRow row, bool isFrom)
    {
        CancelCapture();
        if (isFrom) row.IsCapturingFrom = true; else row.IsCapturingTo = true;
        IsCapturing = true;

        _capture = new CaptureRequest(
            vk =>
            {
                row.SetKey(isFrom, vk);
                ClearFlags();
            },
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

    public bool HandleCaptureKey(int vk)
    {
        if (_capture is null)
            return false;
        if (vk == VirtualKeys.Escape)
        {
            _capture.Cancel();
            return true;
        }

        // Enter belongs to Warcraft's chat line and RemapEngine never touches it. As a trigger
        // it would store a cell that looks bound and never fires; as a target it would type
        // into the game's chat instead of casting. Swallow it and keep waiting for a real key.
        if (vk == VirtualKeys.Enter)
            return true;

        _capture.Assign(vk == VirtualKeys.Back ? 0 : vk);
        return true;
    }

    public void CancelCapture() => _capture?.Cancel();

    private void ToggleOverlay()
    {
        if (_overlay is { IsVisible: true })
        {
            CloseOverlay();
            return;
        }

        _overlaySession.Reset();
        EnsureOverlay();
        _hook.ConfigMode = true;
        RenderOverlay();
        _overlay!.PlaceAt(_profile.OverlayLeft, _profile.OverlayTop);
    }

    private void EnsureOverlay()
    {
        if (_overlay is not null)
            return;

        _overlay = new OverlayWindow();
        _overlay.SlotClicked += index =>
        {
            _overlaySession.SelectSlot(index);
            RenderOverlay();
        };
        _overlay.Moved += (left, top) =>
        {
            _profile.OverlayLeft = left;
            _profile.OverlayTop = top;
            Save();
        };
    }

    private void CloseOverlay()
    {
        _hook.ConfigMode = false;
        _overlaySession.Reset();
        _overlay?.Hide();

        // Deliberately NOT resetting the chat tracker. The overlay swallows every key while it
        // is open, so it cannot have changed whether Warcraft's chat line is open — but the
        // player may well have opened chat, then pressed Ctrl+F6 to fix a binding mid-fight.
        // Declaring "chat closed" there inverts the tracker: the player goes back to typing
        // into a prompt that is still open, and every letter both mangles the message and
        // casts an ability. Leaving the tracker alone keeps whatever was true before.
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
        _overlay?.ShowSlots(BuildSkillSlots(), _overlaySession.Prompt);
        RefreshRowsFromProfile();
    }

    private List<OverlaySlot> BuildSkillSlots()
    {
        var selected = _overlaySession.Step == OverlayStep.ChoosingSlot ? -1 : _overlaySession.SelectedIndex;
        return _profile.Skills.Select((m, i) => new OverlaySlot(
                i,
                CellText(m),
                Background: i == selected ? "#40FFFFFF" : "#66000000",
                Border: i == selected ? "#FFFFFFFF" : "#4DFFFFFF"))
            .ToList();
    }

    private static string CellText(KeyMap map)
    {
        if (map.FromVk == 0)
            return "-";
        var from = VirtualKeys.NameOf(map.FromVk);
        return map.ToVk == 0 ? $"{from} !" : $"{from}->{VirtualKeys.NameOf(map.ToVk)}";
    }

    private void RefreshRowsFromProfile()
    {
        foreach (var row in SkillRows)
            row.NotifyModelChanged();
    }

    private void Save()
    {
        _profile.NormaliseSlots();
        try { _store.Save(_profile); } catch { }
        RefreshConflicts();
    }

    private void RefreshConflicts()
    {
        var problems = new List<string>();

        if (_store.LoadWarning is { } warning)
            problems.Add(warning);
        if (!_hook.IsInstalled)
            problems.Add("Keyboard hook is not running. Close and reopen the app.");

        problems.AddRange(RemapEngine.FindDeadBindings(_profile));

        var conflicts = RemapEngine.FindConflicts(_profile);
        if (conflicts.Count > 0)
            problems.Add("One trigger key is assigned in more than one place: " + string.Join(", ", conflicts.Select(VirtualKeys.NameOf)));

        HasProblems = problems.Count > 0;
        ProblemText = string.Join("\n", problems.Select(p => "- " + p));
    }

    private void RefreshStatus()
    {
        var focused = _watcher.IsGameFocused();
        StatusIsLive = IsEnabled && focused && !_engine.ChatOpen;

        if (!focused)
            _engine.ResetChatState();
        else if (_engine.ChatOpenFor > StuckChatLine)
        {
            DiagnosticLog.Write($"chat line forced shut after {_engine.ChatOpenFor.TotalSeconds:F0}s");
            _engine.ResetChatState();
        }

        if (focused && IsEnabled)
        {
            try
            {
                _hook.ReArmIfSilent(TimeSpan.FromSeconds(15));
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"keyboard hook re-arm failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (!IsEnabled)
        {
            StatusText = "Disabled";
            StatusDetail = "";
        }
        else if (focused && _engine.ChatOpen)
        {
            StatusText = "Chat open";
            StatusDetail = "Remapping is paused";
        }
        else if (!_hook.IsInstalled)
        {
            StatusText = "Keyboard hook failed";
            StatusDetail = "Close and reopen the app";
        }
        else
        {
            StatusText = "";
            StatusDetail = "";
        }

        HasStatus = StatusText.Length > 0;
        RefreshConflicts();
    }

    public void Shutdown()
    {
        _statusTimer.Stop();
        _overlay?.Close();
        _hook.Dispose();
        Save();
    }
}
