using System.Windows.Input;
using Avalonia.Threading;
using ClaudeTray.Core;

namespace ClaudeTray;

/// <summary>State behind the tray icon and the panel, and the actions they trigger.
/// Data is gathered on background threads and applied on the UI thread.</summary>
public sealed class MainViewModel : ObservableObject
{
    readonly AppConfig _cfg;
    readonly UsageService _usage;
    readonly SessionWindows _windows = new();
    string _sessionsKey = "";
    int _sessionsBusy, _usageBusy;
    string _status = "";
    CancellationTokenSource? _statusClear;

    public MainViewModel(AppConfig cfg)
    {
        _cfg = cfg;
        _usage = new UsageService(cfg);
        Groups =
        [
            new(SessionGroup.Needs, "NEEDS INPUT", "?", Palette.Orange),
            new(SessionGroup.Working, "WORKING", "▶", Palette.Blue),
            new(SessionGroup.Done, "COMPLETED", "✓", Palette.Green),
            new(SessionGroup.Failed, "FAILED", "✕", Palette.Red),
        ];
        RefreshCommand = new RelayCommand(_ => _ = RefreshUsageAsync(force: true));
        RestartCommand = new RelayCommand(_ => Restart());
        OpenClaudeCommand = new RelayCommand(_ => OpenClaude());
        UsagePageCommand = new RelayCommand(_ => { RequestHide?.Invoke(); Terminal.OpenUrl("https://claude.ai/settings/usage"); });
        SettingsCommand = new RelayCommand(_ => OpenSettings?.Invoke());
        ClearModelCommand = new RelayCommand(_ => Background(() => _usage.ClearDefaultModel(), refreshUsage: true));
    }

    /// <summary>Running / percentages / needs-input count changed: redraw the tray icon.</summary>
    public event Action? TrayStateChanged;
    public event Action? RequestHide;
    public event Action? OpenSettings;

    // ---- tray state
    public bool Running { get; private set; }
    public double? SessionPercent { get; private set; }
    public double MaxPercent { get; private set; }
    public int NeedsCount { get; private set; }

    public string ToolTip =>
        (Running ? "Claude Code running" : "Claude Code not running")
        + (NeedsCount > 0 ? $"\n{NeedsCount} session{(NeedsCount > 1 ? "s" : "")} need input" : "")
        + (SessionPercent is { } s ? $"\nSession {s:0}% · weekly {MaxPercent:0}% max" : "");

    // ---- header / footer
    string _plan = "";
    public string Subtitle => (_plan.Length > 0 ? _plan + "  ·  " : "") + (Running ? "running" : "not running");
    string? _error;
    public string? Error { get => _error; private set { if (Set(ref _error, value)) Raise(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(Error);
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool CanRestart => Running && _usage.Tmux.Configured;
    public string OpenText => Running ? "Open Claude Code" : "Start Claude Code";
    public ICommand RefreshCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand OpenClaudeCommand { get; }
    public ICommand UsagePageCommand { get; }
    public ICommand ClearModelCommand { get; }
    public ICommand SettingsCommand { get; }

    // ---- sessions column
    public List<SessionGroupVM> Groups { get; }
    bool _noSessions = true;
    public bool NoSessions { get => _noSessions; private set => Set(ref _noSessions, value); }
    string? _sessionsError;
    public string? SessionsError { get => _sessionsError; private set { if (Set(ref _sessionsError, value)) Raise(nameof(HasSessionsError)); } }
    public bool HasSessionsError => !string.IsNullOrEmpty(SessionsError);

    // ---- usage column
    List<LimitRow> _limits = [];
    public List<LimitRow> Limits { get => _limits; private set { Set(ref _limits, value); Raise(nameof(HasLimits)); } }
    public bool HasLimits => Limits.Count > 0;
    List<DayRow> _days = [];
    public List<DayRow> Days { get => _days; private set => Set(ref _days, value); }
    List<ModelRow> _models = [];
    public List<ModelRow> Models { get => _models; private set { Set(ref _models, value); Raise(nameof(HasModels)); } }
    public bool HasModels => Models.Count > 0;
    List<ModelChoiceVM> _choices = [];
    public List<ModelChoiceVM> ModelChoices { get => _choices; private set => Set(ref _choices, value); }
    bool _live;
    public string ModelHeader => _live ? "MODEL · RUNNING SESSION" : "MODEL · NEW SESSIONS";
    public bool ShowClearModel => !_live;

    // ---- polling (called by the tray controller's timers)

    public async Task PollRunningAsync()
    {
        bool running = await Task.Run(() => _usage.Tmux.Configured ? _usage.Tmux.HasSession() : ClaudeCli.AnyRunning());
        if (running == Running) return;
        Running = running;
        Raise(nameof(Subtitle)); Raise(nameof(CanRestart)); Raise(nameof(OpenText));
        TrayStateChanged?.Invoke();
    }

    public async Task RefreshUsageAsync(bool force = false)
    {
        if (Interlocked.Exchange(ref _usageBusy, 1) == 1) return;
        try
        {
            var snap = await Task.Run(() => _usage.GetAsync(force));
            ApplyUsage(snap);
            if (force) ShowStatus("Refreshed");
        }
        catch (Exception e)
        {
            Log.Error("usage refresh", e);
        }
        finally { Interlocked.Exchange(ref _usageBusy, 0); }
    }

    public async Task RefreshSessionsAsync()
    {
        if (Interlocked.Exchange(ref _sessionsBusy, 1) == 1) return;
        try
        {
            var result = await Task.Run(() => SessionsService.Get(_cfg, _windows));
            ApplySessions(result);
        }
        catch (Exception e)
        {
            Log.Error("sessions refresh", e);
        }
        finally { Interlocked.Exchange(ref _sessionsBusy, 0); }
    }

    /// <summary>Every 30 s while the panel is open: the "ago" labels.</summary>
    public void Tick()
    {
        foreach (var g in Groups) foreach (var s in g.Items) s.Tick();
    }

    public void PanelClosed()
    {
        foreach (var g in Groups) g.ResetShowAll();
    }

    void ApplyUsage(UsageSnapshot u)
    {
        var now = u.Taken;
        _plan = u.Plan;
        Raise(nameof(Subtitle));
        Error = u.Limits.Error;
        Limits = u.Limits.Limits.Select(l =>
        {
            var pace = Format.Pace(l, now);
            return new LimitRow(l.Label, $"{l.Percent:0}%", l.Percent, Palette.Level(l.Percent), l.Percent >= 90,
                l.ResetsAt is { } r ? "resets in " + Format.Until(r, now) : "",
                pace, pace.EndsWith("before reset", StringComparison.Ordinal));
        }).ToList();
        double dayPeak = Math.Max(1, u.Tokens.Days.Select(d => d.Total).DefaultIfEmpty(0).Max());
        Days = u.Tokens.Days.Select(d => new DayRow(d.Today ? "Today" : d.Name, Format.Human(d.Total), d.Total / dayPeak * 100, d.Today)).ToList();
        double modelPeak = Math.Max(1, u.Tokens.Models.Select(m => m.Total).DefaultIfEmpty(0).Max());
        Models = u.Tokens.Models.Select(m => new ModelRow(m.Name, Format.Human(m.Total), m.Total / modelPeak * 100,
            $"in {Format.Human(m.Input)} · out {Format.Human(m.Output)} · cache {Format.Human(m.Cache)}")).ToList();
        _live = u.LiveModelSwitch;
        var pick = new RelayCommand(p => { if (p is ModelChoice m) Background(() => _usage.SetModel(m), refreshUsage: true); });
        ModelChoices = _cfg.Models.Select(m => new ModelChoiceVM(m, m.Id == u.CurrentModel, pick)).ToList();
        Raise(nameof(ModelHeader)); Raise(nameof(ShowClearModel));

        var session = u.Limits.Limits.FirstOrDefault(l => l.Kind == "session");
        double max = u.Limits.Limits.Select(l => l.Percent).DefaultIfEmpty(0).Max();
        if (session?.Percent != SessionPercent || max != MaxPercent)
        {
            SessionPercent = session?.Percent;
            MaxPercent = max;
            TrayStateChanged?.Invoke();
        }
    }

    void ApplySessions(SessionsResult r)
    {
        SessionsError = r.Error;
        var key = string.Join('\n', r.Sessions.Select(s => $"{s.Id}|{s.Group}|{s.Name}|{s.Text}|{s.Updated}|{s.Open}"));
        if (key == _sessionsKey) return;
        _sessionsKey = key;
        foreach (var g in Groups)
            g.SetAll(r.Sessions.Where(s => s.Group == g.Group).Select(s => new SessionVM(s, this, g.Glyph, g.Brush)).ToList());
        NoSessions = r.Sessions.Count == 0;
        int needs = r.Sessions.Count(s => s.Group == SessionGroup.Needs);
        if (needs != NeedsCount)
        {
            NeedsCount = needs;
            TrayStateChanged?.Invoke();
        }
    }

    // ---- actions

    public void OpenClaude()
    {
        RequestHide?.Invoke();
        Background(() => Terminal.OpenClaude(_cfg) ?? "");
    }

    public void OpenSession(SessionVM s)
    {
        RequestHide?.Invoke();
        Background(() => Terminal.OpenSession(_cfg, _windows, s.Info.Id, s.Info.Name) ?? "");
        _ = Task.Delay(1500).ContinueWith(_ => Dispatcher.UIThread.Post(() => _ = RefreshSessionsAsync()));
    }

    public void RemoveSession(SessionVM s)
    {
        foreach (var g in Groups)
            if (g.Group == s.Info.Group)
                g.SetAll(g.Items.Where(x => x != s).ToList());
        _sessionsKey = "";
        Background(() => SessionsService.Remove(_cfg, _windows, s.Info.Id) is { } err ? "Couldn't remove: " + err : "", refreshSessions: true);
    }

    void Restart()
    {
        RequestHide?.Invoke();
        Background(() => _usage.Tmux.Restart());
    }

    /// <summary>Runs an action off the UI thread and shows the message it returns (and notifies on Linux).</summary>
    void Background(Func<string> action, bool refreshUsage = false, bool refreshSessions = false)
    {
        Task.Run(action).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            var msg = t.IsFaulted ? "Error: " + t.Exception?.InnerException?.Message : t.Result;
            if (msg.Length > 0) ShowStatus(msg);
            if (refreshUsage) _ = RefreshUsageAsync(force: false);
            if (refreshSessions) _ = RefreshSessionsAsync();
        }));
    }

    void ShowStatus(string msg)
    {
        Log.Info("status: " + msg);
        Status = msg;
        Notifier.Show(msg);
        _statusClear?.Cancel();
        _statusClear = new CancellationTokenSource();
        var token = _statusClear.Token;
        _ = Task.Delay(8000, token).ContinueWith(t => { if (!t.IsCanceled) Dispatcher.UIThread.Post(() => Status = ""); });
    }
}

/// <summary>Desktop notification for action results while the panel is closed (Linux: notify-send).</summary>
static class Notifier
{
    public static Func<bool> PanelOpen { get; set; } = () => false;

    public static void Show(string message)
    {
        if (PanelOpen() || OperatingSystem.IsWindows() || message == "Refreshed") return;
        if (Proc.Which("notify-send") is { } ns)
            Proc.Start([ns, "-a", "Claude Tray", "-i", "claude-tray", "Claude Code", message]);
    }
}
