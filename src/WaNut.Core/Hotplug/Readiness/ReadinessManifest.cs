using System.Text.Json;

namespace WaNut.Core.Hotplug.Readiness;

/// <summary>Reads a manifest: <c>{"files":["a.zip","b.zip"]}</c>, naming the files of one batch.</summary>
public static class ReadinessManifest
{
    /// <returns>The file names, or null with <paramref name="problem"/> set when the text isn't a usable manifest.</returns>
    public static IReadOnlyList<string>? TryRead(string json, out string? problem)
    {
        problem = null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("files", out var files)
                || files.ValueKind != JsonValueKind.Array)
            {
                problem = "The manifest needs a \"files\" list.";
                return null;
            }

            var names = new List<string>();
            foreach (var item in files.EnumerateArray())
            {
                var name = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) || name.AsSpan().IndexOfAny('/', '\\', '\0') >= 0 || name is "." or "..")
                {
                    problem = "Every name in the manifest must be a plain file name.";
                    return null;
                }

                names.Add(name);
            }

            if (names.Count == 0)
            {
                problem = "The manifest names no files.";
                return null;
            }

            return names.Distinct(StringComparer.Ordinal).ToList();
        }
        catch (JsonException e)
        {
            problem = "The manifest isn't valid JSON: " + e.Message;
            return null;
        }
    }
}
