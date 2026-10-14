using System.Text.Json;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Frpm.Abstractions.Models;
using Frpm.Domain.Enums;

namespace Frpm.Desktop;

internal sealed partial class MainWindow
{
    private async Task RenderTunnelsAsync()
    {
        var tunnels = await _workspace.GetTunnelsAsync(_lifetime.Token);
        var accounts = await _workspace.GetAccountsAsync(_lifetime.Token);
        var runs = await _workspace.GetRunsAsync(ct: _lifetime.Token);
        var tunnelIdsWithRuns = runs.Select(run => run.TunnelId).ToHashSet();
        BeginPage("隧道", "配置本地映射、启动本机 frpc，或查看实时日志");
        var filter = Input(_tunnelFilter, "搜索隧道、账号、节点或映射地址");
        var list = new StackPanel { Spacing = 8 };
        void UpdateList()
        {
            list.Children.Clear();
            var query = _tunnelFilter.Trim();
            foreach (var tunnel in tunnels.Where(t => query.Length == 0 ||
                $"{t.Name} {t.ProviderAccountName} {t.NodeName} {t.LocalAddress} {t.RemoteAddress} {t.Remark}".Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                heading.Children.Add(Text(tunnel.Name, size: 18));
                var pill = StatusPill(RuntimeText(tunnel.RuntimeState), tunnel.RuntimeState);
                Grid.SetColumn(pill, 1);
                heading.Children.Add(pill);
                var localEndpoint = tunnel.LocalPort is null ? "未配置本地地址" : $"{tunnel.LocalAddress}:{tunnel.LocalPort}";
                var rowContent = Stack(heading,
                    Text($"{localEndpoint}  →  {tunnel.RemoteAddress ?? "等待远端映射"}", size: 15),
                    Text($"{tunnel.ProviderAccountName} · {tunnel.ProviderType} · {tunnel.Type.ToUpperInvariant()} · {tunnel.NodeName ?? tunnel.NodeId ?? "默认节点"}", hint: true));
                if (!string.IsNullOrWhiteSpace(tunnel.RuntimeMessage) && tunnel.RuntimeState is TunnelRuntimeState.Failed)
                    rowContent.Children.Add(Text(tunnel.RuntimeMessage, hint: true));
                if (!string.IsNullOrWhiteSpace(tunnel.Remark))
                    rowContent.Children.Add(Text(tunnel.Remark, hint: true));

                var actions = new List<Control>();
                if (tunnel.RuntimeState == TunnelRuntimeState.Running)
                    actions.Add(ActionButton("停止", () => _workspace.StopTunnelAsync(tunnel.Id, _lifetime.Token)));
                else if (tunnel.RuntimeState is not (TunnelRuntimeState.Starting or TunnelRuntimeState.Stopping))
                    actions.Add(ActionButton("启动", () => _workspace.StartTunnelAsync(tunnel.Id, _lifetime.Token), primary: true));
                actions.Add(ActionButton("编辑", () => EditTunnelAsync(tunnel)));
                if (tunnel.LocalPort is not null)
                    actions.Add(ActionButton("测试本地服务", async () =>
                    {
                        var result = await _workspace.TestEndpointAsync(new(tunnel.LocalAddress ?? "127.0.0.1", tunnel.LocalPort, tunnel.Type), _lifetime.Token);
                        await new FormDialog("本地连通测试", Stack(Text(result.Message)), () => Task.CompletedTask, "关闭") { Height = 280 }.ShowDialog<bool>(this);
                    }));
                if (tunnelIdsWithRuns.Contains(tunnel.Id))
                    actions.Add(ActionButton("运行日志", () => OpenTunnelLogAsync(tunnel.Id)));
                actions.Add(ActionButton("删除", async () =>
                {
                    if (await ConfirmAsync("删除隧道", $"这会停止并删除供应商端的“{tunnel.Name}”，是否继续？"))
                        await _workspace.DeleteTunnelAsync(tunnel.Id, _lifetime.Token);
                }));
                rowContent.Children.Add(Actions(actions.ToArray()));
                var row = new Border { Child = rowContent };
                row.Classes.Add("listRow");
                list.Children.Add(row);
            }
            if (list.Children.Count == 0)
                list.Children.Add(Section(Text(query.Length == 0
                    ? "还没有隧道。先同步供应商账号，或手动创建一条隧道。"
                    : "没有匹配的隧道。请检查名称、账号或映射地址。")));
        }
        filter.TextChanged += (_, _) => { _tunnelFilter = filter.Text ?? string.Empty; UpdateList(); };
        var pageActions = new List<Control>();
        if (accounts.Count > 0)
            pageActions.Add(ActionButton("创建隧道", () => EditTunnelAsync(null), primary: true));
        else
            pageActions.Add(NavigationButton("添加供应商账号", "providers", primary: true));
        if (accounts.Any(account => account.Enabled))
            pageActions.Add(ActionButton("同步供应商", () => _workspace.SyncAllAsync(_lifetime.Token)));
        PageContent.Children.Add(Actions(pageActions.ToArray()));
        PageContent.Children.Add(filter);
        PageContent.Children.Add(list);
        UpdateList();
    }

    private static Border StatusPill(string text, TunnelRuntimeState state)
    {
        var (background, foreground) = state switch
        {
            TunnelRuntimeState.Running => ("#E8F5E9", "#107C10"),
            TunnelRuntimeState.Failed => ("#FDE7E9", "#C42B1C"),
            TunnelRuntimeState.Starting or TunnelRuntimeState.Stopping => ("#FFF4CE", "#8A5A00"),
            _ => ("#F2F2F2", "#5D5D5D")
        };
        var label = new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.Parse(foreground)) };
        label.Classes.Add("statusText");
        var pill = new Border { Child = label, Background = new SolidColorBrush(Color.Parse(background)) };
        pill.Classes.Add("statusPill");
        return pill;
    }

    private static string RuntimeText(TunnelRuntimeState state) => state switch
    {
        TunnelRuntimeState.Running => "运行中", TunnelRuntimeState.Starting => "启动中",
        TunnelRuntimeState.Stopping => "停止中", TunnelRuntimeState.Failed => "失败",
        TunnelRuntimeState.Stopped => "已停止", _ => "状态未知"
    };

    private async Task EditTunnelAsync(TunnelModel? tunnel)
    {
        var accounts = await _workspace.GetAccountsAsync(_lifetime.Token);
        if (accounts.Count == 0) throw new InvalidOperationException("请先添加供应商账号。");
        var accountBox = Choice(accounts, accounts.FirstOrDefault(a => a.Id == tunnel?.ProviderAccountId) ?? accounts[0], a => $"{a.DisplayName} · {a.ProviderType}");
        accountBox.IsEnabled = tunnel is null;
        var name = Input(tunnel?.Name);
        var remark = Input(tunnel?.Remark);
        var selectedAccount = (ProviderAccountModel)accountBox.SelectedItem!;
        var type = Choice(TunnelInputRules.TypesFor(selectedAccount.ProviderType), tunnel?.Type ?? "tcp", t => t.ToUpperInvariant());
        var nodes = Choice(Array.Empty<ProviderNodeModel>(), null, n => $"{n.Name} · {(n.Available ? "可用" : "不可用")} · {n.Id}");
        var nodeHelp = Text("正在加载节点…", hint: true);
        var suggestions = await _workspace.GetLocalAddressesAsync(_lifetime.Token);
        var initialAddress = tunnel?.LocalAddress ?? "127.0.0.1";
        var addressSuggestions = Choice(suggestions.Append(initialAddress).Distinct(StringComparer.OrdinalIgnoreCase), initialAddress, s => s);
        addressSuggestions.IsEditable = true;
        addressSuggestions.Text = initialAddress;
        addressSuggestions.PlaceholderText = "输入或选择本机地址";
        var localPort = Port(tunnel?.LocalPort);
        var savedTunnels = await _workspace.GetTunnelsAsync(_lifetime.Token);
        var existingRemotePort = tunnel is not null
            && int.TryParse(tunnel.RemoteAddress, out var parsedPort)
            && parsedPort is >= 1 and <= 65535
                ? parsedPort
                : (int?)null;
        var remotePort = Port(existingRemotePort);
        FormDialog? dialog = null;
        var randomRemotePort = new Button { Content = "随机端口", Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        randomRemotePort.IsEnabled = !selectedAccount.Capabilities.HasNodes;
        randomRemotePort.Click += (_, _) =>
        {
            try
            {
                var account = (ProviderAccountModel)accountBox.SelectedItem!;
                var selectedNode = nodes.SelectedItem as ProviderNodeModel;
                var nodeId = selectedNode?.Id ?? tunnel?.NodeId;
                var usedRemotePorts = savedTunnels
                    .Where(item => item.Id != tunnel?.Id
                        && item.ProviderAccountId == account.Id
                        && (nodeId is null || item.NodeId == nodeId))
                    .Select(item => int.TryParse(item.RemoteAddress, out var port) ? port : 0)
                    .Where(port => port is >= 1 and <= 65535)
                    .ToHashSet();
                remotePort.Value = RandomRemotePort(usedRemotePorts, selectedNode?.PortRange);
            }
            catch (Exception ex) { dialog?.ShowError(ex.Message); }
        };
        var remotePortInput = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        remotePortInput.Children.Add(remotePort);
        Grid.SetColumn(randomRemotePort, 1);
        randomRemotePort.Margin = new Avalonia.Thickness(8, 0, 0, 0);
        remotePortInput.Children.Add(randomRemotePort);
        var domains = Input(tunnel is not null && TunnelInputRules.IsDomainType(tunnel.Type) ? tunnel.RemoteAddress : null, "多个域名用逗号分隔");
        var advanced = Input("{}", "JSON 对象，留空使用默认选项");
        advanced.AcceptsReturn = true;
        advanced.MinHeight = 85;
        var localField = FormDialog.Field("本地地址", addressSuggestions);
        var localPortField = FormDialog.Field("本地端口", localPort);
        var remotePortField = FormDialog.Field("远端端口（FRP 服务端）", Stack(remotePortInput,
            Text("留空时由服务商分配；随机按钮优先使用当前节点公布的端口范围。", hint: true)));
        var domainsField = FormDialog.Field("自定义域名", domains);
        var nodeField = FormDialog.Field("节点", Stack(nodes, nodeHelp));
        var testButton = new Button { Content = "测试本地服务", Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);

        void UpdateTypeFields()
        {
            var account = (ProviderAccountModel)accountBox.SelectedItem!;
            var currentType = type.SelectedItem as string ?? "tcp";
            var local = TunnelInputRules.RequiresLocalEndpoint(account.ProviderType, currentType);
            localField.IsVisible = local;
            localPortField.IsVisible = local;
            testButton.IsVisible = local;
            remotePortField.IsVisible = account.Capabilities.SupportsRemotePort && TunnelInputRules.UsesRemotePort(account.ProviderType, currentType);
            domainsField.IsVisible = account.Capabilities.SupportsDomains && TunnelInputRules.IsDomainType(currentType);
            nodeField.IsVisible = account.Capabilities.HasNodes;
        }
        var nodesGeneration = 0;
        async Task LoadNodesAsync()
        {
            var generation = ++nodesGeneration;
            var account = (ProviderAccountModel)accountBox.SelectedItem!;
            nodes.IsEnabled = false;
            try
            {
                var result = await _workspace.GetNodesAsync(account.Id, operation.Token);
                if (generation != nodesGeneration || operation.IsCancellationRequested) return;
                nodes.ItemsSource = result.OrderByDescending(n => n.Available).ThenBy(n => n.Name).ToArray();
                nodes.SelectedItem = result.FirstOrDefault(n => n.Id == tunnel?.NodeId);
                randomRemotePort.IsEnabled = !account.Capabilities.HasNodes || nodes.SelectedItem is ProviderNodeModel;
                nodeHelp.Text = result.Count == 0 ? "供应商没有返回节点。" : $"{result.Count} 个节点，创建前请选择节点。";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { nodeHelp.Text = $"节点加载失败：{ex.Message}"; }
            finally { if (generation == nodesGeneration) nodes.IsEnabled = tunnel is null; }
        }
        accountBox.SelectionChanged += async (_, _) =>
        {
            var account = (ProviderAccountModel)accountBox.SelectedItem!;
            type.ItemsSource = TunnelInputRules.TypesFor(account.ProviderType);
            type.SelectedItem = "tcp";
            randomRemotePort.IsEnabled = !account.Capabilities.HasNodes;
            UpdateTypeFields();
            await LoadNodesAsync();
        };
        nodes.SelectionChanged += (_, _) =>
        {
            var account = (ProviderAccountModel)accountBox.SelectedItem!;
            randomRemotePort.IsEnabled = !account.Capabilities.HasNodes || nodes.SelectedItem is ProviderNodeModel;
        };
        type.SelectionChanged += (_, _) => UpdateTypeFields();
        testButton.Click += async (_, _) =>
        {
            testButton.IsEnabled = false;
            try
            {
                var result = await _workspace.TestEndpointAsync(new(addressSuggestions.Text ?? "", Number(localPort), type.SelectedItem as string ?? "tcp"), operation.Token);
                dialog?.ShowError(result.Message);
            }
            catch (Exception ex) { dialog?.ShowError(ex.Message); }
            finally { testButton.IsEnabled = true; }
        };
        var body = Stack(FormDialog.Field("供应商账号", accountBox), FormDialog.Field("隧道名称", name),
            FormDialog.Field("备注", remark), FormDialog.Field("类型", type), nodeField, localField, localPortField,
            remotePortField, domainsField, testButton, FormDialog.Field("高级选项（JSON，可选）", advanced));
        dialog = new FormDialog(tunnel is null ? "创建隧道" : "编辑隧道", body, async () =>
        {
            var account = (ProviderAccountModel)accountBox.SelectedItem!;
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("请填写隧道名称。");
            if (tunnel is null && account.Capabilities.HasNodes && nodes.SelectedItem is not ProviderNodeModel) throw new InvalidOperationException("请选择节点。");
            var currentType = type.SelectedItem as string ?? "tcp";
            await _workspace.SaveTunnelAsync(new()
            {
                TunnelId = tunnel?.Id, RemoteId = tunnel?.RemoteId, ProviderAccountId = account.Id,
                Name = name.Text.Trim(), Remark = remark.Text, Type = currentType,
                NodeId = (nodes.SelectedItem as ProviderNodeModel)?.Id,
                LocalAddress = TunnelInputRules.RequiresLocalEndpoint(account.ProviderType, currentType) ? addressSuggestions.Text?.Trim() ?? "" : "",
                LocalPort = localPortField.IsVisible ? Number(localPort) : null,
                RemotePort = remotePortField.IsVisible ? Number(remotePort) : null,
                CustomDomain = domainsField.IsVisible ? domains.Text : null,
                AdvancedOptions = string.IsNullOrWhiteSpace(advanced.Text) ? [] : JsonSerializer.Deserialize<Dictionary<string, string?>>(advanced.Text) ?? []
            }, operation.Token);
        });
        dialog.Closed += (_, _) => operation.Cancel();
        UpdateTypeFields();
        dialog.Opened += async (_, _) => await LoadNodesAsync();
        await dialog.ShowDialog<bool>(this);
    }

    internal static int RandomRemotePort(HashSet<int> usedPorts, string? portRange = null)
    {
        var ranges = ParsePortRanges(portRange);
        if (ranges.Count == 0) ranges = [(20_000, 60_000)];

        for (var attempt = 0; attempt < 1024; attempt++)
        {
            var range = ranges[RandomNumberGenerator.GetInt32(0, ranges.Count)];
            var candidate = RandomNumberGenerator.GetInt32(range.Minimum, range.Maximum + 1);
            if (!usedPorts.Contains(candidate)) return candidate;
        }
        throw new InvalidOperationException("无法在当前节点的端口范围内找到未使用的远端端口，请手动输入端口号。");
    }

    private static List<(int Minimum, int Maximum)> ParsePortRanges(string? value)
    {
        var ranges = new List<(int Minimum, int Maximum)>();
        if (string.IsNullOrWhiteSpace(value)) return ranges;

        foreach (var token in value.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bounds = token.Split('-', StringSplitOptions.TrimEntries);
            if (bounds.Length is < 1 or > 2 || !int.TryParse(bounds[0], out var minimum))
                continue;

            var maximum = bounds.Length == 1 ? minimum
                : int.TryParse(bounds[1], out var parsedMaximum) ? parsedMaximum : 0;
            if (minimum is >= 1 and <= 65535 && maximum is >= 1 and <= 65535 && minimum <= maximum)
                ranges.Add((minimum, maximum));
        }

        return ranges;
    }

    private async Task OpenTunnelLogAsync(Guid tunnelId)
    {
        var run = (await _workspace.GetRunsAsync(tunnelId, _lifetime.Token)).OrderByDescending(r => r.StartedAt).FirstOrDefault()
            ?? throw new InvalidOperationException("这条隧道尚未产生运行日志。");
        ShowLogWindow(run);
    }
}
