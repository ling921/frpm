namespace Frpm.Client.Components;

public partial class EnvironmentInfoItem : ComponentBase
{
    [Parameter, EditorRequired] public string Label { get; set; } = string.Empty;

    [Parameter, EditorRequired] public string Value { get; set; } = string.Empty;

    [Parameter] public bool Wide { get; set; }
}
