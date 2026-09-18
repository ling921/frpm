namespace Frpm.Client.Pages;

public partial class Home : ComponentBase, IAsyncDisposable
{
    [Inject]
    private ITunnelService TunnelService { get; set; } = default!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = default!;

    private DashboardModel? _dashboard;
    private bool _loading;
    private readonly CancellationTokenSource _cts = new();
    private Task? _refreshTask;

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
            _dashboard = await TunnelService.GetDashboardAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task RefreshLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                await LoadAsync();
                await InvokeAsync(StateHasChanged);
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
