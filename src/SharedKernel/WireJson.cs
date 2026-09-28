using System.Text.Json.Serialization;

namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// How a closed set travels (ADR 0043): as its C# member name, and never as a
/// number. One definition, because the API and the MCP server serialise the same
/// records with two different option sets, and a rule written twice is two rules.
/// </summary>
public static class WireJson
{
    public static JsonConverter Enums() => new JsonStringEnumConverter(allowIntegerValues: false);
}
