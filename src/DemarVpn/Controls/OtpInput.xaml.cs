using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DemarVpn.Controls;

public sealed partial class OtpInput : UserControl
{
    public event EventHandler? Completed;

    public string Code => Input.Text;

    private const int Length = 6;
    private readonly Border[] _cells = new Border[Length];
    private readonly TextBlock[] _digits = new TextBlock[Length];
    private bool _suppressChange;

    public OtpInput()
    {
        InitializeComponent();
        BuildCells();
        Input.TextChanged += (_, _) => OnTextChanged();
    }

    public void Reset()
    {
        _suppressChange = true;
        Input.Text = string.Empty;
        _suppressChange = false;
        RefreshCells();
    }

    private void BuildCells()
    {
        var res = Application.Current.Resources;
        var bg = (Brush)res["DemarSurfaceHighBrush"];
        for (var i = 0; i < Length; i++)
        {
            var digit = new TextBlock
            {
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var cell = new Border
            {
                Width = 44,
                Height = 52,
                CornerRadius = new CornerRadius(8),
                Background = bg,
                BorderThickness = new Thickness(1),
                Child = digit,
            };
            _cells[i] = cell;
            _digits[i] = digit;
            Cells.Children.Add(cell);
        }
        RefreshCells();
    }

    private void OnTextChanged()
    {
        if (_suppressChange) return;
        var filtered = new string(Input.Text.Where(char.IsDigit).Take(Length).ToArray());
        if (filtered != Input.Text)
        {
            _suppressChange = true;
            Input.Text = filtered;
            Input.SelectionStart = filtered.Length;
            _suppressChange = false;
        }
        RefreshCells();
        if (filtered.Length == Length)
            Completed?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshCells()
    {
        var code = Input.Text;
        var focused = Input.FocusState != FocusState.Unfocused;
        var res = Application.Current.Resources;
        var primary = (Brush)res["DemarPrimaryBrush"];
        var outlineVariant = (Brush)res["DemarOutlineVariantBrush"];
        for (var i = 0; i < Length; i++)
        {
            _digits[i].Text = i < code.Length ? code[i].ToString() : string.Empty;
            var active = focused && i == code.Length;
            _cells[i].BorderBrush = active ? primary : outlineVariant;
        }
    }

    private void OnInputFocus(object sender, RoutedEventArgs e) => RefreshCells();
    private void OnInputBlur(object sender, RoutedEventArgs e) => RefreshCells();
    private void OnCellsPressed(object sender, PointerRoutedEventArgs e) =>
        Input.Focus(FocusState.Pointer);
}
