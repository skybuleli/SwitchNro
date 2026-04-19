using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Animation;
using Avalonia.Styling;
using Avalonia;

namespace SwitchNro.UI.Helpers;

/// <summary>
/// 竹风美学 UI 辅助工具
/// 用代码动态应用 Glassmorphism 样式
/// </summary>
public static class BambooUIHelper
{
 // 竹风色彩系统
 public static Color BambooPrimary => Color.Parse("#2D8C5A");
 public static Color BambooDark => Color.Parse("#0D3B25");
 public static Color BambooMid => Color.Parse("#1B5E38");
 public static Color JadeAccent => Color.Parse("#4A90E2");
 public static Color GlowColor => Color.Parse("#81C784");
 public static Color GlassSurface => Color.Parse("#14FFFFFF");
 public static Color GlassBorder => Color.Parse("#40FFFFFF");

 /// <summary>应用玻璃拟态边框效果</summary>
 public static void ApplyGlassBorder(this Border border, double radius = 12)
 {
 border.Background = new SolidColorBrush(GlassSurface);
 border.BorderBrush = new SolidColorBrush(GlassBorder);
 border.BorderThickness = new Thickness(1);
 border.CornerRadius = new CornerRadius(radius);
 border.AddShadow(GlowColor, 8);
 }

 /// <summary>添加发光阴影</summary>
 public static void AddShadow(this Control control, Color color, double blur)
 {
 // Avalonia 11.x shadow API
 if (control is Border border)
 {
 border.BoxShadow = new BoxShadows(new BoxShadow
 {
 OffsetX = 0,
 OffsetY = blur,
 Blur = blur * 2,
 Color = new Color(0x30, color.R, color.G, color.B)
 });
 }
 }

 /// <summary>应用竹绿色渐变背景</summary>
 public static void ApplyBambooGradient(this Border border)
 {
 var brush = new LinearGradientBrush
 {
 StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
 EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative)
 };
 brush.GradientStops.Add(new GradientStop(BambooDark, 0));
 brush.GradientStops.Add(new GradientStop(BambooPrimary, 0.5));
 brush.GradientStops.Add(new GradientStop(BambooDark, 1));
 border.Background = brush;
 }

 /// <summary>创建呼吸动画</summary>
 public static void ApplyBreathingGlow(this Control control)
 {
 var animation = new Animation
 {
 Duration = TimeSpan.FromSeconds(4),
 IterationCount = IterationCount.Infinite,
 Children =
 {
 new KeyFrame
 {
 Cue = Cue.Parse("0%", CultureInfo.InvariantCulture),
 Setters = { new Setter(Visual.OpacityProperty, 0.6) }
 },
 new KeyFrame
 {
 Cue = Cue.Parse("50%", CultureInfo.InvariantCulture),
 Setters = { new Setter(Visual.OpacityProperty, 1.0) }
 },
 new KeyFrame
 {
 Cue = Cue.Parse("100%", CultureInfo.InvariantCulture),
 Setters = { new Setter(Visual.OpacityProperty, 0.6) }
 }
 }
 };
 animation.RunAsync(control);
 }

 /// <summary>应用发光按钮样式</summary>
 public static void ApplyGlowButton(this Button button)
 {
 button.CornerRadius = new CornerRadius(8);
 button.Padding = new Thickness(16, 8);
 button.Background = new SolidColorBrush(Color.Parse("#202D8C5A"));
 button.BorderBrush = new SolidColorBrush(Color.Parse("#402D8C5A"));
 button.BorderThickness = new Thickness(1);
 }

 /// <summary>应用竹风(并返回色彩刷)</summary>
 public static void ApplyBambooText(this TextBlock text, bool isPrimary = true)
 {
 text.Foreground = new SolidColorBrush(isPrimary
 ? Color.Parse("#E8F5E9")
 : Color.Parse("#A5D6A7"));
 }
}
