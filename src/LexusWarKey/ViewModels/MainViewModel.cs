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
    private readonly RemapEngine _engine;
    private readonly KeyboardHookService _hook;
    private readonly MouseHookService _mouseHook;
    private readonly WarKeyProfile _profile;
    private readonly System.Windows.Threading.DispatcherTimer _statusTimer;

    /// <summary>How long the chat line may stay open before we stop believing our own tracker.
    /// Generous on purpose: the cost of cutting a real message short is one garbled sentence,
    /// the cost of staying wrong is every key for the rest of the match.</summary>
    private static readonly TimeSpan StuckChatLine = TimeSpan.FromSeconds(20);

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _onlyWhenGameFocused;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _statusDetail = "";
    [ObservableProperty] private bool _statusIsLive;

    /// <summary>Whether the status pill has anything worth showing. Armed-and-waiting and
    /// armed-and-playing are the normal states and say nothing the user did not already know.</summary>
    [ObservableProperty] private bool _hasStatus;
    [ObservableProperty] private string _problemText = "";
    [ObservableProperty] private bool _hasProblems;
    [ObservableProperty] private bool _isCapturing;

    private CaptureRequest? _capture;
    private bool _linkAnywayConfirmed;
    private readonly OverlayConfigSession _overlaySession;
    private OverlayWindow? _overlay;

    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _minimiseToTray;

    private readonly StartupService _startup = new();

    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateText = "";
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private bool _isCheckingUpdate;
    [ObservableProperty] private double _updateProgress;
    private UpdateInfo? _pendingUpdate;

    public string VersionText => $"v{UpdateChecker.CurrentVersion}";
    [ObservableProperty] private string _calibrationText = "";
    [ObservableProperty] private bool _isCalibrated;

    // ---- community activation (TierBot /warkey) ----

    [ObservableProperty] private bool _isActivated;

    /// <summary>Shown in the activation banner so the user can hand it to the bot. It is a
    /// one-way hash of this Windows install — not a serial number, not anything personal.</summary>
    public string MachineCode => Core.MachineId.Current;

    /// <summary>Ready to paste straight into Discord.</summary>
    public string ActivationCommand => $"/warkey {Core.MachineId.Current}";

    /// <summary>True when the current code predates machine binding — still honoured, but the
    /// user is nudged to refresh it so a copied code stops working on someone else's PC.</summary>
    [ObservableProperty] private bool _activationIsLegacy;
    [ObservableProperty] private string _activationInput = "";
    [ObservableProperty] private string _activationStatus = "";
    private DateTimeOffset? _activationExpiry;

    /// <summary>Plain-language state of the current code, for the always-visible Help panel.</summary>
    public string ActivationSummary => !IsActivated
        ? "Идэвхжүүлээгүй байна."
        : ActivationDaysLeft is { } days
            ? $"Идэвхтэй — {days} хоног үлдсэн." + (ActivationIsLegacy ? " Кодоо шинэчлэхийг зөвлөж байна." : "")
            : "Идэвхтэй.";

    public string ActivationHeading => !IsActivated
        ? "Идэвхжүүлэлт шаардлагатай"
        : ActivationIsLegacy
            ? "Кодоо шинэчилнэ үү"
            : "Ашиглах хугацаа дуусах гэж байна";

    [RelayCommand]
    private void Activate()
    {
        var result = Core.Activation.Validate(ActivationInput, DateTimeOffset.UtcNow);
        if (!result.Valid)
        {
            ActivationStatus = "✗ " + result.Error;
            return;
        }

        _profile.ActivationToken = ActivationInput.Trim();
        _activationExpiry = result.ExpiresUtc;
        ActivationIsLegacy = result.IsLegacy;
        IsActivated = true;
        ActivationInput = "";
        ActivationStatus = "";
        Save();
        RefreshStatus();
        RefreshConflicts();
    }

    /// <summary>Whether the activation box should be on screen. Not simply "unactivated": a
    /// member with a legacy or nearly-expired code is being ASKED for a new one, and hiding
    /// the only place to paste it is how this first went wrong.</summary>
    public bool NeedsActivationAttention =>
        !IsActivated || ActivationIsLegacy || ActivationDaysLeft is <= 5;

    partial void OnIsActivatedChanged(bool value)
    {
        OnPropertyChanged(nameof(NeedsActivationAttention));
        OnPropertyChanged(nameof(ActivationHeading));
        OnPropertyChanged(nameof(ActivationSummary));
    }

    partial void OnActivationIsLegacyChanged(bool value)
    {
        OnPropertyChanged(nameof(NeedsActivationAttention));
        OnPropertyChanged(nameof(ActivationHeading));
        OnPropertyChanged(nameof(ActivationSummary));
    }

    /// <summary>Days of activation left, or null when not activated. Used for the reminder.</summary>
    private int? ActivationDaysLeft =>
        IsActivated && _activationExpiry is { } expiry
            ? Math.Max(0, (int)Math.Ceiling((expiry - DateTimeOffset.UtcNow).TotalDays))
            : null;

    public MainViewModel()
    {
        _store = new ProfileStore(log: DiagnosticLog.Write);
        _profile = _store.Load();
        DiagnosticLog.Write($"startup v{UpdateChecker.CurrentVersion}; profile: skills={_profile.Skills.Count(m => m.ClaimsKey)}, overrides={(_profile.CommandCard.Overrides?.Count ?? 0)}, warning={_store.LoadWarning ?? "none"}");

        _watcher = new GameWindowWatcher();
        SeedCardIfNeeded();
        RescaleCardIfScreenChanged();

        var saved = Core.Activation.Validate(_profile.ActivationToken, DateTimeOffset.UtcNow);
        _isActivated = saved.Valid;
        _activationExpiry = saved.ExpiresUtc;
        _activationIsLegacy = saved.IsLegacy;

        _engine = new RemapEngine(() => _profile, _watcher.IsGameFocused, () => IsActivated);

        // While the tracker says the chat line is open the app deliberately does nothing, and in
        // fullscreen the status pill saying so is behind the game where nobody can read it. So it
        // goes in the log: an "opened" with no "closed" after it is the signature of the tracker
        // losing sync, and there is no other way to tell that apart from "the keys just stopped".
        _engine.ChatOpenChanged += open =>
            DiagnosticLog.Write(open
                ? "chat line opened — remapping suspended until it closes"
                : "chat line closed — remapping live again");

        _hook = new KeyboardHookService(_engine, _watcher.GameWindowForClicks,
                                        () => _profile.PostClicksToGameWindow,
                                        () => (_profile.PostedSettleMs, _profile.PostedHoldMs));
        _hook.OverlayToggleRequested += () => Application.Current?.Dispatcher.BeginInvoke(ToggleOverlay);
        _hook.ConfigKeyPressed += vk => Application.Current?.Dispatcher.BeginInvoke(() => OnOverlayKey(vk));

        // Wheel and side buttons run through the same decision path as keys. The hook is only
        // installed while something is actually bound to the mouse — a low-level mouse hook
        // sees every movement report, and a player who binds nothing should pay nothing.
        _mouseHook = new MouseHookService(_engine, _hook.HandleMouseControl);
        _overlaySession = new OverlayConfigSession(_profile, () => { Save(); RefreshRowsFromProfile(); });

        _isEnabled = _profile.Enabled;
        _onlyWhenGameFocused = _profile.OnlyWhenGameFocused;
        _minimiseToTray = _profile.MinimiseToTray;
        _startWithWindows = _startup.IsEnabled();
        if (_startWithWindows)
            _startup.RepairPathIfNeeded();

        InventoryRows = new ObservableCollection<KeyMapRow>(
            _profile.Inventory.Select((m, i) => new KeyMapRow($"{i + 1}", m, Save, BeginKeyCapture)));
        // Slots 1-4 are Move/Stop/Hold/Attack: never rebound, never shown. The label keeps the
        // real card number so a ring marked 7 is the cell marked 7.
        SkillRows = new ObservableCollection<KeyMapRow>(
            _profile.Skills
                .Select((m, i) => (map: m, index: i))
                .Where(x => x.index >= CommandCard.FirstBindableSlot)
                .Select(x => new KeyMapRow($"{x.index + 1}", x.map, Save, BeginKeyCapture)));
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

    private async Task CheckForUpdateAsync(bool announceWhenUpToDate = false)
    {
        IsCheckingUpdate = true;
        var checker = new UpdateChecker();
        UpdateInfo? info;
        try
        {
            info = await checker.CheckAsync().ConfigureAwait(true);
        }
        finally
        {
            IsCheckingUpdate = false;
        }

        if (info is null)
        {
            if (!announceWhenUpToDate)
                return;

            // "No update" and "could not ask" are the same null. Telling an offline user they are
            // up to date is worse than saying nothing — they stop looking.
            MessageBox.Show(
                checker.LastCheckFailed
                    ? "GitHub-тай холбогдож чадсангүй. Интернэтээ шалгаад дахин оролдоно уу."
                    : $"Та хамгийн сүүлийн хувилбар дээр байна (v{UpdateChecker.CurrentVersion}).",
                "Lexus WarKey", MessageBoxButton.OK,
                checker.LastCheckFailed ? MessageBoxImage.Warning : MessageBoxImage.Information);
            return;
        }

        _pendingUpdate = info;
        UpdateAvailable = true;
        UpdateText = $"Шинэ хувилбар гарсан: v{info.Version} ({info.SizeBytes / (1024 * 1024)} MB)";

        // Updates are not optional: one member on a stale version means desynced behaviour
        // in the community the app serves, so a found update installs immediately. Offline
        // machines simply keep running the version they have until the next successful check.
        await InstallUpdateAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private Task CheckUpdateNow() => CheckForUpdateAsync(announceWhenUpToDate: true);

    [RelayCommand]
    private Task InstallUpdate() => InstallUpdateAsync();

    // No confirmation and no opt-out: every member must run the same version, so a found
    // update simply installs. The only thing that stops it is being offline.
    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdate is null || IsUpdating)
            return;

        IsUpdating = true;
        UpdateProgress = 0;
        try
        {
            var installer = new UpdateInstaller();
            var progress = new Progress<double>(p => UpdateProgress = p);
            var staged = await installer.DownloadAsync(_pendingUpdate, progress).ConfigureAwait(true);
            Save();
            // Shutdown runs inside ApplyAndRestart, after the swap is committed — if any of the
            // file moves fail we are still fully alive and the catch below is telling the truth.
            installer.ApplyAndRestart(staged, Shutdown);
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

    /// <summary>Gives an unlinked card a starting position. It measures the GAME's client area
    /// when the game is running, and only falls back to the whole screen otherwise: windowed
    /// Warcraft draws its command card at the window's bottom-right, so a screen-fraction guess
    /// lands on empty desktop — which is exactly what "the rings are somewhere else" looks like.
    /// Never touches a card the user has already linked or adjusted.</summary>
    private void SeedCardIfNeeded()
    {
        if (_profile.CommandCard.IsCalibrated)
            return;

        if (_watcher.TryGetGameArea(out var left, out var top, out var width, out var height))
        {
            _profile.CommandCard = CommandCard.DefaultForArea(left, top, width, height);
            DiagnosticLog.Write($"card seeded from game area {width}x{height} at ({left},{top})");
        }
        else
        {
            var screenW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
            var screenH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
            _profile.CommandCard = CommandCard.DefaultFor(screenW, screenH);
            DiagnosticLog.Write($"card seeded from screen {screenW}x{screenH} (game not running)");
        }
    }

    /// <summary>Human-readable display and game geometry, for the Settings tab and for anyone
    /// reporting a problem. "The rings are in the wrong place" is unanswerable without it.</summary>
    public string DisplayInfo
    {
        get
        {
            var screenW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
            var screenH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
            var text = $"Дэлгэц: {screenW} x {screenH}";

            if (_watcher.TryGetGameArea(out var l, out var t, out var w, out var h))
            {
                text += w == screenW && h == screenH && l == 0 && t == 0
                    ? "   ·   Warcraft: бүтэн дэлгэц"
                    : $"   ·   Warcraft цонх: {w} x {h}  ({l}, {t})";
            }
            else
            {
                text += "   ·   Warcraft ажиллаагүй байна";
            }

            var card = _profile.CommandCard;
            text += card.IsCalibrated
                ? $"   ·   Карт: ({card.TopLeftX}, {card.TopLeftY}) → ({card.BottomRightX}, {card.BottomRightY})"
                : "   ·   Карт холбоогүй";
            return text;
        }
    }

    /// <summary>Records which physical screen the card's pixels belong to.</summary>
    private void StampCardScreen()
    {
        _profile.CommandCard.CapturedWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        _profile.CommandCard.CapturedHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
    }

    /// <summary>Moves the calibration onto the current screen when the resolution changed —
    /// at startup (user changed display settings between sessions) and live (fullscreen games
    /// switch the display mode). Absolute pixels going stale here once read as "settings lost".</summary>
    private void RescaleCardIfScreenChanged()
    {
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        if (_profile.CommandCard.RescaleTo(width, height))
        {
            DiagnosticLog.Write($"card rescaled to {width}x{height}");
            Save();
            RefreshCalibration();
        }
    }

    private void RefreshCalibration()
    {
        var card = _profile.CommandCard;
        IsCalibrated = card.IsCalibrated;
        // Only the state that needs doing something about is worth a line on the Keys tab.
        // "Linked" is where the user lives; Тохиргоо carries the detail and the undo.
        CalibrationText = card.IsCalibrated
            ? ""
            : "Командын карт холбоогүй. Тоглоом дотроо Ctrl + F6 дараад самбарын торыг тоглоомын карт дээр давхарлаж «Холбох» дар.";
    }

    [RelayCommand]
    private void ClearCalibration()
    {
        _profile.CommandCard.Clear();
        Save();
        RefreshCalibration();
    }

    // Toggling also clears the chat tracker, in case it ever got out of step with the game.
    partial void OnIsEnabledChanged(bool value)
    {
        _profile.Enabled = value;
        _engine.ResetChatState();
        Save();
        RefreshStatus();
        RefreshConflicts();
    }
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
    /// <summary>Feeds a mouse control into an open key-capture, so the wheel and side buttons
    /// can be bound the same way a key is — pressed, not picked from a list.</summary>
    public bool HandleCaptureMouse(int vk) => HandleCaptureKey(vk);

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
        _linkAnywayConfirmed = false;
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
        _overlay.SetCellSize(_profile.OverlayCellWidth, _profile.OverlayCellHeight);
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
        _overlay.Resized += (cellW, cellH) =>
        {
            _profile.OverlayCellWidth = cellW;
            _profile.OverlayCellHeight = cellH;
            Save();
        };
        _overlay.LinkRequested += (visibleTopLeft, bottomRight) =>
        {
            // The panel shows the card's bottom TWO rows, so its corners are slot 5 and slot
            // 12. The stored card is still a full 4x3, so extrapolate the hidden top row from
            // the row pitch — that keeps every visible slot exactly where the user put it.
            var rowPitch = bottomRight.Y - visibleTopLeft.Y;   // slot 5 to slot 12 = one row
            var topLeft = new ScreenPoint(visibleTopLeft.X, visibleTopLeft.Y - rowPitch);

            var error = CommandCard.Validate(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
            if (error is not null)
            {
                RenderOverlayPrompt("⚠ " + error);
                return;
            }

            // The real command card lives in the bottom-right of the screen. Linking a panel
            // parked mid-screen has destroyed a good calibration before — once, with the
            // panel at half height, every skill click landed on empty map. Ask once.
            var screenHeight = Windows.NativeMethods.GetSystemMetrics(Windows.NativeMethods.SM_CYSCREEN);
            if (bottomRight.Y < screenHeight * 0.65 && !_linkAnywayConfirmed)
            {
                _linkAnywayConfirmed = true;
                RenderOverlayPrompt("⚠ Самбар чинь дэлгэцийн дээд хэсэгт байна — тоглоомын командын карт БАРУУН ДООД буланд байдаг. " +
                                    "Панелаа картан дээр давхарлаад дахин Холбох дар. Зориуд энд холбох гэсэн бол дахиад нэг Холбох дар.");
                return;
            }
            _linkAnywayConfirmed = false;

            var card = _profile.CommandCard;
            card.TopLeftX = topLeft.X;
            card.TopLeftY = topLeft.Y;
            card.BottomRightX = bottomRight.X;
            card.BottomRightY = bottomRight.Y;
            // A fresh alignment supersedes any hand-dragged ring positions from the old one.
            card.Overrides = null;
            StampCardScreen();
            Save();
            RefreshCalibration();
            RefreshConflicts();
            RenderOverlayPrompt("✓ Холбогдлоо — 📍 Шалгах дарж цагираг бүр өөрийн нүдэн дээрээ буусныг шалгаарай. Ctrl+F6 = хаах.");
        };
        _overlay.AdjustSaveRequested += () => SlotAdjustWindow.Current?.SavePositions();
        _overlay.AdjustTidyRequested += () => SlotAdjustWindow.Current?.TidyRings();
        _overlay.AdjustCancelRequested += () =>
        {
            SlotAdjustWindow.Current?.Close();
            _overlay?.SetAdjusting(false);
            RenderOverlayPrompt("");
        };
        _overlay.MarkersRequested += () =>
        {
            if (!_profile.CommandCard.IsCalibrated)
            {
                RenderOverlayPrompt("⚠ Эхлээд Холбох хэрэгтэй — торыг тоглоомын картан дээр давхарлаад 🔗 Холбох дар.");
                return;
            }

            SlotAdjustWindow.Open(_profile.CommandCard, () =>
            {
                StampCardScreen();
                Save();
                RefreshCalibration();
                _overlay?.SetAdjusting(false);
                RenderOverlayPrompt("✓ Байрлал хадгалагдлаа");
            });
            _overlay?.SetAdjusting(true);
            RenderOverlayPrompt("Цагирагаа зөв товчин дээр чирээд Хадгална уу");
        };
    }

    /// <summary>Redraws the overlay with a specific message in place of the usual prompt.</summary>
    private void RenderOverlayPrompt(string prompt)
    {
        _overlay?.ShowSlots(
            BuildSlots(SlotGroup.Inventory, _profile.Inventory),
            BuildSlots(SlotGroup.Skill, _profile.Skills),
            prompt);
    }

    private void CloseOverlay()
    {
        _hook.ConfigMode = false;
        SlotAdjustWindow.Current?.Close();
        _overlay?.SetAdjusting(false);
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

        // Monochrome on black: the selected slot is marked with a solid white outline
        // rather than a colour, so the panel stays readable over any game background.
        // The card hides its top row; the index carried is still the model's, so clicking a
        // cell binds the slot its number says.
        var first = group == SlotGroup.Skill ? CommandCard.FirstBindableSlot : 0;
        return maps.Select((m, i) => (map: m, index: i))
            .Where(x => x.index >= first)
            .Select(x => new OverlaySlot(
                group,
                x.index,
                x.map.FromVk == 0 ? "—" : VirtualKeys.NameOf(x.map.FromVk),
                Background: x.index == selected ? "#40FFFFFF" : "#66000000",
                Border: x.index == selected ? "#FFFFFFFF" : "#4DFFFFFF"))
            .ToList();
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

    /// <summary>Installs or drops the mouse hook to match whether the profile binds anything
    /// to the mouse. Called wherever bindings can change.</summary>
    private void SyncMouseHook() => _mouseHook.SetActive(_engine.AnyMouseControlBound());

    private void RefreshConflicts()
    {
        SyncMouseHook();

        var problems = new List<string>();

        if (!IsActivated)
            problems.Add($"Идэвхжүүлээгүй — Lexus Discord сервер дээр \"{ActivationCommand}\" гэж бичээд ирсэн кодоо дээрх талбарт буулгаарай.");
        else if (ActivationDaysLeft is <= 5 and { } daysLeft)
            problems.Add($"Ашиглах хугацаа {daysLeft} хоногийн дараа дуусна — Lexus Discord server дээр \"{ActivationCommand}\" гэж бичээд хугацаагаа сунгаарай.");
        else if (ActivationIsLegacy)
            problems.Add($"Lexus Discord server дээр \"{ActivationCommand}\" гэж бичээд кодоо шинэчлээрэй.");

        // Anything wrong with the profile file itself belongs at the top: it explains why the
        // bindings below may not be the ones the user set, and it must never scroll past unseen.
        if (_store.LoadWarning is { } warning)
            problems.Add(warning);
        if (!_hook.IsInstalled)
            problems.Add("Товч уншигч ажиллахгүй байна. Аппаа хааж дахин нээнэ үү.");

        problems.AddRange(RemapEngine.FindDeadBindings(_profile));

        // Only reachable for someone who has hand-edited postClicksToGameWindow on, which is an
        // experiment rather than a supported setting. It is deliberately NOT shown on the cursor
        // path: injected cursor movement and injected keystrokes are governed by the same
        // integrity rule, the chat macros prove that rule is not biting, and this text told the
        // player to run as administrator for a problem the evidence says they do not have.
        if (_profile.PostClicksToGameWindow && _watcher.GameIsOutOfReach())
        {
            problems.Insert(0,
                "Warcraft администратор эрхээр ажиллаж байна, энэ апп ажиллахгүй байна — " +
                "Windows товчны дохиог чимээгүй хаяж байна. Lexus WarKey дээр баруун товч дараад " +
                "«Администратороор ажиллуулах» гэж сонгоно уу.");
        }

        // A card linked at another resolution, or before the game was windowed, points outside
        // where the game is drawing. Nothing about that is visible in play — the key simply
        // does nothing — so say it here.
        if (_profile.CommandCard.IsCalibrated
            && _watcher.TryGetGameArea(out var gl, out var gt, out var gw, out var gh)
            && !_profile.CommandCard.FitsInside(gl, gt, gw, gh))
        {
            problems.Add($"Командын картын байрлал Warcraft-ын цонхны гадна байна ({gw}x{gh}). " +
                         "Дэлгэцийн нягтрал эсвэл цонхны горим өөрчлөгдсөн бололтой — " +
                         "тоглоом дотроо Ctrl + F6 дараад дахин «Холбох» дар.");
        }

        var conflicts = RemapEngine.FindConflicts(_profile);
        if (conflicts.Count > 0)
            problems.Add("Нэг товч хоёр газар оноогдсон: " + string.Join(", ", conflicts.Select(VirtualKeys.NameOf))
                         + " — нэгийг нь цэвэрлэнэ үү.");

        HasProblems = problems.Count > 0;
        ProblemText = string.Join("\n", problems.Select(p => "• " + p));
    }

    private void RefreshStatus()
    {
        // Keep trying to place an unlinked card, not just once at startup. The app usually starts
        // with Windows and the game arrives long afterwards, so the one attempt in the constructor
        // ran when there was no game to measure — and the card then sat unlinked, telling the
        // player to go and link it by hand, when the answer was available the whole time. This
        // returns immediately once the card has a position, and never touches one that has.
        SeedCardIfNeeded();
        RescaleCardIfScreenChanged();

        var focused = _watcher.IsGameFocused();
        StatusIsLive = IsEnabled && (focused || !OnlyWhenGameFocused);

        // Second line of defence for the chat tracker: if the game is not in front there is
        // no chat line, so anything the tracker believes is stale. Without this a mistracked
        // Enter would leave the app permanently, and invisibly, doing nothing.
        OnPropertyChanged(nameof(NeedsActivationAttention));
        OnPropertyChanged(nameof(ActivationSummary));
        OnPropertyChanged(nameof(DisplayInfo));

        if (IsActivated && _activationExpiry is { } activeUntil && activeUntil <= DateTimeOffset.UtcNow)
        {
            // The code ran out while the app was open — same treatment as never activated.
            IsActivated = false;
            RefreshConflicts();
        }

        if (!focused)
            _engine.ResetChatState();

        // Alt-tabbing away in the middle of a half-typed message leaves the game's chat line open
        // while ours is cleared, and from then on our idea of it is inverted — which would suspend
        // remapping indefinitely. Nobody leaves a message half-written for twenty seconds, so a
        // line still open that long is taken as proof it is not really open.
        //
        // Measured from when the line OPENED, not from the last keystroke. Keyboard silence was
        // the wrong signal and made this guard useless exactly when it was needed: mid-match the
        // player is pressing keys constantly, so SinceLastKey never reached twenty seconds and an
        // inverted tracker survived until the next alt-tab — a whole game with the remapper dead
        // and nothing on screen to say so.
        else if (_engine.ChatOpenFor > StuckChatLine)
        {
            DiagnosticLog.Write($"chat line forced shut after {_engine.ChatOpenFor.TotalSeconds:F0}s "
                                + "— the tracker was almost certainly out of step with the game");
            _engine.ResetChatState();
        }

        // While the game is in front, keys are being pressed constantly. Fifteen seconds of
        // silence means Windows dropped our hook without telling us — put it back before the
        // player notices their keys have quietly stopped working. A minute was long enough to
        // lose a teamfight over.
        if (focused && IsEnabled)
        {
            try
            {
                _hook.ReArmIfSilent(TimeSpan.FromSeconds(15));
            }
            catch
            {
                // Windows refused. Fall through: the panel below now says the remapper is dead,
                // which is the one thing the user has to know.
            }

            if (!_hook.IsInstalled)
                RefreshConflicts();
        }

        if (!IsEnabled)
        {
            StatusText = "Унтраалттай";
            StatusDetail = "Тохиргоо таб дээрээс асаана";
        }
        else if (focused && _engine.ChatOpen)
        {
            StatusText = "Чат нээлттэй";
            StatusDetail = "Товч солилт түр зогссон";
        }
        else if (!OnlyWhenGameFocused)
        {
            StatusText = "Бүх программд";
            StatusDetail = "«Зөвхөн Warcraft дотор» унтраалттай";
        }
        else
        {
            // Armed and waiting, or armed and playing. Neither needs saying.
            StatusText = "";
            StatusDetail = "";
        }
        HasStatus = StatusText.Length > 0;
    }

    public void Shutdown()
    {
        _statusTimer.Stop();
        _overlay?.Close();
        _mouseHook.Dispose();
        _hook.Dispose();
        Save();
    }
}
