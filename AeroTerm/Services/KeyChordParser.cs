// <copyright file="KeyChordParser.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia.Input;

/// <summary>
/// Parses and serializes <see cref="KeyChord"/> values using a small DSL
/// such as <c>"Cmd+Shift+T"</c>, <c>"Ctrl+PageDown"</c>, <c>"Cmd+1"</c>.
/// </summary>
public static class KeyChordParser
{
    private static readonly Dictionary<string, Key> NamedKeys =
        new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase)
        {
            ["tab"] = Key.Tab,
            ["enter"] = Key.Enter,
            ["return"] = Key.Return,
            ["escape"] = Key.Escape,
            ["esc"] = Key.Escape,
            ["space"] = Key.Space,
            ["pageup"] = Key.PageUp,
            ["pagedown"] = Key.PageDown,
            ["home"] = Key.Home,
            ["end"] = Key.End,
            ["insert"] = Key.Insert,
            ["delete"] = Key.Delete,
            ["backspace"] = Key.Back,
            ["left"] = Key.Left,
            ["right"] = Key.Right,
            ["up"] = Key.Up,
            ["down"] = Key.Down,
            ["comma"] = Key.OemComma,
            ["period"] = Key.OemPeriod,
            ["minus"] = Key.OemMinus,
            ["plus"] = Key.OemPlus,
        };

    /// <summary>
    /// Attempts to parse a chord string into a <see cref="KeyChord"/>.
    /// Returns <see langword="false"/> (with <paramref name="chord"/> set to
    /// <see langword="null"/>) rather than throwing on unparseable input.
    /// </summary>
    /// <param name="text">The chord text.</param>
    /// <param name="chord">The parsed chord on success.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? text, out KeyChord? chord)
    {
        chord = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var modifiers = KeyModifiers.None;
        Key? key = null;

        foreach (var token in parts)
        {
            if (TryParseModifier(token, out var mod))
            {
                if ((modifiers & mod) != 0)
                {
                    return false;
                }

                modifiers |= mod;
                continue;
            }

            if (key.HasValue)
            {
                return false;
            }

            if (!TryParseKey(token, out var parsedKey))
            {
                return false;
            }

            key = parsedKey;
        }

        if (!key.HasValue)
        {
            return false;
        }

        chord = new KeyChord(modifiers, key.Value);
        return true;
    }

    /// <summary>
    /// Serializes a <see cref="KeyChord"/> into the DSL form. Modifier
    /// order is always <c>Ctrl</c>, <c>Alt</c>, <c>Shift</c>, <c>Cmd</c>.
    /// </summary>
    /// <param name="chord">The chord to serialize.</param>
    /// <returns>A parseable chord string.</returns>
    public static string Serialize(KeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        var sb = new StringBuilder();
        if (chord.Modifiers.HasFlag(KeyModifiers.Control))
        {
            sb.Append("Ctrl+");
        }

        if (chord.Modifiers.HasFlag(KeyModifiers.Alt))
        {
            sb.Append("Alt+");
        }

        if (chord.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            sb.Append("Shift+");
        }

        if (chord.Modifiers.HasFlag(KeyModifiers.Meta))
        {
            sb.Append("Cmd+");
        }

        sb.Append(SerializeKey(chord.Key));
        return sb.ToString();
    }

    private static bool TryParseModifier(string token, out KeyModifiers modifier)
    {
        switch (token.ToLowerInvariant())
        {
            case "ctrl":
            case "control":
                modifier = KeyModifiers.Control;
                return true;
            case "shift":
                modifier = KeyModifiers.Shift;
                return true;
            case "alt":
            case "option":
            case "opt":
                modifier = KeyModifiers.Alt;
                return true;
            case "cmd":
            case "command":
            case "meta":
            case "super":
            case "win":
                modifier = KeyModifiers.Meta;
                return true;
            default:
                modifier = KeyModifiers.None;
                return false;
        }
    }

    private static bool TryParseKey(string token, out Key key)
    {
        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z')
            {
                key = (Key)((int)Key.A + (c - 'A'));
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                key = (Key)((int)Key.D0 + (c - '0'));
                return true;
            }
        }

        if (token.Length == 2 && (token[0] is 'D' or 'd') && token[1] is >= '0' and <= '9')
        {
            key = (Key)((int)Key.D0 + (token[1] - '0'));
            return true;
        }

        if ((token.Length == 2 || token.Length == 3)
            && (token[0] is 'F' or 'f')
            && int.TryParse(token.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int fn)
            && fn is >= 1 and <= 24)
        {
            key = (Key)((int)Key.F1 + (fn - 1));
            return true;
        }

        if (NamedKeys.TryGetValue(token, out var named))
        {
            key = named;
            return true;
        }

        key = default;
        return false;
    }

    private static string SerializeKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return ((char)('A' + (key - Key.A))).ToString();
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return "D" + (char)('0' + (key - Key.D0));
        }

        if (key is >= Key.F1 and <= Key.F24)
        {
            return "F" + ((int)key - (int)Key.F1 + 1).ToString(CultureInfo.InvariantCulture);
        }

        return key switch
        {
            Key.OemComma => "Comma",
            Key.OemPeriod => "Period",
            Key.OemMinus => "Minus",
            Key.OemPlus => "Plus",
            _ => key.ToString(),
        };
    }
}
