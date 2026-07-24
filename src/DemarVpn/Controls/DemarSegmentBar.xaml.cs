using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DemarVpn.Controls;

public readonly record struct SegmentOption(string Id, string Title);

public sealed partial class DemarSegmentBar : UserControl
{
    private IReadOnlyList<SegmentOption> _options = [];
    private string _selectedId = "";
    private bool _bold;
    private bool _scrubbing;

    public event EventHandler<string>? SelectionChanged;

    public DemarSegmentBar() => InitializeComponent();

    public bool Bold
    {
        get => _bold;
        set { _bold = value; Rebuild(); }
    }

    public string SelectedId
    {
        get => _selectedId;
        set
        {
            if (_selectedId == value) return;
            _selectedId = value;
            ApplySelection();
        }
    }

    public void SetOptions(IReadOnlyList<SegmentOption> options, string selectedId)
    {
        _options = options;
        _selectedId = selectedId;
        Rebuild();
    }

    private void Rebuild()
    {
        Segments.Children.Clear();
        Segments.ColumnDefinitions.Clear();
        for (var i = 0; i < _options.Count; i++)
        {
            Segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var opt = _options[i];
            var cell = new Border
            {
                Background = new SolidColorBrush(Colors.Transparent),
                Child = new TextBlock
                {
                    Text = opt.Title,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = _bold ? 14 : 13,
                    TextAlignment = TextAlignment.Center,
                },
            };
            Grid.SetColumn(cell, i);
            Segments.Children.Add(cell);
        }
        ApplySelection();
    }

    private void ApplySelection()
    {
        var res = Application.Current.Resources;
        var primary = ((SolidColorBrush)res["DemarSeedBrush"]).Color;
        var onSel = (Brush)res["DemarOnPrimaryContainerBrush"];
        var onNorm = (Brush)res["DemarSecondaryBrush"];
        for (var i = 0; i < Segments.Children.Count; i++)
        {
            if (Segments.Children[i] is not Border cell || cell.Child is not TextBlock label) continue;
            var selected = i < _options.Count && _options[i].Id == _selectedId;
            cell.Background = selected
                ? new SolidColorBrush(Color.FromArgb(46, primary.R, primary.G, primary.B))
                : new SolidColorBrush(Colors.Transparent);
            label.FontWeight = selected
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Medium;
            label.Foreground = selected ? onSel : onNorm;
        }
    }

    private void OnPointer(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(Root);
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
            && !pt.Properties.IsLeftButtonPressed)
            return;

        if (!_scrubbing)
        {
            _scrubbing = true;
            Root.CapturePointer(e.Pointer);
            Root.PointerReleased += OnReleased;
            Root.PointerCaptureLost += OnReleased;
        }
        Pick(pt.Position.X);
        e.Handled = true;
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        _scrubbing = false;
        Root.ReleasePointerCapture(e.Pointer);
        Root.PointerReleased -= OnReleased;
        Root.PointerCaptureLost -= OnReleased;
    }

    private void Pick(double x)
    {
        if (_options.Count == 0 || Root.ActualWidth <= 0) return;
        var index = Math.Clamp((int)((x / Root.ActualWidth) * _options.Count), 0, _options.Count - 1);
        var id = _options[index].Id;
        if (id == _selectedId) return;
        _selectedId = id;
        ApplySelection();
        SelectionChanged?.Invoke(this, id);
    }
}
