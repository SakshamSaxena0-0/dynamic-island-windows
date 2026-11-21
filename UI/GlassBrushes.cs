using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.UI;

public static class GlassBrushes
{
    public static readonly Color Bone = Color.FromRgb(0xEC, 0xEC, 0xE8);
    public static readonly Color Danger = Color.FromRgb(0xFF, 0x6B, 0x7A);

    public static readonly Brush Solid = Vertical((Colors.White, 0), (Bone, 0.48), (Color.FromRgb(0xBD, 0xBD, 0xB8), 1));

    public static readonly Brush Sheen = Vertical((White(.13), 0), (White(.04), 0.5), (White(.07), 1));

    public static readonly Brush Remaining = Vertical((White(.40), 0), (White(.18), 1));

    public static readonly Brush Steel = Freeze(new LinearGradientBrush(new GradientStopCollection
    {
        new(Rgb(0xCACAC6), 0), new(Rgb(0x8E8E8A), .06), new(Rgb(0xF2F2EE), .125), new(Rgb(0x7E7E7A), .19),
        new(Rgb(0xA2A29E), .25), new(Rgb(0xDCDCD8), .32), new(Rgb(0xFFFFFF), .375), new(Rgb(0xD0D0CC), .43),
        new(Rgb(0x82827E), .50), new(Rgb(0x72726E), .57), new(Rgb(0xE6E6E2), .625), new(Rgb(0x7A7A76), .69),
        new(Rgb(0xAAAAA6), .75), new(Rgb(0xE0E0DC), .82), new(Rgb(0xFFFFFF), .875), new(Rgb(0xE6E6E2), .94),
        new(Rgb(0xCACAC6), 1),
    }, new Point(0, 0), new Point(1, 1)));

    public static Brush Fill(Color baseColor, double opacity) => Vertical(
        (ColorUtil.WithAlpha(ColorUtil.Blend(baseColor, Colors.White, .13), opacity), 0),
        (ColorUtil.WithAlpha(ColorUtil.Blend(baseColor, Colors.White, .04), opacity), 0.5),
        (ColorUtil.WithAlpha(ColorUtil.Blend(baseColor, Colors.White, .07), opacity), 1));

    public static Brush Rim(Color color, double strength) => Freeze(new LinearGradientBrush(new GradientStopCollection
    {
        new(ColorUtil.WithAlpha(color, 1.00 * strength), 0), new(ColorUtil.WithAlpha(color, .45 * strength), .14),
        new(ColorUtil.WithAlpha(color, .06 * strength), .36), new(ColorUtil.WithAlpha(color, .06 * strength), .64),
        new(ColorUtil.WithAlpha(color, .40 * strength), .86), new(ColorUtil.WithAlpha(color, .85 * strength), 1),
    }, new Point(0, 0), new Point(1, 1)));

    public static Brush Done(Color bone) => Freeze(new LinearGradientBrush(new GradientStopCollection
    {
        new(ColorUtil.Blend(bone, Colors.Black, .35), 0), new(bone, .6), new(ColorUtil.Blend(bone, Colors.White, .45), 1),
    }, new Point(0, 0), new Point(1, 0)));

    private static Color White(double alpha) => ColorUtil.WithAlpha(Colors.White, alpha);

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static Brush Vertical(params (Color Color, double Offset)[] stops)
    {
        var collection = new GradientStopCollection();
        foreach (var (color, offset) in stops) collection.Add(new GradientStop(color, offset));
        return Freeze(new LinearGradientBrush(collection, new Point(0, 0), new Point(0, 1)));
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
