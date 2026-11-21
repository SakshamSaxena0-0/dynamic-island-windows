using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

public partial class IslandWindow
{
    private readonly NotificationFeedService _notes = new();
    private string _notesShown = "";
    private bool _notesReading;

    private async void RefreshNotifications()
    {
        if (_notesReading) return;
        _notesReading = true;
        try
        {
            IReadOnlyList<NoteItem>? items = await _notes.ReadAsync();
            if (items == null)
            {
                ShowNotes(Array.Empty<NoteItem>(), _notes.Allowed
                    ? "Couldn't read notifications"
                    : "Turn on notification access in Settings > Privacy > Notifications");
                return;
            }
            ShowNotes(items, "No notifications");
        }
        finally
        {
            _notesReading = false;
        }
    }

    private void ShowNotes(IReadOnlyList<NoteItem> items, string emptyText)
    {
        string key = emptyText + "|" + DateTime.Now.ToString("HHmm") + "|" + string.Join(",", items.Select(n => n.Id));
        if (key == _notesShown) return;
        _notesShown = key;

        NotesCount.Text = items.Count == 0 ? "" : items.Count == 1 ? "1 new" : $"{items.Count} new";
        NotesClear.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotesEmpty.Text = emptyText;
        NotesEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NotesList.Children.Clear();
        foreach (NoteItem item in items)
        {
            if (NotesList.Children.Count > 0)
            {
                var divider = new Border { Height = 1, Margin = new Thickness(0, 2, 0, 3), Opacity = 0.25, IsHitTestVisible = false };
                divider.SetResourceReference(Border.BackgroundProperty, "IslandText");
                NotesList.Children.Add(divider);
            }
            NotesList.Children.Add(NoteRow(item));
        }
    }

    private FrameworkElement NoteRow(NoteItem item)
    {
        var icon = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(7), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0) };
        icon.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        if (item.Logo != null)
        {
            icon.Child = new Image { Source = item.Logo, Stretch = Stretch.Uniform, Margin = new Thickness(4) };
            RenderOptions.SetBitmapScalingMode(icon.Child, BitmapScalingMode.HighQuality);
        }
        else
        {
            icon.Child = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        DockPanel.SetDock(icon, Dock.Left);

        var dismiss = new Button
        {
            Style = (Style)FindResource("CpTile"),
            Background = Brushes.Transparent,
            Width = 24,
            Height = 24,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "Dismiss",
            Tag = item,
            Content = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        dismiss.Click += NoteDismiss_Click;
        DockPanel.SetDock(dismiss, Dock.Right);

        var header = new DockPanel();
        var when = new TextBlock { Text = Ago(item.When), Style = (Style)FindResource("Caption"), FontSize = 11, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(when, Dock.Right);
        header.Children.Add(when);
        header.Children.Add(new TextBlock { Text = item.App, Style = (Style)FindResource("Caption"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });

        var text = new StackPanel();
        text.Children.Add(header);
        if (item.Title.Length > 0)
            text.Children.Add(new TextBlock { Text = item.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (item.Body.Length > 0)
        {
            var body = new TextBlock { Text = item.Body, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            body.SetResourceReference(TextBlock.ForegroundProperty, "SubtleText");
            text.Children.Add(body);
        }

        var line = new DockPanel { Margin = new Thickness(6, 5, 4, 5) };
        line.Children.Add(icon);
        line.Children.Add(dismiss);
        line.Children.Add(text);

        var row = new Button
        {
            Style = (Style)FindResource("CpTile"),
            Background = Brushes.Transparent,
            Margin = new Thickness(-6, 0, 0, 0),
            Content = line,
            Tag = item,
            ToolTip = string.Join("\n", new[] { item.Title, item.Body, $"Click to open {item.App}" }.Where(t => t.Length > 0)),
        };
        row.Click += NoteOpen_Click;
        return row;
    }

    private void NoteOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NoteItem item }) return;
        _notes.Open(item);
        SetExpanded(false);
        RefreshNotifications();
    }

    private static string Ago(DateTimeOffset when)
    {
        TimeSpan age = DateTimeOffset.Now - when;
        if (age < TimeSpan.FromMinutes(1)) return "now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes}m";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours}h";
        return when.LocalDateTime.ToString("d MMM");
    }

    private void NoteDismiss_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: NoteItem item }) return;
        _notes.Remove(item.Id);
        RefreshNotifications();
    }

    private async void NotesClear_Click(object sender, RoutedEventArgs e)
    {
        await _notes.ClearAllAsync();
        RefreshNotifications();
    }
}
