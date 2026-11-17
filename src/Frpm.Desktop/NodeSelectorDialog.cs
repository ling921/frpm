using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Frpm.Abstractions.Models;

namespace Frpm.Desktop;

internal sealed class NodeSelectorDialog : Window
{
    private readonly IReadOnlyList<ProviderNodeModel> _allNodes;
    private readonly bool _isSelectionMode;
    private readonly TextBox _searchBox;
    private readonly ComboBox _protocolBox;
    private readonly CheckBox _onlyOnlineBox;
    private readonly CheckBox _onlyLowLoadBox;
    private readonly Button _toggleAllButton;
    private readonly TextBlock _matchCountText;
    private readonly TextBlock _selectedSummaryText;
    private readonly Button _confirmButton;
    private readonly Button _closeButton;
    private readonly StackPanel _groupsContainer;
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.OrdinalIgnoreCase);

    public ProviderNodeModel? SelectedNode { get; private set; }

    public NodeSelectorDialog(
        IReadOnlyList<ProviderNodeModel> nodes,
        string? selectedNodeId = null,
        string? defaultProtocol = null,
        string title = "选择服务器节点",
        bool isSelectionMode = true)
    {
        _allNodes = nodes ?? Array.Empty<ProviderNodeModel>();
        _isSelectionMode = isSelectionMode;

        if (!string.IsNullOrWhiteSpace(selectedNodeId))
        {
            SelectedNode = _allNodes.FirstOrDefault(n => string.Equals(n.Id, selectedNodeId, StringComparison.OrdinalIgnoreCase));
        }

        Title = title;
        Width = 780;
        Height = 660;
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = DesktopShell.Icon();

        // 1. Title / Header
        var titleStack = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 12) };
        titleStack.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold });
        var headerSubtitle = new TextBlock
        {
            Text = $"共有 {_allNodes.Count} 个可用节点{(SelectedNode is not null ? $" · 当前已选: {SelectedNode.Name}" : "")}",
            Classes = { "hint" }
        };
        titleStack.Children.Add(headerSubtitle);

        // 2. Filters Row: Search input and Protocol type input aligned at bottom
        _searchBox = new TextBox
        {
            Watermark = "搜索节点名称、描述、地址...",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var availableProtocols = GetAvailableProtocols(_allNodes);
        _protocolBox = new ComboBox
        {
            ItemsSource = availableProtocols.ToArray(),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _protocolBox.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((val, _) =>
            new TextBlock { Text = string.IsNullOrEmpty(val) ? "全部协议" : val.ToUpperInvariant() });

        if (!string.IsNullOrWhiteSpace(defaultProtocol) && availableProtocols.Contains(defaultProtocol, StringComparer.OrdinalIgnoreCase))
        {
            _protocolBox.SelectedItem = availableProtocols.First(p => string.Equals(p, defaultProtocol, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            _protocolBox.SelectedItem = string.Empty;
        }

        var filterGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,160"),
            Margin = new Thickness(0, 0, 0, 10),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        var searchField = FormDialog.Field("搜索节点", _searchBox);
        var protocolField = FormDialog.Field("协议类型", _protocolBox);
        protocolField.Margin = new Thickness(12, 0, 0, 0);

        Grid.SetColumn(searchField, 0);
        Grid.SetColumn(protocolField, 1);
        filterGrid.Children.Add(searchField);
        filterGrid.Children.Add(protocolField);

        // 3. Quick filters row
        _onlyOnlineBox = new CheckBox { Content = "仅看在线", Cursor = new Cursor(StandardCursorType.Hand) };
        _onlyLowLoadBox = new CheckBox { Content = "仅看低负载 (≤50%)", Margin = new Thickness(12, 0, 0, 0), Cursor = new Cursor(StandardCursorType.Hand) };
        _toggleAllButton = new Button
        {
            Content = "全部折叠",
            Cursor = new Cursor(StandardCursorType.Hand),
            Margin = new Thickness(12, 0, 0, 0)
        };
        _matchCountText = new TextBlock
        {
            Text = $"匹配 {_allNodes.Count} / {_allNodes.Count} 个节点",
            Classes = { "hint" },
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var quickBar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto"),
            Margin = new Thickness(0, 0, 0, 12),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_onlyOnlineBox, 0);
        Grid.SetColumn(_onlyLowLoadBox, 1);
        Grid.SetColumn(_toggleAllButton, 2);
        Grid.SetColumn(_matchCountText, 4);
        quickBar.Children.Add(_onlyOnlineBox);
        quickBar.Children.Add(_onlyLowLoadBox);
        quickBar.Children.Add(_toggleAllButton);
        quickBar.Children.Add(_matchCountText);

        // 4. Scrollable Groups Container
        _groupsContainer = new StackPanel { Spacing = 6 };
        var scrollViewer = new ScrollViewer
        {
            Content = _groupsContainer,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };

        // 5. Dialog Actions footer: [Selected info] ... [确认] [关闭]
        _selectedSummaryText = new TextBlock
        {
            Classes = { "hint" },
            VerticalAlignment = VerticalAlignment.Center
        };
        UpdateSelectedSummary();

        _confirmButton = new Button
        {
            Content = "确认",
            Cursor = new Cursor(StandardCursorType.Hand),
            IsEnabled = SelectedNode is not null
        };
        _confirmButton.Classes.Add("accent");
        _confirmButton.Click += (_, _) =>
        {
            if (SelectedNode is not null)
            {
                Close(true);
            }
        };

        _closeButton = new Button
        {
            Content = "关闭",
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            IsCancel = true
        };
        _closeButton.Click += (_, _) => Close(false);

        var actionButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { _confirmButton, _closeButton }
        };

        if (!_isSelectionMode)
        {
            _confirmButton.IsVisible = false;
        }

        var footerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 14, 0, 0)
        };
        Grid.SetColumn(_selectedSummaryText, 0);
        Grid.SetColumn(actionButtons, 1);
        footerGrid.Children.Add(_selectedSummaryText);
        footerGrid.Children.Add(actionButtons);

        // Assemble main layout
        var mainLayout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            Margin = new Thickness(20)
        };
        Grid.SetRow(titleStack, 0);
        Grid.SetRow(filterGrid, 1);
        Grid.SetRow(quickBar, 2);
        Grid.SetRow(scrollViewer, 3);
        Grid.SetRow(footerGrid, 4);

        mainLayout.Children.Add(titleStack);
        mainLayout.Children.Add(filterGrid);
        mainLayout.Children.Add(quickBar);
        mainLayout.Children.Add(scrollViewer);
        mainLayout.Children.Add(footerGrid);

        Content = mainLayout;

        // Wire event handlers
        _searchBox.TextChanged += (_, _) => RefreshList();
        _protocolBox.SelectionChanged += (_, _) => RefreshList();
        _onlyOnlineBox.IsCheckedChanged += (_, _) => RefreshList();
        _onlyLowLoadBox.IsCheckedChanged += (_, _) => RefreshList();
        _toggleAllButton.Click += (_, _) => ToggleAllGroups();

        RefreshList();
    }

    private static IReadOnlyList<string> GetAvailableProtocols(IReadOnlyList<ProviderNodeModel> nodes)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.AllowedTypes))
            {
                var parts = node.AllowedTypes.Split(new[] { ',', '/', ' ', '|', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var trimmed = part.Trim().ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(trimmed)) set.Add(trimmed);
                }
            }
        }

        var result = new List<string> { string.Empty };
        string[] preferredOrder = { "tcp", "udp", "http", "https", "xtcp", "stcp" };
        foreach (var p in preferredOrder)
        {
            if (set.Contains(p)) result.Add(p);
        }
        foreach (var p in set.Where(p => !preferredOrder.Contains(p)).OrderBy(p => p))
        {
            result.Add(p);
        }
        return result;
    }

    private void UpdateSelectedSummary()
    {
        if (SelectedNode is not null)
        {
            _selectedSummaryText.Text = $"已选节点：{SelectedNode.Name} ({SelectedNode.RegionGroup}" +
                $"{(string.IsNullOrWhiteSpace(SelectedNode.Bandwidth) ? "" : $" · {SelectedNode.Bandwidth}")})";
            _confirmButton.IsEnabled = true;
        }
        else
        {
            _selectedSummaryText.Text = _isSelectionMode ? "点击卡片选中节点" : string.Empty;
            _confirmButton.IsEnabled = false;
        }
    }

    private void RefreshList()
    {
        var query = _allNodes.AsEnumerable();

        var selectedProto = _protocolBox.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(selectedProto))
        {
            var p = selectedProto.Trim();
            query = query.Where(n =>
                string.IsNullOrWhiteSpace(n.AllowedTypes) ||
                n.AllowedTypes.Split(new[] { ',', '/', ' ', '|', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(t => string.Equals(t.Trim(), p, StringComparison.OrdinalIgnoreCase)));
        }

        if (_onlyOnlineBox.IsChecked == true)
        {
            query = query.Where(n => n.IsOnline);
        }

        if (_onlyLowLoadBox.IsChecked == true)
        {
            query = query.Where(n => n.LoadPercent is null || n.LoadPercent <= 50);
        }

        var search = _searchBox.Text?.Trim();
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        if (hasSearch)
        {
            query = query.Where(n =>
                (n.Name?.Contains(search!, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (n.Description?.Contains(search!, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (n.Address?.Contains(search!, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (n.Region?.Contains(search!, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (n.AllowedTypes?.Contains(search!, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var filtered = query.OrderBy(n => NodeRegionClassifier.GetDisplayOrder(n.RegionGroup))
            .ThenByDescending(n => n.IsOnline)
            .ThenBy(n => n.LoadPercent ?? 50)
            .ThenBy(n => n.Name)
            .ToList();

        _matchCountText.Text = $"匹配 {filtered.Count} / {_allNodes.Count} 个节点";
        _groupsContainer.Children.Clear();

        if (filtered.Count == 0)
        {
            _groupsContainer.Children.Add(new TextBlock
            {
                Text = "没有找到匹配条件的节点。",
                Classes = { "hint" },
                Margin = new Thickness(0, 24, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        foreach (var (groupId, displayName) in NodeRegionClassifier.DisplayGroups)
        {
            var groupNodes = filtered
                .Where(n => string.Equals(n.RegionGroup, groupId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (groupNodes.Count == 0) continue;

            var onlineInGroup = groupNodes.Count(n => n.IsOnline);
            var isCollapsed = !hasSearch && _collapsedGroups.Contains(groupId);

            // Group Panel Container
            var chevron = new TextBlock
            {
                Text = isCollapsed ? "⌄" : "⌃",
                FontWeight = FontWeight.Bold,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Foreground = new SolidColorBrush(Color.FromArgb(160, 128, 128, 128))
            };
            var groupTitle = new TextBlock
            {
                Text = displayName,
                FontWeight = FontWeight.Bold,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#0078D4"))
            };
            var groupBadge = new TextBlock
            {
                Text = $"{groupNodes.Count} 个节点 ({onlineInGroup} 在线)",
                Classes = { "hint" },
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };

            var headerLeft = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { groupTitle, groupBadge }
            };

            var headerGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(headerLeft, 0);
            Grid.SetColumn(chevron, 1);
            headerGrid.Children.Add(headerLeft);
            headerGrid.Children.Add(chevron);

            var groupHeader = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(16, 0, 120, 212)),
                Padding = new Thickness(12, 9),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = headerGrid
            };

            var cardsStack = new StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(10, 10, 10, 6),
                IsVisible = !isCollapsed
            };

            var panelStack = new StackPanel
            {
                Children = { groupHeader, cardsStack }
            };

            var groupPanel = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 0, 120, 212)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 8),
                ClipToBounds = true,
                Child = panelStack
            };

            var capturedGroupId = groupId;
            groupHeader.PointerPressed += (_, _) =>
            {
                if (!_collapsedGroups.Add(capturedGroupId))
                {
                    _collapsedGroups.Remove(capturedGroupId);
                }
                var nowCollapsed = _collapsedGroups.Contains(capturedGroupId);
                chevron.Text = nowCollapsed ? "⌄" : "⌃";
                cardsStack.IsVisible = !nowCollapsed;
            };

            foreach (var node in groupNodes)
            {
                cardsStack.Children.Add(CreateNodeCard(node));
            }

            _groupsContainer.Children.Add(groupPanel);
        }
    }

    private Control CreateNodeCard(ProviderNodeModel node)
    {
        var isSelected = SelectedNode is not null && string.Equals(node.Id, SelectedNode.Id, StringComparison.OrdinalIgnoreCase);

        var cardBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = node.Available ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.No),
            Opacity = node.Available ? 1.0 : 0.55
        };

        void ApplyCardStyle(bool selected, bool hovered = false)
        {
            if (selected)
            {
                cardBorder.BorderBrush = new SolidColorBrush(Color.Parse("#0078D4"));
                cardBorder.BorderThickness = new Thickness(2);
                cardBorder.Background = new SolidColorBrush(Color.FromArgb(24, 0, 120, 212));
            }
            else
            {
                cardBorder.BorderThickness = new Thickness(1);
                cardBorder.BorderBrush = new SolidColorBrush(Color.Parse(hovered ? "#0078D4" : "#D2D0CE"));
                cardBorder.Background = new SolidColorBrush(Color.Parse(hovered ? "#F8F9FA" : "#FFFFFF"));
            }
        }

        ApplyCardStyle(isSelected);

        cardBorder.PointerEntered += (_, _) =>
        {
            if (node.Available && !(SelectedNode is not null && string.Equals(node.Id, SelectedNode.Id, StringComparison.OrdinalIgnoreCase)))
            {
                ApplyCardStyle(false, hovered: true);
            }
        };
        cardBorder.PointerExited += (_, _) =>
        {
            var currentSelected = SelectedNode is not null && string.Equals(node.Id, SelectedNode.Id, StringComparison.OrdinalIgnoreCase);
            ApplyCardStyle(currentSelected);
        };

        var contentStack = new StackPanel { Spacing = 4 };

        // Top line: Name + Online dot + VIP badge + Selected badge
        var topLine = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var titleLeft = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var statusDot = new TextBlock
        {
            Text = node.IsOnline ? "🟢" : "🔴",
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(statusDot, node.IsOnline ? "节点在线" : "节点离线/维护");

        var nameText = new TextBlock
        {
            Text = node.Name,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleLeft.Children.Add(statusDot);
        titleLeft.Children.Add(nameText);

        var titleRight = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (node.IsVip)
        {
            var vipPill = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#FFF4CE")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1),
                Child = new TextBlock { Text = "VIP", FontSize = 10, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#8A5A00")) }
            };
            titleRight.Children.Add(vipPill);
        }

        if (isSelected)
        {
            var selectedPill = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#0078D4")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2),
                Child = new TextBlock { Text = "✓ 已选中", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brushes.White }
            };
            titleRight.Children.Add(selectedPill);
        }

        Grid.SetColumn(titleLeft, 0);
        Grid.SetColumn(titleRight, 1);
        topLine.Children.Add(titleLeft);
        topLine.Children.Add(titleRight);
        contentStack.Children.Add(topLine);

        // Load progress bar (if present)
        if (node.LoadPercent is not null)
        {
            var loadStack = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("60,*"),
                Margin = new Thickness(0, 2, 0, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            var loadLabel = new TextBlock
            {
                Text = $"负载 {node.LoadPercent}%",
                Classes = { "hint" },
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            var barColor = node.LoadPercent < 50 ? "#107C10" : node.LoadPercent < 80 ? "#8A5A00" : "#C42B1C";
            var loadBar = new ProgressBar
            {
                Value = node.LoadPercent.Value,
                Maximum = 100,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Foreground = new SolidColorBrush(Color.Parse(barColor)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(loadLabel, 0);
            Grid.SetColumn(loadBar, 1);
            loadStack.Children.Add(loadLabel);
            loadStack.Children.Add(loadBar);
            contentStack.Children.Add(loadStack);
        }

        // Tags line (Bandwidth, Ratio, NeedKyc, NeedBeian)
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(node.Bandwidth)) tags.Add($"⚡ {node.Bandwidth}");
        if (node.TrafficRatio is not null) tags.Add($"倍率: {node.TrafficRatio:0.#}x");
        if (node.NeedKyc) tags.Add("需实名");
        if (node.NeedBeian) tags.Add("需备案");

        if (tags.Count > 0)
        {
            contentStack.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", tags),
                Classes = { "hint" },
                FontSize = 12
            });
        }

        if (!string.IsNullOrWhiteSpace(node.AllowedTypes))
        {
            contentStack.Children.Add(new TextBlock
            {
                Text = $"支持协议: {node.AllowedTypes}",
                Classes = { "hint" },
                FontSize = 12
            });
        }

        // Description: 2 lines max, hover shows full tooltip
        if (!string.IsNullOrWhiteSpace(node.Description))
        {
            var desc = new TextBlock
            {
                Text = node.Description,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Classes = { "hint" },
                FontStyle = FontStyle.Italic,
                Margin = new Thickness(0, 2, 0, 0)
            };
            ToolTip.SetTip(desc, node.Description);
            contentStack.Children.Add(desc);
        }

        cardBorder.Child = contentStack;

        if (_isSelectionMode && node.Available)
        {
            cardBorder.PointerPressed += (_, _) =>
            {
                SelectedNode = node;
                UpdateSelectedSummary();
                RefreshList();
            };

            cardBorder.DoubleTapped += (_, _) =>
            {
                SelectedNode = node;
                Close(true);
            };
        }

        return cardBorder;
    }

    private void ToggleAllGroups()
    {
        var allGroups = NodeRegionClassifier.DisplayGroups.Select(g => g.GroupId).ToList();
        var allCollapsed = allGroups.All(g => _collapsedGroups.Contains(g));

        if (allCollapsed)
        {
            _collapsedGroups.Clear();
            _toggleAllButton.Content = "全部折叠";
        }
        else
        {
            foreach (var g in allGroups) _collapsedGroups.Add(g);
            _toggleAllButton.Content = "全部展开";
        }
        RefreshList();
    }
}
