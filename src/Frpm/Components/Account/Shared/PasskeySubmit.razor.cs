namespace Frpm.Components.Account.Shared;

public partial class PasskeySubmit
{
    [Inject]
    private IServiceProvider Services { get; set; } = default!;

    private AntiforgeryTokenSet? tokens;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public PasskeyOperation Operation { get; set; }

    [Parameter]
    [EditorRequired]
    public string Name { get; set; } = default!;

    [Parameter]
    public string? EmailName { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    protected override void OnInitialized()
    {
        tokens = Services.GetService<IAntiforgery>()?.GetTokens(HttpContext);
    }
}
