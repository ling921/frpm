using Frpm.Abstractions.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frpm.Client.Components;

public partial class NodeSelectorDialog : ComponentBase
{
    [Parameter] public IReadOnlyList<ProviderNodeModel> Nodes { get; set; } = [];
    [Parameter] public string? SelectedNodeId { get; set; }
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;

    private string _searchQuery = string.Empty;
    private int _activeTab;
    private bool _onlyOnline;
    private bool _onlyLowLoad;

    private string CurrentGroupId => _activeTab >= 0 && _activeTab < NodeRegionClassifier.AllGroups.Count
        ? NodeRegionClassifier.AllGroups[_activeTab].GroupId
        : NodeRegionClassifier.GroupAll;

    private string? GetSelectedNodeName() =>
        Nodes.FirstOrDefault(n => string.Equals(n.Id, SelectedNodeId, StringComparison.OrdinalIgnoreCase))?.Name;

    private int GetGroupCount(string groupId)
    {
        if (groupId == NodeRegionClassifier.GroupAll) return Nodes.Count;
        return Nodes.Count(n => string.Equals(n.RegionGroup, groupId, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<ProviderNodeModel> FilteredNodes
    {
        get
        {
            var query = Nodes.AsEnumerable();

            if (CurrentGroupId != NodeRegionClassifier.GroupAll)
            {
                query = query.Where(n => string.Equals(n.RegionGroup, CurrentGroupId, StringComparison.OrdinalIgnoreCase));
            }

            if (_onlyOnline)
            {
                query = query.Where(n => n.IsOnline);
            }

            if (_onlyLowLoad)
            {
                query = query.Where(n => n.LoadPercent is null || n.LoadPercent <= 50);
            }

            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                var q = _searchQuery.Trim();
                query = query.Where(n =>
                    (n.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (n.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (n.Address?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (n.Region?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (n.AllowedTypes?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            return query.OrderByDescending(n => n.IsOnline)
                .ThenBy(n => n.LoadPercent ?? 50)
                .ThenBy(n => n.Name)
                .ToList();
        }
    }

    private static Color GetLoadColor(int load) => load switch
    {
        < 50 => Color.Success,
        < 80 => Color.Warning,
        _ => Color.Error
    };

    private void SelectNode(ProviderNodeModel node)
    {
        MudDialog.Close(DialogResult.Ok(node));
    }

    private void Cancel()
    {
        MudDialog.Cancel();
    }
}
