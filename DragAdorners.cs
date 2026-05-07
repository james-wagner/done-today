using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DoneToday;

/// <summary>
/// Floating semi-transparent snapshot of the dragged row that follows the cursor.
/// </summary>
public class DragGhostAdorner : Adorner
{
    private readonly ImageSource _image;
    private readonly Size _imageSize;
    private Point _offset;

    public DragGhostAdorner(UIElement adornedElement, ImageSource image, Size imageSize)
        : base(adornedElement)
    {
        _image = image;
        _imageSize = imageSize;
        IsHitTestVisible = false;
        Opacity = 0.85;
    }

    public void UpdatePosition(Point pointInAdornedElement)
    {
        if (_offset != pointInAdornedElement)
        {
            _offset = pointInAdornedElement;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Cursor sits near the upper-left of the ghost so it visibly trails
        var rect = new Rect(
            _offset.X + 14,
            _offset.Y + 10,
            _imageSize.Width,
            _imageSize.Height);

        // Subtle shadow underlay
        var shadowBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00));
        drawingContext.DrawRectangle(
            shadowBrush, null,
            new Rect(rect.X + 2, rect.Y + 4, rect.Width, rect.Height));

        drawingContext.DrawImage(_image, rect);

        // Accent outline
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0xA0, 0xF2)), 1.5);
        drawingContext.DrawRectangle(null, pen, rect);
    }
}

/// <summary>
/// Thin blue line drawn at the top or bottom edge of the row to indicate where the drop will insert.
/// </summary>
public class InsertionLineAdorner : Adorner
{
    private static readonly Brush LineBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0xA0, 0xF2));
    private bool _isTop;

    public InsertionLineAdorner(UIElement adornedElement, bool isTop)
        : base(adornedElement)
    {
        _isTop = isTop;
        IsHitTestVisible = false;
    }

    public void Update(bool isTop)
    {
        if (_isTop != isTop)
        {
            _isTop = isTop;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = ((UIElement)AdornedElement).RenderSize;
        double y = _isTop ? 0 : size.Height;
        var pen = new Pen(LineBrush, 2.5);
        drawingContext.DrawLine(pen, new Point(0, y), new Point(size.Width, y));
        // Small bullet at the left for emphasis
        drawingContext.DrawEllipse(LineBrush, null, new Point(0, y), 3.5, 3.5);
    }
}
