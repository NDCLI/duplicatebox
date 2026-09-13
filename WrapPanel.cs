using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace CvatDuplicateChecker;

/// Panel xếp các phần tử thành các hàng ngang (wrap theo chiều rộng khả dụng),
/// mỗi hàng cao bằng phần tử cao nhất trong hàng — giống grid CSS của bản web,
/// cho phép card đang chọn (có ô xem nhanh ảnh) cao hơn các card khác mà không bị cắt xén.
public sealed class WrapPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var availWidth = double.IsInfinity(availableSize.Width) ? 4096 : availableSize.Width;
        double x = 0, y = 0, rowHeight = 0, maxWidth = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(availWidth, availableSize.Height));
            var (w, h) = DesiredWithMargin(child);
            if (x + w > availWidth && x > 0)
            {
                y += rowHeight;
                rowHeight = 0;
                x = 0;
            }
            x += w;
            rowHeight = Math.Max(rowHeight, h);
            maxWidth = Math.Max(maxWidth, x);
        }
        return new Size(maxWidth, y + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, rowHeight = 0;
        foreach (var child in Children)
        {
            var (w, h) = DesiredWithMargin(child);
            if (x + w > finalSize.Width && x > 0)
            {
                y += rowHeight;
                rowHeight = 0;
                x = 0;
            }
            var margin = (child as FrameworkElement)?.Margin ?? default;
            child.Arrange(new Rect(x + margin.Left, y + margin.Top, child.DesiredSize.Width, child.DesiredSize.Height));
            x += w;
            rowHeight = Math.Max(rowHeight, h);
        }
        return finalSize;
    }

    private static (double Width, double Height) DesiredWithMargin(UIElement child)
    {
        var margin = (child as FrameworkElement)?.Margin ?? default;
        return (child.DesiredSize.Width + margin.Left + margin.Right,
                child.DesiredSize.Height + margin.Top + margin.Bottom);
    }
}
