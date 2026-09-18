namespace Frpm.Client.Components;

public partial class TunnelActionButtons : ComponentBase
{
    [Parameter, EditorRequired] public TunnelModel Tunnel { get; set; } = default!;

    [Parameter, EditorRequired] public EventCallback<TunnelModel> Start { get; set; }

    [Parameter, EditorRequired] public EventCallback<TunnelModel> Stop { get; set; }

    [Parameter, EditorRequired] public EventCallback<TunnelModel> Edit { get; set; }

    [Parameter, EditorRequired] public EventCallback<TunnelModel> OpenLog { get; set; }

    [Parameter, EditorRequired] public EventCallback<TunnelModel> Delete { get; set; }

    private bool IsRunning => Tunnel.RuntimeState == TunnelRuntimeState.Running;

    private Task ToggleAsync() => IsRunning
        ? Stop.InvokeAsync(Tunnel)
        : Start.InvokeAsync(Tunnel);
}
