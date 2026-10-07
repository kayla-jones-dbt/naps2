namespace NAPS2.Sdk.Tests.Images;

/// <summary>
/// Builds synthetic scans (e.g. a page on a scanner background) for testing.
/// </summary>
public class TestCanvas(int width, int height, byte grey)
{
    public const byte PaperWhite = 250;

    private readonly byte[] _rgb = Enumerable.Repeat(grey, width * height * 3).ToArray();

    public void FillRect(int x0, int y0, int x1, int y1, byte grey) => FillRect(x0, y0, x1, y1, grey, grey, grey);

    public void FillRect(int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        for (int y = Math.Max(y0, 0); y < Math.Min(y1, height); y++)
        {
            for (int x = Math.Max(x0, 0); x < Math.Min(x1, width); x++)
            {
                Set(x, y, r, g, b);
            }
        }
    }

    public void FillRotatedRect(double cx, double cy, double w, double h, double angle, byte grey)
    {
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double dx = x - cx, dy = y - cy;
                double u = dx * cos + dy * sin, v = -dx * sin + dy * cos;
                if (Math.Abs(u) <= w / 2 && Math.Abs(v) <= h / 2)
                {
                    Set(x, y, grey, grey, grey);
                }
            }
        }
    }

    private void Set(int x, int y, byte r, byte g, byte b)
    {
        int i = (y * width + x) * 3;
        _rgb[i] = r;
        _rgb[i + 1] = g;
        _rgb[i + 2] = b;
    }

    public unsafe IMemoryImage ToImage(ImageContext imageContext)
    {
        var image = imageContext.Create(width, height, ImagePixelFormat.RGB24);
        using var lockState = image.Lock(LockMode.WriteOnly, out var data);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pixel = data.ptr + data.stride * y + data.bytesPerPixel * x;
                int i = (y * width + x) * 3;
                *(pixel + data.rOff) = _rgb[i];
                *(pixel + data.gOff) = _rgb[i + 1];
                *(pixel + data.bOff) = _rgb[i + 2];
            }
        }
        return image;
    }

    /// <summary>
    /// Draws manga-like content (bordered panels with tone and line art) inside a page with margins.
    /// </summary>
    public void DrawMangaContent(int left, int top, int right, int bottom)
    {
        int w = right - left, h = bottom - top;
        int mx = w / 12, my = h / 14;
        var random = new Random(42);
        int[] rowsY = [top + my, top + h * 2 / 5, top + h * 2 / 3, bottom - my];
        for (int r = 0; r < 3; r++)
        {
            int y0 = rowsY[r] + (r > 0 ? 6 : 0), y1 = rowsY[r + 1];
            int split = left + mx + (int) (w * (0.35 + 0.3 * random.NextDouble()));
            foreach (var (x0, x1) in new[] { (left + mx, split - 4), (split + 4, right - mx) })
            {
                FillRect(x0, y0, x1, y1, 0);
                FillRect(x0 + 3, y0 + 3, x1 - 3, y1 - 3, PaperWhite);
                // Screentone dots
                for (int y = y0 + 10; y < y1 - 10; y += 4)
                {
                    for (int x = x0 + 10; x < (x0 + x1) / 2; x += 4)
                    {
                        FillRect(x, y, x + 2, y + 2, 120);
                    }
                }
                // Line art
                for (int i = 0; i < 12; i++)
                {
                    int lx = random.Next(x0 + 5, x1 - 30), ly = random.Next(y0 + 5, y1 - 30);
                    FillRect(lx, ly, lx + random.Next(2, 25), ly + 2, 20);
                }
            }
        }
    }
}
