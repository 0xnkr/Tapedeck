using Tapedeck.Core.Genres;
using Tapedeck.Core.Models;

namespace Tapedeck.Tests;

/// <summary>Mapping fine genres onto the few main genres that go into the files.</summary>
public sealed class GenreMapTests
{
    [Theory]
    [InlineData("Hip Hop", "Hip-Hop")]
    [InlineData("West Coast Hip Hop", "Hip-Hop")]
    [InlineData("G-Funk", "Hip-Hop")]
    [InlineData("Jazz Rap", "Hip-Hop")]
    [InlineData("Trap", "Hip-Hop")]
    [InlineData("Deep House", "Electronic")]
    [InlineData("Drum & Bass", "Electronic")]
    [InlineData("Dubstep", "Electronic")]
    [InlineData("Dance-Pop", "Pop")]
    [InlineData("Electropop", "Pop")]
    [InlineData("Pop Rock", "Rock")]
    [InlineData("Pop Punk", "Punk")]
    [InlineData("Garage Rock", "Rock")]
    [InlineData("Nu Metal", "Metal")]
    [InlineData("Indie Pop", "Pop")]
    [InlineData("Alternative Rock", "Rock")]
    [InlineData("Indie Folk", "Country & Folk")]
    [InlineData("Indietronica", "Electronic")]
    [InlineData("Trip Hop", "Electronic")]
    [InlineData("Dance-Rock", "Rock")]
    [InlineData("Contemporary R&B", "R&B")]
    [InlineData("Neo Soul", "Soul & Funk")]
    [InlineData("K-Pop", "K-Pop")]
    [InlineData("Anime", "Anime & J-Pop")]
    [InlineData("Reggaeton", "Latin")]
    [InlineData("Latin Trap", "Latin")]
    [InlineData("Dancehall", "Reggae")]
    [InlineData("Schlager", "Schlager")]
    [InlineData("Film Score", "Soundtrack")]
    [InlineData("Emo Rap", "Hip-Hop")]
    public void Fine_genres_have_a_main_genre(string fine, string main) =>
        Assert.Equal(main, GenreMap.Of(fine)?.Name);

    [Theory]
    [InlineData("seen live")]
    [InlineData("favorites")]
    [InlineData("female vocalists")]
    [InlineData("00s")]
    [InlineData("Entertainment")]
    [InlineData("")]
    public void Noise_has_no_main_genre(string tag) => Assert.Null(GenreMap.Of(tag));

    [Theory]
    [InlineData("Hip-Hop")]
    [InlineData("hip hop")]
    [InlineData("Anime & J-Pop")]
    [InlineData("Soul & Funk")]
    public void A_main_genre_maps_to_itself(string name) => Assert.Contains(GenreMap.Of(name)!.Name, GenreMap.ALL.Select(g => g.Name));

    [Fact]
    public void Several_fine_genres_of_one_style_make_one_main_genre() =>
        Assert.Equal(["Hip-Hop"], GenreMap.MainGenres(["Conscious Hip Hop", "Hip Hop", "Jazz Rap"]));

    [Fact]
    public void A_scene_comes_first_even_when_broader_genres_are_more()
    {
        Assert.Equal(["K-Pop", "Pop"], GenreMap.MainGenres(["Dance-Pop", "Electropop", "K-Pop"]));
    }

    [Fact]
    public void A_second_main_genre_needs_half_as_many_fine_genres()
    {
        Assert.Equal(["Electronic"], GenreMap.MainGenres(["Deep House", "House", "Tech House", "Pop"]));
        Assert.Equal(["Electronic", "Pop"], GenreMap.MainGenres(["Deep House", "House", "Pop"]));
    }

    [Fact]
    public void At_most_two_main_genres() =>
        Assert.Equal(2, GenreMap.MainGenres(["Rock", "Jazz", "Hip Hop", "Reggae"]).Count);

    [Fact]
    public void Noise_alone_gives_no_main_genre() =>
        Assert.Empty(GenreMap.MainGenres(["seen live", "favorites"]));

    [Fact]
    public void Track_shows_the_main_genres_of_its_genres()
    {
        var track = new TrackDisplay { Genres = "G-Funk;Hip Hop;West Coast Hip Hop" };

        Assert.Equal(["Hip-Hop"], track.MainGenreList);
    }
}
