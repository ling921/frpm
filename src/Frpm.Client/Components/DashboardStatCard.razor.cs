namespace Frpm.Client.Components;

public partial class DashboardStatCard : ComponentBase
{
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;

    [Parameter] public int Value { get; set; }

    [Parameter, EditorRequired] public string Icon { get; set; } = string.Empty;

    [Parameter] public Color Color { get; set; }
}
