using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DynamicIsland.UI;

public partial class ColorField : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(ColorField),
        new FrameworkPropertyMetadata("#000000", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((ColorField)d).ShowValue()));

    private static readonly string[] PaletteColors =
    {
        "#000000", "#1C1C1E", "#3A3A3C", "#8E8E93", "#F2F2F7", "#FFFFFF",
        "#FF453A", "#FF9F0A", "#FFD60A", "#30D158", "#64D2FF", "#0A84FF",
        "#5E5CE6", "#BF5AF2", "#FF2BD6", "#FF8A50", "#0B1022", "#0C1A12",
    };

    private static int[] _customColors = Array.Empty<int>();

    public ColorField()
    {
        InitializeComponent();
        Palette.ItemsSource = PaletteColors;
        ShowValue();
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private void ShowValue()
    {
        Color color = ColorUtil.Parse(Value, Colors.Black);
        SwatchButton.Background = ColorUtil.Brush(color);
        if (!HexBox.IsKeyboardFocused) HexBox.Text = ColorUtil.ToHex(color);
    }

    private void Commit()
    {
        if (ColorUtil.TryParse(HexBox.Text, out Color color)) Value = ColorUtil.ToHex(color);
        HexBox.Text = ColorUtil.ToHex(ColorUtil.Parse(Value, Colors.Black));
    }

    private void HexBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Commit();
        HexBox.SelectAll();
        e.Handled = true;
    }

    private void Swatch_Click(object sender, RoutedEventArgs e) => PalettePopup.IsOpen = !PalettePopup.IsOpen;

    private void PaletteColor_Click(object sender, RoutedEventArgs e)
    {
        Value = (string)((FrameworkElement)sender).Tag;
        PalettePopup.IsOpen = false;
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        PalettePopup.IsOpen = false;
        Color current = ColorUtil.Parse(Value, Colors.Black);
        using var dialog = new Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = Drawing.Color.FromArgb(current.R, current.G, current.B),
            CustomColors = _customColors,
        };
        IntPtr owner = new WindowInteropHelper(Window.GetWindow(this)).Handle;
        if (dialog.ShowDialog(new OwnerWindow(owner)) != Forms.DialogResult.OK) return;
        _customColors = dialog.CustomColors;
        Value = ColorUtil.ToHex(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
    }

    private sealed class OwnerWindow : Forms.IWin32Window
    {
        public OwnerWindow(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }
}
