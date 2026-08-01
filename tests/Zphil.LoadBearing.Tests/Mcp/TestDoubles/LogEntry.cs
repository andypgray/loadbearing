using Microsoft.Extensions.Logging;

namespace Zphil.LoadBearing.Tests.Mcp.TestDoubles;

/// <summary>One captured logger call: its level, formatted message, exception (if any), and category name.</summary>
// The record is a faithful transcript of ILogger.Log; assertions read the parts they need.
// ReSharper disable NotAccessedPositionalProperty.Global
internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception, string Category);