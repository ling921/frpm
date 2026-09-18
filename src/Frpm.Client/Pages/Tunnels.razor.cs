namespace Frpm.Client.Pages;

public partial class Tunnels : ComponentBase, IAsyncDisposable
{
    [Inject]
    private ITunnelService TunnelService { get; set; } = default!;

    [Inject]
    private IProviderAccountService AccountService { get; set; } = default!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = default!;

    [Inject]
    private IDialogService DialogService { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    private List<TunnelModel> _tunnels = [];
    private List<ProviderAccountModel> _accounts = [];
    private List<ProviderNodeModel> _nodes = [];
    private readonly Dictionary<Guid, IReadOnlyDictionary<string, ProviderNodeModel>> _nodesByAccount = [];
    private List<string> _localAddressSuggestions = ["127.0.0.1", "0.0.0.0", "::1", "::"];
    private SaveTunnelRequest _request = NewRequest();
    private string _search = string.Empty;
    private bool _loading = true;
    private bool _busy;
    private bool _editorOpen;
    private bool _loadingNodes;
    private bool _testingEndpoint;
    private bool _refreshing;
    private readonly CancellationTokenSource _cts = new();
    private Task? _refreshTask;
    private IReadOnlyList<TunnelModel> Filtered => string.IsNullOrWhiteSpace(_search)
        ? _tunnels
        : _tunnels.Where(MatchesSearch).ToList();
    private bool RequiresNode => _accounts.FirstOrDefault(account => account.Id == _request.ProviderAccountId)?.Capabilities.HasNodes == true;
    private ProviderType SelectedProviderType => _accounts.FirstOrDefault(account => account.Id == _request.ProviderAccountId)?.ProviderType ?? ProviderType.SakuraFrp;
    private IReadOnlyList<string> AvailableTunnelTypes => TunnelInputRules.TypesFor(SelectedProviderType);
    private bool RequiresLocalEndpoint => TunnelInputRules.RequiresLocalEndpoint(SelectedProviderType, _request.Type);
    private bool UsesDomains => TunnelInputRules.IsDomainType(_request.Type);
    private bool UsesRemotePort => TunnelInputRules.UsesRemotePort(SelectedProviderType, _request.Type);

    private string DomainHelperText => SelectedProviderType switch
    {
        ProviderType.LoliaFrp => "仅支持一个域名，请勿包含协议、端口或路径",
        ProviderType.SakuraFrp => "最多 3 个域名，可使用英文逗号、空格或换行分隔",
        _ => "支持多个域名，可使用英文逗号、空格或换行分隔"
    };

    private string NodeHelperText => _loadingNodes ? "正在加载节点…" : _nodes.Count == 0 ? "该账号没有可选择的节点" : "下拉项依次显示节点名称、地址和可用状态";

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        _refreshTask = RefreshLoopAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _accounts = (await AccountService.GetListAsync(_cts.Token)).ToList();
            _tunnels = (await TunnelService.GetListAsync(_cts.Token)).ToList();
            _localAddressSuggestions = (await TunnelService.GetLocalAddressSuggestionsAsync(_cts.Token)).ToList();
            await LoadNodeAddressesAsync();
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

    private async Task LoadNodeAddressesAsync()
    {
        var accountsWithNodes = _accounts.Where(account => account.Capabilities.HasNodes).ToList();
        var results = await Task.WhenAll(accountsWithNodes.Select(async account =>
        {
            try
            {
                var nodes = await AccountService.GetNodesAsync(account.Id, _cts.Token);
                return (account.Id, Nodes: (IReadOnlyDictionary<string, ProviderNodeModel>)nodes.ToDictionary(node => node.Id, StringComparer.Ordinal));
            }
            catch (Exception) when (!_cts.IsCancellationRequested)
            {
                return (account.Id, Nodes: (IReadOnlyDictionary<string, ProviderNodeModel>)new Dictionary<string, ProviderNodeModel>());
            }
        }));
        foreach (var result in results) _nodesByAccount[result.Id] = result.Nodes;
    }
    private async Task RefreshLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                try
                {
                    _refreshing = true;
                    await InvokeAsync(StateHasChanged);
                    _tunnels = (await TunnelService.GetListAsync(_cts.Token)).ToList();
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception)
                {
                    // A transient read failure must not terminate later refreshes.
                }
                finally
                {
                    _refreshing = false;
                    await InvokeAsync(StateHasChanged);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
    private async Task OpenCreate()
    {
        if (_accounts.Count == 0)
        {
            Snackbar.Add("请先添加供应商账号。", Severity.Warning);
            return;
        }

        _request = NewRequest();
        _request.ProviderAccountId = _accounts[0].Id;
        _editorOpen = true;
        await LoadNodesAsync(_request.ProviderAccountId);
    }

    private async Task EditAsync(TunnelModel tunnel)
    {
        _request = new()
        {
            TunnelId = tunnel.Id,
            ProviderAccountId = tunnel.ProviderAccountId,
            RemoteId = tunnel.RemoteId,
            Name = tunnel.Name,
            Remark = tunnel.Remark,
            Type = tunnel.Type,
            NodeId = tunnel.NodeId,
            LocalAddress = tunnel.LocalAddress ?? "127.0.0.1",
            LocalPort = tunnel.LocalPort,
            CustomDomain = tunnel.Type is "http" or "https" ? tunnel.RemoteAddress : null,
            RemotePort = tunnel.Type is not ("http" or "https")
                && int.TryParse(tunnel.RemoteAddress, out var port)
                    ? port
                    : null
        };
        _editorOpen = true;
        await LoadNodesAsync(_request.ProviderAccountId);
    }

    private void CloseEditor()
    {
        _editorOpen = false;
        _nodes = [];
    }

    private async Task OnProviderAccountChangedAsync(Guid accountId)
    {
        _request.ProviderAccountId = accountId;
        _request.NodeId = null;
        if (!TunnelInputRules.TypesFor(SelectedProviderType).Contains(_request.Type, StringComparer.OrdinalIgnoreCase))
        {
            OnTunnelTypeChanged("tcp");
        }
        await LoadNodesAsync(accountId);
    }

    private void OnTunnelTypeChanged(string type)
    {
        _request.Type = type;
        if (!TunnelInputRules.IsDomainType(type)) _request.CustomDomain = null;
        if (!TunnelInputRules.UsesRemotePort(SelectedProviderType, type)) _request.RemotePort = null;
        if (!TunnelInputRules.RequiresLocalEndpoint(SelectedProviderType, type))
        {
            _request.LocalAddress = string.Empty;
            _request.LocalPort = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(_request.LocalAddress)) _request.LocalAddress = "127.0.0.1";
        _request.LocalPort ??= type switch
        {
            "http" => 80,
            "https" => 443,
            "eudp" or "udp" => 19132,
            _ => null
        };
    }

    private async Task LoadNodesAsync(Guid accountId)
    {
        try
        {
            _loadingNodes = true;
            _nodes = (await AccountService.GetNodesAsync(accountId, _cts.Token))
                .OrderByDescending(node => node.Available)
                .ThenBy(node => node.Name)
                .ToList();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _nodes = [];
            Snackbar.Add($"加载节点失败：{ex.Message}", Severity.Error);
        }
        finally
        {
            _loadingNodes = false;
        }
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_request.Name) || _request.ProviderAccountId == Guid.Empty)
        {
            Snackbar.Add("请填写账号和隧道名称。", Severity.Warning);
            return;
        }

        if (RequiresNode && string.IsNullOrWhiteSpace(_request.NodeId))
        {
            Snackbar.Add("请选择节点。", Severity.Warning);
            return;
        }

        try
        {
            _busy = true;
            await TunnelService.SaveAsync(_request, _cts.Token);
            Snackbar.Add("隧道已保存。", Severity.Success);
            _editorOpen = false;
            _nodes = [];
            await LoadAsync();
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
    private async Task TestLocalEndpointAsync()
    {
        try
        {
            _testingEndpoint = true;
            var result = await TunnelService.TestLocalEndpointAsync(new(_request.LocalAddress, _request.LocalPort, _request.Type), _cts.Token);
            Snackbar.Add(result.Message, result.Reachable ? Severity.Success : result.Conclusive ? Severity.Warning : Severity.Info);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Snackbar.Add($"测试失败：{ex.Message}", Severity.Error);
        }
        finally
        {
            _testingEndpoint = false;
        }
    }

    private Task<IEnumerable<string>> SearchLocalAddressesAsync(string? value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var suggestions = _localAddressSuggestions
            .Concat(_tunnels.Select(tunnel => tunnel.LocalAddress).OfType<string>().Where(address => !string.IsNullOrWhiteSpace(address)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(address => string.IsNullOrWhiteSpace(value) || address.Contains(value, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(suggestions);
    }

    private Task<IEnumerable<int?>> SearchLocalPortsAsync(string? value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var defaults = _request.Type switch
        {
            "http" => new[] { 80, 3000, 5000, 5173, 8000, 8080, 8888 },
            "https" => new[] { 443, 8443 },
            "udp" or "eudp" => new[] { 53, 123, 19132, 25565 },
            _ => new[] { 22, 80, 443, 3389, 3306, 5432, 6379, 8080, 25565 }
        };
        var suggestions = defaults
            .Concat(_tunnels.Where(tunnel => tunnel.LocalPort is not null).Select(tunnel => tunnel.LocalPort!.Value))
            .Distinct()
            .Order()
            .Where(port => string.IsNullOrWhiteSpace(value) || port.ToString().Contains(value, StringComparison.OrdinalIgnoreCase))
            .Select(port => (int?)port);
        return Task.FromResult(suggestions);
    }

    private static string FormatPort(int? port) => port?.ToString() ?? "";

    private async Task StartAsync(TunnelModel tunnel)
    {
        try
        {
            if (TunnelInputRules.RequiresLocalEndpoint(tunnel.ProviderType, tunnel.Type)
                && !string.IsNullOrWhiteSpace(tunnel.LocalAddress)
                && tunnel.LocalPort is not null)
            {
                var test = await TunnelService.TestLocalEndpointAsync(new(tunnel.LocalAddress, tunnel.LocalPort, tunnel.Type), _cts.Token);
                if (test.Conclusive && !test.Reachable)
                {
                    Snackbar.Add($"本地服务检查未通过：{test.Message} 隧道仍将继续启动。", Severity.Warning);
                }
            }
            await TunnelService.StartAsync(tunnel.Id, _cts.Token);
            Snackbar.Add("启动命令已执行。", Severity.Success);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task StopAsync(TunnelModel tunnel)
    {
        try
        {
            await TunnelService.StopAsync(tunnel.Id, _cts.Token);
            Snackbar.Add("隧道已停止。", Severity.Info);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task DeleteAsync(TunnelModel tunnel)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "删除隧道",
            $"将停止隧道“{tunnel.Name}”，并从第三方供应商删除它。此操作无法撤销。",
            yesText: "删除",
            cancelText: "取消");
        if (confirmed is not true)
        {
            return;
        }

        try
        {
            await TunnelService.DeleteAsync(tunnel.Id, _cts.Token);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }
    private async Task OpenLogAsync(TunnelModel tunnel)
    {
        try
        {
            var runs = await TunnelService.GetRunsAsync(tunnel.Id, _cts.Token);
            var run = tunnel.RuntimeState == TunnelRuntimeState.Running
                ? runs.FirstOrDefault(candidate => candidate.EndedAt is null)
                : runs.FirstOrDefault();
            if (run is null)
            {
                Snackbar.Add(tunnel.RuntimeState == TunnelRuntimeState.Running ? "当前运行记录尚未就绪，请稍后重试。" : "该隧道还没有运行日志。", Severity.Info);
                return;
            }

            var parameters = new DialogParameters { [nameof(RealtimeLogDialog.Run)] = run };
            var options = new DialogOptions { FullWidth = true, MaxWidth = MaxWidth.Large, CloseButton = true };
            await DialogService.ShowAsync<RealtimeLogDialog>(string.Empty, parameters, options);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Snackbar.Add($"打开日志失败：{ex.Message}", Severity.Error);
        }
    }

    private static string NodeOptionText(ProviderNodeModel node)
    {
        var details = new List<string> { node.Name };
        AddNodeDetail(details, node.Region);
        AddNodeDetail(details, node.Bandwidth);
        AddNodeDetail(details, NormalizeNodeTypes(node.AllowedTypes));
        AddNodeDetail(details, string.IsNullOrWhiteSpace(node.PortRange) ? null : $"端口 {node.PortRange}");
        AddNodeDetail(details, node.Description);
        AddNodeDetail(details, node.Address);
        details.Add(node.Available ? "可用" : "不可用");
        return string.Join(" · ", details);
    }

    private string NodeDisplay(TunnelModel tunnel)
    {
        var name = tunnel.NodeName ?? "未指定节点";
        return _nodesByAccount.TryGetValue(tunnel.ProviderAccountId, out var nodes)
            && tunnel.NodeId is not null
            && nodes.TryGetValue(tunnel.NodeId, out var node)
            && !string.IsNullOrWhiteSpace(node.Address)
                ? $"{name} · {node.Address}"
                : name;
    }

    private IReadOnlyList<PublicAddress> GetPublicAddresses(TunnelModel tunnel)
    {
        var remoteAddress = tunnel.RemoteAddress?.Trim();
        if (string.IsNullOrWhiteSpace(remoteAddress)) return [];
        if (TunnelInputRules.IsDomainType(tunnel.Type))
        {
            var scheme = string.Equals(tunnel.Type, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
            return TunnelInputRules.ParseDomains(remoteAddress)
                .Select(domain => new PublicAddress(domain, $"{scheme}://{domain}"))
                .ToList();
        }

        var host = tunnel.ProviderType switch
        {
            ProviderType.SakuraFrp => tunnel.ProviderDomain ?? NodeAddress(tunnel),
            ProviderType.LoliaFrp => tunnel.NodeAddress ?? NodeAddress(tunnel),
            ProviderType.MeFrp => NodeAddress(tunnel),
            _ => NodeAddress(tunnel)
        };
        return [new PublicAddress(FormatEndpoint(host, remoteAddress), null)];
    }

    private string? NodeAddress(TunnelModel tunnel) =>
        _nodesByAccount.TryGetValue(tunnel.ProviderAccountId, out var nodes)
        && tunnel.NodeId is not null
        && nodes.TryGetValue(tunnel.NodeId, out var node)
        && !string.IsNullOrWhiteSpace(node.Address)
            ? node.Address.Trim()
            : null;

    private static string FormatEndpoint(string? host, string remoteAddress)
    {
        if (string.IsNullOrWhiteSpace(host)) return remoteAddress;
        return host.Contains(':') && !host.StartsWith('[')
            ? $"[{host}]:{remoteAddress}"
            : $"{host}:{remoteAddress}";
    }

    private async Task CopyPublicAddressAsync(string address)
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", address);
            Snackbar.Add("映射地址已复制。", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"复制失败：{ex.Message}", Severity.Error);
        }
    }

    private sealed record PublicAddress(string Display, string? Url);

    private bool MatchesSearch(TunnelModel tunnel)
    {
        var text = $"{tunnel.Name} {tunnel.ProviderAccountName} {tunnel.RemoteAddress} "
            + $"{tunnel.RuntimeState} {tunnel.RemoteState} "
            + $"{RuntimeText(tunnel.RuntimeState)} {RemoteText(tunnel.RemoteState)}";
        return text.Contains(_search, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddNodeDetail(List<string> details, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            details.Add(value.Trim());
        }
    }

    private static string? NormalizeNodeTypes(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : string.Join(" / ", value.Split([';', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(type => type.ToUpperInvariant()));

    private static Color RuntimeColor(TunnelRuntimeState state) => state switch
    {
        TunnelRuntimeState.Running => Color.Success,
        TunnelRuntimeState.Failed => Color.Error,
        TunnelRuntimeState.Starting or TunnelRuntimeState.Stopping => Color.Warning,
        _ => Color.Default
    };

    private static Color RemoteColor(TunnelRemoteState state) => state switch
    {
        TunnelRemoteState.Active => Color.Success,
        TunnelRemoteState.RemoteMissing or TunnelRemoteState.Deleted => Color.Error,
        TunnelRemoteState.Inactive => Color.Info,
        _ => Color.Default
    };

    private static string RuntimeTooltip(TunnelModel tunnel) => string.IsNullOrWhiteSpace(tunnel.RuntimeMessage)
        ? RuntimeText(tunnel.RuntimeState)
        : $"{RuntimeText(tunnel.RuntimeState)} · {tunnel.RuntimeMessage}";

    private static string RuntimeText(TunnelRuntimeState state) => state switch
    {
        TunnelRuntimeState.Stopped => "已停止",
        TunnelRuntimeState.Starting => "启动中",
        TunnelRuntimeState.Running => "运行中",
        TunnelRuntimeState.Stopping => "停止中",
        TunnelRuntimeState.Failed => "失败",
        _ => "/"
    };

    private static string RemoteText(TunnelRemoteState state) => state switch
    {
        TunnelRemoteState.Active => "在线",
        TunnelRemoteState.Inactive => "离线",
        TunnelRemoteState.RemoteMissing => "远端缺失",
        TunnelRemoteState.Deleted => "已删除",
        _ => "/"
    };

    private static SaveTunnelRequest NewRequest() => new() { Type = "tcp", LocalAddress = "127.0.0.1" };

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
