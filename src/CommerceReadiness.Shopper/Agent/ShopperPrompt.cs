namespace CommerceReadiness.Shopper.Agent;

/// <summary>The versioned system instructions. Changing the text requires a new version and resource.</summary>
public static class ShopperPrompt
{
    public const string Version = "shopper-prompt-v2";

    public static string Text { get; } = Load();

    private static string Load()
    {
        using var stream = typeof(ShopperPrompt).Assembly.GetManifestResourceStream($"{Version}.md")
            ?? throw new InvalidOperationException($"Embedded prompt '{Version}.md' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }
}
