using Avalonia.Media.Fonts;

namespace D2Compare;

// Bundled fonts in Assets/Fonts, referenced as "fonts:D2Compare#<family>"
public sealed class AppFontCollection()
    : EmbeddedFontCollection(
        new Uri("fonts:D2Compare", UriKind.Absolute),
        new Uri("avares://D2Compare/Assets/Fonts", UriKind.Absolute)
    );
