using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Frpm.Abstractions.Models;
using Frpm.Domain.Enums;

namespace Frpm.Desktop;

internal sealed partial class MainWindow
{
    private static ComboBox Choice<T>(IEnumerable<T> items, T? selected, Func<T, string> label)
    {
        var combo = new ComboBox { ItemsSource = items.ToArray(), SelectedItem = selected,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        combo.ItemTemplate = new FuncDataTemplate<T>((value, _) => Text(value is null ? string.Empty : label(value)));
        return combo;
    }

    private static TextBox Input(string? value = null, string? watermark = null, bool secret = false) => new()
    {
        Text = value ?? string.Empty, Watermark = watermark, PasswordChar = secret ? '●' : '\0'
    };

    private static NumericUpDown Port(int? value) => new() { Minimum = 1, Maximum = 65535, Value = value,
        FormatString = "0", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private static int? Number(NumericUpDown value) => value.Value is { } number ? (int)number : null;

    private async Task RenderProvidersAsync()
    {
        var accounts = await _workspace.GetAccountsAsync(_lifetime.Token);
        BeginPage("供应商账号", "访问令牌和 OAuth 凭据加密保存，编辑时留空可复用已保存的凭据");
        var pageActions = new List<Control> { ActionButton("添加账号", () => EditAccountAsync(null), primary: true) };
        if (accounts.Any(account => account.Enabled))
            pageActions.Add(ActionButton("同步全部", () => _workspace.SyncAllAsync(_lifetime.Token)));
        PageContent.Children.Add(Actions(pageActions.ToArray()));
        if (accounts.Count == 0) PageContent.Children.Add(Section(Text("还没有供应商账号。点击“添加账号”开始。")));
        foreach (var account in accounts)
        {
            var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            heading.Children.Add(Text(account.DisplayName, size: 18));
            var state = new TextBlock { Text = account.Enabled ? "已启用" : "已禁用" };
            state.Classes.Add("statusText");
            state.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(account.Enabled ? "#107C10" : "#5D5D5D"));
            var statePill = new Border
            {
                Child = state,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(account.Enabled ? "#E8F5E9" : "#F2F2F2"))
            };
            statePill.Classes.Add("statusPill");
            Grid.SetColumn(statePill, 1);
            heading.Children.Add(statePill);
            var content = Stack(heading,
                Text($"{account.ProviderType} · {account.TunnelCount} 条隧道", hint: true),
                Text($"最近同步：{Time(account.LastSyncAt)} · {account.LastSyncMessage ?? "尚未执行"}", hint: true));
            var actions = new List<Control> { ActionButton("编辑账号", () => EditAccountAsync(account)) };
            if (account.Enabled)
                actions.Add(ActionButton("立即同步", () => _workspace.SyncAccountAsync(account.Id, _lifetime.Token), primary: true));
            else
                actions.Add(Text("账号已禁用，不会自动同步。编辑账号可重新启用。", hint: true));
            if (account.Capabilities.HasNodes)
                actions.Add(ActionButton("查看节点", () => ShowNodesAsync(account)));
            actions.Add(ActionButton("删除账号", async () =>
            {
                if (await ConfirmAsync("删除账号", $"删除“{account.DisplayName}”以及本机的关联隧道记录？"))
                    await _workspace.DeleteAccountAsync(account.Id, _lifetime.Token);
            }, danger: true));
            content.Children.Add(Actions(actions.ToArray()));
            var row = new Border { Child = content };
            row.Classes.Add("listRow");
            PageContent.Children.Add(row);
        }
    }

    private async Task EditAccountAsync(ProviderAccountModel? account)
    {
        var provider = Choice(BuiltInProviderCatalog.All,
            BuiltInProviderCatalog.Get(account?.ProviderType ?? ProviderType.SakuraFrp), p => p.DisplayName);
        provider.IsEnabled = account is null;
        var name = Input(account?.DisplayName);
        var api = Input(account?.ApiBaseUrl, "留空使用供应商默认 API 地址");
        var token = Input(watermark: account is null ? "供应商访问令牌" : "留空保留现有令牌", secret: true);
        var clientId = Input(watermark: account is null ? "LoliaFrp OAuth Client ID" : "留空复用已保存的 Client ID");
        var clientSecret = Input(watermark: account is null ? "OAuth Client Secret" : "留空复用已保存的 Secret", secret: true);
        var enabled = new CheckBox { Content = "启用账号并自动同步", IsChecked = account?.Enabled ?? true };
        var callbackPort = Port(8180);
        var callback = new TextBox { IsReadOnly = true };
        void UpdateCallback() => callback.Text = $"http://127.0.0.1:{Number(callbackPort) ?? 8180}/oauth/lolia/callback";
        callbackPort.ValueChanged += (_, _) => UpdateCallback();
        UpdateCallback();
        var oauthFields = Stack(
            Text("LoliaFrp 也可使用 OAuth 授权。请在供应商的 Client 设置中登记下面的回调地址；监听只在授权期间存在。", hint: true),
            FormDialog.Field("OAuth Client ID", clientId), FormDialog.Field("OAuth Client Secret", clientSecret),
            FormDialog.Field("OAuth 回调端口", callbackPort), FormDialog.Field("OAuth 回调地址", callback));
        var body = Stack(FormDialog.Field("供应商", provider), FormDialog.Field("账号名称", name),
            FormDialog.Field("API 地址（可选）", api), FormDialog.Field("访问令牌", token), enabled, oauthFields);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        SaveProviderAccountRequest Request()
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("请填写账号名称。");
            return new()
            {
                Id = account?.Id, ProviderType = ((BuiltInProviderDefinition)provider.SelectedItem!).Type,
                DisplayName = name.Text.Trim(), ApiBaseUrl = api.Text?.Trim() ?? string.Empty,
                AccessToken = token.Text, ClientId = clientId.Text, ClientSecret = clientSecret.Text,
                Enabled = enabled.IsChecked == true
            };
        }
        var dialog = new FormDialog(account is null ? "添加供应商账号" : "编辑供应商账号", body, async () =>
            await _workspace.SaveAccountAsync(Request(), operation.Token));
        var oauthButton = dialog.AddAction("通过 OAuth 授权", async () =>
        {
            var request = Request();
            if (request.ProviderType != ProviderType.LoliaFrp) throw new InvalidOperationException("目前只有 LoliaFrp 支持 OAuth 授权。");
            if (callbackPort.Value is null) throw new InvalidOperationException("请填写回调端口。");
            await _oauth.AuthorizeLoliaAsync(new()
            {
                AccountId = account?.Id, DisplayName = request.DisplayName, Enabled = request.Enabled,
                ApiBaseUrl = string.IsNullOrWhiteSpace(request.ApiBaseUrl)
                    ? BuiltInProviderCatalog.Get(ProviderType.LoliaFrp).DefaultApiBaseUrl : request.ApiBaseUrl,
                ClientId = request.ClientId ?? string.Empty, ClientSecret = request.ClientSecret,
                CallbackUrl = callback.Text!
            }, url => { DesktopShell.OpenUrl(url); return Task.CompletedTask; }, operation.Token);
        });
        void UpdateProvider()
        {
            var supportsOAuth = ((BuiltInProviderDefinition)provider.SelectedItem!).SupportsOAuth;
            oauthFields.IsVisible = supportsOAuth;
            oauthButton.IsVisible = supportsOAuth;
            dialog.Height = supportsOAuth ? 720 : 470;
        }
        provider.SelectionChanged += (_, _) => UpdateProvider();
        UpdateProvider();
        dialog.Closed += (_, _) => operation.Cancel();
        await dialog.ShowDialog<bool>(this);
    }

    private async Task ShowNodesAsync(ProviderAccountModel account)
    {
        var nodes = await _workspace.GetNodesAsync(account.Id, _lifetime.Token);
        var body = Stack(Text($"{account.DisplayName} · {nodes.Count} 个节点", size: 18));
        foreach (var node in nodes.OrderByDescending(n => n.Available).ThenBy(n => n.Name))
            body.Children.Add(Section(Stack(Text($"{node.Name} · {(node.Available ? "可用" : "不可用")}"),
                Text($"{node.Address} · {node.Region} · {node.Bandwidth}", hint: true),
                Text($"类型：{node.AllowedTypes ?? "供应商默认"} · 端口：{node.PortRange ?? "供应商默认"}", hint: true))));
        await new FormDialog("供应商节点", body, () => Task.CompletedTask, "关闭").ShowDialog<bool>(this);
    }
}
