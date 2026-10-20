using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ClipShelf;

namespace ClipShelfLayoutTests;

// Runs on real Windows WPF, using MainWindow's actual XAML, ItemsPanel,
// ItemContainerStyle, converters, bindings and routed button event handlers.
// Never copies user phrases or writes their text to a log.
static class Program
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly List<object> Evidence = new();
    private static int Assertions;
    private static void Stage(string label)
    {
        Directory.CreateDirectory("validation");
        File.AppendAllText("validation/layout-stage.log", $"{DateTime.UtcNow:O} {label}\n");
    }

    [STAThread]
    public static int Main()
    {
        int exit = 0;
        Stage("ENTER Main");
        try
        {
            Stage("before WPF Application");
            // Never instantiate ClipShelf.App here: its OnStartup launches a SECOND
            // MainWindow when the test pumps Dispatcher, competing for UiServer.
            // A plain WPF Application plus exact production resource values allows
            // the actual MainWindow and templates to be tested once in isolation.
            var app = new System.Windows.Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var brushes = new Dictionary<string, string>
            {
                ["AccentBrush"] = "#0D9488", ["AccentSoftBrush"] = "#CCFBF1",
                ["TextBrush"] = "#16202A", ["MutedBrush"] = "#708090",
                ["LineBrush"] = "#E5E7EB", ["SurfaceBrush"] = "#F8FAFC"
            };
            foreach (var pair in brushes)
                app.Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
            Stage("after WPF Application; before MainWindow");
            Environment.SetEnvironmentVariable("CLIPSHELF_GEOMETRY_TEST","1");

            var window = new MainWindow { Width = 1120, Height = 780, ShowInTaskbar = false };
            Stage("after MainWindow construction");
            var list = (ListBox?)window.FindName("HistoryList")
                ?? throw new InvalidOperationException("Actual HistoryList not found");
            var items = (ObservableCollection<ClipItem>?)typeof(MainWindow).GetField("_visibleItems", Private)?.GetValue(window)
                ?? throw new InvalidOperationException("Production backing collection not found");
            items.Clear();
            for (int i = 0; i < 3; i++)
                items.Add(new ClipItem { Title = $"Layout sample {i + 1}", Text = $"short text {i + 1}",
                    Kind = "Text", HistoryVisible = true, LastUsedAt = DateTime.Now.AddMinutes(-i) });

            Stage("before Window.Show");
            window.Show();
            Stage("after Window.Show; before first dispatcher Pump");
            Pump(window);
            Stage("after first dispatcher Pump");
            Verify(window, list, "initial-vertical", false, false);
            Stage("before first GridLayoutButton");
            Press(window, "GridLayoutButton");
            Stage("after first GridLayoutButton; before Pump");
            Pump(window);
            Stage("after first grid Pump");
            Verify(window, list, "three-short-cards-wide", true, true);

            for (int i = 0; i < 20; i++)
            {
                Press(window, "VerticalLayoutButton");
                Pump(window);
                Verify(window, list, $"repeat-{i + 1}-vertical", false, false);
                Press(window, "GridLayoutButton");
                Pump(window);
                Verify(window, list, $"repeat-{i + 1}-grid", true, true);
            }
            window.Width = 640;
            Pump(window);
            Verify(window, list, "narrow-one-column", true, false);
            window.Width = 1120;
            Pump(window);
            Verify(window, list, "wide-restores-two-columns", true, true);

            // The non-virtualized card panel must NEVER see the full history.
            items.Clear();
            for (int i = 0; i < 63; i++)
                items.Add(new ClipItem { Title = $"Fixture {i+1}", Text = "minimal fixture",
                    Kind = "Text", HistoryVisible = true, LastUsedAt = DateTime.Now.AddMinutes(-i) });
            // Refresh the bounded production projection through the real mode switch.
            Press(window, "VerticalLayoutButton");
            Pump(window);
            Press(window, "GridLayoutButton");
            Pump(window);
            Assert(list.Items.Count <= CardGridPaging.PageSize, "grid bounds loaded cards to 24");
            Evidence.Add(new { step = "sixty-three-records", loaded = list.Items.Count,
                sourceCount = items.Count, pass = true });
            Verify(window, list, "many-records-grid", true, true);

            Stage("all layout verifications finished");
            Console.WriteLine($"WPF GEOMETRY PASS: {Assertions} actual-window assertions");
        }
        catch (Exception ex)
        {
            exit = 1;
            Stage("FAIL " + ex.GetType().Name + " " + ex.Message);
            Console.Error.WriteLine("WPF GEOMETRY FAIL: " + ex);
        }
        finally
        {
            Directory.CreateDirectory("validation");
            File.WriteAllText("validation/layout-geometry.json",
                JsonSerializer.Serialize(new { status = exit == 0 ? "PASS" : "FAIL",
                    assertions = Assertions, evidence = Evidence },
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        // A dedicated CI process has created the actual MainWindow. Exit without
        // the user-facing Close workflow or keeping background services alive.
        Environment.Exit(exit);
        return exit;
    }

    private static void Press(MainWindow window, string buttonName)
    {
        var button = (RadioButton?)window.FindName(buttonName)
            ?? throw new InvalidOperationException(buttonName + " missing");
        button.IsChecked = true;
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }

    private static T? FindVisual<T>(DependencyObject root, Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && (predicate is null || predicate(match))) return match;
            var found = FindVisual(child, predicate);
            if (found is not null) return found;
        }
        return null;
    }

    private static void Verify(Window window, ListBox list, string name, bool grid, bool twoColumns)
    {
        var panel = FindVisual<Panel>(list, p => p.IsItemsHost)
            ?? throw new Exception($"{name}: ItemsHost panel not realized");
        var panelType = panel.GetType().Name;
        Assert(panelType == (grid ? "WrapPanel" : "VirtualizingStackPanel"),
            $"{name}: actual items panel is {panelType}");

        var first = new List<Rect>();
        var widths = new List<double>();
        var widthSources = new List<string>();
        for (int i = 0; i < Math.Min(3, list.Items.Count); i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem row)
                throw new Exception($"{name}: actual item container {i} not realized");
            var xy = row.TransformToAncestor(list).Transform(new Point());
            first.Add(new Rect(xy.X, xy.Y, row.ActualWidth, row.ActualHeight));
            widths.Add(row.ActualWidth);
            var value = DependencyPropertyHelper.GetValueSource(row, FrameworkElement.WidthProperty);
            var bind = BindingOperations.GetBindingExpressionBase(row, FrameworkElement.WidthProperty);
            widthSources.Add($"{value.BaseValueSource}/expression={value.IsExpression}/binding={bind?.Status.ToString() ?? "none"}");
        }
        var viewer = FindVisual<ScrollViewer>(list);
        var viewport = viewer?.ViewportWidth ?? list.ActualWidth;
        var sample = new {
            step = name, mode = grid ? "Grid" : "List", panelType, 
            listWidth = list.ActualWidth, panelWidth = panel.ActualWidth,
            viewportWidth = viewport, dpi = VisualTreeHelper.GetDpi(list).DpiScaleX,
            cardBounds = first.Select(r => new { x = r.X, y = r.Y,
                width = r.Width, height = r.Height }), widthSources
        };
        Evidence.Add(sample);
        Console.WriteLine(JsonSerializer.Serialize(sample));

        if (first.Count >= 2)
        {
            if (twoColumns)
            {
                Assert(Math.Abs(first[0].Y - first[1].Y) <= 3,
                    $"{name}: first and second cards share the same Y row");
                Assert(first[1].X > first[0].Right + 1,
                    $"{name}: second card begins right of first");
                Assert(first[1].Right <= list.ActualWidth + 3,
                    $"{name}: second card fits list width");
            }
            else
                Assert(first[1].Y > first[0].Y + 1,
                    $"{name}: expected vertical stacking");
        }
    }

    private static void Assert(bool condition, string reason)
    {
        if (!condition) throw new Exception(reason);
        Assertions++;
    }
}
