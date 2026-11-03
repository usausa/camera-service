namespace CameraService.Core;

using System.Buffers;
using System.Collections.Frozen;

// Draws a line of text on a full range NV12 image with an own bitmap font of digits and a few symbols (no font of the OS is used):
// white with a black outline on the Y plane, and gray chroma under the text, so that the colors of the image do not tint it
public static class TextOverlay
{
    public const string Characters = "0123456789 +-./:";

    // Height of the glyphs and the gap between them at scale 1
    private const int GlyphHeight = 7;
    private const int Spacing = 1;

    // Distance from the edges of the image at scale 1
    private const int Margin = 4;

    // Lines of the image per step of the scale (480 lines: 2, 1080 lines: 4)
    private const int LinesPerScale = 240;

    private const byte TextLuma = 255;
    private const byte OutlineLuma = 0;
    private const byte NeutralChroma = 128;

    // Values of the mask
    private const byte None = 0;
    private const byte Outline = 1;
    private const byte Text = 2;

    private static readonly SearchValues<char> SupportedCharacters = SearchValues.Create(Characters);

    // '#' is a pixel of the glyph, the length of the rows is the width of the glyph
    private static readonly FrozenDictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
    {
        ['0'] = [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['1'] = ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
        ['2'] = [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
        ['3'] = [".###.", "#...#", "....#", "..##.", "....#", "#...#", ".###."],
        ['4'] = ["...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#."],
        ['5'] = ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
        ['6'] = ["..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###."],
        ['7'] = ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
        ['8'] = [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
        ['9'] = [".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.."],
        [' '] = ["...", "...", "...", "...", "...", "...", "..."],
        ['+'] = [".....", "..#..", "..#..", "#####", "..#..", "..#..", "....."],
        ['-'] = ["....", "....", "....", "####", "....", "....", "...."],
        ['.'] = ["..", "..", "..", "..", "..", "##", "##"],
        ['/'] = ["....#", "....#", "...#.", "..#..", ".#...", "#....", "#...."],
        [':'] = ["..", "##", "##", "..", "##", "##", ".."]
    }.ToFrozenDictionary();

    public static bool IsSupported(ReadOnlySpan<char> text) => !text.ContainsAnyExcept(SupportedCharacters);

    // Scale for the height of an image (the glyphs are 7 pixels high at scale 1)
    public static int GetScale(int height) => Math.Max(1, height / LinesPerScale);

    // The text is clipped by the edges of the image
    public static void Draw(Nv12Image image, string text, TextPosition position, int scale)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        if (!IsSupported(text))
        {
            throw new ArgumentException("The text has an unsupported character.", nameof(text));
        }

        if (text.Length == 0)
        {
            return;
        }

        // The outline grows with the scale
        var outline = Math.Max(1, scale / 2);
        var width = (MeasureWidth(text) * scale) + (outline * 2);
        var height = (GlyphHeight * scale) + (outline * 2);
        var margin = Margin * scale;
        var left = position is TextPosition.TopLeft or TextPosition.BottomLeft ? margin : image.Width - margin - width;
        var top = position is TextPosition.TopLeft or TextPosition.TopRight ? margin : image.Height - margin - height;

        var rented = ArrayPool<byte>.Shared.Rent(width * height);
        try
        {
            var mask = rented.AsSpan(0, width * height);
            mask.Clear();
            RenderText(mask, width, text, scale, outline);
            RenderOutline(mask, width, height, outline);
            Apply(image, mask, width, height, left, top);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    // Width at scale 1
    private static int MeasureWidth(string text)
    {
        var width = (text.Length - 1) * Spacing;
        foreach (var c in text)
        {
            width += Glyphs[c][0].Length;
        }

        return width;
    }

    private static void RenderText(Span<byte> mask, int width, string text, int scale, int outline)
    {
        var x = outline;
        foreach (var c in text)
        {
            var glyph = Glyphs[c];
            for (var row = 0; row < GlyphHeight; row++)
            {
                var line = glyph[row];
                for (var column = 0; column < line.Length; column++)
                {
                    if (line[column] == '#')
                    {
                        FillBlock(mask, width, x + (column * scale), outline + (row * scale), scale);
                    }
                }
            }

            x += (glyph[0].Length + Spacing) * scale;
        }
    }

    private static void FillBlock(Span<byte> mask, int width, int x, int y, int size)
    {
        for (var row = y; row < y + size; row++)
        {
            mask.Slice((row * width) + x, size).Fill(Text);
        }
    }

    // Marks the pixels around the text up to the outline width
    private static void RenderOutline(Span<byte> mask, int width, int height, int outline)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (mask[(y * width) + x] != Text)
                {
                    continue;
                }

                var left = Math.Max(0, x - outline);
                var right = Math.Min(width - 1, x + outline);
                for (var row = Math.Max(0, y - outline); row <= Math.Min(height - 1, y + outline); row++)
                {
                    var line = mask.Slice(row * width, width);
                    for (var column = left; column <= right; column++)
                    {
                        if (line[column] == None)
                        {
                            line[column] = Outline;
                        }
                    }
                }
            }
        }
    }

    private static void Apply(Nv12Image image, ReadOnlySpan<byte> mask, int width, int height, int left, int top)
    {
        var luma = image.Y;
        var chroma = image.Uv;
        var stride = image.Stride;
        for (var y = 0; y < height; y++)
        {
            var imageY = top + y;
            if ((uint)imageY >= (uint)image.Height)
            {
                continue;
            }

            var line = mask.Slice(y * width, width);
            for (var x = 0; x < width; x++)
            {
                var imageX = left + x;
                var value = line[x];
                if ((value == None) || ((uint)imageX >= (uint)image.Width))
                {
                    continue;
                }

                luma[(imageY * stride) + imageX] = value == Text ? TextLuma : OutlineLuma;
                var offset = ((imageY / 2) * stride) + ((imageX / 2) * 2);
                chroma[offset] = NeutralChroma;
                chroma[offset + 1] = NeutralChroma;
            }
        }
    }
}
