using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DoneToday;

public partial class ArchivedDialog : Window
{
    private readonly MainWindow _main;

    public ArchivedDialog(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Refresh();
    }

    public class EntryRow
    {
        public TodoItem Item { get; set; } = null!;
        public string TitleText { get; set; } = "";
        public string MetaText { get; set; } = "";
    }

    public class DayGroup
    {
        public string DateLabel { get; set; } = "";
        public List<EntryRow> Entries { get; set; } = new();
    }

    private DateRange CurrentRange
    {
        get
        {
            var tag = (RangeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (tag == "Custom")
                return DateRange.ForCustom(FromPicker.SelectedDate, ToPicker.SelectedDate);
            return DateRange.ForPreset(tag);
        }
    }

    private void RangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomRow == null) return;
        var tag = (RangeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        CustomRow.Visibility = tag == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    private void CustomDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if ((RangeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Custom")
            Refresh();
    }

    private void Refresh()
    {
        var range = CurrentRange;
        var today = DateTime.Today;

        // Source: top-level archived items (filter by completion date if available, else archive date)
        var archived = _main.Items.Where(i => i.Archived).ToList();

        var rows = archived
            .Select(i => new
            {
                Item = i,
                Stamp = i.CompletedAt ?? i.ArchivedAt ?? DateTime.MinValue
            })
            .Where(x => range.Contains(x.Stamp))
            .OrderByDescending(x => x.Stamp)
            .ToList();

        var groups = rows
            .GroupBy(x => x.Stamp.Date)
            .OrderByDescending(g => g.Key)
            .Select(g => new DayGroup
            {
                DateLabel = FormatDateLabel(g.Key, today),
                Entries = g.OrderBy(x => x.Stamp)
                           .Select(x => new EntryRow
                           {
                               Item = x.Item,
                               TitleText = BuildTitle(x.Item),
                               MetaText = BuildMeta(x.Item)
                           })
                           .ToList()
            })
            .ToList();

        DaysList.ItemsSource = groups;

        bool empty = groups.Count == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ContentScroller.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        StatusText.Text = rows.Count == 1 ? "1 item" : $"{rows.Count} items";
    }

    private static string BuildTitle(TodoItem item)
    {
        // Top-level items only get archived; show subtask count if any
        if (item.Children.Count == 0) return item.Text;
        int done = item.Children.Count(c => c.Done);
        return $"{item.Text}  ({done}/{item.Children.Count} subtasks)";
    }

    private static string BuildMeta(TodoItem item)
    {
        var parts = new List<string>();
        if (item.CompletedAt is DateTime done)
            parts.Add($"completed {done:yyyy-MM-dd HH:mm}");
        if (item.ArchivedAt is DateTime arch)
            parts.Add($"archived {arch:yyyy-MM-dd HH:mm}");
        return string.Join("  ·  ", parts);
    }

    private static string FormatDateLabel(DateTime date, DateTime today)
    {
        if (date == today) return $"Today  ·  {date:yyyy-MM-dd ddd}";
        if (date == today.AddDays(-1)) return $"Yesterday  ·  {date:yyyy-MM-dd ddd}";
        return date.ToString("yyyy-MM-dd ddd");
    }

    private void Unarchive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is EntryRow row)
        {
            row.Item.Archived = false;
            row.Item.ArchivedAt = null;
            _main.RefreshItemsView();
            Refresh();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
