using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.VisualTree;

using D2Compare.Core.Models;

namespace D2Compare.Controls;

public class FormattedTextPresenter : UserControl
{
    public static readonly StyledProperty<FormattedDocument?> DocumentProperty =
        AvaloniaProperty.Register<FormattedTextPresenter, FormattedDocument?>(nameof(Document));

    public static readonly StyledProperty<string?> SearchTermProperty =
        AvaloniaProperty.Register<FormattedTextPresenter, string?>(nameof(SearchTerm));

    // Cached immutable brushes — avoids allocations per line
    private static readonly IBrush s_headerLight = new ImmutableSolidColorBrush(Color.FromRgb(25, 25, 112));
    private static readonly IBrush s_headerDark = new ImmutableSolidColorBrush(Color.FromRgb(65, 105, 225));
    private static readonly IBrush s_addedLight = Brushes.Green;
    private static readonly IBrush s_addedDark = new ImmutableSolidColorBrush(Color.FromRgb(80, 200, 120));
    private static readonly IBrush s_removedLight = Brushes.Red;
    private static readonly IBrush s_removedDark = new ImmutableSolidColorBrush(Color.FromRgb(255, 100, 100));
    private static readonly IBrush s_fileNameLight = new ImmutableSolidColorBrush(Color.FromRgb(255, 140, 0));
    private static readonly IBrush s_fileNameDark = new ImmutableSolidColorBrush(Color.FromRgb(255, 180, 70));
    private static readonly IBrush s_fileNameMuted = new ImmutableSolidColorBrush(Color.FromRgb(150, 150, 150));
    private static readonly IBrush s_defaultLight = Brushes.Black;
    private static readonly IBrush s_defaultDark = new ImmutableSolidColorBrush(Color.FromRgb(220, 220, 220));
    private static readonly IBrush s_highlightLight = Brushes.Yellow;
    private static readonly IBrush s_highlightDark = new ImmutableSolidColorBrush(Color.FromRgb(80, 80, 0));
    private static readonly IBrush s_highlightFgDark = Brushes.White;

    private readonly ItemsControl _itemsControl;

    public FormattedDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public string? SearchTerm
    {
        get => GetValue(SearchTermProperty);
        set => SetValue(SearchTermProperty, value);
    }

    public FormattedTextPresenter()
    {
        _itemsControl = new ItemsControl
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<FormattedLine>(BuildLineBlock, supportsRecycling: false),
        };

        // Template with ScrollViewer so VirtualizingStackPanel can get viewport info
        _itemsControl.Template = new FuncControlTemplate<ItemsControl>((_, scope) =>
        {
            var presenter = new ItemsPresenter { Name = "PART_ItemsPresenter" };
            presenter.RegisterInNameScope(scope);
            return new ScrollViewer
            {
                Content = presenter,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            };
        });

        Content = _itemsControl;
    }

    static FormattedTextPresenter()
    {
        DocumentProperty.Changed.AddClassHandler<FormattedTextPresenter>((x, _) => x.OnDocumentChanged());
        SearchTermProperty.Changed.AddClassHandler<FormattedTextPresenter>((x, _) => x.UpdateRealizedItems());
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property.Name == "ActualThemeVariant")
            UpdateRealizedItems();
    }

    private void OnDocumentChanged()
    {
        _itemsControl.ItemsSource = Document?.Lines;
    }

    /// <summary>
    /// Update only realized (visible) containers in-place via ContainerFromIndex.
    /// No ItemsSource reassignment — no virtualizer rebuild — instant.
    /// Newly scrolled items pick up current SearchTerm/theme via BuildLineBlock.
    /// </summary>
    private void UpdateRealizedItems()
    {
        if (Document is null) return;

        var isDark = ActualThemeVariant == ThemeVariant.Dark;
        var searchTerm = SearchTerm;

        for (int i = 0; i < _itemsControl.ItemCount; i++)
        {
            var container = _itemsControl.ContainerFromIndex(i);
            if (container is null) continue; // not realized (off-screen)

            // Find the TextBlock and its data item
            var tb = FindChildTextBlock(container);
            if (tb is null) continue;

            var line = _itemsControl.Items[i] as FormattedLine;
            if (line is null) continue;

            // Set a NEW InlineCollection to guarantee visual update
            tb.Inlines = BuildInlines(line, searchTerm, isDark);
        }
    }

    private static TextBlock? FindChildTextBlock(Control container)
    {
        // Container is typically ContentPresenter; TextBlock is its direct visual child
        foreach (var child in container.GetVisualChildren())
        {
            if (child is TextBlock tb)
                return tb;
        }
        return null;
    }

    private Control BuildLineBlock(FormattedLine line, INameScope _)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.NoWrap };

        if (line.Spans.Count == 0)
            return tb;

        tb.Inlines = BuildInlines(line, SearchTerm, ActualThemeVariant == ThemeVariant.Dark);
        return tb;
    }

    private static InlineCollection BuildInlines(FormattedLine line, string? searchTerm, bool isDark)
    {
        var inlines = new InlineCollection();

        foreach (var span in line.Spans)
        {
            var foreground = ResolveColor(span.Color, isDark);
            var fontWeight = span.Style == SpanStyle.Bold ? FontWeight.Bold : FontWeight.Normal;
            var fontSize = span.Color is SpanColor.FileName or SpanColor.FileNameMuted ? 15d : 13d;

            if (!string.IsNullOrEmpty(searchTerm) && span.Text.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            {
                AddHighlightedRuns(inlines, span.Text, searchTerm, foreground, fontWeight, fontSize, isDark);
            }
            else
            {
                inlines.Add(new Run(span.Text)
                {
                    Foreground = foreground,
                    FontWeight = fontWeight,
                    FontSize = fontSize,
                });
            }
        }

        return inlines;
    }

    private static void AddHighlightedRuns(InlineCollection inlines, string text, string term, IBrush foreground, FontWeight weight, double fontSize, bool isDark)
    {
        int pos = 0;
        var highlightBg = isDark ? s_highlightDark : s_highlightLight;
        var highlightFg = isDark ? s_highlightFgDark : foreground;

        while (pos < text.Length)
        {
            int matchIdx = text.IndexOf(term, pos, StringComparison.OrdinalIgnoreCase);
            if (matchIdx == -1)
            {
                inlines.Add(new Run(text[pos..])
                {
                    Foreground = foreground,
                    FontWeight = weight,
                    FontSize = fontSize,
                });
                break;
            }

            if (matchIdx > pos)
            {
                inlines.Add(new Run(text[pos..matchIdx])
                {
                    Foreground = foreground,
                    FontWeight = weight,
                    FontSize = fontSize,
                });
            }

            inlines.Add(new Run(text[matchIdx..(matchIdx + term.Length)])
            {
                Foreground = highlightFg,
                FontWeight = weight,
                FontSize = fontSize,
                Background = highlightBg,
            });

            pos = matchIdx + term.Length;
        }
    }

    private static IBrush ResolveColor(SpanColor color, bool isDark) => color switch
    {
        SpanColor.Header when isDark => s_headerDark,
        SpanColor.Header => s_headerLight,
        SpanColor.Added when isDark => s_addedDark,
        SpanColor.Added => s_addedLight,
        SpanColor.Removed when isDark => s_removedDark,
        SpanColor.Removed => s_removedLight,
        SpanColor.FileName when isDark => s_fileNameDark,
        SpanColor.FileName => s_fileNameLight,
        SpanColor.FileNameMuted => s_fileNameMuted,
        SpanColor.Default when isDark => s_defaultDark,
        SpanColor.Default => s_defaultLight,
        _ => s_defaultLight,
    };
}