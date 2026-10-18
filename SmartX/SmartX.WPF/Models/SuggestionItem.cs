using System.ComponentModel;
using System.Windows.Media;

namespace SmartX.WPF.Models;

public class SuggestionItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _reason;

    public SuggestionItem(SuggestionDto dto)
    {
        Key = dto.Key;
        Kind = dto.Kind;
        Title = dto.Title;
        _reason = dto.Reason;
        Confidence = dto.Confidence;
        Severity = dto.Severity;
        Mac = dto.Mac;
        Action = dto.Action;
    }

    public string Key { get; }
    public string Kind { get; }
    public string Title { get; }
    public double Confidence { get; }
    public string Severity { get; }
    public string? Mac { get; }
    public string? Action { get; }

    public string Reason
    {
        get => _reason;
        set
        {
            if (_reason == value) return;
            _reason = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Reason)));
        }
    }

    public string ConfidenceText => $"{Confidence * 100:0}% confidence";

    public string ButtonText => Kind == "Command" ? "Apply" : "Inspect";

    public Brush SeverityBrush => Severity switch
    {
        "critical" => Brushes.Crimson,
        "warning" => Brushes.DarkOrange,
        _ => Brushes.SteelBlue
    };
}