using System.Text;

namespace Tapedeck.Core.Csv;

/// <summary>Broken CSV syntax; <see cref="Line"/> is 1-based.</summary>
public sealed class CsvFormatException(int line, string message) : FormatException(message)
{
    public int Line { get; } = line;
}

/// <summary>A parsed CSV record and the line it starts on (1-based, for error messages).</summary>
public sealed record CsvRecord(int Line, IReadOnlyList<string> Fields);

/// <summary>
/// Minimal RFC 4180 CSV: comma separated, fields with commas, quotes or line breaks in double quotes,
/// quotes doubled inside. Files are UTF-8 with BOM so Excel shows umlauts correctly.
/// </summary>
public static class CsvFormat
{
    /// <summary>UTF-8 with BOM.</summary>
    public static readonly Encoding ENCODING = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static void WriteRecord(TextWriter writer, IEnumerable<string?> fields)
    {
        writer.Write(string.Join(',', fields.Select(Escape)));
        writer.Write("\r\n");
    }

    /// <summary>Reads all records; a malformed record throws <see cref="CsvFormatException"/>.</summary>
    public static IEnumerable<CsvRecord> Read(TextReader reader)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var recordLine = 1;
        var inQuotes = false;
        var fieldStarted = false;

        while (reader.Read() is var read and >= 0)
        {
            var c = (char)read;
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                        if (reader.Peek() is not (',' or '\r' or '\n' or -1))
                            throw new CsvFormatException(line, "Unexpected character after a closing quote.");
                    }
                }
                else
                {
                    if (c == '\n')
                        line++;
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    fieldStarted = true;
                    break;
                case '"':
                    throw new CsvFormatException(line, "Quote inside an unquoted field.");
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    fieldStarted = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    if (fieldStarted || field.Length > 0 || fields.Count > 0)
                    {
                        fields.Add(field.ToString());
                        yield return new CsvRecord(recordLine, fields);
                    }
                    fields = [];
                    field.Clear();
                    fieldStarted = false;
                    line++;
                    recordLine = line;
                    break;
                default:
                    field.Append(c);
                    fieldStarted = true;
                    break;
            }
        }

        if (inQuotes)
            throw new CsvFormatException(recordLine, "Quoted field is not closed.");
        if (fieldStarted || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return new CsvRecord(recordLine, fields);
        }
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? '"' + value.Replace("\"", "\"\"") + '"'
            : value;
    }
}
