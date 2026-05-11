using PoeStudio.Contracts;

namespace PoeStudio.Core.Resources;

public static class ResourceClassifier
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xml", ".json", ".txt", ".filter", ".hlsl"
    };

    private static readonly HashSet<string> TableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".datc64", ".dat", ".fmt", ".tdt", ".ot", ".otc"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dds", ".png", ".bmp", ".jpg", ".jpeg"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ogg", ".wav"
    };

    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ttf"
    };

    private static readonly HashSet<string> UiExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ui", ".atlas"
    };

    private static readonly HashSet<string> MaterialExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mat", ".tgm", ".aoc", ".ao", ".fxgraph"
    };

    private static readonly HashSet<string> ModelExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".smd", ".sm", ".amd", ".arm", ".ast", ".pet"
    };

    public static ResourceKind Classify(string virtualPath)
    {
        var extension = Path.GetExtension(virtualPath);

        if (TextExtensions.Contains(extension))
        {
            return ResourceKind.Text;
        }

        if (TableExtensions.Contains(extension))
        {
            return ResourceKind.Table;
        }

        if (ImageExtensions.Contains(extension))
        {
            return ResourceKind.Image;
        }

        if (AudioExtensions.Contains(extension))
        {
            return ResourceKind.Audio;
        }

        if (FontExtensions.Contains(extension))
        {
            return ResourceKind.Font;
        }

        if (UiExtensions.Contains(extension))
        {
            return ResourceKind.Ui;
        }

        if (MaterialExtensions.Contains(extension))
        {
            return ResourceKind.Material;
        }

        if (ModelExtensions.Contains(extension))
        {
            return ResourceKind.Model;
        }

        return ResourceKind.Binary;
    }
}
