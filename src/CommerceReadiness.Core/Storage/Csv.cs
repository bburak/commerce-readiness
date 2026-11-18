using System.Text;

namespace CommerceReadiness.Core.Storage;

/// <summary>Minimal RFC 4180 CSV with LF line endings.</summary>
public static class Csv
{
    public static string Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        AppendRow(builder, header);
        foreach (var row in rows)
        {
            AppendRow(builder, row);
        }

        return builder.ToString();
    }

    public static List<Dictionary<string, string>> Read(string text)
    {
        var records = Parse(text);
        if (records.Count == 0)
        {
            return [];
        }

        var header = records[0];
        return records.Skip(1)
            .Select(r => header.Select((name, i) => (name, value: r[i])).ToDictionary(x => x.name, x => x.value, StringComparer.Ordinal))
            .ToList();
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string> row)
    {
        builder.AppendJoin(',', row.Select(Escape));
        builder.Append('\n');
    }

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;

    private static List<List<string>> Parse(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = [];
                    break;
                case '\r':
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }
}
