using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DoneToday;

public partial class CompletedDialog : Window
{
    public CompletedDialog()
    {
        InitializeComponent();
        Refresh();
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

    public class EntryRow
    {
        public string Text { get; set; } = "";
        public string? ParentText { get; set; }
        public string Details { get; set; } = "";
        public DateTime CompletedAt { get; set; }
        public TimeSpan? TimeToComplete { get; set; }
        public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
        public bool HasDuration => TimeToComplete is TimeSpan ts && ts > TimeSpan.Zero;
        public string DurationText => HasDuration ? $"Took: {DurationFormatter.Format(TimeToComplete!.Value)}" : "";
        public string DisplayText =>
            string.IsNullOrWhiteSpace(ParentText) ? Text : $"{ParentText}: {Text}";
    }

    public class DayGroup
    {
        public string DateLabel { get; set; } = "";
        public List<EntryRow> Entries { get; set; } = new();
    }

    private void Refresh()
    {
        var range = CurrentRange;
        var entries = CompletedLog.LoadAll()
            .Where(e => range.Contains(e.CompletedAt))
            .ToList();

        var today = DateTime.Today;
        var groups = entries
            .OrderByDescending(e => e.CompletedAt)
            .GroupBy(e => e.CompletedAt.Date)
            .OrderByDescending(g => g.Key)
            .Select(g => new DayGroup
            {
                DateLabel = FormatDateLabel(g.Key, today),
                Entries = g.OrderBy(e => e.CompletedAt)
                           .Select(e => new EntryRow
                           {
                               Text = e.Text,
                               ParentText = e.ParentText,
                               Details = e.Details,
                               CompletedAt = e.CompletedAt,
                               TimeToComplete = e.TimeToComplete
                           })
                           .ToList()
            })
            .ToList();

        DaysList.ItemsSource = groups;

        bool empty = groups.Count == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ContentScroller.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        StatusText.Text = entries.Count == 1
            ? "1 entry"
            : $"{entries.Count} entries";
    }

    private static string FormatDateLabel(DateTime date, DateTime today)
    {
        if (date == today) return $"Today  ·  {date:yyyy-MM-dd ddd}";
        if (date == today.AddDays(-1)) return $"Yesterday  ·  {date:yyyy-MM-dd ddd}";
        return date.ToString("yyyy-MM-dd ddd");
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var range = CurrentRange;
        var entries = CompletedLog.LoadAll().Where(x => range.Contains(x.CompletedAt)).ToList();
        if (entries.Count == 0)
        {
            StatusText.Text = "Nothing to copy in selected range.";
            return;
        }
        var text = CompletedLog.FormatForAdo(entries);
        try
        {
            Clipboard.SetText(text);
            StatusText.Text = $"Copied {entries.Count} entries to clipboard.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Copy failed: " + ex.Message;
        }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(CompletedLog.FilePath);
            StatusText.Text = "Path copied: " + CompletedLog.FilePath;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Copy failed: " + ex.Message;
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "Permanently clear the completed-tasks log? This won't affect tasks in your active list.",
            "Clear log",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            CompletedLog.Clear();
            Refresh();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
