namespace Frpm.Client.Pages;

public partial class Providers : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IProviderAccountService AccountService { get; set; } = default!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = default!;

    [Inject]
    private IDialogService DialogService { get; set; } = default!;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    private IProviderOAuthService OAuthService { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private List<ProviderAccountModel> _accounts = [];
    private SaveProviderAccountRequest _request = NewRequest();
    private bool _loading = true, _busy, _editorOpen;
    private DotNetObjectReference<Providers>? _oauthCallbackReference;

    [SupplyParameterFromQuery(Name = "oauthSuccess")]
    public bool OAuthSuccess { get; set; }

    [SupplyParameterFromQuery(Name = "oauthError")]
    public string? OAuthError { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (OAuthSuccess)
        {
            Snackbar.Add("LoliaFrp OAuth 授权成功。", Severity.Success);
        }

        if (!string.IsNullOrWhiteSpace(OAuthError))
        {
            Snackbar.Add($"OAuth 失败：{OAuthError}", Severity.Error);
        }

        await LoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _oauthCallbackReference = DotNetObjectReference.Create(this);
        await JSRuntime.InvokeVoidAsync("frpmOAuth.initialize", _oauthCallbackReference);
    }

    private async Task LoadAsync()
    {
        try
        {
            _loading = true;
            _accounts = (await AccountService.GetListAsync()).ToList();
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

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_request.DisplayName)
            || (_request.Id is null && string.IsNullOrWhiteSpace(_request.AccessToken)))
        {
            Snackbar.Add("请填写名称和访问令牌。", Severity.Warning);
            return;
        }

        try
        {
            _busy = true;
            await AccountService.SaveAsync(_request);
            Snackbar.Add("账号已保存并同步。", Severity.Success);
            ResetEditor();
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

    private async Task SyncAsync(ProviderAccountModel account)
    {
        try
        {
            await AccountService.SyncAsync(account.Id);
            Snackbar.Add("同步完成。", Severity.Success);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task DeleteAsync(ProviderAccountModel account)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "删除供应商账号",
            $"将删除账号“{account.DisplayName}”及其本地历史记录。此操作无法撤销。",
            yesText: "删除",
            cancelText: "取消");
        if (confirmed is not true)
        {
            return;
        }

        try
        {
            await AccountService.DeleteAsync(account.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task StartOAuthAsync()
    {
        if (string.IsNullOrWhiteSpace(_request.DisplayName) || string.IsNullOrWhiteSpace(_request.ClientId))
        {
            Snackbar.Add("请先填写账号名称和 OAuth Client ID。", Severity.Warning);
            return;
        }

        try
        {
            _busy = true;
            if (!await JSRuntime.InvokeAsync<bool>("frpmOAuth.openPending"))
            {
                Snackbar.Add("浏览器阻止了授权弹窗，请允许本站打开弹窗后重试。", Severity.Warning);
                _busy = false;
                return;
            }

            var result = await OAuthService.StartLoliaAsync(new()
            {
                AccountId = _request.Id,
                DisplayName = _request.DisplayName,
                ApiBaseUrl = string.IsNullOrWhiteSpace(_request.ApiBaseUrl)
                    ? BuiltInProviderCatalog.Get(ProviderType.LoliaFrp).DefaultApiBaseUrl
                    : _request.ApiBaseUrl,
                ClientId = _request.ClientId,
                ClientSecret = _request.ClientSecret,
                CallbackUrl = new Uri(new Uri(Navigation.BaseUri), "oauth/lolia/callback").ToString(),
                Enabled = _request.Enabled
            });
            await JSRuntime.InvokeVoidAsync("frpmOAuth.navigate", result.AuthorizationUrl);
        }
        catch (Exception ex)
        {
            await JSRuntime.InvokeVoidAsync("frpmOAuth.cancel");
            Snackbar.Add(ex.Message, Severity.Error);
            _busy = false;
        }
    }

    [JSInvokable]
    public async Task OnLoliaOAuthCompleted(bool success, string message)
    {
        _busy = false;
        Snackbar.Add(message, success ? Severity.Success : Severity.Error);
        if (success)
        {
            ResetEditor();
            await LoadAsync();
        }
        await InvokeAsync(StateHasChanged);
    }

    private void Edit(ProviderAccountModel account)
    {
        _request = new()
        {
            Id = account.Id,
            ProviderType = account.ProviderType,
            DisplayName = account.DisplayName,
            ApiBaseUrl = account.ApiBaseUrl,
            Enabled = account.Enabled
        };
        _editorOpen = true;
    }

    private void ResetEditor()
    {
        _request = NewRequest();
        _editorOpen = false;
    }

    private static SaveProviderAccountRequest NewRequest() => new() { ProviderType = ProviderType.SakuraFrp, Enabled = true };

    public async ValueTask DisposeAsync()
    {
        if (_oauthCallbackReference is not null)
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("frpmOAuth.dispose");
            }
            catch (JSDisconnectedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
        _oauthCallbackReference?.Dispose();
    }
}
