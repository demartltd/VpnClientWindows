using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
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
  private bool _scrubbing;

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
      NotifySelect(idx);
  }

  private void OnMainPointer(object sender, PointerRoutedEventArgs e)
  {
    var pt = e.GetCurrentPoint(MainShell);
    if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
        && !pt.Properties.IsLeftButtonPressed)
      return;

    if (!_scrubbing)
    {
      _scrubbing = true;
      MainShell.CapturePointer(e.Pointer);
      MainShell.PointerReleased += OnReleased;
      MainShell.PointerCaptureLost += OnReleased;
    }
    PickMain(pt.Position.X);
    e.Handled = true;
  }

  private void OnReleased(object sender, PointerRoutedEventArgs e)
  {
    _scrubbing = false;
    MainShell.ReleasePointerCapture(e.Pointer);
    MainShell.PointerReleased -= OnReleased;
    MainShell.PointerCaptureLost -= OnReleased;
  }

  private void PickMain(double x)
  {
    if (MainShell.ActualWidth <= 0) return;
    var index = Math.Clamp((int)((x / MainShell.ActualWidth) * 3), 0, 2);
    if (index == _selectedIndex) return;
    NotifySelect(index);
  }

  private void NotifySelect(int index)
  {
    Select(index);
    SelectionChanged?.Invoke(this, new SelectionChangedEventArgs([], []));
  }

  public void Select(int index)
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
