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

    private static readonly IEqualityComparer<TunnelModel> _tunnelComparer = new TunnelModelIdEqualityComparer();

    private sealed class TunnelModelIdEqualityComparer : IEqualityComparer<TunnelModel>
    {
        public bool Equals(TunnelModel? x, TunnelModel? y) => x?.Id == y?.Id;
        public int GetHashCode(TunnelModel obj) => obj.Id.GetHashCode();
    }

    private List<TunnelModel> _tunnels = [];
    private readonly HashSet<Guid> _selectedTunnelIds = [];
    private HashSet<TunnelModel> _selectedTunnels = new(_tunnelComparer);
    private readonly HashSet<Guid> _busyTunnelIds = [];
    private bool _batchBusy;
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
    private IReadOnlyList<string> AvailableTunnelTypes => TunnelInputRules.TypesFor(SelectedProviderType, isEditing: _request.TunnelId is not null);
    private bool RequiresLocalEndpoint => TunnelInputRules.RequiresLocalEndpoint(SelectedProviderType, _request.Type);
    private bool UsesDomains => TunnelInputRules.IsDomainType(_request.Type);
    private bool UsesRemotePort => TunnelInputRules.UsesRemotePort(SelectedProviderType, _request.Type);

    private bool IsTunnelBusy(Guid id) => _busyTunnelIds.Contains(id) || _batchBusy;
    private bool IsSelected(TunnelModel tunnel) => _selectedTunnelIds.Contains(tunnel.Id);

    private void OnSelectedItemsChanged(HashSet<TunnelModel> items)
    {
        _selectedTunnels = new HashSet<TunnelModel>(items, _tunnelComparer);
        _selectedTunnelIds.Clear();
        foreach (var item in items)
        {
            _selectedTunnelIds.Add(item.Id);
        }
    }

    private void SyncSelectedTunnels()
    {
        var validIds = _tunnels.Select(t => t.Id).ToHashSet();
        _selectedTunnelIds.RemoveWhere(id => !validIds.Contains(id));
        _selectedTunnels = _tunnels.Where(t => _selectedTunnelIds.Contains(t.Id)).ToHashSet(_tunnelComparer);
    }

    private void ToggleSelect(TunnelModel tunnel, bool selected)
    {
        if (selected)
        {
            _selectedTunnelIds.Add(tunnel.Id);
        }
        else
        {
            _selectedTunnelIds.Remove(tunnel.Id);
        }
        SyncSelectedTunnels();
    }

    private void ClearSelection()
    {
        _selectedTunnelIds.Clear();
        _selectedTunnels = new HashSet<TunnelModel>(_tunnelComparer);
    }

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
            _refreshing = true;
            _accounts = (await AccountService.GetListAsync(_cts.Token)).ToList();
            var loaded = (await TunnelService.GetListAsync(_cts.Token)).ToList();
            _tunnels = loaded;
            SyncSelectedTunnels();
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
            _refreshing = false;
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

    private static bool TunnelsEqual(IReadOnlyList<TunnelModel> a, IReadOnlyList<TunnelModel> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x.Id != y.Id || x.RuntimeState != y.RuntimeState || x.RemoteState != y.RemoteState ||
                x.DesiredState != y.DesiredState || x.Name != y.Name || x.Remark != y.Remark ||
                x.RemoteAddress != y.RemoteAddress || x.LocalAddress != y.LocalAddress ||
                x.LocalPort != y.LocalPort || x.UpdatedAt != y.UpdatedAt)
                return false;
        }
        return true;
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
                    var updated = (await TunnelService.GetListAsync(_cts.Token)).ToList();
                    if (!TunnelsEqual(_tunnels, updated))
                    {
                        _tunnels = updated;
                        SyncSelectedTunnels();
                        await InvokeAsync(StateHasChanged);
                    }
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception)
                {
                    // A transient read failure must not terminate later refreshes.
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
        if (!TunnelInputRules.TypesFor(SelectedProviderType, isEditing: _request.TunnelId is not null).Contains(_request.Type, StringComparer.OrdinalIgnoreCase))
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

    private ProviderNodeModel? SelectedNode =>
        _nodes.FirstOrDefault(n => string.Equals(n.Id, _request.NodeId, StringComparison.OrdinalIgnoreCase));

    private async Task OpenNodeSelectorAsync()
    {
        if (_loadingNodes || _request.TunnelId is not null) return;
        if (_nodes.Count == 0)
        {
            Snackbar.Add("当前账号下暂无可用服务器节点。", Severity.Info);
            return;
        }

        var parameters = new DialogParameters<NodeSelectorDialog>
        {
            { x => x.Nodes, _nodes },
            { x => x.SelectedNodeId, _request.NodeId }
        };

        var options = new DialogOptions
        {
            MaxWidth = MaxWidth.Medium,
            FullWidth = true,
            CloseButton = true
        };

        var dialog = await DialogService.ShowAsync<NodeSelectorDialog>("挑选隧道服务器节点", parameters, options);
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: ProviderNodeModel selectedNode })
        {
            _request.NodeId = selectedNode.Id;
        }
    }

    private int _batchStartCount;
    private string? _batchStartReason;
    private bool CanBatchStart
    {
        get
        {
            var (can, reason, count) = TunnelActionEvaluator.EvaluateBatchStart(_selectedTunnels);
            _batchStartCount = count;
            _batchStartReason = reason;
            return can;
        }
    }
    private string BatchStartTooltip => _batchStartReason ?? $"启动选中的 {_batchStartCount} 个已停止隧道";

    private int _batchStopCount;
    private string? _batchStopReason;
    private bool CanBatchStop
    {
        get
        {
            var (can, reason, count) = TunnelActionEvaluator.EvaluateBatchStop(_selectedTunnels);
            _batchStopCount = count;
            _batchStopReason = reason;
            return can;
        }
    }
    private string BatchStopTooltip => _batchStopReason ?? $"停止选中的 {_batchStopCount} 个运行中隧道";

    private int _batchKickCount;
    private string? _batchKickReason;
    private bool IsBatchKickVisible
    {
        get
        {
            var (_, visible, _, _) = TunnelActionEvaluator.EvaluateBatchKick(_selectedTunnels, t => BuiltInProviderCatalog.Get(t.ProviderType).Capabilities);
            return visible;
        }
    }
    private bool CanBatchKick
    {
        get
        {
            var (can, _, reason, count) = TunnelActionEvaluator.EvaluateBatchKick(_selectedTunnels, t => BuiltInProviderCatalog.Get(t.ProviderType).Capabilities);
            _batchKickCount = count;
            _batchKickReason = reason;
            return can;
        }
    }
    private string BatchKickTooltip => _batchKickReason ?? $"对选中的 {_batchKickCount} 个隧道向供应商发送强制下线并解除禁用";

    private int _batchDeleteCount;
    private bool CanBatchDelete
    {
        get
        {
            var (can, _, count) = TunnelActionEvaluator.EvaluateBatchDelete(_selectedTunnels);
            _batchDeleteCount = count;
            return can;
        }
    }
    private string BatchDeleteTooltip => $"删除选中的 {_selectedTunnels.Count} 个隧道";

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null or 0) return "0 B";
        var b = bytes.Value;
        if (b < 1024) return $"{b} B";
        if (b < 1024 * 1024) return $"{(b / 1024.0):F1} KB";
        if (b < 1024 * 1024 * 1024) return $"{(b / (1024.0 * 1024)):F1} MB";
        return $"{(b / (1024.0 * 1024 * 1024)):F2} GB";
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
            _busyTunnelIds.Add(tunnel.Id);
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
            Snackbar.Add($"隧道“{tunnel.Name}”启动命令已执行。", Severity.Success);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"启动失败：{ex.Message}", Severity.Error);
        }
        finally
        {
            _busyTunnelIds.Remove(tunnel.Id);
        }
    }

    private async Task StopAsync(TunnelModel tunnel)
    {
        try
        {
            _busyTunnelIds.Add(tunnel.Id);
            await TunnelService.StopAsync(tunnel.Id, _cts.Token);
            Snackbar.Add($"隧道“{tunnel.Name}”已停止。", Severity.Info);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"停止失败：{ex.Message}", Severity.Error);
        }
        finally
        {
            _busyTunnelIds.Remove(tunnel.Id);
        }
    }

    private async Task KickAsync(TunnelModel tunnel)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "强制下线并重置",
            $"将向供应商发送强制下线指令并解除禁用状态，同时重置运行状态。适用于隧道残留导致“隧道当前在线”或连接异常的情况。是否继续？",
            yesText: "执行下线",
            cancelText: "取消");
        if (confirmed is not true)
        {
            return;
        }

        try
        {
            _busyTunnelIds.Add(tunnel.Id);
            await TunnelService.KickAsync(tunnel.Id, _cts.Token);
            Snackbar.Add($"隧道“{tunnel.Name}”已向供应商发送下线指令并解除禁用。", Severity.Success);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"强制下线失败：{ex.Message}", Severity.Error);
        }
        finally
        {
            _busyTunnelIds.Remove(tunnel.Id);
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
            _busyTunnelIds.Add(tunnel.Id);
            await TunnelService.DeleteAsync(tunnel.Id, _cts.Token);
            Snackbar.Add($"隧道“{tunnel.Name}”已成功删除。", Severity.Success);
            _selectedTunnelIds.Remove(tunnel.Id);
            SyncSelectedTunnels();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"删除失败：{ex.Message}", Severity.Error);
        }
        finally
        {
            _busyTunnelIds.Remove(tunnel.Id);
        }
    }

    private async Task BatchStartAsync()
    {
        var targets = _selectedTunnels.Where(t => TunnelActionEvaluator.CanStart(t, out _)).ToList();
        if (targets.Count == 0)
        {
            Snackbar.Add("选中的隧道均已在运行中或不可用。", Severity.Info);
            return;
        }

        _batchBusy = true;
        var successCount = 0;
        var failCount = 0;
        try
        {
            foreach (var tunnel in targets)
            {
                try
                {
                    _busyTunnelIds.Add(tunnel.Id);
                    await TunnelService.StartAsync(tunnel.Id, _cts.Token);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failCount++;
                    Snackbar.Add($"启动隧道“{tunnel.Name}”失败：{ex.Message}", Severity.Error);
                }
                finally
                {
                    _busyTunnelIds.Remove(tunnel.Id);
                }
            }

            if (successCount > 0)
            {
                Snackbar.Add($"批量启动完成：成功启动 {successCount} 个隧道" + (failCount > 0 ? $"，失败 {failCount} 个" : "") + "。", failCount > 0 ? Severity.Warning : Severity.Success);
            }
            await LoadAsync();
        }
        finally
        {
            _batchBusy = false;
        }
    }

    private async Task BatchStopAsync()
    {
        var targets = _selectedTunnels.Where(t => TunnelActionEvaluator.CanStop(t, out _)).ToList();
        if (targets.Count == 0)
        {
            Snackbar.Add("选中的隧道均已处于停止状态。", Severity.Info);
            return;
        }

        _batchBusy = true;
        var successCount = 0;
        var failCount = 0;
        try
        {
            foreach (var tunnel in targets)
            {
                try
                {
                    _busyTunnelIds.Add(tunnel.Id);
                    await TunnelService.StopAsync(tunnel.Id, _cts.Token);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failCount++;
                    Snackbar.Add($"停止隧道“{tunnel.Name}”失败：{ex.Message}", Severity.Error);
                }
                finally
                {
                    _busyTunnelIds.Remove(tunnel.Id);
                }
            }

            if (successCount > 0)
            {
                Snackbar.Add($"批量停止完成：已停止 {successCount} 个隧道" + (failCount > 0 ? $"，失败 {failCount} 个" : "") + "。", Severity.Info);
            }
            await LoadAsync();
        }
        finally
        {
            _batchBusy = false;
        }
    }

    private async Task BatchKickAsync()
    {
        var targets = _selectedTunnels
            .Where(t => TunnelActionEvaluator.CanKick(t, BuiltInProviderCatalog.Get(t.ProviderType).Capabilities, out _))
            .ToList();
        if (targets.Count == 0)
        {
            Snackbar.Add("选中的隧道均不支持强制下线或无需下线。", Severity.Info);
            return;
        }

        var confirmed = await DialogService.ShowMessageBoxAsync(
            "批量强制下线并重置",
            $"将向供应商发送强制下线指令并解除禁用状态（符合条件的共 {targets.Count} 个隧道）。是否继续？",
            yesText: "执行下线",
            cancelText: "取消");
        if (confirmed is not true) return;

        _batchBusy = true;
        var successCount = 0;
        var failCount = 0;
        try
        {
            foreach (var tunnel in targets)
            {
                try
                {
                    _busyTunnelIds.Add(tunnel.Id);
                    await TunnelService.KickAsync(tunnel.Id, _cts.Token);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failCount++;
                    Snackbar.Add($"强制下线隧道“{tunnel.Name}”失败：{ex.Message}", Severity.Error);
                }
                finally
                {
                    _busyTunnelIds.Remove(tunnel.Id);
                }
            }

            Snackbar.Add($"批量强制下线完成：已处理 {successCount} 个隧道" + (failCount > 0 ? $"，失败 {failCount} 个" : "") + "。", failCount > 0 ? Severity.Warning : Severity.Success);
            await LoadAsync();
        }
        finally
        {
            _batchBusy = false;
        }
    }

    private async Task BatchDeleteAsync()
    {
        var targets = _selectedTunnels.ToList();
        if (targets.Count == 0) return;

        var confirmed = await DialogService.ShowMessageBoxAsync(
            "批量删除隧道",
            $"确定要彻底删除已选中的 {targets.Count} 个隧道吗？此操作将停止它们并从第三方供应商删除，无法撤销。",
            yesText: "批量删除",
            cancelText: "取消");
        if (confirmed is not true) return;

        _batchBusy = true;
        var successCount = 0;
        var failCount = 0;
        try
        {
            foreach (var tunnel in targets)
            {
                try
                {
                    _busyTunnelIds.Add(tunnel.Id);
                    await TunnelService.DeleteAsync(tunnel.Id, _cts.Token);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failCount++;
                    Snackbar.Add($"删除隧道“{tunnel.Name}”失败：{ex.Message}", Severity.Error);
                }
                finally
                {
                    _busyTunnelIds.Remove(tunnel.Id);
                }
            }

            Snackbar.Add($"批量删除完成：成功删除 {successCount} 个隧道" + (failCount > 0 ? $"，失败 {failCount} 个" : "") + "。", failCount > 0 ? Severity.Warning : Severity.Success);
            ClearSelection();
            await LoadAsync();
        }
        finally
        {
            _batchBusy = false;
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
        if (tunnel.Type is "http" or "https")
        {
            var hosts = (tunnel.RemoteAddress ?? string.Empty)
                .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (hosts.Count > 0)
            {
                return hosts.Select(host =>
                {
                    var uriString = $"{tunnel.Type}://{host}";
                    var valid = Uri.TryCreate(uriString, UriKind.Absolute, out var uri);
                    return new PublicAddress(host, valid ? uri!.ToString() : null);
                }).ToList();
            }

            if (!string.IsNullOrWhiteSpace(tunnel.ProviderDomain))
            {
                var uriString = $"{tunnel.Type}://{tunnel.ProviderDomain}";
                var valid = Uri.TryCreate(uriString, UriKind.Absolute, out var uri);
                return [new PublicAddress(tunnel.ProviderDomain, valid ? uri!.ToString() : null)];
            }
        }

        if (tunnel.ProviderType == ProviderType.SakuraFrp
            && _nodesByAccount.TryGetValue(tunnel.ProviderAccountId, out var nodes)
            && tunnel.NodeId is not null
            && nodes.TryGetValue(tunnel.NodeId, out var node)
            && !string.IsNullOrWhiteSpace(node.Address))
        {
            var port = string.IsNullOrWhiteSpace(tunnel.RemoteAddress) ? "" : $":{tunnel.RemoteAddress}";
            return [new PublicAddress($"{node.Address}{port}", null)];
        }

        if (!string.IsNullOrWhiteSpace(tunnel.NodeAddress))
        {
            var port = string.IsNullOrWhiteSpace(tunnel.RemoteAddress) ? "" : $":{tunnel.RemoteAddress}";
            return [new PublicAddress($"{tunnel.NodeAddress}{port}", null)];
        }

        if (!string.IsNullOrWhiteSpace(tunnel.RemoteAddress))
        {
            return [new PublicAddress(tunnel.RemoteAddress, null)];
        }

        return [];
    }

    private async Task CopyPublicAddressAsync(string text)
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", text);
            Snackbar.Add("已复制到剪贴板。", Severity.Success);
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
