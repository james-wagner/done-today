using System;
using System.Windows;
using System.Windows.Controls;

namespace DoneToday;

public partial class CompleteAtDialog : Window
{
    public DateTime Result { get; private set; }
    public TimeSpan? ResultDuration { get; private set; }

    private readonly DateTime? _createdAt;

    public CompleteAtDialog(string taskTitle, DateTime initial, TimeSpan? initialDuration = null, DateTime? createdAt = null)
    {
        InitializeComponent();
        TaskTitle.Text = taskTitle;
        _createdAt = createdAt;
        Apply(initial);
        ApplyDuration(initialDuration);
        UpdateCreatedLabel();
        Result = initial;
        ResultDuration = initialDuration;

        // keep "open for X" live as user edits date/time
        DatePicker.SelectedDateChanged += (_, __) => UpdateCreatedLabel();
        HourBox.TextChanged   += (_, __) => UpdateCreatedLabel();
        MinuteBox.TextChanged += (_, __) => UpdateCreatedLabel();

        Loaded += (_, __) => DurHourBox.Focus();
    }

    private void UpdateCreatedLabel()
    {
        if (_createdAt is not DateTime created)
        {
            CreatedLabel.Visibility = Visibility.Collapsed;
            return;
        }

        string text = $"Created: {created:yyyy-MM-dd HH:mm}";

        if (DatePicker.SelectedDate is DateTime d)
        {
            int.TryParse(HourBox.Text, out int hr);
            int.TryParse(MinuteBox.Text, out int mn);
            hr = Math.Clamp(hr, 0, 23);
            mn = Math.Clamp(mn, 0, 59);
            var completion = new DateTime(d.Year, d.Month, d.Day, hr, mn, 0);
            var span = completion - created;
            if (span.TotalSeconds < 0) span = TimeSpan.Zero;
            text += $"  ·  open for {FormatOpenFor(span)}";
        }

        CreatedLabel.Text = text;
        CreatedLabel.Visibility = Visibility.Visible;
    }

    private static string FormatOpenFor(TimeSpan span)
    {
        if (span.TotalMinutes < 1) return "just now";
        int days = (int)span.TotalDays;
        int hours = span.Hours;
        int mins = span.Minutes;
        if (days > 0) return hours > 0 ? $"{days}d {hours}h" : $"{days}d";
        if (hours > 0) return mins > 0 ? $"{hours}h {mins}m" : $"{hours}h";
        return $"{mins}m";
    }

    private void Apply(DateTime when)
    {
        DatePicker.SelectedDate = when.Date;
        HourBox.Text = when.Hour.ToString("D2");
        MinuteBox.Text = when.Minute.ToString("D2");
    }

    private void ApplyDuration(TimeSpan? d)
    {
        if (d is TimeSpan ts && ts > TimeSpan.Zero)
        {
            DurHourBox.Text = ((int)Math.Floor(ts.TotalHours)).ToString();
            DurMinuteBox.Text = ts.Minutes.ToString();
        }
        else
        {
            DurHourBox.Text = "";
            DurMinuteBox.Text = "";
        }
    }

    private void Now_Click(object sender, RoutedEventArgs e) => Apply(DateTime.Now);

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (DatePicker.SelectedDate is not DateTime d) { DatePicker.Focus(); return; }
        int.TryParse(HourBox.Text, out int hr);
        int.TryParse(MinuteBox.Text, out int mn);
        hr = Math.Clamp(hr, 0, 23);
        mn = Math.Clamp(mn, 0, 59);
        Result = new DateTime(d.Year, d.Month, d.Day, hr, mn, 0);

        int.TryParse(DurHourBox.Text, out int dh);
        int.TryParse(DurMinuteBox.Text, out int dm);
        if (dh < 0) dh = 0;
        if (dm < 0) dm = 0;
        var total = new TimeSpan(dh, dm, 0);
        ResultDuration = total > TimeSpan.Zero ? total : (TimeSpan?)null;

        DialogResult = true;
    }
}
