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
    private List<List<FormattedLine>>? _sections;

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
            ItemTemplate = new FuncDataTemplate<List<FormattedLine>>(BuildSectionBlock, supportsRecycling: false),
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
        _sections = Document is null ? null : SplitIntoSections(Document.Lines);
        _itemsControl.ItemsSource = _sections;
    }

    // Split lines into sections at FileName/FileNameMuted boundaries.
    // Single-file mode (no file-name lines) → one section → full multi-line selection.
    // Batch mode → one section per file → selection within each file, virtualized across files.
    private static List<List<FormattedLine>> SplitIntoSections(IReadOnlyList<FormattedLine> lines)
    {
        var sections = new List<List<FormattedLine>>();
        List<FormattedLine>? current = null;

        foreach (var line in lines)
        {
            bool isFileHeader = line.Spans.Count > 0
                && line.Spans[0].Color is SpanColor.FileName or SpanColor.FileNameMuted;

            if (isFileHeader)
            {
                current = new List<FormattedLine> { line };
                sections.Add(current);
            }
            else
            {
                // No file header yet (single-file mode) — create initial section
                if (current is null)
                {
                    current = new List<FormattedLine>();
                    sections.Add(current);
                }
                current.Add(line);
            }
        }

        return sections;
    }

    // Update only realized (visible) containers in-place via ContainerFromIndex.
    // No ItemsSource reassignment — no virtualizer rebuild — instant.
    // Newly scrolled items pick up current SearchTerm/theme via BuildSectionBlock.
    private void UpdateRealizedItems()
    {
        if (_sections is null) return;

        var isDark = ActualThemeVariant == ThemeVariant.Dark;
        var searchTerm = SearchTerm;

        for (int i = 0; i < _itemsControl.ItemCount; i++)
        {
            var container = _itemsControl.ContainerFromIndex(i);
            if (container is null) continue;

            var tb = FindChildTextBlock(container);
            if (tb is null) continue;

            if (_itemsControl.Items[i] is not List<FormattedLine> section) continue;

            tb.Inlines = BuildSectionInlines(section, searchTerm, isDark);
        }
    }

    private static SelectableTextBlock? FindChildTextBlock(Control container)
    {
        foreach (var child in container.GetVisualChildren())
        {
            if (child is SelectableTextBlock tb)
                return tb;
        }
        return null;
    }

    private Control BuildSectionBlock(List<FormattedLine> section, INameScope _)
    {
        var tb = new SelectableTextBlock { TextWrapping = TextWrapping.NoWrap };

        if (section.Count == 0)
            return tb;

        tb.Inlines = BuildSectionInlines(section, SearchTerm, ActualThemeVariant == ThemeVariant.Dark);
        return tb;
    }

    private static InlineCollection BuildSectionInlines(List<FormattedLine> section, string? searchTerm, bool isDark)
    {
        var inlines = new InlineCollection();

        for (int i = 0; i < section.Count; i++)
        {
            if (i > 0)
                inlines.Add(new LineBreak());

            foreach (var span in section[i].Spans)
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
