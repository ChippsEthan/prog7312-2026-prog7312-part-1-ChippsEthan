using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SmartX.WPF.Models;
using SmartX.WPF.Services;

namespace SmartX.WPF.Views;

public class HistoryRow
{
    public string Time { get; set; } = "";
    public string Value { get; set; } = "";
    public string DataType { get; set; } = "";
}

public partial class TopologyView : UserControl
{
    private const string GatewayId = "GATEWAY";

    private static readonly SolidColorBrush Green = Rgb(0x16, 0xA3, 0x4A);
    private static readonly SolidColorBrush Red = Rgb(0xDC, 0x26, 0x26);
    private static readonly SolidColorBrush Blue = Rgb(0x25, 0x63, 0xEB);
    private static readonly SolidColorBrush Amber = Rgb(0xF5, 0x9E, 0x0B);
    private static readonly SolidColorBrush Grey = Rgb(0x9C, 0xA3, 0xAF);
    private static readonly SolidColorBrush Muted = Rgb(0x6B, 0x72, 0x80);
    private static readonly SolidColorBrush Ink = Rgb(0x11, 0x18, 0x27);

    private readonly AppState _state = AppState.Instance;
    private readonly DispatcherTimer _timer;
    private readonly ObservableCollection<HistoryRow> _history = new();
    private readonly ObservableCollection<string> _reroutes = new();
    private readonly Dictionary<string, Point> _positions = new();

    private MeshSnapshotDto? _mesh;
    private MeshRouteDto? _route;
    private string? _selectedId;
    private string _lastSignature = "";
    private bool _refreshing;

    public TopologyView()
    {
        InitializeComponent();

        HistoryGrid.ItemsSource = _history;
        RerouteList.ItemsSource = _reroutes;

        var now = DateTime.Now;
        ToBox.Text = now.ToString("HH:mm");
        FromBox.Text = (now.Hour == 0 ? now.Date : now.AddHours(-1)).ToString("HH:mm");

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await RefreshAsync();

        Loaded += async (_, _) =>
        {
            _timer.Start();
            await RefreshAsync();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private static SolidColorBrush Rgb(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }


    private async Task RefreshAsync(bool force = false)
    {
        if ((_refreshing && !force) || !_state.ApiOnline) return;
        _refreshing = true;

        try
        {
            _mesh = await _state.Api.GetMeshAsync();

            if (_selectedId is not null)
            {
                try { _route = await _state.Api.GetRouteAsync(_selectedId); }
                catch (Exception) { _route = null; }
            }

            ApplyMesh();
        }
        catch (Exception)
        {
            SummaryText.Text = "Waiting for the API...";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ApplyMesh()
    {
        if (_mesh is null) return;

        int sensorCount = _mesh.Nodes.Count(n => !n.IsGateway);
        SummaryText.Text =
            $"{_mesh.ReachableCount} of {sensorCount} sensors reach the gateway   |   " +
            $"Spanning tree power cost {_mesh.MstTotalCost:0.#}   |   {_mesh.DownNodes.Count} down";

        if (_route is null || _selectedId is null)
        {
            RouteText.Text = "No node selected.";
        }
        else if (!_route.Reachable)
        {
            RouteText.Text = $"{_route.Name}: {_route.Note}";
        }
        else
        {
            RouteText.Text =
                $"{_route.Name} ({_route.Id})\n" +
                $"Path: {string.Join(" -> ", _route.PathNames)}\n" +
                $"Hops: {_route.Hops}   |   Power cost: {_route.TotalCost:0.#}";
        }

        UpdateSelectionUi();
        RebuildRerouteLog();

        var signature = Signature();
        if (signature != _lastSignature)
        {
            _lastSignature = signature;
            Draw();
        }
    }

    private void UpdateSelectionUi()
    {
        var node = _mesh?.Nodes.FirstOrDefault(n => n.Id == _selectedId);

        SelectedText.Text = node is null ? "Selected: none" : $"Selected: {node.Name} ({node.Id})";
        FailButton.IsEnabled = node is { IsGateway: false, ForcedDown: false };
        RestoreButton.IsEnabled = node is { ForcedDown: true };
    }

    private void RebuildRerouteLog()
    {
        if (_mesh is null) return;

        var lines = new List<string>();

        foreach (var r in _mesh.Reroutes)
        {
            if (r.Isolated)
                lines.Add($"{r.Name}: ISOLATED, no live path to the gateway.");
            else
                lines.Add($"{r.Name}: rerouted via {string.Join(" -> ", r.NewPath.Select(NameOf))} " +
                          $"(cost {r.NewCost:0.#}, was {r.OldCost:0.#})");
        }

        if (lines.Count == 0)
            lines.Add("All routes healthy. Fail a node to watch the mesh heal itself.");

        if (_reroutes.SequenceEqual(lines)) return;

        _reroutes.Clear();
        foreach (var line in lines) _reroutes.Add(line);
    }

    private string Signature()
    {
        if (_mesh is null) return "";

        var sb = new System.Text.StringBuilder();
        foreach (var n in _mesh.Nodes) sb.Append(n.Id).Append(n.IsUp ? '1' : '0');
        foreach (var l in _mesh.Links) sb.Append(l.Active ? '1' : '0').Append(l.InMst ? '1' : '0');
        sb.Append('|').Append(_selectedId).Append('|');
        if (_route is not null) sb.Append(string.Join(",", _route.Path));
        return sb.ToString();
    }

    private string NameOf(string id) =>
        _mesh?.Nodes.FirstOrDefault(n => n.Id == id)?.Name ?? id;


    private void MeshCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Draw();

    private void Draw()
    {
        MeshCanvas.Children.Clear();
        _positions.Clear();

        if (_mesh is null) return;

        double w = MeshCanvas.ActualWidth;
        double h = MeshCanvas.ActualHeight;
        if (w < 120 || h < 120) return;

        var gateway = _mesh.Nodes.FirstOrDefault(n => n.IsGateway);
        var sensors = _mesh.Nodes.Where(n => !n.IsGateway).ToList();

        var center = new Point(w / 2, h / 2);
        if (gateway is not null) _positions[gateway.Id] = center;

        double rx = Math.Max(90, w / 2 - 95);
        double ry = Math.Max(70, h / 2 - 55);

        for (int i = 0; i < sensors.Count; i++)
        {
            double angle = -Math.PI / 2 + 2 * Math.PI * i / sensors.Count;
            _positions[sensors[i].Id] = new Point(center.X + rx * Math.Cos(angle), center.Y + ry * Math.Sin(angle));
        }

        var costLabels = new List<TextBlock>();
        foreach (var l in _mesh.Links)
        {
            if (!_positions.TryGetValue(l.A, out var a) || !_positions.TryGetValue(l.B, out var b)) continue;

            var line = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y };

            if (!l.Active)
            {
                line.Stroke = Red;
                line.StrokeThickness = 1.5;
                line.StrokeDashArray = new DoubleCollection { 4, 3 };
            }
            else if (l.InMst)
            {
                line.Stroke = Green;
                line.StrokeThickness = 3.5;
            }
            else
            {
                line.Stroke = Grey;
                line.StrokeThickness = 1.5;
            }

            MeshCanvas.Children.Add(line);

            var label = new TextBlock
            {
                Text = l.Cost.ToString("0.#"),
                FontSize = 10,
                Foreground = Muted,
                Background = Brushes.White,
                Padding = new Thickness(2, 0, 2, 0),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(label, (a.X + b.X) / 2 - 7);
            Canvas.SetTop(label, (a.Y + b.Y) / 2 - 8);
            costLabels.Add(label);
        }

        if (_route is { Reachable: true } && _route.Path.Count > 1)
        {
            for (int i = 0; i + 1 < _route.Path.Count; i++)
            {
                if (!_positions.TryGetValue(_route.Path[i], out var a) ||
                    !_positions.TryGetValue(_route.Path[i + 1], out var b)) continue;

                MeshCanvas.Children.Add(new Line
                {
                    X1 = a.X,
                    Y1 = a.Y,
                    X2 = b.X,
                    Y2 = b.Y,
                    Stroke = Amber,
                    StrokeThickness = 6,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
            }
        }

        foreach (var label in costLabels) MeshCanvas.Children.Add(label);

        foreach (var n in _mesh.Nodes)
        {
            if (!_positions.TryGetValue(n.Id, out var p)) continue;

            double size = n.IsGateway ? 58 : 46;
            bool selected = n.Id == _selectedId;

            string state = n.IsGateway ? "GATEWAY"
                : n.IsUp ? "UP"
                : n.ForcedDown ? "DOWN (simulated failure)" : "DOWN (offline)";

            var dot = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = n.IsGateway ? Blue : (n.IsUp ? Green : Red),
                Stroke = selected ? Amber : Brushes.White,
                StrokeThickness = selected ? 4 : 2,
                Cursor = Cursors.Hand,
                Tag = n.Id,
                ToolTip = $"{n.Name}\n{n.Id}\n{n.Location}\n{state}"
            };
            dot.MouseLeftButtonUp += Node_Click;
            Canvas.SetLeft(dot, p.X - size / 2);
            Canvas.SetTop(dot, p.Y - size / 2);
            MeshCanvas.Children.Add(dot);

            var initial = new TextBlock
            {
                Text = n.IsGateway ? "GW" : (n.Category.Length > 0 ? n.Category[..1] : "?"),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Width = size,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(initial, p.X - size / 2);
            Canvas.SetTop(initial, p.Y - 9);
            MeshCanvas.Children.Add(initial);

            var name = new TextBlock
            {
                Text = n.IsUp ? n.Name : $"{n.Name} (down)",
                Foreground = Ink,
                FontSize = 11,
                Width = 120,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(name, p.X - 60);
            Canvas.SetTop(name, p.Y + size / 2 + 2);
            MeshCanvas.Children.Add(name);
        }
    }


    private async void Node_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
            await SelectAsync(id);
    }

    private async Task SelectAsync(string id)
    {
        _selectedId = id;
        TrackBox.Text = id;

        try { _route = await _state.Api.GetRouteAsync(id); }
        catch (Exception) { _route = null; }

        _lastSignature = "";
        ApplyMesh();

        if (id == GatewayId)
        {
            _history.Clear();
            HistoryTitle.Text = "Telemetry history: select a sensor";
            HistoryInfo.Text = "The gateway has no telemetry. Click a sensor.";
        }
        else
        {
            await LoadLastMinutesAsync(15);
        }
    }

    private string? ResolveNode(string text)
    {
        if (_mesh is null) return null;

        var t = text.Trim();
        if (t.Length == 0) return null;

        var mac = t.Replace('-', ':').ToUpperInvariant();

        var exact = _mesh.Nodes.FirstOrDefault(n => n.Id.Equals(mac, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.Id;

        var partial = _mesh.Nodes.FirstOrDefault(n =>
            n.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
            n.Id.Contains(mac, StringComparison.OrdinalIgnoreCase));

        return partial?.Id;
    }

    private async void Track_Click(object sender, RoutedEventArgs e)
    {
        var id = ResolveNode(TrackBox.Text);
        if (id is null)
        {
            RouteText.Text = $"No node matches \"{TrackBox.Text.Trim()}\". Try a full MAC address or part of a name.";
            return;
        }

        await SelectAsync(id);
    }

    private void TrackBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Track_Click(sender, e);
    }


    private async void Fail_Click(object sender, RoutedEventArgs e) => await SetFailedAsync(true);

    private async void Restore_Click(object sender, RoutedEventArgs e) => await SetFailedAsync(false);

    private async Task SetFailedAsync(bool fail)
    {
        if (_selectedId is null || _selectedId == GatewayId) return;

        try
        {
            var result = fail
                ? await _state.Api.FailNodeAsync(_selectedId)
                : await _state.Api.RestoreNodeAsync(_selectedId);

            ActionText.Text = result.Ok
                ? (fail ? $"{NameOf(_selectedId)} failed. The spanning tree is rebuilding without it."
                        : $"{NameOf(_selectedId)} restored. Routes are recalculating.")
                : result.Message;

            await RefreshAsync(force: true);
        }
        catch (Exception)
        {
            ActionText.Text = "Cannot reach the API. Is SmartX.API running?";
        }
    }

    private async void Bfs_Click(object sender, RoutedEventArgs e) => await TraverseAsync("bfs");

    private async void Dfs_Click(object sender, RoutedEventArgs e) => await TraverseAsync("dfs");

    private async Task TraverseAsync(string mode)
    {
        var start = _selectedId ?? GatewayId;

        try
        {
            var result = await _state.Api.TraverseAsync(start, mode);

            if (result.Steps.Count == 0)
            {
                TraversalText.Text = $"{NameOf(start)} is down, so nothing can be reached from it.";
                return;
            }

            var lines = result.Steps.Select((s, i) =>
                $"{i + 1}. {s.Name} ({s.Hops} hop{(s.Hops == 1 ? "" : "s")})");

            var visited = result.Steps.Select(s => s.Id).ToHashSet();
            var missed = _mesh?.Nodes.Where(n => !visited.Contains(n.Id)).Select(n => n.Name).ToList()
                         ?? new List<string>();

            TraversalText.Text =
                $"{result.Mode} from {NameOf(start)} reached {result.Visited} node(s):\n" +
                string.Join("\n", lines) +
                (missed.Count > 0 ? $"\n\nNot reachable (down or cut off): {string.Join(", ", missed)}" : "");
        }
        catch (Exception)
        {
            TraversalText.Text = "Cannot reach the API. Is SmartX.API running?";
        }
    }

    // ---------- historical log (AVL tree range query) ----------

    private async void LoadRange_Click(object sender, RoutedEventArgs e)
    {
        if (!TimeSpan.TryParse(FromBox.Text.Trim(), CultureInfo.InvariantCulture, out var fromTime) ||
            !TimeSpan.TryParse(ToBox.Text.Trim(), CultureInfo.InvariantCulture, out var toTime) ||
            fromTime < TimeSpan.Zero || fromTime >= TimeSpan.FromDays(1) ||
            toTime < TimeSpan.Zero || toTime >= TimeSpan.FromDays(1))
        {
            HistoryInfo.Text = "Enter times like 14:00 and 16:00.";
            return;
        }

        var start = DateTime.Today + fromTime;
        var end = DateTime.Today + toTime + TimeSpan.FromSeconds(59);

        if (end <= start)
        {
            HistoryInfo.Text = "The end time must be after the start time (same day).";
            return;
        }

        await LoadRangeAsync(start, end);
    }

    private async void Last5_Click(object sender, RoutedEventArgs e) => await LoadLastMinutesAsync(5);

    private async void LastHour_Click(object sender, RoutedEventArgs e) => await LoadLastMinutesAsync(60);

    private async Task LoadLastMinutesAsync(int minutes)
    {
        var end = DateTime.Now;
        var start = end.AddMinutes(-minutes);

        FromBox.Text = start.ToString("HH:mm");
        ToBox.Text = end.ToString("HH:mm");

        await LoadRangeAsync(start, end);
    }

    private async Task LoadRangeAsync(DateTime startLocal, DateTime endLocal)
    {
        if (_selectedId is null || _selectedId == GatewayId)
        {
            HistoryInfo.Text = "Select a sensor first (click it on the map).";
            return;
        }

        var node = _mesh?.Nodes.FirstOrDefault(n => n.Id == _selectedId);
        var device = _state.Devices.FirstOrDefault(d => d.MacAddress == _selectedId);
        HistoryTitle.Text = $"Telemetry history: {node?.Name ?? _selectedId}";

        try
        {
            var sw = Stopwatch.StartNew();
            var result = await _state.Api.GetRangeAsync(_selectedId, startLocal.ToUniversalTime(), endLocal.ToUniversalTime());
            sw.Stop();

            _history.Clear();

            foreach (var r in result.Readings.OrderByDescending(r => r.Timestamp).Take(1000))
            {
                string value = device is { Category: "Actuator" }
                    ? (r.Value > 0.5 ? "ON" : "OFF")
                    : $"{r.Value:0.#}{device?.Unit}";

                _history.Add(new HistoryRow
                {
                    Time = r.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                    Value = value,
                    DataType = r.SensorType
                });
            }

            HistoryInfo.Text =
                $"{result.Count} reading(s) between {startLocal:HH:mm} and {endLocal:HH:mm}" +
                (result.Count > 1000 ? " (newest 1000 shown)" : "") +
                $".  The AVL tree visited {result.NodesVisited} of {result.TreeSize} stored readings " +
                $"(tree height {result.TreeHeight}) in {sw.ElapsedMilliseconds} ms.";
        }
        catch (Exception)
        {
            HistoryInfo.Text = "Could not load history. Is SmartX.API running?";
        }
    }
}