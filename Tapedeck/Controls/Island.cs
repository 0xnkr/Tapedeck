using Avalonia.Controls;

namespace Tapedeck.Controls;

/// <summary>
/// A rounded surface that holds content (table, sections, link bar). Content never sits directly on the background.
/// Look and padding come from its ControlTheme; class <c>compact</c> uses the smaller padding.
/// </summary>
public sealed class Island : ContentControl;
