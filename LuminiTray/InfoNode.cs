using System.ComponentModel;

namespace LuminiTray;

/// <summary>Nó da árvore de informações do sistema.</summary>
public class InfoNode : INotifyPropertyChanged
{
    public string Header { get; set; } = "";
    public string Value { get; set; } = "";
    public List<InfoNode> Children { get; } = new();

    public string MaskedValue => PrivacyModeService.MaskIfSensitiveField(Header, Value);
    public string Display => string.IsNullOrEmpty(Value) ? Header : $"{Header}: {MaskedValue}";
    public bool IsGroup => Children.Count > 0;

    public InfoNode Add(string header, string value = "")
    {
        var n = new InfoNode { Header = header, Value = value };
        Children.Add(n);
        return n;
    }

    public void NotifyPrivacyChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaskedValue)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
        foreach (var child in Children)
            child.NotifyPrivacyChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
