// <copyright file="SearchOptions.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System.Text.RegularExpressions;

/// <summary>
/// Toggleable search options. Mirrors the overlay's three toggle buttons.
/// </summary>
/// <param name="Regex">When <see langword="true"/> the query is compiled
/// as a .NET regular expression; otherwise it is escaped and matched
/// literally.</param>
/// <param name="CaseSensitive">When <see langword="false"/>
/// <see cref="RegexOptions.IgnoreCase"/> is applied.</param>
/// <param name="WholeWord">When <see langword="true"/> the pattern is
/// wrapped in lookarounds against AeroTerm's custom word-char class
/// (same definition as double-click word selection).</param>
internal sealed record SearchOptions(bool Regex, bool CaseSensitive, bool WholeWord);
