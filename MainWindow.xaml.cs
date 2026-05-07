using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DoneToday;

public partial class MainWindow : Window
{
    private static string SaveDir => AppPaths.DataDir;
    private static string SavePath => Path.Combine(SaveDir, "todos.json");

    public ObservableCollection<TodoItem> Items { get; } = new();
    private ICollectionView? _itemsView;

    private Point _dragStart;
    private TodoItem? _dragItem;
    private TodoItem? _pendingDragItem;
    private ListBoxItem? _pendingDragContainer;
    private DragGhostAdorner? _ghostAdorner;
    private AdornerLayer? _ghostLayer;
    private InsertionLineAdorner? _lineAdorner;
    private ListBoxItem? _lineTarget;

    private Settings _settings = new();
    private const int HOTKEY_ID = 0x9001;
    private const int WM_HOTKEY = 0x0312;
    private bool _hotkeyRegistered;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    public MainWindow()
    {
        InitializeComponent();
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (v != null) Title = $"Done Today  —  v{v.Major}.{v.Minor}.{v.Build}";
        _itemsView = CollectionViewSource.GetDefaultView(Items);
        _itemsView.Filter = o => o is TodoItem t && !t.Archived;
        TodoList.ItemsSource = _itemsView;
        Items.CollectionChanged += OnTopLevelCollectionChanged;
        _settings = Settings.Load();
        ApplyFontSize(_settings.FontSize);
        Load();
        UpdateStatus();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        ApplySavedGeometry();
    }

    private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var src = HwndSource.FromHwnd(hwnd);
        src?.AddHook(WndProc);
        TryRegisterHotkey();
        AddBox.Focus();
        CheckForUpdatesOnStartup();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        TryUnregisterHotkey();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        // Use RestoreBounds when minimized/maximized so we capture the "normal" rect
        var rect = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
        {
            _settings.WindowLeft = rect.Left;
            _settings.WindowTop = rect.Top;
            _settings.WindowWidth = rect.Width;
            _settings.WindowHeight = rect.Height;
            _settings.Save();
        }
    }

    private void ApplySavedGeometry()
    {
        // Size first (so position validation uses the actual size)
        if (_settings.WindowWidth is double w && w > 0)
            Width = Math.Max(MinWidth, w);
        if (_settings.WindowHeight is double h && h > 0)
            Height = Math.Max(MinHeight, h);

        bool hasPos = _settings.WindowLeft.HasValue && _settings.WindowTop.HasValue;
        if (hasPos && IsRectVisible(_settings.WindowLeft!.Value, _settings.WindowTop!.Value, Width, Height))
        {
            Left = _settings.WindowLeft.Value;
            Top = _settings.WindowTop.Value;
        }
        else
        {
            // First run, or saved monitor is no longer attached → default to top-right of primary
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 16;
            Top = wa.Top + 16;
        }
    }

    /// <summary>
    /// True if a meaningful portion of the rect lies inside the current virtual screen
    /// (handles the case where the monitor that hosted the window is no longer attached).
    /// </summary>
    private static bool IsRectVisible(double left, double top, double width, double height)
    {
        var virt = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var win = new Rect(left, top, width, height);
        var visible = Rect.Intersect(win, virt);
        // Require at least a sane chunk so user can grab the title bar
        return !visible.IsEmpty && visible.Width >= 120 && visible.Height >= 60;
    }

    // ------- Global hotkey -------
    private IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    private void TryRegisterHotkey()
    {
        TryUnregisterHotkey();
        if (!_settings.HotkeyEnabled) { UpdateStatus(); return; }

        // Add MOD_NOREPEAT so holding the keys doesn't fire repeatedly
        uint mods = _settings.HotkeyModifiers | HotkeyFormatter.MOD_NOREPEAT;
        _hotkeyRegistered = RegisterHotKey(Hwnd, HOTKEY_ID, mods, _settings.HotkeyVirtualKey);
        if (!_hotkeyRegistered)
        {
            StatusText.Text = $"Hotkey {_settings.DescribeHotkey()} is unavailable (in use by another app).";
        }
        else
        {
            UpdateStatus();
        }
    }

    private void TryUnregisterHotkey()
    {
        if (_hotkeyRegistered)
        {
            UnregisterHotKey(Hwnd, HOTKEY_ID);
            _hotkeyRegistered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            ToggleSummon();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ToggleSummon()
    {
        // If we're already the foreground window, hide. Otherwise summon.
        if (IsVisible && GetForegroundWindow() == Hwnd)
        {
            Hide();
            return;
        }

        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

        // Bounce Topmost to force-bring above other topmost windows
        Topmost = false;
        Topmost = true;
        Activate();
        AddBox.Focus();
    }

    // ------- Completed log dialog -------
    private void Completed_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CompletedDialog { Owner = this };
        dlg.ShowDialog();
    }

    // ------- Settings dialog -------
    private void ApplyFontSize(double size)
    {
        FontSize = size;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        double originalFontSize = _settings.FontSize;
        var dlg = new HotkeyDialog(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey, _settings.FontSize, _settings.UpdateSource)
        {
            Owner = this,
            FontSizePreview = ApplyFontSize
        };

        TryUnregisterHotkey();

        if (dlg.ShowDialog() == true)
        {
            _settings.HotkeyModifiers = dlg.ResultModifiers;
            _settings.HotkeyVirtualKey = dlg.ResultVirtualKey;
            _settings.FontSize = dlg.ResultFontSize;
            _settings.UpdateSource = dlg.ResultUpdateSource;
            _settings.Save();
            ApplyFontSize(_settings.FontSize);
        }
        else
        {
            // Revert any live-preview font-size changes
            ApplyFontSize(originalFontSize);
        }

        TryRegisterHotkey();
    }

    // ------- Update banner -------
    private UpdateInfo? _pendingUpdate;
    private string? _pendingUpdateSource;

    public void ShowUpdateBanner(UpdateInfo info, string source)
    {
        _pendingUpdate = info;
        _pendingUpdateSource = source;
        UpdateBannerText.Text = $"Update available — v{info.Version}.";
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private void UpdateDismiss_Click(object sender, RoutedEventArgs e)
    {
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    private async void UpdateInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null || _pendingUpdateSource == null) return;
        UpdateInstallButton.IsEnabled = false;
        UpdateBannerText.Text = $"Downloading v{_pendingUpdate.Version}…";
        var newExe = await UpdateChecker.DownloadAsync(_pendingUpdate, _pendingUpdateSource);
        if (newExe == null)
        {
            UpdateBannerText.Text = "Download failed. Check the source.";
            UpdateInstallButton.IsEnabled = true;
            return;
        }
        var targetExe = Process.GetCurrentProcess().MainModule!.FileName!;
        UpdateBannerText.Text = "Restarting…";
        UpdateChecker.InstallAndRestart(newExe, targetExe);
    }

    private async void CheckForUpdatesOnStartup()
    {
        if (string.IsNullOrWhiteSpace(_settings.UpdateSource)) return;
        try
        {
            var info = await UpdateChecker.CheckAsync(_settings.UpdateSource);
            if (info != null) ShowUpdateBanner(info, _settings.UpdateSource);
        }
        catch { /* best-effort */ }
    }

    // ------- Add -------
    private void AddBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var text = AddBox.Text.Trim();
            if (text.Length > 0)
            {
                Items.Add(new TodoItem { Text = text, CreatedAt = DateTime.Now });
                AddBox.Clear();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            AddBox.Clear();
        }
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is TodoItem item && e.PropertyName == nameof(TodoItem.Done))
        {
            if (item.Done)
            {
                // Honor a pre-set CompletedAt (e.g. from CompleteAtDialog); otherwise stamp Now
                if (item.CompletedAt == null) item.CompletedAt = DateTime.Now;
                CompletedLog.Append(new CompletedEntry
                {
                    ItemId = item.Id,
                    Text = item.Text,
                    Details = item.Details,
                    CompletedAt = item.CompletedAt.Value,
                    TimeToComplete = item.TimeToComplete,
                    ParentItemId = item.Parent?.Id,
                    ParentText = item.Parent?.Text
                });
            }
            else
            {
                item.CompletedAt = null;
                item.TimeToComplete = null;
                CompletedLog.RemoveLatestByItemId(item.Id);
            }
            item.Parent?.NotifySubtaskBadgeChanged();
        }
        if (sender is TodoItem it && e.PropertyName == nameof(TodoItem.Archived))
        {
            _itemsView?.Refresh();
        }
        UpdateStatus();
        Save();
    }

    public void RefreshItemsView() => _itemsView?.Refresh();

    // ------- Wire/unwire items + their descendants -------
    public void WireItem(TodoItem item, TodoItem? parent)
    {
        item.Parent = parent;
        item.PropertyChanged += Item_PropertyChanged;
        item.Children.CollectionChanged += (s, e) => OnChildrenChanged(item, e);
        foreach (var c in item.Children) WireItem(c, item);
    }

    public void UnwireItem(TodoItem item)
    {
        item.PropertyChanged -= Item_PropertyChanged;
        foreach (var c in item.Children) UnwireItem(c);
    }

    private void OnTopLevelCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (TodoItem n in e.NewItems) WireItem(n, null);
        if (e.OldItems != null)
            foreach (TodoItem n in e.OldItems) UnwireItem(n);
        UpdateStatus();
        Save();
    }

    private void OnChildrenChanged(TodoItem parent, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (TodoItem n in e.NewItems) WireItem(n, parent);
        if (e.OldItems != null)
            foreach (TodoItem n in e.OldItems) UnwireItem(n);
        parent.NotifySubtaskBadgeChanged();
        Save();
    }

    // ------- Toggle done by click -------
    // Plain click on undone item → opens the Mark-complete dialog (asks for time + duration).
    // Plain click on done item → un-toggles immediately (no prompt).
    // Ctrl+click → opens dialog (useful to edit a done item's completion time/duration).
    private void Item_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragItem != null) return;
        if (sender is FrameworkElement fe && fe.DataContext is TodoItem item)
        {
            if (item.IsEditing) return;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (ctrl || !item.Done)
            {
                OpenCompleteAtDialog(item);
            }
            else
            {
                item.Done = false;
            }
            e.Handled = true;
        }
    }

    // Set the completion time on an item — if it wasn't done yet it becomes done at that time;
    // if it was already done, the existing log entry's timestamp/duration are updated.
    public void SetCompletionTime(TodoItem item, DateTime when, TimeSpan? duration)
    {
        item.TimeToComplete = duration;
        if (item.Done)
        {
            item.CompletedAt = when;
            CompletedLog.UpdateLatestByItemId(item.Id, when, item.Text, item.Details, duration);
            Save();
        }
        else
        {
            item.CompletedAt = when;
            item.Done = true; // PropertyChanged handler honors the pre-set CompletedAt + TimeToComplete
        }
    }

    private void OpenCompleteAtDialog(TodoItem item)
    {
        var initial = item.CompletedAt ?? DateTime.Now;
        var dlg = new CompleteAtDialog(item.Text, initial, item.TimeToComplete, item.CreatedAt) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            SetCompletionTime(item, dlg.Result, dlg.ResultDuration);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TodoItem item)
        {
            RemoveItemFromAnywhere(item);
        }
        e.Handled = true;
    }

    private void RemoveItemFromAnywhere(TodoItem item)
    {
        if (item.Parent != null) item.Parent.Children.Remove(item);
        else Items.Remove(item);
    }

    // ------- Subtasks drill-in -------
    private void Subtasks_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TodoItem item)
        {
            var dlg = new SubtasksDialog(this, item) { Owner = this };
            dlg.ShowDialog();
        }
        e.Handled = true;
    }

    // ------- Right-click → details dialog -------
    private void Item_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TodoItem item)
        {
            OpenDetails(item);
            e.Handled = true;
        }
    }

    private void OpenDetails(TodoItem item)
    {
        var dlg = new DetailsDialog(item.Text, item.Details) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            if (string.IsNullOrWhiteSpace(dlg.ResultTitle))
            {
                RemoveItemFromAnywhere(item);
                return;
            }
            item.Text = dlg.ResultTitle;
            item.Details = dlg.ResultDetails;
        }
    }

    // ------- Inline edit -------
    private string? _editOriginal;

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TodoItem item)
        {
            // Only one item edits at a time
            foreach (var i in Items) if (i != item) i.IsEditing = false;
            _editOriginal = item.Text;
            item.IsEditing = true;
        }
        e.Handled = true;
    }

    private void EditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.IsVisible)
        {
            // Defer focus until after layout/visibility settles
            Dispatcher.BeginInvoke(new Action(() =>
            {
                tb.Focus();
                tb.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void EditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not TodoItem item) return;

        if (e.Key == Key.Enter)
        {
            CommitEdit(item, tb);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Revert
            if (_editOriginal != null) item.Text = _editOriginal;
            item.IsEditing = false;
            _editOriginal = null;
            // Move focus off so the binding stops blocking
            TodoList.Focus();
            e.Handled = true;
        }
    }

    private void EditBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is TodoItem item && item.IsEditing)
        {
            CommitEdit(item, tb);
        }
    }

    private void CommitEdit(TodoItem item, TextBox tb)
    {
        var text = tb.Text.Trim();
        if (text.Length == 0)
        {
            RemoveItemFromAnywhere(item);
        }
        else
        {
            item.Text = text;
        }
        item.IsEditing = false;
        _editOriginal = null;
    }

    private void ArchiveDone_Click(object sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        foreach (var item in Items)
        {
            if (item.Done && !item.Archived)
            {
                item.Archived = true;
                item.ArchivedAt = now;
            }
        }
        _itemsView?.Refresh();
    }

    private void Archived_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ArchivedDialog(this) { Owner = this };
        dlg.ShowDialog();
    }

    // ------- Keyboard reorder / delete -------
    private void TodoList_KeyDown(object sender, KeyEventArgs e)
    {
        // Don't intercept keys when typing in an inline edit box
        if (Keyboard.FocusedElement is TextBox) return;
        if (TodoList.SelectedItem is not TodoItem sel) return;
        int idx = Items.IndexOf(sel);

        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        if (alt && e.Key == Key.Up && idx > 0)
        {
            Items.Move(idx, idx - 1);
            TodoList.SelectedIndex = idx - 1;
            e.Handled = true;
        }
        else if (alt && e.Key == Key.Down && idx < Items.Count - 1)
        {
            Items.Move(idx, idx + 1);
            TodoList.SelectedIndex = idx + 1;
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            sel.PropertyChanged -= Item_PropertyChanged;
            Items.RemoveAt(idx);
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            sel.Done = !sel.Done;
            e.Handled = true;
        }
    }

    // ------- Drag-and-drop reorder -------
    private void TodoList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = null;
        _pendingDragItem = null;
        _pendingDragContainer = null;

        // Don't arm a drag when starting on a button (edit/delete) or inside an edit textbox
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null) return;
        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) != null) return;

        var src = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (src?.DataContext is TodoItem item)
        {
            _pendingDragItem = item;
            _pendingDragContainer = src;
        }
    }

    private void TodoList_PreviewMouseMove(object sender, MouseEventArgs e)
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

        // Snapshot the row for the floating ghost
        ImageSource? snapshot = TryRenderToBitmap(container);

        _ghostLayer = AdornerLayer.GetAdornerLayer(TodoList);
        if (_ghostLayer != null && snapshot != null)
        {
            _ghostAdorner = new DragGhostAdorner(
                TodoList,
                snapshot,
                new Size(container.ActualWidth, container.ActualHeight));
            _ghostAdorner.UpdatePosition(e.GetPosition(TodoList));
            _ghostLayer.Add(_ghostAdorner);
        }

        // Fade the original row so the ghost reads as the moving copy
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

    private void ClearGhost()
    {
        if (_ghostAdorner != null && _ghostLayer != null)
        {
            _ghostLayer.Remove(_ghostAdorner);
        }
        _ghostAdorner = null;
        _ghostLayer = null;
    }

    private void TodoList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TodoItem)) ? DragDropEffects.Move : DragDropEffects.None;

        if (_ghostAdorner != null)
        {
            _ghostAdorner.UpdatePosition(e.GetPosition(TodoList));
        }

        UpdateInsertionLine(e);
        e.Handled = true;
    }

    private void TodoList_DragLeave(object sender, DragEventArgs e)
    {
        // If the cursor truly leaves the list (not just crossing onto a child), clear the line
        var pt = e.GetPosition(TodoList);
        if (pt.X < 0 || pt.Y < 0 || pt.X > TodoList.ActualWidth || pt.Y > TodoList.ActualHeight)
        {
            ClearInsertionLine();
        }
    }

    private (ListBoxItem? target, bool insertBefore) ComputeDropTarget(DragEventArgs e)
    {
        var hit = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (hit?.DataContext is TodoItem item && !ReferenceEquals(item, _dragItem))
        {
            var pt = e.GetPosition(hit);
            return (hit, pt.Y < hit.ActualHeight / 2);
        }

        if (Items.Count == 0) return (null, false);

        // Empty area — bias to top of first or bottom of last by Y
        var dropY = e.GetPosition(TodoList).Y;
        if (TodoList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem first)
        {
            var firstTop = first.TransformToAncestor(TodoList).Transform(new Point(0, 0)).Y;
            if (dropY < firstTop) return (first, true);
        }
        if (TodoList.ItemContainerGenerator.ContainerFromIndex(Items.Count - 1) is ListBoxItem last)
        {
            return (last, false);
        }
        return (null, false);
    }

    private void UpdateInsertionLine(DragEventArgs e)
    {
        var (target, isTop) = ComputeDropTarget(e);
        if (target == null)
        {
            ClearInsertionLine();
            return;
        }
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

    private void TodoList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(TodoItem)) is not TodoItem source) return;
        int oldIdx = Items.IndexOf(source);
        if (oldIdx < 0) return;

        var (target, insertBefore) = ComputeDropTarget(e);
        if (target?.DataContext is not TodoItem targetItem) return;

        int targetIdx = Items.IndexOf(targetItem);
        if (targetIdx < 0) return;

        // Insert-before-or-after → final index, accounting for ObservableCollection.Move semantics
        int newIdx = insertBefore ? targetIdx : targetIdx + 1;
        if (oldIdx < newIdx) newIdx--;

        if (newIdx < 0) newIdx = 0;
        if (newIdx > Items.Count - 1) newIdx = Items.Count - 1;

        if (newIdx != oldIdx)
        {
            Items.Move(oldIdx, newIdx);
            TodoList.SelectedIndex = newIdx;
        }
        e.Handled = true;
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

    // ------- Status / persistence -------
    private void UpdateStatus()
    {
        int total = 0, done = 0;
        foreach (var i in Items)
        {
            if (i.Archived) continue;
            total++;
            if (i.Done) done++;
        }

        string left = total == 0
            ? "No tasks. Add one above."
            : $"{done} of {total} done";

        string right = _settings.HotkeyEnabled
            ? $"{_settings.DescribeHotkey()} to summon"
            : "";

        StatusText.Text = string.IsNullOrEmpty(right) ? left : $"{left}\n{right}";
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(SaveDir);
            var json = JsonSerializer.Serialize(Items, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SavePath, json);
        }
        catch { }
    }

    private static void EnsureIds(TodoItem item)
    {
        if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
        foreach (var c in item.Children) EnsureIds(c);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(SavePath)) return;
            var json = File.ReadAllText(SavePath);
            var loaded = JsonSerializer.Deserialize<TodoItem[]>(json);
            if (loaded == null) return;
            foreach (var it in loaded)
            {
                EnsureIds(it);
                Items.Add(it);  // OnTopLevelCollectionChanged wires children too
            }
        }
        catch { }
    }
}

public class TodoItem : INotifyPropertyChanged
{
    private Guid _id = Guid.NewGuid();
    private string _text = "";
    private string _details = "";
    private bool _done;
    private DateTime? _completedAt;
    private DateTime? _createdAt;
    private TimeSpan? _timeToComplete;
    private bool _isEditing;
    private TodoItem? _parent;
    private bool _archived;
    private DateTime? _archivedAt;

    public bool Archived
    {
        get => _archived;
        set { if (_archived != value) { _archived = value; OnPropertyChanged(); } }
    }
    public DateTime? ArchivedAt
    {
        get => _archivedAt;
        set { if (_archivedAt != value) { _archivedAt = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<TodoItem> Children { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public TodoItem? Parent
    {
        get => _parent;
        set => _parent = value;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string SubtaskBadge
    {
        get
        {
            int done = 0;
            foreach (var c in Children) if (c.Done) done++;
            return $"{done}/{Children.Count}  ▸";
        }
    }

    public void NotifySubtaskBadgeChanged() => OnPropertyChanged(nameof(SubtaskBadge));

    public Guid Id
    {
        get => _id;
        set { if (_id != value) { _id = value; OnPropertyChanged(); } }
    }
    public DateTime? CompletedAt
    {
        get => _completedAt;
        set { if (_completedAt != value) { _completedAt = value; OnPropertyChanged(); } }
    }
    public TimeSpan? TimeToComplete
    {
        get => _timeToComplete;
        set { if (_timeToComplete != value) { _timeToComplete = value; OnPropertyChanged(); } }
    }
    public DateTime? CreatedAt
    {
        get => _createdAt;
        set { if (_createdAt != value) { _createdAt = value; OnPropertyChanged(); } }
    }

    public string Text
    {
        get => _text;
        set { if (_text != value) { _text = value; OnPropertyChanged(); } }
    }
    public string Details
    {
        get => _details;
        set
        {
            var v = value ?? "";
            if (_details != v)
            {
                _details = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasDetails));
            }
        }
    }
    public bool Done
    {
        get => _done;
        set { if (_done != value) { _done = value; OnPropertyChanged(); } }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasDetails => !string.IsNullOrWhiteSpace(_details);

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEditing
    {
        get => _isEditing;
        set { if (_isEditing != value) { _isEditing = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class BoolToVisibilityHiddenConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is bool b && b) ? Visibility.Visible : Visibility.Hidden;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class FontSizeRatioConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double size) return value!;
        double ratio = 1.0;
        if (parameter is string s &&
            double.TryParse(s, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
        {
            ratio = r;
        }
        return Math.Max(8.0, size * ratio);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class DoneToTextDecorationsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? TextDecorations.Strikethrough : new TextDecorationCollection();
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class DoneToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Active = new(Color.FromRgb(0xF0, 0xF3, 0xF8));
    private static readonly SolidColorBrush Muted  = new(Color.FromRgb(0xA0, 0xAA, 0xC0));
    private static readonly SolidColorBrush Accent = new(Color.FromRgb(0x5A, 0xA0, 0xF2));
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool done = value is bool b && b;
        if (parameter as string == "glyph")
            return done ? "☑" : "☐";
        if (parameter as string == "accent")
            return Accent;
        return done ? Muted : Active;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
