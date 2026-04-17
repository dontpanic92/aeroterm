// <copyright file="HotkeyKeyMap.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.WindowEffects.QuakeHotkey;

using Avalonia.Input;

/// <summary>
/// Translates Avalonia <see cref="Key"/> / <see cref="KeyModifiers"/>
/// values into the native virtual-key and modifier masks expected by
/// the Windows (<c>RegisterHotKey</c>) and macOS (Carbon
/// <c>RegisterEventHotKey</c>) backends.
/// </summary>
internal static class HotkeyKeyMap
{
    /// <summary>
    /// Converts an Avalonia <see cref="Key"/> to a Windows virtual-key
    /// code. Returns <c>0</c> when the key is unsupported for global
    /// registration.
    /// </summary>
    /// <param name="key">The Avalonia key.</param>
    /// <returns>A Win32 VK_ code, or <c>0</c>.</returns>
    public static uint ToWin32VirtualKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            return (uint)('A' + (key - Key.A));
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            return (uint)('0' + (key - Key.D0));
        }

        if (key >= Key.F1 && key <= Key.F24)
        {
            // VK_F1 = 0x70
            return 0x70u + (uint)(key - Key.F1);
        }

        return key switch
        {
            Key.OemTilde or Key.Oem3 => 0xC0u,
            Key.OemMinus => 0xBDu,
            Key.OemPlus => 0xBBu,
            Key.OemComma => 0xBCu,
            Key.OemPeriod => 0xBEu,
            Key.Space => 0x20u,
            Key.Tab => 0x09u,
            Key.Enter => 0x0Du,
            Key.Escape => 0x1Bu,
            Key.Home => 0x24u,
            Key.End => 0x23u,
            Key.PageUp => 0x21u,
            Key.PageDown => 0x22u,
            Key.Insert => 0x2Du,
            Key.Delete => 0x2Eu,
            _ => 0u,
        };
    }

    /// <summary>
    /// Converts an Avalonia <see cref="KeyModifiers"/> mask to the
    /// Windows <c>MOD_*</c> flags consumed by <c>RegisterHotKey</c>.
    /// </summary>
    /// <param name="modifiers">The modifier mask.</param>
    /// <returns>The corresponding Win32 modifier flags (including MOD_NOREPEAT).</returns>
    public static uint ToWin32Modifiers(KeyModifiers modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            result |= 0x0001u; // MOD_ALT
        }

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            result |= 0x0002u; // MOD_CONTROL
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            result |= 0x0004u; // MOD_SHIFT
        }

        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            result |= 0x0008u; // MOD_WIN
        }

        result |= 0x4000u; // MOD_NOREPEAT
        return result;
    }

    /// <summary>
    /// Converts an Avalonia <see cref="Key"/> to a macOS kVK_ANSI_* virtual
    /// key code. Returns <c>-1</c> when the key is unsupported.
    /// </summary>
    /// <param name="key">The Avalonia key.</param>
    /// <returns>A kVK code, or <c>-1</c>.</returns>
    public static int ToMacVirtualKey(Key key)
    {
        return key switch
        {
            Key.A => 0x00,
            Key.S => 0x01,
            Key.D => 0x02,
            Key.F => 0x03,
            Key.H => 0x04,
            Key.G => 0x05,
            Key.Z => 0x06,
            Key.X => 0x07,
            Key.C => 0x08,
            Key.V => 0x09,
            Key.B => 0x0B,
            Key.Q => 0x0C,
            Key.W => 0x0D,
            Key.E => 0x0E,
            Key.R => 0x0F,
            Key.Y => 0x10,
            Key.T => 0x11,
            Key.D1 => 0x12,
            Key.D2 => 0x13,
            Key.D3 => 0x14,
            Key.D4 => 0x15,
            Key.D6 => 0x16,
            Key.D5 => 0x17,
            Key.D9 => 0x19,
            Key.D7 => 0x1A,
            Key.D8 => 0x1C,
            Key.D0 => 0x1D,
            Key.O => 0x1F,
            Key.U => 0x20,
            Key.I => 0x22,
            Key.P => 0x23,
            Key.L => 0x25,
            Key.J => 0x26,
            Key.K => 0x28,
            Key.N => 0x2D,
            Key.M => 0x2E,
            Key.OemTilde or Key.Oem3 => 0x32,
            Key.OemMinus => 0x1B,
            Key.OemPlus => 0x18,
            Key.OemComma => 0x2B,
            Key.OemPeriod => 0x2F,
            Key.Space => 0x31,
            Key.Tab => 0x30,
            Key.Enter => 0x24,
            Key.Escape => 0x35,
            Key.Home => 0x73,
            Key.End => 0x77,
            Key.PageUp => 0x74,
            Key.PageDown => 0x79,
            Key.Delete => 0x75,
            Key.F1 => 0x7A,
            Key.F2 => 0x78,
            Key.F3 => 0x63,
            Key.F4 => 0x76,
            Key.F5 => 0x60,
            Key.F6 => 0x61,
            Key.F7 => 0x62,
            Key.F8 => 0x64,
            Key.F9 => 0x65,
            Key.F10 => 0x6D,
            Key.F11 => 0x67,
            Key.F12 => 0x6F,
            _ => -1,
        };
    }

    /// <summary>
    /// Converts an Avalonia <see cref="KeyModifiers"/> mask to the
    /// Carbon <c>cmdKey/shiftKey/optionKey/controlKey</c> flags used by
    /// <c>RegisterEventHotKey</c>.
    /// </summary>
    /// <param name="modifiers">The modifier mask.</param>
    /// <returns>The corresponding Carbon modifier flags.</returns>
    public static uint ToMacModifiers(KeyModifiers modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            result |= 0x0100u; // cmdKey
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            result |= 0x0200u; // shiftKey
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            result |= 0x0800u; // optionKey
        }

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            result |= 0x1000u; // controlKey
        }

        return result;
    }
}
