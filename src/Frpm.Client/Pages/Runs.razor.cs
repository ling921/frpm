namespace Frpm.Client.Pages;

public partial class Runs : ComponentBase, IAsyncDisposable
{
    [Inject]
    private ITunnelService TunnelService { get; set; } = default!;

    [Inject]
    private ILogService LogService { get; set; } = default!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = default!;

    [SupplyParameterFromQuery]
    public Guid? TunnelId { get; set; }
    private List<TunnelRunModel> _runs = [];
    private TunnelRunModel? _selected;
    private readonly List<string> _lines = [];
    private long _cursor;
    private bool _loading = true;
    private readonly CancellationTokenSource _cts = new();
    private Task? _refreshTask;
    protected override async Task OnInitializedAsync()
    {
        try
        {
            _runs = (await TunnelService.GetRunsAsync(TunnelId, _cts.Token)).ToList();
            if (_runs.Count > 0)
            {
                await SelectRunAsync(_runs[0]);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally
        {
            _loading = false;
        }

        _refreshTask = RefreshLoopAsync();
    }

    private async Task SelectRunAsync(TunnelRunModel run)
    {
        _selected = run;
        _cursor = 0;
        _lines.Clear();
        await RefreshLogAsync();
    }

    private async Task RefreshLogAsync()
    {
        if (_selected is null)
        {
            return;
        }

        try
        {
            var tail = await LogService.TailAsync(_selected.Id, _cursor, 500, _cts.Token);
            _cursor = tail.NextCursor;
            _lines.AddRange(tail.Lines);
            await InvokeAsync(StateHasChanged);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task RefreshLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                await RefreshLogAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_refreshTask is not null)
        {
            try
            {
                await _refreshTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
    }
}
