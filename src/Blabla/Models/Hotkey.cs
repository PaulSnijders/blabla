using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace Blabla.Models;

/// <summary>A global hotkey combination, serialized as e.g. "Ctrl+Alt+Space".</summary>
[JsonConverter(typeof(HotkeyJsonConverter))]
public sealed record Hotkey(ModifierKeys Modifiers, Key Key)
{
    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }

    public static Hotkey Parse(string text)
    {
        return TryParse(text, out var hotkey)
            ? hotkey
            : throw new FormatException($"Invalid hotkey: '{text}'");
    }

    public static bool TryParse(string? text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Hotkey? hotkey)
    {
        hotkey = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var modifiers = ModifierKeys.None;
        Key key = Key.None;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; break;
                default:
                    if (key != Key.None || !Enum.TryParse(raw, ignoreCase: true, out key) || key == Key.None)
                        return false;
                    break;
            }
        }

        if (key == Key.None) return false;
        hotkey = new Hotkey(modifiers, key);
        return true;
    }
}

public sealed class HotkeyJsonConverter : JsonConverter<Hotkey>
{
    public override Hotkey? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => Hotkey.TryParse(reader.GetString(), out var hotkey) ? hotkey : null;

    public override void Write(Utf8JsonWriter writer, Hotkey value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
