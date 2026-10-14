using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Frpm.Abstractions.Models;
using Frpm.Application;
using Frpm.Domain.Enums;

namespace Frpm.Desktop;

internal sealed partial class MainWindow
{
    private async Task RenderPackagesAsync()
    {
        var packages = await _workspace.GetPackagesAsync(_lifetime.Token);
        var environment = await _workspace.GetSettingsAsync(_lifetime.Token);
        BeginPage("CLI 版本", $"管理本机 frpc · {environment.OperatingSystem} / {environment.Architecture}");
        PageContent.Children.Add(Actions(ActionButton("从本地文件安装", () => InstallPackageAsync(fromUrl: false), primary: true),
            ActionButton("从链接安装", () => InstallPackageAsync(fromUrl: true))));
        PageContent.Children.Add(Text("切换活动版本会重启该供应商正在运行的隧道。", hint: true));

        var download = Stack();
        foreach (var provider in BuiltInProviderCatalog.All)
        {
            var guide = BuiltInCliDownloadCatalog.Get(provider.Type);
            var recommendation = BuiltInCliDownloadCatalog.Recommend(provider.Type, environment.OperatingSystem, environment.Architecture);
            var guideRow = Stack(Text(provider.DisplayName, size: 16),
                Text(recommendation.RecommendedDisplayText ?? recommendation.Message ?? "暂无可用的下载建议", hint: true),
                Text(recommendation.ExpectedFileName ?? string.Join(" ", guide.Steps), hint: true),
                Actions(ActionButton($"打开 {provider.DisplayName} 下载页", () => { DesktopShell.OpenUrl(guide.OfficialDownloadUrl); return Task.CompletedTask; })));
            var row = new Border { Child = guideRow };
            row.Classes.Add("listRow");
            download.Children.Add(row);
        }
        PageContent.Children.Add(new Expander
        {
            Header = "官方下载渠道与版本建议",
            Content = download,
            IsExpanded = packages.Count == 0
        });

        PageContent.Children.Add(Text($"已安装版本 · {packages.Count}", size: 19));
        if (packages.Count == 0)
            PageContent.Children.Add(Section(Text("尚未安装 CLI。选择上方的本地文件或下载链接开始安装；安装向导也会列出各供应商的官方下载地址。")));
        foreach (var package in packages.OrderByDescending(item => item.IsActive).ThenByDescending(item => item.InstalledAt))
        {
            var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            heading.Children.Add(Text($"{package.ProviderType} · {package.Version}", size: 17));
            var stateLabel = new TextBlock { Text = package.IsActive ? "正在使用" : "已安装" };
            stateLabel.Classes.Add("statusText");
            stateLabel.Foreground = new SolidColorBrush(Color.Parse(package.IsActive ? "#107C10" : "#5D5D5D"));
            var statePill = new Border { Child = stateLabel, Background = new SolidColorBrush(Color.Parse(package.IsActive ? "#E8F5E9" : "#F2F2F2")) };
            statePill.Classes.Add("statusPill");
            Grid.SetColumn(statePill, 1);
            heading.Children.Add(statePill);

            var details = Stack(heading,
                Text($"{package.OperatingSystem} / {package.Architecture} · 安装于 {Time(package.InstalledAt)}", hint: true));
            var info = new Expander { Header = "安装信息", Content = Text($"SHA-256：{package.Sha256}\n来源：{package.SourceUrl ?? "本地文件"}", hint: true) };
            details.Children.Add(info);
            var actions = new List<Control>();
            if (package.IsActive)
                actions.Add(Text("当前活动版本不能删除。先激活其他版本后即可移除。", hint: true));
            else
            {
                if (package.OperatingSystem == environment.OperatingSystem && package.Architecture == environment.Architecture)
                    actions.Add(ActionButton("设为活动版本", async () =>
                    {
                        if (await ConfirmAsync("切换 CLI 版本", $"使用 {package.ProviderType} {package.Version} 并重启该供应商正在运行的隧道？"))
                            await _workspace.ActivateCliAsync(package.Id, _lifetime.Token);
                    }, primary: true));
                else
                    actions.Add(Text("此版本不适用于当前操作系统或处理器架构。", hint: true));
                actions.Add(ActionButton("删除版本", async () =>
                {
                    if (await ConfirmAsync("删除 CLI 版本", $"删除 {package.ProviderType} {package.Version}？既有运行历史会保留。"))
                        await _workspace.DeleteCliAsync(package.Id, _lifetime.Token);
                }, danger: true));
            }
            details.Children.Add(Actions(actions.ToArray()));
            var row = new Border { Child = details };
            row.Classes.Add("listRow");
            PageContent.Children.Add(row);
        }
    }

    private async Task InstallPackageAsync(bool fromUrl)
    {
        IStorageFile? selectedFile = null;
        var provider = Choice(BuiltInProviderCatalog.All, BuiltInProviderCatalog.All[0], p => p.DisplayName);
        var version = Input(watermark: "例如 0.64.0，按供应商实际版本填写");
        var url = Input(watermark: "安装包的最终 HTTP/HTTPS 文件直链");
        var sha = Input(watermark: "可选；填写供应商公布的 SHA-256");
        var activate = new CheckBox { Content = "安装后立即激活并重启相关隧道", IsChecked = false };
        var body = Stack(FormDialog.Field("供应商", provider), FormDialog.Field("CLI 版本", version));
        if (fromUrl) body.Children.Add(FormDialog.Field("下载地址", url));
        var fileName = Text("尚未选择文件", hint: true);
        body.Children.Add(FormDialog.Field("SHA-256 校验值", sha));
        body.Children.Add(activate);
        body.Children.Add(Text("同版本替换或降级需要确认；最大文件大小 256 MB。", hint: true));
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        FormDialog? dialog = null;
        if (!fromUrl)
        {
            var browse = new Button { Content = "选择文件…" };
            browse.Click += async (_, _) =>
            {
                try
                {
                    var files = await dialog!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = "选择 frpc 程序或压缩包", AllowMultiple = false,
                        FileTypeFilter = [new FilePickerFileType("frpc 程序或压缩包") { Patterns = ["*.exe", "*.zip", "*.tar.gz", "*.tgz", "*"] }]
                    });
                    if (files.FirstOrDefault() is { } file)
                    {
                        selectedFile = file;
                        fileName.Text = file.Name;
                        browse.Content = "重新选择…";
                    }
                }
                catch (Exception ex) { dialog!.ShowError(ex.Message); }
            };
            body.Children.Insert(2, FormDialog.Field("本地文件", Stack(fileName, browse)));
        }
        dialog = new FormDialog(fromUrl ? "从链接安装 CLI" : "从文件安装 CLI", body, async () =>
        {
            if (!fromUrl && selectedFile is null) throw new InvalidOperationException("请选择 frpc 程序或压缩包。");
            if (string.IsNullOrWhiteSpace(version.Text)) throw new InvalidOperationException("请填写 CLI 版本。");
            var providerType = ((BuiltInProviderDefinition)provider.SelectedItem!).Type;
            var plan = CliInstallationPolicy.Plan(await _workspace.GetPackagesAsync(operation.Token), providerType, version.Text,
                await _workspace.GetSettingsAsync(operation.Token), activate.IsChecked == true);
            if (plan.Confirmation is not null && !await ConfirmAsync("确认安装 CLI", plan.Confirmation, dialog))
                throw new OperationCanceledException();
            if (fromUrl)
            {
                await _workspace.InstallCliAsync(new()
                {
                    ProviderType = providerType, Version = version.Text.Trim(), Url = url.Text ?? "",
                    Sha256 = sha.Text, ReplaceExisting = plan.ReplaceExisting, ActivateAfterInstall = plan.ActivateAfterInstall
                }, operation.Token);
            }
            else
            {
                await using var stream = await selectedFile!.OpenReadAsync();
                var length = stream.CanSeek ? stream.Length : 0;
                if (length > 268_435_456) throw new InvalidOperationException("CLI 包不能超过 256 MB。");
                await _workspace.InstallCliFileAsync(providerType, version.Text.Trim(), selectedFile.Name, stream,
                    length, sha.Text, plan.ReplaceExisting, plan.ActivateAfterInstall, operation.Token);
            }
        }) { Height = 530 };
        dialog.Closed += (_, _) => operation.Cancel();
        await dialog.ShowDialog<bool>(this);
    }
}
