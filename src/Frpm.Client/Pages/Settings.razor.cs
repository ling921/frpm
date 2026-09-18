namespace Frpm.Client.Pages;

public partial class Settings : ComponentBase, IAsyncDisposable
{
    [Inject]
    private ISystemSettingsService SettingsService { get; set; } = default!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = default!;

    private readonly CancellationTokenSource _cts = new();
    private SystemSettingsModel? _settings;
    private Task? _refreshTask;
    private int _syncIntervalSeconds = 60;
    private long _uptimeBaseSeconds;
    private DateTimeOffset _uptimeCapturedAt;
    private bool _loading = true;
    private bool _busy;
    private bool _refreshWarningShown;

    private string OperatingSystemText => string.IsNullOrWhiteSpace(_settings?.OperatingSystemDescription)
        ? _settings?.OperatingSystem ?? "/"
        : $"{_settings.OperatingSystem} · {_settings.OperatingSystemDescription}";
    private long CurrentUptimeSeconds => _uptimeBaseSeconds
        + Math.Max(0, (long)(DateTimeOffset.UtcNow - _uptimeCapturedAt).TotalSeconds);

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        _refreshTask = RefreshLoopAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _loading = true;
            _settings = await SettingsService.GetAsync(_cts.Token);
            _syncIntervalSeconds = _settings.ProviderSyncIntervalSeconds;
            CaptureUptime(_settings.UptimeSeconds);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Snackbar.Add($"加载系统设置失败：{ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            _busy = true;
            _settings = await SettingsService.SaveAsync(new(_syncIntervalSeconds), _cts.Token);
            Snackbar.Add("系统设置已保存并立即生效。", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task SyncNowAsync()
    {
        try
        {
            _busy = true;
            await SettingsService.RequestProviderSyncAsync(_cts.Token);
            Snackbar.Add("已请求立即同步。", Severity.Success);
            _settings = await SettingsService.GetAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RefreshLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var ticks = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                ticks++;
                if (ticks % 5 == 0)
                {
                    try
                    {
                        _settings = await SettingsService.GetAsync(_cts.Token);
                        _refreshWarningShown = false;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        if (!_refreshWarningShown)
                        {
                            _refreshWarningShown = true;
                            await InvokeAsync(() => Snackbar.Add($"系统状态刷新暂时失败：{ex.Message}", Severity.Warning));
                        }
                    }
                }
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CaptureUptime(long seconds)
    {
        _uptimeBaseSeconds = Math.Max(0, seconds);
        _uptimeCapturedAt = DateTimeOffset.UtcNow;
    }

    private static string FormatTime(DateTimeOffset value) => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    private static string FormatOptionalTime(DateTimeOffset? value) => value is null ? "尚未执行" : FormatTime(value.Value);
    private static string FormatDuration(long seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.Days > 0
            ? $"{duration.Days} 天 {duration.Hours} 小时 {duration.Minutes} 分 {duration.Seconds} 秒"
            : duration.Hours > 0
                ? $"{duration.Hours} 小时 {duration.Minutes} 分 {duration.Seconds} 秒"
                : $"{duration.Minutes} 分 {duration.Seconds} 秒";
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_refreshTask is not null)
        {
            try { await _refreshTask; }
            catch (OperationCanceledException) { }
        }
        _cts.Dispose();
    }
}
