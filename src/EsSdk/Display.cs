using System.Globalization;

namespace EsSdk;

/// <summary>Human-friendly rendering helpers shared by the CLI and the MCP server.</summary>
public static class Display
{
    private static readonly string[] Units = ["KB", "MB", "GB", "TB", "PB"];

    /// <summary>Format a byte count as <c>1.2 MB</c>, <c>512 B</c>, etc.</summary>
    public static string HumanSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes.ToString(CultureInfo.InvariantCulture)} B";

        double value = bytes;
        var unit = -1;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value.ToString("0.#", CultureInfo.InvariantCulture)} {Units[unit]}";
    }
}
