namespace Frpm.Client.Pages;

public partial class CliPackages : ComponentBase
{
    private const long MaxPackageBytes = 268_435_456;

    [Inject]
    private ICliPackageService PackageService { get; set; } = default!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = default!;

    [Inject]
    private IDialogService DialogService { get; set; } = default!;

    [Inject]
    private ISystemSettingsService SystemSettingsService { get; set; } = default!;

    private List<CliPackageModel> _packages = [];
    private InstallCliFromUrlRequest _urlRequest = new() { ProviderType = ProviderType.SakuraFrp };
    private ProviderType _uploadProvider = ProviderType.SakuraFrp;
    private SystemSettingsModel? _settings;
    private string _uploadVersion = string.Empty, _uploadSha256 = string.Empty;
    private IBrowserFile? _file;
    private bool _loading = true, _busy, _uploading;
    private string SelectedFileDescription => _file is null
        ? "尚未选择文件"
        : $"{_file.Name}（{FormatFileSize(_file.Size)}）";
    private ProviderCliDownloadGuide DownloadGuide => BuiltInCliDownloadCatalog.Get(_uploadProvider);
    private CliDownloadRecommendation? DownloadRecommendation => _settings is null
        ? null
        : BuiltInCliDownloadCatalog.Recommend(
            _uploadProvider,
            _settings.OperatingSystem,
            _settings.Architecture);
    private string EnvironmentDisplayText => _settings is null
        ? "暂时无法读取"
        : $"{_settings.OperatingSystem} / {_settings.Architecture}";

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _packages = (await PackageService.GetListAsync()).ToList();
        }
        catch (Exception ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }

        try
        {
            _settings = await SystemSettingsService.GetAsync();
        }
        catch
        {
            _settings = null;
        }
        finally { _loading = false; }
    }

    private async Task InstallUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(_urlRequest.Version) || string.IsNullOrWhiteSpace(_urlRequest.Url))
        {
            Snackbar.Add("请填写版本号和下载地址。", Severity.Warning);
            return;
        }

        var action = await ConfirmInstallAsync(_urlRequest.ProviderType, _urlRequest.Version);
        if (!action.Proceed) return;

        try
        {
            _busy = true;
            _urlRequest.ReplaceExisting = action.ReplaceExisting;
            _urlRequest.ActivateAfterInstall = action.ActivateAfterInstall;
            var installed = await PackageService.InstallFromUrlAsync(_urlRequest);
            Snackbar.Add(InstallSuccessMessage(action, installed.IsActive), Severity.Success);
            await LoadAsync();
        }
        catch (OperationCanceledException)
        {
            Snackbar.Add("CLI 下载已取消或等待超时，请检查网络后重试。", Severity.Warning);
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

    private void SelectFile(IBrowserFile? file)
    {
        if (file is not null && file.Size > MaxPackageBytes)
        {
            _file = null;
            Snackbar.Add($"CLI 包不能超过 {FormatFileSize(MaxPackageBytes)}。", Severity.Warning);
            return;
        }

        _file = file;
    }

    private async Task UploadAsync()
    {
        if (_file is null || string.IsNullOrWhiteSpace(_uploadVersion))
        {
            Snackbar.Add("请选择文件并填写版本号。", Severity.Warning);
            return;
        }

        var action = await ConfirmInstallAsync(_uploadProvider, _uploadVersion);
        if (!action.Proceed) return;

        try
        {
            _busy = true;
            _uploading = true;
            await using var stream = _file.OpenReadStream(MaxPackageBytes);
            var upload = new RemoteUploadFile(stream, _file.Name, _file.ContentType, _file.Size, true);
            var installed = await PackageService.UploadAsync(
                _uploadProvider, _uploadVersion, upload, _uploadSha256,
                action.ReplaceExisting, action.ActivateAfterInstall);
            Snackbar.Add(InstallSuccessMessage(action, installed.IsActive), Severity.Success);
            _file = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add(FriendlyUploadError(ex), Severity.Error);
        }
        finally
        {
            _uploading = false;
            _busy = false;
        }
    }

    private static string FriendlyUploadError(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return message.Contains("413", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Request Entity Too Large", StringComparison.OrdinalIgnoreCase)
               || message.Contains("request body too large", StringComparison.OrdinalIgnoreCase)
            ? $"CLI 包超过服务器允许的上传大小（最大 {FormatFileSize(MaxPackageBytes)}）。"
            : message;
    }

    private static string FormatFileSize(long bytes)
    {
        const long megabyte = 1_048_576;
        return bytes >= megabyte
            ? $"{bytes / (double)megabyte:0.##} MB"
            : $"{bytes / 1024d:0.##} KB";
    }

    private async Task ActivateAsync(CliPackageModel package)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "激活 CLI",
            $"激活 {package.Version}，并重启该供应商正在运行的隧道？",
            yesText: "激活并重启",
            cancelText: "取消");
        if (confirmed is not true)
        {
            return;
        }

        try
        {
            await PackageService.ActivateAsync(package.Id, true);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task DeleteAsync(CliPackageModel package)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "删除 CLI",
            $"删除 CLI {package.Version}？运行历史会保留，之后仍可重新安装该版本。",
            yesText: "删除",
            cancelText: "取消");
        if (confirmed is not true)
        {
            return;
        }

        try
        {
            _busy = true;
            await PackageService.DeleteAsync(package.Id);
            Snackbar.Add("CLI 已删除。", Severity.Success);
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

    private async Task<InstallAction> ConfirmInstallAsync(ProviderType providerType, string version)
    {
        var normalizedVersion = version.Trim();
        var existing = _packages.FirstOrDefault(package =>
            package.ProviderType == providerType
            && string.Equals(package.Version, normalizedVersion, StringComparison.OrdinalIgnoreCase));
        var active = _packages.FirstOrDefault(package => package.ProviderType == providerType && package.IsActive);
        var downgrade = active is not null && CompareVersions(normalizedVersion, active.Version) < 0;
        if (existing is null && !downgrade)
            return new InstallAction(true, false, false, false);

        var providerName = BuiltInProviderCatalog.Get(providerType).DisplayName;
        var message = existing is not null && downgrade
            ? $"{providerName} {normalizedVersion} 已安装。将替换该版本，并从活动版本 {active!.Version} 降级后重启正在运行的隧道。"
            : existing is not null
                ? $"{providerName} {normalizedVersion} 已安装，将用新文件替换。同版本为活动版本时会重启正在运行的隧道。"
                : $"将从 {providerName} 活动版本 {active!.Version} 降级到 {normalizedVersion}，并重启正在运行的隧道。";
        var confirmed = await DialogService.ShowMessageBoxAsync(
            existing is not null ? "确认替换 CLI" : "确认降级 CLI",
            message,
            yesText: existing is not null ? "确认替换" : "确认降级",
            cancelText: "取消");
        return new InstallAction(confirmed is true, existing is not null, downgrade, downgrade);
    }

    private static string InstallSuccessMessage(InstallAction action, bool isActive) => action.IsDowngrade
        ? "CLI 已降级并重启相关隧道。"
        : action.ReplaceExisting
            ? isActive
                ? "CLI 已替换并设为活动版本；相关隧道已按需重启。"
                : "CLI 已替换，激活后生效。"
            : isActive
                ? "CLI 已安装并自动激活。"
                : "CLI 已安装，激活后生效。";

    private static int CompareVersions(string left, string right)
    {
        var leftParts = ParseVersion(left);
        var rightParts = ParseVersion(right);
        if (leftParts is null || rightParts is null) return 0;
        for (var index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
        {
            var leftPart = index < leftParts.Length ? leftParts[index] : 0;
            var rightPart = index < rightParts.Length ? rightParts[index] : 0;
            if (leftPart != rightPart) return leftPart.CompareTo(rightPart);
        }
        return 0;
    }

    private static int[]? ParseVersion(string value)
    {
        var text = value.Trim().TrimStart('v', 'V');
        var numeric = new string(text.TakeWhile(character => char.IsDigit(character) || character == '.').ToArray()).Trim('.');
        if (numeric.Length == 0) return null;
        var segments = numeric.Split('.');
        var result = new int[segments.Length];
        for (var index = 0; index < segments.Length; index++)
        {
            if (!int.TryParse(segments[index], out result[index])) return null;
        }
        return result;
    }

    private sealed record InstallAction(bool Proceed, bool ReplaceExisting, bool ActivateAfterInstall, bool IsDowngrade);
}
