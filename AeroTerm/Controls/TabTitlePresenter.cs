// <copyright file="TabTitlePresenter.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System.Globalization;
using System.Runtime.InteropServices;
using AeroTerm.Pty;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

/// <summary>
/// Presents tab title text, using Avalonia text for normal titles and a
/// Skia color emoji path for emoji-containing titles on Windows.
/// </summary>
internal sealed class TabTitlePresenter : UserControl
{
    private readonly TextBlock fallbackText;
    private readonly SkiaEmojiTextControl emojiText;
    private IBrush? foregroundBrush;
    private string text = string.Empty;
    private double titleFontSize = 12;
    private TextTrimming textTrimming = TextTrimming.CharacterEllipsis;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabTitlePresenter"/> class.
    /// </summary>
    public TabTitlePresenter()
    {
        this.fallbackText = new TextBlock
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            TextTrimming = this.textTrimming,
            FontSize = this.titleFontSize,
        };

        this.emojiText = new SkiaEmojiTextControl
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            IsVisible = false,
        };

        this.Content = new Grid
        {
            Children =
            {
                this.fallbackText,
                this.emojiText,
            },
        };
    }

    /// <summary>
    /// Gets or sets the title text.
    /// </summary>
    public string Text
    {
        get => this.text;
        set
        {
            string normalized = value ?? string.Empty;
            if (this.text == normalized)
            {
                return;
            }

            this.text = normalized;
            this.fallbackText.Text = normalized;
            this.emojiText.Text = normalized;
            this.RefreshRenderingMode();
        }
    }

    /// <summary>
    /// Gets or sets the brush used for non-emoji glyphs and fallback text.
    /// </summary>
    public IBrush? ForegroundBrush
    {
        get => this.foregroundBrush;
        set
        {
            if (ReferenceEquals(this.foregroundBrush, value))
            {
                return;
            }

            if (this.foregroundBrush is AvaloniaObject oldBrush)
            {
                oldBrush.PropertyChanged -= this.OnForegroundBrushPropertyChanged;
            }

            this.foregroundBrush = value;
            if (this.foregroundBrush is AvaloniaObject newBrush)
            {
                newBrush.PropertyChanged += this.OnForegroundBrushPropertyChanged;
            }

            this.fallbackText.Foreground = value;
            this.emojiText.ForegroundBrush = value;
        }
    }

    /// <summary>
    /// Gets or sets the title font size in device-independent pixels.
    /// </summary>
    public double TitleFontSize
    {
        get => this.titleFontSize;
        set
        {
            if (Math.Abs(this.titleFontSize - value) < double.Epsilon)
            {
                return;
            }

            this.titleFontSize = value;
            this.fallbackText.FontSize = value;
            this.emojiText.TitleFontSize = value;
        }
    }

    /// <summary>
    /// Gets or sets how overflowing title text is trimmed.
    /// </summary>
    public TextTrimming TextTrimming
    {
        get => this.textTrimming;
        set
        {
            if (this.textTrimming == value)
            {
                return;
            }

            this.textTrimming = value;
            this.fallbackText.TextTrimming = value;
            this.emojiText.TextTrimming = value;
        }
    }

    private void OnForegroundBrushPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        this.emojiText.InvalidateVisual();
    }

    private void RefreshRenderingMode()
    {
        bool useColorEmoji = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && EmojiPresentation.ContainsEmojiPresentation(this.text);

        this.fallbackText.IsVisible = !useColorEmoji;
        this.emojiText.IsVisible = useColorEmoji;
    }

    private sealed class SkiaEmojiTitlePainter
    {
        private const string Ellipsis = "\u2026";
        private static readonly Lazy<SKTypeface> PrimaryTypeface = new(CreatePrimaryTypeface);
        private static readonly Lazy<SKTypeface> EmojiTypeface = new(CreateEmojiTypeface);

        private SkiaEmojiTitlePainter()
        {
        }

        public static Size Measure(string text, double fontSize)
        {
            using var paint = CreatePaint(GetForegroundColor(null));
            using var primaryFont = CreateFont(PrimaryTypeface.Value, fontSize);
            float height = GetLineHeight(primaryFont);
            float width = 0;

            foreach (var run in BuildRuns(text))
            {
                using var font = CreateFont(run.IsEmoji ? EmojiTypeface.Value : PrimaryTypeface.Value, fontSize);
                width += font.MeasureText(run.Text, paint);
            }

            return new Size(Math.Ceiling(width), Math.Ceiling(height));
        }

        public static void Draw(
            SKCanvas canvas,
            string text,
            double fontSize,
            Size bounds,
            IBrush? foreground,
            TextTrimming trimming)
        {
            if (string.IsNullOrEmpty(text) || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            using var paint = CreatePaint(GetForegroundColor(foreground));
            using var primaryFont = CreateFont(PrimaryTypeface.Value, fontSize);
            float baseline = GetCenteredBaseline(primaryFont, (float)bounds.Height);
            float maxWidth = (float)bounds.Width;
            var runs = BuildRuns(text);
            if (trimming == TextTrimming.CharacterEllipsis)
            {
                runs = TrimRuns(runs, maxWidth, fontSize, paint);
            }

            float x = 0;
            foreach (var run in runs)
            {
                using var font = CreateFont(run.IsEmoji ? EmojiTypeface.Value : PrimaryTypeface.Value, fontSize);
                canvas.DrawText(run.Text, x, baseline, font, paint);
                x += font.MeasureText(run.Text, paint);
            }
        }

        private static List<(string Text, bool IsEmoji)> BuildRuns(string text)
        {
            var runs = new List<(string Text, bool IsEmoji)>();
            if (string.IsNullOrEmpty(text))
            {
                return runs;
            }

            var enumerator = StringInfo.GetTextElementEnumerator(text);
            bool? currentIsEmoji = null;
            var current = new System.Text.StringBuilder();
            while (enumerator.MoveNext())
            {
                string element = enumerator.GetTextElement();
                bool isEmoji = EmojiPresentation.IsEmojiPresentationElement(element);
                if (currentIsEmoji is not null && currentIsEmoji.Value != isEmoji)
                {
                    runs.Add((current.ToString(), currentIsEmoji.Value));
                    current.Clear();
                }

                currentIsEmoji = isEmoji;
                current.Append(element);
            }

            if (currentIsEmoji is not null && current.Length > 0)
            {
                runs.Add((current.ToString(), currentIsEmoji.Value));
            }

            return runs;
        }

        private static List<(string Text, bool IsEmoji)> TrimRuns(
            List<(string Text, bool IsEmoji)> runs,
            float maxWidth,
            double fontSize,
            SKPaint paint)
        {
            if (MeasureRuns(runs, fontSize, paint) <= maxWidth)
            {
                return runs;
            }

            using var primaryFont = CreateFont(PrimaryTypeface.Value, fontSize);
            float ellipsisWidth = primaryFont.MeasureText(Ellipsis, paint);
            if (ellipsisWidth > maxWidth)
            {
                return new List<(string Text, bool IsEmoji)>();
            }

            var trimmed = new List<(string Text, bool IsEmoji)>();
            float width = 0;
            foreach (var run in runs)
            {
                var enumerator = StringInfo.GetTextElementEnumerator(run.Text);
                while (enumerator.MoveNext())
                {
                    string element = enumerator.GetTextElement();
                    using var font = CreateFont(run.IsEmoji ? EmojiTypeface.Value : PrimaryTypeface.Value, fontSize);
                    float elementWidth = font.MeasureText(element, paint);
                    if (width + elementWidth + ellipsisWidth > maxWidth)
                    {
                        AppendRun(trimmed, Ellipsis, isEmoji: false);
                        return trimmed;
                    }

                    AppendRun(trimmed, element, run.IsEmoji);
                    width += elementWidth;
                }
            }

            AppendRun(trimmed, Ellipsis, isEmoji: false);
            return trimmed;
        }

        private static void AppendRun(List<(string Text, bool IsEmoji)> runs, string text, bool isEmoji)
        {
            if (runs.Count > 0 && runs[^1].IsEmoji == isEmoji)
            {
                var last = runs[^1];
                runs[^1] = (last.Text + text, isEmoji);
            }
            else
            {
                runs.Add((text, isEmoji));
            }
        }

        private static float MeasureRuns(List<(string Text, bool IsEmoji)> runs, double fontSize, SKPaint paint)
        {
            float width = 0;
            foreach (var run in runs)
            {
                using var font = CreateFont(run.IsEmoji ? EmojiTypeface.Value : PrimaryTypeface.Value, fontSize);
                width += font.MeasureText(run.Text, paint);
            }

            return width;
        }

        private static SKPaint CreatePaint(SKColor foreground)
        {
            return new SKPaint
            {
                Color = foreground,
                IsAntialias = true,
            };
        }

        private static SKFont CreateFont(SKTypeface typeface, double fontSize)
        {
            return new SKFont(typeface, (float)fontSize)
            {
                Edging = SKFontEdging.Antialias,
                Subpixel = true,
            };
        }

        private static float GetLineHeight(SKFont font)
        {
            _ = font.GetFontMetrics(out var metrics);
            return MathF.Ceiling(metrics.Descent - metrics.Ascent + metrics.Leading);
        }

        private static float GetCenteredBaseline(SKFont font, float height)
        {
            _ = font.GetFontMetrics(out var metrics);
            float textHeight = metrics.Descent - metrics.Ascent;
            return ((height - textHeight) / 2f) - metrics.Ascent;
        }

        private static SKColor GetForegroundColor(IBrush? brush)
        {
            if (brush is ISolidColorBrush solid)
            {
                var color = solid.Color;
                return new SKColor(color.R, color.G, color.B, color.A);
            }

            return SKColors.White;
        }

        private static SKTypeface CreatePrimaryTypeface()
        {
            return SKTypeface.FromFamilyName("Segoe UI Variable Text")
                ?? SKTypeface.FromFamilyName("Segoe UI")
                ?? SKTypeface.Default;
        }

        private static SKTypeface CreateEmojiTypeface()
        {
            return SKTypeface.FromFamilyName("Segoe UI Emoji")
                ?? SKTypeface.FromFamilyName("Segoe UI Symbol")
                ?? PrimaryTypeface.Value;
        }
    }

    private sealed class SkiaEmojiTextControl : Control
    {
        private readonly EmojiTitleDrawOperation drawOperation;
        private IBrush? foregroundBrush;
        private string text = string.Empty;
        private double titleFontSize = 12;
        private TextTrimming textTrimming = TextTrimming.CharacterEllipsis;

        public SkiaEmojiTextControl()
        {
            this.drawOperation = new EmojiTitleDrawOperation(this);
            this.ClipToBounds = true;
        }

        public string Text
        {
            get => this.text;
            set
            {
                string normalized = value ?? string.Empty;
                if (this.text == normalized)
                {
                    return;
                }

                this.text = normalized;
                this.InvalidateMeasure();
                this.InvalidateVisual();
            }
        }

        public IBrush? ForegroundBrush
        {
            get => this.foregroundBrush;
            set
            {
                if (ReferenceEquals(this.foregroundBrush, value))
                {
                    return;
                }

                this.foregroundBrush = value;
                this.InvalidateVisual();
            }
        }

        public double TitleFontSize
        {
            get => this.titleFontSize;
            set
            {
                if (Math.Abs(this.titleFontSize - value) < double.Epsilon)
                {
                    return;
                }

                this.titleFontSize = value;
                this.InvalidateMeasure();
                this.InvalidateVisual();
            }
        }

        public TextTrimming TextTrimming
        {
            get => this.textTrimming;
            set
            {
                if (this.textTrimming == value)
                {
                    return;
                }

                this.textTrimming = value;
                this.InvalidateMeasure();
                this.InvalidateVisual();
            }
        }

        public string RenderText => this.text;

        public double RenderFontSize => this.titleFontSize;

        public IBrush? RenderForegroundBrush => this.foregroundBrush;

        public TextTrimming RenderTextTrimming => this.textTrimming;

        public override void Render(DrawingContext context)
        {
            this.drawOperation.Bounds = new Rect(0, 0, this.Bounds.Width, this.Bounds.Height);
            context.Custom(this.drawOperation);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var measured = SkiaEmojiTitlePainter.Measure(this.text, this.titleFontSize);
            double width = double.IsInfinity(availableSize.Width)
                ? measured.Width
                : Math.Min(measured.Width, availableSize.Width);
            return new Size(width, measured.Height);
        }
    }

    private sealed class EmojiTitleDrawOperation : ICustomDrawOperation
    {
        private readonly SkiaEmojiTextControl owner;

        public EmojiTitleDrawOperation(SkiaEmojiTextControl owner)
        {
            this.owner = owner;
        }

        public Rect Bounds { get; set; }

        public void Dispose()
        {
        }

        public bool Equals(ICustomDrawOperation? other) => false;

        public bool HitTest(Point p) => this.Bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature is null)
            {
                return;
            }

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            canvas.Save();
            try
            {
                canvas.ClipRect(SKRect.Create((float)this.Bounds.Width, (float)this.Bounds.Height));
                SkiaEmojiTitlePainter.Draw(
                    canvas,
                    this.owner.RenderText,
                    this.owner.RenderFontSize,
                    this.Bounds.Size,
                    this.owner.RenderForegroundBrush,
                    this.owner.RenderTextTrimming);
            }
            finally
            {
                canvas.Restore();
            }
        }
    }
}
