using Frpm.Abstractions.Models;
using Frpm.Domain.Enums;
using Microsoft.AspNetCore.Components;

namespace Frpm.Client.Components;

public partial class TunnelActionButtons : ComponentBase
{
    [Parameter, EditorRequired] public TunnelModel Tunnel { get; set; } = default!;

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public bool Loading { get; set; }

    [Parameter, EditorRequired] public EventCallback<TunnelModel> Start { get; set; }

    [Parameter, EditorRequired] public EventCallback<TunnelModel> Stop { get; set; }

    [Parameter] public EventCallback<TunnelModel> Kick { get; set; }

    [Parameter, EditorRequired] public EventCallback<TunnelModel> Edit { get; set; }

    [Parameter, EditorRequired] public EventCallback<TunnelModel> OpenLog { get; set; }

    [Parameter, EditorRequired] public EventCallback<TunnelModel> Delete { get; set; }

    private ProviderCapabilitiesModel Capabilities =>
        BuiltInProviderCatalog.Get(Tunnel.ProviderType).Capabilities;

    private bool IsRunning => Tunnel.RuntimeState == TunnelRuntimeState.Running;

    private string? _startReason;
    private bool CanStartNow => TunnelActionEvaluator.CanStart(Tunnel, out _startReason);

    private string? _stopReason;
    private bool CanStopNow => TunnelActionEvaluator.CanStop(Tunnel, out _stopReason);

    private string? _kickReason;
    private bool IsKickVisible => TunnelActionEvaluator.IsKickVisible(Capabilities) && Kick.HasDelegate;
    private bool CanKickNow => TunnelActionEvaluator.CanKick(Tunnel, Capabilities, out _kickReason);

    private string? _deleteReason;
    private bool CanDeleteNow => TunnelActionEvaluator.CanDelete(Tunnel, out _deleteReason);

    private string? _editReason;
    private bool CanEditNow => TunnelActionEvaluator.CanEdit(Tunnel, out _editReason);

    private Task ToggleAsync() => IsRunning
        ? Stop.InvokeAsync(Tunnel)
        : Start.InvokeAsync(Tunnel);
}
