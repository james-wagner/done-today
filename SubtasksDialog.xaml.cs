using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DoneToday;

public partial class SubtasksDialog : Window
{
    private readonly MainWindow _owner;
    private readonly TodoItem _parent;
    private string? _editOriginal;

    // Drag-and-drop state
    private Point _dragStart;
    private TodoItem? _dragItem;
    private TodoItem? _pendingDragItem;
    private ListBoxItem? _pendingDragContainer;
    private DragGhostAdorner? _ghostAdorner;
    private AdornerLayer? _ghostLayer;
    private InsertionLineAdorner? _lineAdorner;
    private ListBoxItem? _lineTarget;

    public SubtasksDialog(MainWindow owner, TodoItem parent)
    {
        InitializeComponent();
        _owner = owner;
        _parent = parent;
        ParentTitle.Text = parent.Text;
        SubList.ItemsSource = parent.Children;
        _parent.PropertyChanged += Parent_PropertyChanged;
        _parent.Children.CollectionChanged += (_, __) => UpdateStatus();
        UpdateStatus();
        Loaded += (_, __) => AddBox.Focus();
        Closed += (_, __) => _parent.PropertyChanged -= Parent_PropertyChanged;
        Title = "Subtasks: " + parent.Text;
    }

    private void Parent_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TodoItem.Text))
        {
            ParentTitle.Text = _parent.Text;
            Title = "Subtasks: " + _parent.Text;
        }
    }

    private void UpdateStatus()
    {
        int total = _parent.Children.Count;
        if (total == 0) { StatusText.Text = "No subtasks yet."; return; }
        int done = 0;
        foreach (var c in _parent.Children) if (c.Done) done++;
        StatusText.Text = $"{done} of {total} done";
    }

    // ------- Add -------
    private void AddBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var text = AddBox.Text.Trim();
            if (text.Length > 0)
            {
                _parent.Children.Add(new TodoItem { Text = text, CreatedAt = System.DateTime.Now });
                AddBox.Clear();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            AddBox.Clear();
        }
    }

    // ------- Click toggle -------
    // Plain click on undone → asks time + duration via dialog.
    // Plain click on done → un-toggles.
    // Ctrl+click → always opens dialog (edits a done item's completion).
    private void Item_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TodoItem item)
        {
            if (item.IsEditing) return;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (ctrl || !item.Done)
            {
                var initial = item.CompletedAt ?? System.DateTime.Now;
                var dlg = new CompleteAtDialog(item.Text, initial, item.TimeToComplete, item.CreatedAt) { Owner = this };
                if (dlg.ShowDialog() == true)
                    _owner.SetCompletionTime(item, dlg.Result, dlg.ResultDuration);
            }
            else
            {
                item.Done = false;
            }
            e.Handled = true;
        }
    }

    // ------- Right-click → details dialog (uses MainWindow's CompletedLog wiring transparently) -------
    private void Item_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TodoItem item)
        {
            var dlg = new DetailsDialog(item.Text, item.Details) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                if (string.IsNullOrWhiteSpace(dlg.ResultTitle))
                {
                    _parent.Children.Remove(item);
                }
                else
                {
                    item.Text = dlg.ResultTitle;
                    item.Details = dlg.ResultDetails;
                }
            }
            e.Handled = true;
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TodoItem item)
        {
            _parent.Children.Remove(item);
        }
        e.Handled = true;
    }

    private void ClearDone_Click(object sender, RoutedEventArgs e)
    {
        for (int i = _parent.Children.Count - 1; i >= 0; i--)
        {
            if (_parent.Children[i].Done) _parent.Children.RemoveAt(i);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ------- Inline edit (mirrors MainWindow logic) -------
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TodoItem item)
        {
            foreach (var i in _parent.Children) if (i != item) i.IsEditing = false;
            _editOriginal = item.Text;
            item.IsEditing = true;
        }
        e.Handled = true;
    }

    private void EditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.IsVisible)
        {
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                tb.Focus();
                tb.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void EditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not TodoItem item) return;
        if (e.Key == Key.Enter) { CommitEdit(item, tb); e.Handled = true; }
        else if (e.Key == Key.Escape)
        {
            if (_editOriginal != null) item.Text = _editOriginal;
            item.IsEditing = false;
            _editOriginal = null;
            SubList.Focus();
            e.Handled = true;
        }
    }

    private void EditBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is TodoItem item && item.IsEditing)
            CommitEdit(item, tb);
    }

    private void CommitEdit(TodoItem item, TextBox tb)
    {
        var text = tb.Text.Trim();
        if (text.Length == 0)
        {
            _parent.Children.Remove(item);
        }
        else
        {
            item.Text = text;
        }
        item.IsEditing = false;
        _editOriginal = null;
    }

    // ------- Drag-and-drop reorder -------
    private void SubList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = null;
        _pendingDragItem = null;
        _pendingDragContainer = null;

        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null) return;
        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) != null) return;

        var src = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (src?.DataContext is TodoItem item)
        {
            _pendingDragItem = item;
            _pendingDragContainer = src;
        }
    }

    private void SubList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (_pendingDragItem == null || _pendingDragContainer == null) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragItem = _pendingDragItem;
        var container = _pendingDragContainer;
        var data = _pendingDragItem;
        _pendingDragItem = null;
        _pendingDragContainer = null;

        ImageSource? snapshot = TryRenderToBitmap(container);

        _ghostLayer = AdornerLayer.GetAdornerLayer(SubList);
        if (_ghostLayer != null && snapshot != null)
        {
            _ghostAdorner = new DragGhostAdorner(
                SubList,
                snapshot,
                new Size(container.ActualWidth, container.ActualHeight));
            _ghostAdorner.UpdatePosition(e.GetPosition(SubList));
            _ghostLayer.Add(_ghostAdorner);
        }

        double originalOpacity = container.Opacity;
        container.Opacity = 0.35;

        try
        {
            DragDrop.DoDragDrop(container, data, DragDropEffects.Move);
        }
        finally
        {
            container.Opacity = originalOpacity;
            ClearGhost();
            ClearInsertionLine();
            _dragItem = null;
        }
    }

    private void SubList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TodoItem)) ? DragDropEffects.Move : DragDropEffects.None;
        if (_ghostAdorner != null) _ghostAdorner.UpdatePosition(e.GetPosition(SubList));
        UpdateInsertionLine(e);
        e.Handled = true;
    }

    private void SubList_DragLeave(object sender, DragEventArgs e)
    {
        var pt = e.GetPosition(SubList);
        if (pt.X < 0 || pt.Y < 0 || pt.X > SubList.ActualWidth || pt.Y > SubList.ActualHeight)
            ClearInsertionLine();
    }

    private void SubList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(TodoItem)) is not TodoItem source) return;
        int oldIdx = _parent.Children.IndexOf(source);
        if (oldIdx < 0) return;

        var (target, insertBefore) = ComputeDropTarget(e);
        if (target?.DataContext is not TodoItem targetItem) return;

        int targetIdx = _parent.Children.IndexOf(targetItem);
        if (targetIdx < 0) return;

        int newIdx = insertBefore ? targetIdx : targetIdx + 1;
        if (oldIdx < newIdx) newIdx--;

        if (newIdx < 0) newIdx = 0;
        if (newIdx > _parent.Children.Count - 1) newIdx = _parent.Children.Count - 1;

        if (newIdx != oldIdx)
        {
            _parent.Children.Move(oldIdx, newIdx);
            SubList.SelectedIndex = newIdx;
        }
        e.Handled = true;
    }

    private (ListBoxItem? target, bool insertBefore) ComputeDropTarget(DragEventArgs e)
    {
        var hit = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (hit?.DataContext is TodoItem item && !ReferenceEquals(item, _dragItem))
        {
            var pt = e.GetPosition(hit);
            return (hit, pt.Y < hit.ActualHeight / 2);
        }

        if (_parent.Children.Count == 0) return (null, false);

        var dropY = e.GetPosition(SubList).Y;
        if (SubList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem first)
        {
            var firstTop = first.TransformToAncestor(SubList).Transform(new Point(0, 0)).Y;
            if (dropY < firstTop) return (first, true);
        }
        if (SubList.ItemContainerGenerator.ContainerFromIndex(_parent.Children.Count - 1) is ListBoxItem last)
        {
            return (last, false);
        }
        return (null, false);
    }

    private void UpdateInsertionLine(DragEventArgs e)
    {
        var (target, isTop) = ComputeDropTarget(e);
        if (target == null) { ClearInsertionLine(); return; }
        SetInsertionLine(target, isTop);
    }

    private void SetInsertionLine(ListBoxItem target, bool isTop)
    {
        if (ReferenceEquals(_lineTarget, target) && _lineAdorner != null)
        {
            _lineAdorner.Update(isTop);
            return;
        }
        ClearInsertionLine();
        var layer = AdornerLayer.GetAdornerLayer(target);
        if (layer == null) return;
        _lineAdorner = new InsertionLineAdorner(target, isTop);
        layer.Add(_lineAdorner);
        _lineTarget = target;
    }

    private void ClearInsertionLine()
    {
        if (_lineAdorner != null && _lineTarget != null)
        {
            var layer = AdornerLayer.GetAdornerLayer(_lineTarget);
            layer?.Remove(_lineAdorner);
        }
        _lineAdorner = null;
        _lineTarget = null;
    }

    private void ClearGhost()
    {
        if (_ghostAdorner != null && _ghostLayer != null) _ghostLayer.Remove(_ghostAdorner);
        _ghostAdorner = null;
        _ghostLayer = null;
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private static ImageSource? TryRenderToBitmap(FrameworkElement element)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return null;
        var dpi = VisualTreeHelper.GetDpi(element);
        int w = (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX);
        int h = (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY);
        if (w <= 0 || h <= 0) return null;
        var rtb = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(element);
        rtb.Freeze();
        return rtb;
    }

    // ------- Keyboard reorder / delete / toggle -------
    private void SubList_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        if (SubList.SelectedItem is not TodoItem sel) return;
        int idx = _parent.Children.IndexOf(sel);

        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        if (alt && e.Key == Key.Up && idx > 0)
        {
            _parent.Children.Move(idx, idx - 1);
            SubList.SelectedIndex = idx - 1;
            e.Handled = true;
        }
        else if (alt && e.Key == Key.Down && idx < _parent.Children.Count - 1)
        {
            _parent.Children.Move(idx, idx + 1);
            SubList.SelectedIndex = idx + 1;
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            _parent.Children.RemoveAt(idx);
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            sel.Done = !sel.Done;
            e.Handled = true;
        }
    }
}
