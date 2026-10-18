using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SmartX.WPF.Models;
using SmartX.WPF.Services;

namespace SmartX.WPF.Views;

public partial class CommandStreamView : UserControl
{
    private readonly AppState _state = AppState.Instance;
    private readonly ICollectionView _view;            
    private readonly DispatcherTimer _pollTimer;      
    private readonly DispatcherTimer _searchTimer;    

    private readonly ObservableCollection<SuggestionItem> _suggestions = new();
    private readonly ObservableCollection<SuggestionItem> _insights = new();
    private readonly ObservableCollection<string> _processed = new();
    private readonly ObservableCollection<string> _history = new();

    private bool _ready;
    private bool _polling;
    private string _suggestionSig = "";
    private string _insightSig = "";
    private int _undoDepth;
    private SensorDisplay? _selected;
    private List<ReadingDto> _lastReadings = new();

    public CommandStreamView()
    {
        InitializeComponent();

        
        var source = new CollectionViewSource { Source = _state.Devices };
        _view = source.View;
        _view.Filter = FilterDevice;

        if (_view is ICollectionViewLiveShaping live && live.CanChangeLiveFiltering)
        {
            live.LiveFilteringProperties.Add(nameof(SensorDisplay.StatusText)); 
            live.IsLiveFiltering = true;
        }

        DeviceList.ItemsSource = _view;
        SuggestionsList.ItemsSource = _suggestions;
        InsightsList.ItemsSource = _insights;
        ProcessedList.ItemsSource = _processed;
        HistoryList.ItemsSource = _history;
        RandomAnomaliesBox.IsChecked = _state.Simulator.RandomAnomalies;

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pollTimer.Tick += async (_, _) => await PollAsync();

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _searchTimer.Tick += SearchTimer_Tick;

        
        Loaded += async (_, _) =>
        {
            _pollTimer.Start();
            OnStateRefreshed();
            await PollAsync();
        };
        Unloaded += (_, _) => _pollTimer.Stop();

        _state.Refreshed += OnStateRefreshed;

        UpdateSelectedPanel();
        _ready = true;
    }

    

    private bool FilterDevice(object obj)
    {
        if (obj is not SensorDisplay d) return false;

        var text = SearchBox.Text.Trim();
        if (text.Length > 0 &&
            !(d.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
              d.MacAddress.Contains(text, StringComparison.OrdinalIgnoreCase) ||
              d.Location.Contains(text, StringComparison.OrdinalIgnoreCase) ||
              d.Category.Contains(text, StringComparison.OrdinalIgnoreCase) ||
              d.StatusText.Contains(text, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (CategoryFilter.SelectedIndex > 0 &&
            CategoryFilter.SelectedItem is ComboBoxItem item &&
            d.Category != item.Content?.ToString())
            return false;

        if (AlertsOnly.IsChecked == true && !IsProblem(d))
            return false;

        return true;
    }

    private static bool IsProblem(SensorDisplay d) =>
        d.StatusText is "WARNING" or "CRITICAL" or "OFFLINE";

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;

        _view.Refresh();
        UpdateResultCount();

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void SearchTimer_Tick(object? sender, EventArgs e)
    {
        _searchTimer.Stop();

        var query = SearchBox.Text.Trim();
        if (query.Length >= 2)
            _ = SafeAsync(() => _state.Api.RecordSearchAsync(query));   
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _view.Refresh();
        UpdateResultCount();
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e) => ClearFilters();

    private void ClearFilters()
    {
        SearchBox.Text = "";
        CategoryFilter.SelectedIndex = 0;
        AlertsOnly.IsChecked = false;
        _view.Refresh();
        UpdateResultCount();
    }

    private void UpdateResultCount()
    {
        ResultCountText.Text = $"Showing {_view.Cast<object>().Count()} of {_state.Devices.Count} devices";
    }

    

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;

        _selected = DeviceList.SelectedItem as SensorDisplay;
        _lastReadings = new List<ReadingDto>();
        UpdateSelectedPanel();
        DrawChart();

        if (_selected is not null)
        {
            var mac = _selected.MacAddress;
            _ = SafeAsync(() => _state.Api.RecordSearchAsync(mac));  
            _ = LoadHistoryAsync();
        }
    }

    private void SelectDevice(string mac)
    {
        ClearFilters();

        var device = _state.Devices.FirstOrDefault(d => d.MacAddress == mac);
        if (device is null) return;

        DeviceList.SelectedItem = device;
        DeviceList.ScrollIntoView(device);
    }

    private void UpdateSelectedPanel()
    {
        if (_selected is null)
        {
            SelectedText.Text = "Select a device in the list. Pumps and relays can be commanded; sensors can be inspected and tested.";
            OnButton.IsEnabled = OffButton.IsEnabled = ToggleBtn.IsEnabled = false;
            ChartTitle.Text = "Timeline: select a device";
            SilenceBtn.Content = "Silence node";
            return;
        }

        bool actuator = _selected.IsActuator;
        SelectedText.Text = actuator
            ? $"{_selected.Name} ({_selected.Location}) is currently {(_selected.IsOn ? "ON" : "OFF")}."
            : $"{_selected.Name} is a sensor, so it cannot be commanded. Select a pump or relay to send overrides.";

        OnButton.IsEnabled = OffButton.IsEnabled = ToggleBtn.IsEnabled = actuator;

        ChartTitle.Text = $"Timeline: {_selected.Name}" + (string.IsNullOrEmpty(_selected.Unit) ? "" : $" ({_selected.Unit})");
        SilenceBtn.Content = _state.Simulator.IsSilenced(_selected.MacAddress) ? "Restore node" : "Silence node";
    }

    private void OnStateRefreshed()
    {
        UpdateResultCount();
        UpdateSummary();
        UpdateSelectedPanel();
    }

    private void UpdateSummary()
    {
        int online = _state.Devices.Count(d => d.IsOnline);

        SummaryText.Text = _state.ApiOnline
            ? $"{online}/{_state.Devices.Count} devices online   |   {_state.ActiveAlerts.Count} active alerts   |   undo stack: {_undoDepth}"
            : "Waiting for the API...";
    }

    

    private async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;

        try
        {
            InsightsDto insights;
            QueueStatsDto stats;
            CommandHistoryDto commands;
            List<ReadingDto> readings;
            var device = _selected;

            try
            {
                var insightsTask = _state.Api.GetInsightsAsync();
                var statsTask = _state.Api.GetQueueStatsAsync();
                var commandsTask = _state.Api.GetCommandsAsync();
                var historyTask = device is null
                    ? Task.FromResult(new List<ReadingDto>())
                    : _state.Api.GetHistoryAsync(device.MacAddress, 60);

                await Task.WhenAll(insightsTask, statsTask, commandsTask, historyTask);

                insights = insightsTask.Result;
                stats = statsTask.Result;
                commands = commandsTask.Result;
                readings = historyTask.Result;
            }
            catch (Exception)
            {
                return;   
            }

            ApplyInsights(insights);
            ApplyQueue(stats);
            ApplyCommands(commands);

            if (device is not null && ReferenceEquals(device, _selected))
            {
                _lastReadings = readings;
                DrawChart();
            }
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task LoadHistoryAsync()
    {
        var device = _selected;
        if (device is null) return;

        try
        {
            var readings = await _state.Api.GetHistoryAsync(device.MacAddress, 60);
            if (ReferenceEquals(device, _selected))
            {
                _lastReadings = readings;
                DrawChart();
            }
        }
        catch (Exception) { }
    }

    private void ApplyInsights(InsightsDto dto)
    {
        SyncItems(_suggestions, dto.SuggestedActions, ref _suggestionSig);
        SyncItems(_insights, dto.Insights, ref _insightSig);

        NoSuggestionsText.Visibility = _suggestions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoInsightsText.Visibility = _insights.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        LearnedText.Text = $"Learned from {dto.EventsLearned} events" +
                           (dto.LastUserEvent is null ? "" : $"   |   your last action: {dto.LastUserEvent}");
    }

    private static void SyncItems(ObservableCollection<SuggestionItem> target, List<SuggestionDto> source, ref string signature)
    {
        var sig = string.Join("||", source.Select(s => $"{s.Key}|{s.Title}|{s.Confidence:0.00}"));

        if (sig == signature)
        {
            
            for (int i = 0; i < source.Count && i < target.Count; i++)
                target[i].Reason = source[i].Reason;
            return;
        }

        signature = sig;
        target.Clear();
        foreach (var s in source) target.Add(new SuggestionItem(s));
    }

    private void ApplyQueue(QueueStatsDto s)
    {
        QueueText.Text =
            $"Standard queue: {s.StandardDepth}    Priority queue: {s.PriorityDepth}\n" +
            $"Ingested: {s.TotalIngested}   Processed: {s.ProcessedStandard} standard / {s.ProcessedPriority} priority";

        var lines = s.RecentlyProcessed
            .Select(p => $"#{p.Order,-5}{p.Lane,-9}{p.DeviceName,-20}{p.Value:0.#}")
            .ToList();

        SyncStrings(_processed, lines);
    }

    private void ApplyCommands(CommandHistoryDto dto)
    {
        _undoDepth = dto.UndoDepth;
        UndoButton.IsEnabled = dto.UndoDepth > 0;
        UndoInfo.Text = dto.NextUndo is null
            ? "Nothing to undo."
            : $"Next undo: {dto.NextUndo}  (stack depth {dto.UndoDepth})";

        var lines = dto.History
            .Select(c => $"{c.IssuedUtc.ToLocalTime():HH:mm:ss}  {c.Summary}")
            .ToList();

        SyncStrings(_history, lines);
        UpdateSummary();
    }

    private static void SyncStrings(ObservableCollection<string> target, List<string> lines)
    {
        if (target.SequenceEqual(lines)) return;  

        target.Clear();
        foreach (var line in lines) target.Add(line);
    }

   

    private async Task RunCommandAsync(string mac, string action)
    {
        ApiResult result;
        try
        {
            result = await _state.Api.SendCommandAsync(mac, action);
        }
        catch (Exception)
        {
            ShowCommandStatus("Cannot reach the API.", false);
            return;
        }

        ShowCommandStatus(result.Message, result.Ok);
        await _state.RefreshAsync();
        await PollAsync();
    }

    private void ShowCommandStatus(string message, bool ok)
    {
        CommandStatus.Text = message;
        CommandStatus.Foreground = (Brush)FindResource(ok ? "OkBrush" : "DangerBrush");
    }

    private async void TurnOn_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is { IsActuator: true } device) await RunCommandAsync(device.MacAddress, "TurnOn");
    }

    private async void TurnOff_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is { IsActuator: true } device) await RunCommandAsync(device.MacAddress, "TurnOff");
    }

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is { IsActuator: true } device) await RunCommandAsync(device.MacAddress, "Toggle");
    }

    private async void AllOff_Click(object sender, RoutedEventArgs e) =>
        await RunCommandAsync("ALL", "TurnOff");

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        ApiResult result;
        try
        {
            result = await _state.Api.UndoAsync();
        }
        catch (Exception)
        {
            ShowCommandStatus("Cannot reach the API.", false);
            return;
        }

        ShowCommandStatus(result.Message, result.Ok);
        await _state.RefreshAsync();
        await PollAsync();
    }

    private async void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SuggestionItem item || item.Mac is null) return;

        if (item.Kind == "Command" && item.Action is not null)
            await RunCommandAsync(item.Mac, item.Action);
        else
            SelectDevice(item.Mac);
    }

    private void Insight_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is SuggestionItem { Mac: not null } item)
            SelectDevice(item.Mac);
    }

    

    private async void SimulateCritical_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selected.IsActuator)
        {
            ToolStatus.Text = "Select a sensor first (not a pump or relay).";
            return;
        }

        bool ok = await _state.Simulator.InjectCriticalAsync(_selected.MacAddress);
        ToolStatus.Text = ok
            ? $"Critical reading for {_selected.Name} sent through the API. It is routed to the PRIORITY queue. Watch the alert, the queue monitor and the suggestions."
            : "That device cannot produce a critical reading.";
    }

    private void Drift_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selected.IsActuator)
        {
            ToolStatus.Text = "Select a sensor first (not a pump or relay).";
            return;
        }

        bool ok = _state.Simulator.StartDrift(_selected.MacAddress);
        ToolStatus.Text = ok
            ? $"{_selected.Name} is now deteriorating steadily. The insights panel should warn about the trend before it turns critical."
            : "That device cannot drift.";
    }

    private void Silence_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            ToolStatus.Text = "Select a device first.";
            return;
        }

        bool silence = !_state.Simulator.IsSilenced(_selected.MacAddress);
        _state.Simulator.SetSilenced(_selected.MacAddress, silence);
        UpdateSelectedPanel();

        ToolStatus.Text = silence
            ? $"{_selected.Name} stopped sending. After about 15 seconds it is marked OFFLINE and ONE disconnect alert is raised (HashSet)."
            : $"{_selected.Name} is sending again and the disconnect alert will clear.";
    }

    private async void QueueDemo_Click(object sender, RoutedEventArgs e)
    {
        var target = _state.Devices.FirstOrDefault(d => d.Metric == "Moisture");
        if (target is null)
        {
            ToolStatus.Text = "No moisture sensor is available for the demo.";
            return;
        }

        QueueDemoBtn.IsEnabled = false;
        try
        {
            ToolStatus.Text = "Queue demo: processor slowed down, 60 normal packets queued, then ONE critical packet queued last...";
            await _state.Api.SetQueueDelayAsync(100);

            var result = await _state.Api.DemoBurstAsync(target.MacAddress, 60);
            ToolStatus.Text = result.Ok
                ? "Watch the queues: the standard queue drains slowly, but the PRIORITY packet (queued last) was processed first."
                : result.Message;

            await Task.Delay(8000);
            await _state.Api.SetQueueDelayAsync(0);   
        }
        catch (Exception)
        {
            ToolStatus.Text = "Cannot reach the API.";
        }
        finally
        {
            QueueDemoBtn.IsEnabled = true;
        }
    }

    private void RandomAnomalies_Click(object sender, RoutedEventArgs e)
    {
        _state.Simulator.RandomAnomalies = RandomAnomaliesBox.IsChecked == true;
    }

    private static async Task SafeAsync(Func<Task> work)
    {
        try { await work(); }
        catch (Exception) { }
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private void DrawChart()
    {
        ChartCanvas.Children.Clear();

        double w = ChartCanvas.ActualWidth;
        double h = ChartCanvas.ActualHeight;
        if (w < 80 || h < 60) return;

        var accent = (Brush)FindResource("AccentBrush");
        var muted = (Brush)FindResource("MutedBrush");
        var grid = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));

        if (_selected is null || _lastReadings.Count < 2)
        {
            AddLabel(_selected is null ? "Select a device to see its timeline" : "Waiting for readings...",
                     w / 2 - 90, h / 2 - 8, muted);
            return;
        }

        const double left = 48, right = 14, top = 8, bottom = 22;
        double plotW = w - left - right;
        double plotH = h - top - bottom;

        double min = _lastReadings.Min(r => r.Value);
        double max = _lastReadings.Max(r => r.Value);
        if (Math.Abs(max - min) < 0.0001) { min -= 1; max += 1; }
        double pad = (max - min) * 0.12;
        min -= pad;
        max += pad;

        for (int i = 0; i <= 4; i++)
        {
            double y = top + plotH * i / 4;
            ChartCanvas.Children.Add(new Line
            {
                X1 = left,
                X2 = left + plotW,
                Y1 = y,
                Y2 = y,
                Stroke = grid,
                StrokeThickness = 1
            });

            double value = max - (max - min) * i / 4;
            AddLabel($"{value:0.#}", 4, y - 8, muted);
        }

        var t0 = _lastReadings[0].Timestamp;
        var t1 = _lastReadings[^1].Timestamp;
        double span = Math.Max((t1 - t0).TotalSeconds, 1);

        var line = new Polyline { Stroke = accent, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
        Point last = new();

        foreach (var r in _lastReadings)
        {
            double x = left + plotW * (r.Timestamp - t0).TotalSeconds / span;
            double y = top + plotH * (max - r.Value) / (max - min);
            last = new Point(x, y);
            line.Points.Add(last);
        }
        ChartCanvas.Children.Add(line);

        var dot = new Ellipse { Width = 9, Height = 9, Fill = accent };
        Canvas.SetLeft(dot, last.X - 4.5);
        Canvas.SetTop(dot, last.Y - 4.5);
        ChartCanvas.Children.Add(dot);

        AddLabel(t0.ToLocalTime().ToString("HH:mm:ss"), left, h - 18, muted);
        AddLabel(t1.ToLocalTime().ToString("HH:mm:ss"), left + plotW - 48, h - 18, muted);
    }

    private void AddLabel(string text, double x, double y, Brush brush)
    {
        var label = new TextBlock { Text = text, FontSize = 10, Foreground = brush };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        ChartCanvas.Children.Add(label);
    }
}