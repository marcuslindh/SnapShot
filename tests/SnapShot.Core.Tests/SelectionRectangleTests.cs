using SnapShot.Core;
using Xunit;

namespace SnapShot.Core.Tests;

public sealed class SelectionRectangleTests
{
    private const int MonitorWidth = 1920;
    private const int MonitorHeight = 1080;

    [Theory]
    [InlineData(100, 200, 300, 260)]
    [InlineData(300, 260, 100, 200)]
    [InlineData(300, 200, 100, 260)]
    [InlineData(100, 260, 300, 200)]
    public void FromDrag_AnyDirection_GivesSameRectangle(int startLeft, int startTop, int currentLeft, int currentTop)
    {
        SelectionRectangle selection = SelectionRectangle.FromDrag(startLeft, startTop, currentLeft, currentTop, MonitorWidth, MonitorHeight);

        Assert.Equal(new SelectionRectangle(Left: 100, Top: 200, Width: 200, Height: 60), selection);
    }

    [Fact]
    public void FromDrag_OutsideMonitor_IsClipped()
    {
        SelectionRectangle selection = SelectionRectangle.FromDrag(1900, 1000, 2500, -50, MonitorWidth, MonitorHeight);

        Assert.Equal(new SelectionRectangle(Left: 1900, Top: 0, Width: 20, Height: 1000), selection);
    }

    [Theory]
    [InlineData(3, 10, false)]
    [InlineData(10, 3, false)]
    [InlineData(0, 0, false)]
    [InlineData(4, 4, true)]
    public void IsLargeEnough_RequiresMinimumSize(int width, int height, bool expected)
    {
        SelectionRectangle selection = new(Left: 0, Top: 0, Width: width, Height: height);

        Assert.Equal(expected, selection.IsLargeEnough);
    }
}
