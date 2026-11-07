namespace Tapedeck.Core.Services;

/// <summary>YouTube rate-limits the requests or asks to sign in; further requests only make it worse for a while.</summary>
public sealed class YtThrottledException(string message) : InvalidOperationException($"YouTube is throttling requests: {message}");
