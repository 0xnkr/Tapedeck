using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Tapedeck.ViewModels;

namespace Tapedeck;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    // one view per view model: going back to a page shows the same view again, with its scroll position
    // and focus, instead of a new one that starts at the top
    private static readonly ConditionalWeakTable<object, Control> VIEWS = new();

    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        // a view still shown elsewhere cannot be shown twice; then a new one is built
        if (VIEWS.TryGetValue(param, out var cached) && cached.Parent is null)
            return cached;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = MobileView(param.GetType()) ?? Type.GetType(name);

        if (type != null)
        {
            var view = (Control)Activator.CreateInstance(type)!;
            VIEWS.AddOrUpdate(param, view);
            return view;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    /// <summary>
    /// On a phone the view built for it, where there is one: <c>Tapedeck.Views.Mobile.LibraryMobileView</c> for the
    /// <c>LibraryViewModel</c>. The other pages use the same view everywhere.
    /// </summary>
    private static Type? MobileView(Type viewModel) =>
        OperatingSystem.IsAndroid()
            ? Type.GetType($"Tapedeck.Views.Mobile.{viewModel.Name.Replace("ViewModel", "MobileView", StringComparison.Ordinal)}")
            : null;

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}