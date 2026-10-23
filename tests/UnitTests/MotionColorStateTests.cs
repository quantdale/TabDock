using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TabDock.Infrastructure;
using Xunit;

namespace TabDock.UnitTests;

[Collection("MotionFeedbackLifecycle")]
public sealed class MotionColorStateTests
{
    private static readonly Color BaseFill = Color.FromRgb(0x20, 0x20, 0x20);
    private static readonly Color BaseEdge = Color.FromRgb(0x50, 0x50, 0x50);
    private static readonly Color SelectedFill = Color.FromRgb(0x28, 0x64, 0xB4);
    private static readonly Color SelectedEdge = Colors.Red;
    private static readonly Color HoverFill = Color.FromRgb(0x22, 0x2A, 0x36);
    private static readonly Color FocusEdge = Color.FromRgb(0x9C, 0xB8, 0xFF);
    private static readonly Color UpdatedBaseFill = Color.FromRgb(0x11, 0x15, 0x1C);
    private static readonly Color UpdatedBaseEdge = Color.FromRgb(0x39, 0x42, 0x52);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectionRoundTrip_RestoresAuthoritativeBaseColors(bool reducedMotion)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using (MotionProfile.OverrideReducedMotion(reducedMotion))
            using (HiddenVisualHost host = HiddenVisualHost.Create())
            {
                (ListViewItem item, Border border) = HostRow(host);
                item.IsSelected = true;
                SettleColor(dispatcher, border, SelectedFill, SelectedEdge);
                item.IsSelected = false;
                SettleColor(dispatcher, border, BaseFill, BaseEdge);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HoverAndInterruptedSelection_ReturnToBaseTokens(bool reducedMotion)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using (MotionProfile.OverrideReducedMotion(reducedMotion))
            using (HiddenVisualHost host = HiddenVisualHost.Create())
            {
                (ListViewItem item, Border border) = HostRow(host);
                RaiseMouse(border, Mouse.MouseEnterEvent);
                SettleColor(dispatcher, border, HoverFill, BaseEdge);
                RaiseMouse(border, Mouse.MouseLeaveEvent);
                SettleColor(dispatcher, border, BaseFill, BaseEdge);

                item.IsSelected = true;
                if (!reducedMotion)
                    StaDispatcherFixture.Pump(dispatcher, TimeSpan.FromMilliseconds(40));
                item.IsSelected = false;
                SettleColor(dispatcher, border, BaseFill, BaseEdge);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FocusExit_PreservesSelectedFillAndBorder(bool reducedMotion)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using (MotionProfile.OverrideReducedMotion(reducedMotion))
            using (HiddenVisualHost host = HiddenVisualHost.Create())
            {
                (ListViewItem item, Border border) = HostRow(host);
                var other = new Button { Width = 40, Height = 20, Content = "other" };
                host.Root.Children.Add(other);
                host.Realize();
                item.IsSelected = true;
                SettleColor(dispatcher, border, SelectedFill, SelectedEdge);
                Assert.True(item.Focus(), "row did not accept keyboard focus");
                SettleColor(dispatcher, border, SelectedFill, FocusEdge);
                Assert.True(other.Focus(), "focus did not leave the selected row");
                SettleColor(dispatcher, border, SelectedFill, SelectedEdge);
            }
        });
    }

    [Fact]
    public void BaseTokenUpdate_ChangesTheRestoreTargetWithoutSamplingAnimation()
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using (MotionProfile.OverrideReducedMotion(true))
            using (HiddenVisualHost host = HiddenVisualHost.Create())
            {
                (ListViewItem item, Border border) = HostRow(host);
                item.IsSelected = true;
                SettleColor(dispatcher, border, SelectedFill, SelectedEdge);
                Assert.True(MotionProfile.TryUpdateBorderBaseTokens(border, UpdatedBaseFill, UpdatedBaseEdge));
                SettleColor(dispatcher, border, SelectedFill, SelectedEdge);
                item.IsSelected = false;
                SettleColor(dispatcher, border, UpdatedBaseFill, UpdatedBaseEdge);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reload_PreservesBaseTokensAcrossAnotherRoundTrip(bool reducedMotion)
    {
        StaDispatcherFixture.Run(dispatcher =>
        {
            using (MotionProfile.OverrideReducedMotion(reducedMotion))
            using (HiddenVisualHost host = HiddenVisualHost.Create())
            {
                (ListViewItem item, Border border) = HostRow(host);
                item.IsSelected = true;
                SettleColor(dispatcher, border, SelectedFill, SelectedEdge);
                item.IsSelected = false;
                SettleColor(dispatcher, border, BaseFill, BaseEdge);
                host.Root.Children.Remove(item);
                host.Realize();
                host.Root.Children.Add(item);
                host.Realize();
                item.IsSelected = true;
                SettleColor(dispatcher, border, SelectedFill, SelectedEdge);
                item.IsSelected = false;
                SettleColor(dispatcher, border, BaseFill, BaseEdge);
            }
        });
    }

    private static (ListViewItem Item, Border Border) HostRow(HiddenVisualHost host)
    {
        var item = new ListViewItem
        {
            Width = 180,
            Height = 40,
            Focusable = true,
        };
        var border = new Border
        {
            Width = 160,
            Height = 28,
            Background = new SolidColorBrush(BaseFill),
            BorderBrush = new SolidColorBrush(BaseEdge),
            BorderThickness = new Thickness(1),
            Focusable = true,
        };
        MotionProfile.SetHoverBackground(border, new SolidColorBrush(HoverFill));
        MotionProfile.SetSelectedBackground(border, new SolidColorBrush(SelectedFill));
        MotionProfile.SetSelectedBorder(border, new SolidColorBrush(SelectedEdge));
        MotionProfile.SetFocusBorder(border, new SolidColorBrush(FocusEdge));
        MotionProfile.SetEnableBorderStates(border, true);
        item.Content = border;
        host.Root.Children.Add(item);
        host.Realize();
        return (item, border);
    }

    private static void RaiseMouse(IInputElement element, RoutedEvent routedEvent)
    {
        Assert.NotNull(Mouse.PrimaryDevice);
        element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = routedEvent });
    }

    private static void SettleColor(Dispatcher dispatcher, Border border, Color fill, Color edge)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            StaDispatcherFixture.Settle(dispatcher);
            if (Same(Fill(border), fill) && Same(Edge(border), edge))
                return;
            StaDispatcherFixture.Pump(dispatcher, TimeSpan.FromMilliseconds(40));
        }
        Assert.Equal(Channel(fill), Channel(Fill(border)));
        Assert.Equal(Channel(edge), Channel(Edge(border)));
    }

    private static Color Fill(Border border) => ((SolidColorBrush)border.Background).Color;
    private static Color Edge(Border border) => ((SolidColorBrush)border.BorderBrush).Color;
    private static bool Same(Color left, Color right) => Channel(left) == Channel(right);
    private static string Channel(Color color) => $"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
