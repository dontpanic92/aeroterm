// <copyright file="EmojiPresentationTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Tests for emoji-presentation detection shared by the terminal and tab titles.
/// </summary>
[TestFixture]
public class EmojiPresentationTests
{
    /// <summary>
    /// Plain terminal titles do not need the emoji presentation path.
    /// </summary>
    [Test]
    public void ContainsEmojiPresentation_PlainText_ReturnsFalse()
    {
        Assert.That(EmojiPresentation.ContainsEmojiPresentation("AeroTerm"), Is.False);
    }

    /// <summary>
    /// Supplementary-plane emoji activate emoji presentation.
    /// </summary>
    [Test]
    public void ContainsEmojiPresentation_SupplementaryEmoji_ReturnsTrue()
    {
        Assert.That(EmojiPresentation.ContainsEmojiPresentation("shell 😀"), Is.True);
    }

    /// <summary>
    /// Emoji presentation selectors activate emoji presentation for ambiguous
    /// text/symbol characters.
    /// </summary>
    [Test]
    public void ContainsEmojiPresentation_VariationSelector16_ReturnsTrue()
    {
        Assert.That(EmojiPresentation.ContainsEmojiPresentation("status \u2601\uFE0F"), Is.True);
    }

    /// <summary>
    /// Text presentation selectors suppress emoji presentation.
    /// </summary>
    [Test]
    public void ContainsEmojiPresentation_VariationSelector15_ReturnsFalse()
    {
        Assert.That(EmojiPresentation.ContainsEmojiPresentation("status \u2764\uFE0E"), Is.False);
    }

    /// <summary>
    /// Emoji modifier sequences activate emoji presentation.
    /// </summary>
    [Test]
    public void ContainsEmojiPresentation_SkinToneModifier_ReturnsTrue()
    {
        Assert.That(EmojiPresentation.ContainsEmojiPresentation("user \U0001F44B\U0001F3FD"), Is.True);
    }

    /// <summary>
    /// Regional-indicator flag sequences activate emoji presentation.
    /// </summary>
    [Test]
    public void ContainsEmojiPresentation_RegionalIndicators_ReturnsTrue()
    {
        Assert.That(EmojiPresentation.ContainsEmojiPresentation("region \U0001F1FA\U0001F1F8"), Is.True);
    }
}
