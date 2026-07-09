using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace DemarVpn.Controls;

public sealed partial class FloatingToolbar : UserControl
{
  public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

  private readonly ToggleButton[] _tabs;
  private readonly Brush _activeIconBrush;
  private readonly Brush _idleIconBrush;
  private readonly double _tabWidth;
  private int _selectedIndex;
  private bool _wide;

  public FloatingToolbar()
  {
    InitializeComponent();
    _tabs = [Tab0, Tab1, Tab2, Tab3];
    var res = Application.Current.Resources;
    _activeIconBrush = (Brush)res["DemarOnPrimaryContainerBrush"];
    _idleIconBrush = (Brush)res["DemarSecondaryBrush"];
    _tabWidth = (double)res["ToolbarTabWidth"];
    Select(0);
  }

  public int SelectedIndex => _selectedIndex;

  public void SetWideLayout(bool wide)
  {
    if (_wide == wide) return;
    _wide = wide;
    LayoutRoot.ColumnDefinitions.Clear();
    if (wide)
    {
      LayoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      LayoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      LayoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      Grid.SetColumn(MainShell, 0);
      Grid.SetColumn(AccountShell, 2);
      LayoutRoot.ColumnSpacing = 0;
      MainShell.HorizontalAlignment = HorizontalAlignment.Left;
    }
    else
    {
      LayoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      LayoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
      Grid.SetColumn(MainShell, 0);
      Grid.SetColumn(AccountShell, 1);
      LayoutRoot.ColumnSpacing = 12;
      MainShell.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    var tabColumn = wide ? new GridLength(_tabWidth) : new GridLength(1, GridUnitType.Star);
    foreach (var col in MainTabGrid.ColumnDefinitions)
      col.Width = tabColumn;
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
    _selectedIndex = index;
    for (var i = 0; i < _tabs.Length; i++)
    {
      _tabs[i].IsChecked = i == index;
      _tabs[i].Foreground = i == index ? _activeIconBrush : _idleIconBrush;
    }

    MainIndicator.Visibility = index < 3 ? Visibility.Visible : Visibility.Collapsed;
    if (index < 3) Grid.SetColumn(MainIndicator, index);

    if (index == 3)
      AccountShell.Background = (Brush)Application.Current.Resources["DemarPrimaryContainerBrush"];
    else
      AccountShell.ClearValue(Border.BackgroundProperty);
  }
}
