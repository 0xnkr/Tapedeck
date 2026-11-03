namespace Tapedeck.Core.Models;

/// <summary>User corrections. <c>null</c> means no correction, the next best value applies.</summary>
public sealed record UserFields(
    string? Artist,
    string? Title,
    string? Album,
    string? AlbumArtist,
    int? Year,
    string? Notes);
