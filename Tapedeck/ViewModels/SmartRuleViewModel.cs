using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tapedeck.Core.Playlists;
using Tapedeck.Resources;

namespace Tapedeck.ViewModels;

/// <summary>One entry of a choice menu (field, operator, order); the header shows, the command picks it.</summary>
public sealed record MenuChoice(string Header, IRelayCommand Command);

/// <summary>The words the smart playlist editor shows for fields, operators and orders.</summary>
public static class SmartLabels
{
    public static string Of(SmartField field) => field switch
    {
        SmartField.Artist => Strings.Field_Artist,
        SmartField.Title => Strings.Field_Title,
        SmartField.Album => Strings.Field_Album,
        SmartField.AlbumArtist => Strings.Field_AlbumArtist,
        SmartField.Genre => Strings.Smart_FieldGenre,
        SmartField.MainGenre => Strings.Smart_FieldMainGenre,
        SmartField.Year => Strings.Field_Year,
        SmartField.Rating => Strings.Field_Rating,
        _ => Strings.Smart_FieldAdded,
    };

    public static string Of(SmartOperator op) => op switch
    {
        SmartOperator.Contains => Strings.Smart_OpContains,
        SmartOperator.NotContains => Strings.Smart_OpNotContains,
        SmartOperator.Is => Strings.Smart_OpIs,
        SmartOperator.IsNot => Strings.Smart_OpIsNot,
        SmartOperator.AtLeast => Strings.Smart_OpAtLeast,
        SmartOperator.AtMost => Strings.Smart_OpAtMost,
        _ => Strings.Smart_OpWithinDays,
    };

    public static string Of(SmartSort sort) => sort switch
    {
        SmartSort.Artist => Strings.Smart_SortArtist,
        SmartSort.Album => Strings.Smart_SortAlbum,
        SmartSort.Year => Strings.Smart_SortYear,
        SmartSort.Rating => Strings.Smart_SortRating,
        _ => Strings.Smart_SortNewest,
    };
}

/// <summary>One rule in the smart playlist editor: field, operator and value. Any change updates the preview.</summary>
public partial class SmartRuleViewModel : ViewModelBase
{
    private readonly Action changed;
    private readonly Action<SmartRuleViewModel> remove;

    public SmartRuleViewModel(SmartRule rule, Action changed, Action<SmartRuleViewModel> remove)
    {
        this.changed = changed;
        this.remove = remove;
        this.FieldChoices = Enum.GetValues<SmartField>()
            .Select(f => new MenuChoice(SmartLabels.Of(f), new RelayCommand(() => this.Field = f)))
            .ToList();
        this.Value = rule.Value;
        this.Field = rule.Field;
        this.FillOperators();
        this.Operator = SmartRules.OperatorsFor(rule.Field).Contains(rule.Operator)
            ? rule.Operator
            : SmartRules.OperatorsFor(rule.Field)[0];
    }

    public IReadOnlyList<MenuChoice> FieldChoices { get; }

    /// <summary>Only the operators that fit the chosen field.</summary>
    public ObservableCollection<MenuChoice> OperatorChoices { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FieldText), nameof(ValuePlaceholder))]
    public partial SmartField Field { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperatorText))]
    public partial SmartOperator Operator { get; set; }

    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    public string FieldText => SmartLabels.Of(this.Field);

    public string OperatorText => SmartLabels.Of(this.Operator);

    /// <summary>An example that shows what to type.</summary>
    public string ValuePlaceholder => this.Field switch
    {
        SmartField.Year => Strings.Smart_PlaceholderYear,
        SmartField.Rating => Strings.Smart_PlaceholderRating,
        SmartField.Added => Strings.Smart_PlaceholderDays,
        SmartField.Genre => Strings.Smart_PlaceholderGenre,
        SmartField.MainGenre => Strings.Smart_PlaceholderMainGenre,
        _ => Strings.Smart_PlaceholderText,
    };

    public SmartRule ToRule() => new(this.Field, this.Operator, this.Value.Trim());

    [RelayCommand]
    private void Remove() => this.remove(this);

    partial void OnFieldChanged(SmartField value)
    {
        this.FillOperators();
        // an operator that does not fit the new field falls back to the field's default
        if (!SmartRules.OperatorsFor(value).Contains(this.Operator))
            this.Operator = SmartRules.OperatorsFor(value)[0];
        this.changed();
    }

    partial void OnOperatorChanged(SmartOperator value) => this.changed();

    partial void OnValueChanged(string value) => this.changed();

    private void FillOperators()
    {
        this.OperatorChoices.Clear();
        foreach (var op in SmartRules.OperatorsFor(this.Field))
            this.OperatorChoices.Add(new MenuChoice(SmartLabels.Of(op), new RelayCommand(() => this.Operator = op)));
    }
}
