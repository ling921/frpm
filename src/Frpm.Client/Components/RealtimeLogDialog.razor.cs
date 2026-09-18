namespace Frpm.Client.Components;

public partial class RealtimeLogDialog : ComponentBase, IAsyncDisposable
{
    [Inject] private ILogService LogService { get; set; } = default!;

    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    private const int MaximumRenderedLines = 2_000;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<string> _lines = [];
    private ElementReference _outputElement;
    private IJSObjectReference? _module;
    private Task? _refreshTask;
    private long _cursor;
    private bool _refreshing;
    private bool _scrollAfterRender = true;
    private bool _rendered;
    private bool _isLive;
    private string? _error;

    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = default!;
    [Parameter, EditorRequired] public TunnelRunModel Run { get; set; } = default!;

    private bool IsLive => _isLive;
    private string RunDescription => IsLive
        ? $"PID {Run.ProcessId?.ToString() ?? "未知"} · 启动于 {Run.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
        : Run.EndedAt is null
            ? $"PID {Run.ProcessId?.ToString() ?? "未知"} · 本次运行已结束"
        : $"最近运行 · {Run.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · 退出码 {Run.ExitCode?.ToString() ?? "未知"}";
    private string LogText => _lines.Count == 0 && _error is null
        ? (IsLive ? "等待日志输出…" : "本次运行没有日志输出。")
        : string.Join(Environment.NewLine, _lines);

    protected override async Task OnInitializedAsync()
    {
        _isLive = Run.EndedAt is null;
        await RefreshLogAsync();
        if (IsLive) _refreshTask = RefreshLoopAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        _rendered = true;
        if (firstRender)
        {
            _module = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./frpm-logs.js");
        }

        if (_scrollAfterRender && _module is not null)
        {
            _scrollAfterRender = false;
            await _module.InvokeVoidAsync("scrollToBottom", _outputElement);
        }
    }

    private async Task RefreshLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                await InvokeAsync(async () =>
                {
                    await RefreshLogAsync();
                    StateHasChanged();
                });
                if (!IsLive) break;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task RefreshNowAsync() => RefreshLogAsync(forceFollow: true);

    private async Task RefreshLogAsync(bool forceFollow = false)
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var shouldFollow = forceFollow || !_rendered || _module is null
                || await _module.InvokeAsync<bool>("isNearBottom", _outputElement);
            var tail = await LogService.TailAsync(Run.Id, _cursor, 500, _cts.Token);
            _cursor = tail.NextCursor;
            if (_isLive && tail.EndOfFile) _isLive = false;
            if (tail.Lines.Count > 0)
            {
                _lines.AddRange(tail.Lines);
                if (_lines.Count > MaximumRenderedLines)
                {
                    _lines.RemoveRange(0, _lines.Count - MaximumRenderedLines);
                }
            }

            _error = null;
            _scrollAfterRender = shouldFollow;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _error = $"读取日志失败：{ex.Message}";
        }
        finally
        {
            _refreshing = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_refreshTask is not null)
        {
            try { await _refreshTask; }
            catch (OperationCanceledException) { }
        }

        if (_module is not null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }

        _cts.Dispose();
    }
}
