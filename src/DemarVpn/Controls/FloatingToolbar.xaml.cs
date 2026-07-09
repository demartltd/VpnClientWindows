using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DemarVpn.Controls;

public sealed partial class FloatingToolbar : UserControl
{
  public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

  private readonly ToggleButton[] _tabs;

  public FloatingToolbar()
  {
    InitializeComponent();
    _tabs = [Tab0, Tab1, Tab2, Tab3];
    var style = BuildToggleStyle();
    foreach (var tab in _tabs) tab.Style = style;
    Select(0);
  }

  public int SelectedIndex
  {
    get
    {
      for (var i = 0; i < _tabs.Length; i++)
        if (_tabs[i].IsChecked == true) return i;
      return 0;
    }
  }

  private void OnTabClick(object sender, RoutedEventArgs e)
  {
    if (sender is ToggleButton btn && int.TryParse(btn.Tag?.ToString(), out var idx))
    {
      Select(idx);
      SelectionChanged?.Invoke(this, new SelectionChangedEventArgs([], [btn]));
    }
  }

  private void Select(int index)
  {
    for (var i = 0; i < _tabs.Length; i++)
      _tabs[i].IsChecked = i == index;
  }

  private static Style BuildToggleStyle()
  {
    var style = new Style { TargetType = typeof(ToggleButton) };
    style.Setters.Add(new Setter(Control.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
    style.Setters.Add(new Setter(Control.VerticalAlignmentProperty, VerticalAlignment.Stretch));
    style.Setters.Add(new Setter(Control.BackgroundProperty, new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent)));
    style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
    return style;
  }
}
