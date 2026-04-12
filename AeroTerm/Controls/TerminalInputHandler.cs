// <copyright file="TerminalInputHandler.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System.Text;
using AeroTerm.Pty;
using Avalonia;
using Avalonia.Input;

/// <summary>
/// Translates Avalonia keyboard and mouse events into terminal escape
/// sequences for writing to the PTY.
/// </summary>
internal sealed class TerminalInputHandler
{
    private static readonly IReadOnlyDictionary<Key, string> SpecialKeys = new Dictionary<Key, string>()
    {
        { Key.Back, "BS" },
        { Key.Tab, "Tab" },
        { Key.LineFeed, "NL" },
        { Key.Return, "CR" },
        { Key.Escape, "Esc" },
        { Key.Space, "Space" },
        { Key.OemBackslash, "Bslash" },
        { Key.Delete, "Del" },
        { Key.Up, "Up" },
        { Key.Down, "Down" },
        { Key.Left, "Left" },
        { Key.Right, "Right" },
        { Key.Insert, "Insert" },
        { Key.Home, "Home" },
        { Key.End, "End" },
        { Key.PageUp, "PageUp" },
        { Key.PageDown, "PageDown" },
        { Key.F1, "F1" },
        { Key.F2, "F2" },
        { Key.F3, "F3" },
        { Key.F4, "F4" },
        { Key.F5, "F5" },
        { Key.F6, "F6" },
        { Key.F7, "F7" },
        { Key.F8, "F8" },
        { Key.F9, "F9" },
        { Key.F10, "F10" },
        { Key.F11, "F11" },
        { Key.F12, "F12" },
    };

    private readonly Action<byte[]> writeToPty;
    private Avalonia.Input.MouseButton? pressedButton;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalInputHandler"/> class.
    /// </summary>
    /// <param name="writeToPty">Callback to write encoded bytes to the PTY.</param>
    public TerminalInputHandler(Action<byte[]> writeToPty)
    {
        this.writeToPty = writeToPty;
    }

    /// <summary>
    /// Gets or sets a value indicating whether application cursor keys (DECCKM) mode is active.
    /// </summary>
    public bool ApplicationCursorKeys { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether bracketed paste mode is active.
    /// </summary>
    public bool BracketedPasteEnabled { get; set; }

    /// <summary>
    /// Gets or sets the current mouse tracking mode.
    /// </summary>
    public MouseTrackingMode MouseTrackingMode { get; set; }

    /// <summary>
    /// Handles a key down event by mapping the key to a terminal escape sequence.
    /// </summary>
    /// <param name="e">The key event arguments.</param>
    /// <returns>True if the event was handled and a sequence was sent.</returns>
    public bool HandleKeyDown(KeyEventArgs e)
    {
        string? vimNotation = MapKeyToVimNotation(e.Key, e.KeyModifiers);
        if (vimNotation is null)
        {
            return false;
        }

        string encoded = TerminalInputEncoder.Encode(vimNotation, this.ApplicationCursorKeys);
        this.Send(encoded);
        return true;
    }

    /// <summary>
    /// Handles text input for regular characters and IME composition.
    /// </summary>
    /// <param name="text">The text input string.</param>
    public void HandleTextInput(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            this.Send(text);
        }
    }

    /// <summary>
    /// Handles pasted text with optional bracketed paste wrapping.
    /// </summary>
    /// <param name="text">The pasted text.</param>
    public void HandlePaste(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (this.BracketedPasteEnabled)
        {
            this.Send($"\x1B[200~{text}\x1B[201~");
        }
        else
        {
            this.Send(text);
        }
    }

    /// <summary>
    /// Handles a pointer press event by sending an SGR mouse press sequence.
    /// </summary>
    /// <param name="e">The pointer pressed event arguments.</param>
    /// <param name="row">The one-based terminal row.</param>
    /// <param name="col">The one-based terminal column.</param>
    /// <returns>True if the event was handled.</returns>
    public bool HandlePointerPressed(PointerPressedEventArgs e, int row, int col)
    {
        if (this.MouseTrackingMode == MouseTrackingMode.None)
        {
            return false;
        }

        int? button = MapPointerButton(e.GetCurrentPoint(null).Properties);
        if (button is null)
        {
            return false;
        }

        this.pressedButton = GetMouseButton(e.GetCurrentPoint(null).Properties);

        int modBits = GetModifierBits(e.KeyModifiers);
        this.SendSgrMouse(button.Value + modBits, col, row, release: false);
        return true;
    }

    /// <summary>
    /// Handles a pointer release event by sending an SGR mouse release sequence.
    /// </summary>
    /// <param name="e">The pointer released event arguments.</param>
    /// <param name="row">The one-based terminal row.</param>
    /// <param name="col">The one-based terminal column.</param>
    /// <returns>True if the event was handled.</returns>
    public bool HandlePointerReleased(PointerReleasedEventArgs e, int row, int col)
    {
        if (this.pressedButton is null)
        {
            return false;
        }

        if (this.MouseTrackingMode == MouseTrackingMode.None)
        {
            this.pressedButton = null;
            return false;
        }

        int button = MapMouseButton(this.pressedButton.Value);
        int modBits = GetModifierBits(e.KeyModifiers);
        this.SendSgrMouse(button + modBits, col, row, release: true);
        this.pressedButton = null;
        return true;
    }

    /// <summary>
    /// Handles pointer move/drag events.
    /// </summary>
    /// <param name="e">The pointer event arguments.</param>
    /// <param name="row">The one-based terminal row.</param>
    /// <param name="col">The one-based terminal column.</param>
    /// <returns>True if the event was handled.</returns>
    public bool HandlePointerMoved(PointerEventArgs e, int row, int col)
    {
        if (this.MouseTrackingMode == MouseTrackingMode.None)
        {
            return false;
        }

        int modBits = GetModifierBits(e.KeyModifiers);

        if (this.pressedButton is not null)
        {
            if (this.MouseTrackingMode is MouseTrackingMode.ButtonEvent or MouseTrackingMode.AnyEvent)
            {
                int button = MapMouseButton(this.pressedButton.Value) + 32;
                this.SendSgrMouse(button + modBits, col, row, release: false);
                return true;
            }
        }
        else if (this.MouseTrackingMode == MouseTrackingMode.AnyEvent)
        {
            // Motion with no button pressed: button code 35
            this.SendSgrMouse(35 + modBits, col, row, release: false);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Handles scroll wheel events.
    /// </summary>
    /// <param name="e">The pointer wheel event arguments.</param>
    /// <param name="row">The one-based terminal row.</param>
    /// <param name="col">The one-based terminal column.</param>
    /// <returns>True if the event was handled.</returns>
    public bool HandlePointerWheel(PointerWheelEventArgs e, int row, int col)
    {
        if (this.MouseTrackingMode == MouseTrackingMode.None)
        {
            return false;
        }

        int modBits = GetModifierBits(e.KeyModifiers);
        bool handled = false;

        if (e.Delta.Y > 0)
        {
            this.SendSgrMouse(64 + modBits, col, row, release: false);
            handled = true;
        }
        else if (e.Delta.Y < 0)
        {
            this.SendSgrMouse(65 + modBits, col, row, release: false);
            handled = true;
        }

        return handled;
    }

    /// <summary>
    /// Clears the pressed mouse button state.
    /// </summary>
    public void ClearPressedButton()
    {
        this.pressedButton = null;
    }

    private static string? MapKeyToVimNotation(Key key, KeyModifiers modifiers)
    {
        bool ctrl = modifiers.HasFlag(KeyModifiers.Control);
        bool shift = modifiers.HasFlag(KeyModifiers.Shift);
        bool alt = modifiers.HasFlag(KeyModifiers.Alt);

        if (SpecialKeys.TryGetValue(key, out string? keyName))
        {
            return DecorateVimNotation(keyName, ctrl, shift, alt);
        }

        if (!ctrl && !alt)
        {
            return null;
        }

        string? baseChar = KeyToBaseCharacter(key);
        if (baseChar is null)
        {
            return null;
        }

        if (baseChar == "<")
        {
            return DecorateVimNotation("lt", ctrl, shift, alt);
        }

        if (baseChar == "\\")
        {
            return DecorateVimNotation("Bslash", ctrl, shift, alt);
        }

        return DecorateVimNotation(baseChar, ctrl, shift, alt);
    }

    private static string DecorateVimNotation(string input, bool ctrl, bool shift, bool alt)
    {
        var sb = new StringBuilder("<", 16);

        if (ctrl)
        {
            sb.Append("C-");
        }

        if (shift)
        {
            sb.Append("S-");
        }

        if (alt)
        {
            sb.Append("A-");
        }

        sb.Append(input);
        sb.Append('>');
        return sb.ToString();
    }

    private static string? KeyToBaseCharacter(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            return ((char)('a' + (key - Key.A))).ToString();
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            return ((char)('0' + (key - Key.D0))).ToString();
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return ((char)('0' + (key - Key.NumPad0))).ToString();
        }

        return key switch
        {
            Key.Multiply => "*",
            Key.Add => "+",
            Key.Subtract => "-",
            Key.Decimal => ".",
            Key.Divide => "/",
            Key.OemSemicolon => ";",
            Key.OemPlus => "=",
            Key.OemComma => ",",
            Key.OemMinus => "-",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemTilde => "`",
            Key.OemOpenBrackets => "[",
            Key.OemPipe => "\\",
            Key.OemCloseBrackets => "]",
            Key.OemQuotes => "'",
            _ => null,
        };
    }

    private static int GetModifierBits(KeyModifiers modifiers)
    {
        int bits = 0;
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            bits += 4;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            bits += 8;
        }

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            bits += 16;
        }

        return bits;
    }

    private static int? MapPointerButton(PointerPointProperties properties)
    {
        if (properties.IsLeftButtonPressed)
        {
            return 0;
        }

        if (properties.IsMiddleButtonPressed)
        {
            return 1;
        }

        if (properties.IsRightButtonPressed)
        {
            return 2;
        }

        return null;
    }

    private static Avalonia.Input.MouseButton GetMouseButton(PointerPointProperties properties)
    {
        if (properties.IsMiddleButtonPressed)
        {
            return Avalonia.Input.MouseButton.Middle;
        }

        if (properties.IsRightButtonPressed)
        {
            return Avalonia.Input.MouseButton.Right;
        }

        return Avalonia.Input.MouseButton.Left;
    }

    private static int MapMouseButton(Avalonia.Input.MouseButton button)
    {
        return button switch
        {
            Avalonia.Input.MouseButton.Left => 0,
            Avalonia.Input.MouseButton.Middle => 1,
            Avalonia.Input.MouseButton.Right => 2,
            _ => 0,
        };
    }

    private void Send(string data)
    {
        this.writeToPty(Encoding.UTF8.GetBytes(data));
    }

    private void SendSgrMouse(int button, int col, int row, bool release)
    {
        char finalChar = release ? 'm' : 'M';
        this.Send($"\x1B[<{button};{col};{row}{finalChar}");
    }
}
