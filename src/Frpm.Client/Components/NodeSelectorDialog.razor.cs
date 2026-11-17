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
    private string _selectedProtocol = string.Empty;
    private string _selectedRegion = NodeRegionClassifier.GroupAll;
    private bool _onlyOnline;
    private bool _onlyLowLoad;

    public sealed record NodeGroupView(
        string GroupId,
        string DisplayName,
        IReadOnlyList<ProviderNodeModel> Nodes);

    private IReadOnlyList<string> AvailableProtocols
    {
        get
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in Nodes)
            {
                if (!string.IsNullOrWhiteSpace(node.AllowedTypes))
                {
                    var parts = node.AllowedTypes.Split([',', '/', ' ', '|', ';'], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        var trimmed = part.Trim().ToLowerInvariant();
                        if (!string.IsNullOrWhiteSpace(trimmed))
                        {
                            set.Add(trimmed);
                        }
                    }
                }
            }

            string[] preferredOrder = ["tcp", "udp", "http", "https", "xtcp", "stcp"];
            var ordered = preferredOrder.Where(p => set.Contains(p)).ToList();
            foreach (var remaining in set.Where(p => !preferredOrder.Contains(p)).OrderBy(p => p))
            {
                ordered.Add(remaining);
            }
            if (ordered.Count == 0)
            {
                return ["tcp", "udp", "http", "https"];
            }
            return ordered;
        }
    }

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

            if (_selectedRegion != NodeRegionClassifier.GroupAll && !string.IsNullOrWhiteSpace(_selectedRegion))
            {
                query = query.Where(n => string.Equals(n.RegionGroup, _selectedRegion, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(_selectedProtocol))
            {
                var targetProto = _selectedProtocol.Trim();
                query = query.Where(n =>
                    string.IsNullOrWhiteSpace(n.AllowedTypes) ||
                    n.AllowedTypes.Split([',', '/', ' ', '|', ';'], StringSplitOptions.RemoveEmptyEntries)
                        .Any(t => string.Equals(t.Trim(), targetProto, StringComparison.OrdinalIgnoreCase)));
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

            return query.OrderBy(n => NodeRegionClassifier.GetDisplayOrder(n.RegionGroup))
                .ThenByDescending(n => n.IsOnline)
                .ThenBy(n => n.LoadPercent ?? 50)
                .ThenBy(n => n.Name)
                .ToList();
        }
    }

    private IReadOnlyList<NodeGroupView> GroupedNodes
    {
        get
        {
            var filtered = FilteredNodes;
            var result = new List<NodeGroupView>();

            foreach (var (groupId, displayName) in NodeRegionClassifier.DisplayGroups)
            {
                if (_selectedRegion != NodeRegionClassifier.GroupAll &&
                    !string.Equals(_selectedRegion, groupId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var groupNodes = filtered
                    .Where(n => string.Equals(n.RegionGroup, groupId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (groupNodes.Count > 0)
                {
                    result.Add(new NodeGroupView(groupId, displayName, groupNodes));
                }
            }

            return result;
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
