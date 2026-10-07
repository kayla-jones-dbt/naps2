using NAPS2.Images.Bitwise;

namespace NAPS2.Images;

/// <summary>
/// Detects the edges of a scanned page (or book cover) so the surrounding scanner background can be cropped away.
/// </summary>
internal static class AutoCropDetector
{
    // Conceptually, detection works like this:
    //
    // 1. Scale the image down to a working size and compute luma and chroma for each pixel.
    // 2. For each side, walk every scan line inward from the image border. Uniform neutral regions (a white scanner
    // lid, a grey backing sheet, a dark ADF backing) are skipped as background. The walk stops at the first event:
    //    - StepUp: the background gets brighter (e.g. a white page on a grey backing).
    //    - ShadowLine: a thin grey line between two background regions (e.g. a page edge shadow on a white lid).
    //    - Content: ink or colour starts. This is reliable evidence of an edge when the background is not white, but
    //    on a white lid it may just be the inner content of a page with a white margin.
    // 3. Each side's events are clustered by position. Page edges are straight (after deskew), so many scan lines
    // agree on the same position, while dust and content produce scattered events.
    // 4. Only strong evidence is used to crop. Content on a white background is only trusted if it forms a dense
    // band (e.g. cover art that bleeds to the edge of the cover). Otherwise that side is left uncropped, since a
    // white page margin on a white lid can't be told apart from the lid.
    // 5. Detection runs twice; the second pass only considers scan lines within the page area found by the first
    // pass, so corners and regions beside the page don't dilute the evidence.

    // The longer side of the image is scaled to this size for detection
    private const int WORKING_SIZE = 1280;
    // Max difference in luma for a pixel to be considered part of the same uniform region
    private const int UNIFORM_TOLERANCE = 14;
    // Max chroma (max - min channel) for a pixel to be considered neutral (white/grey/black)
    private const int NEUTRAL_CHROMA = 28;
    // Pixels at least this bright (relative to the paper white level) count as white
    private const int WHITE_MARGIN = 24;
    // Thin lines are at most this many working pixels wide
    private const int MAX_LINE_WIDTH = 4;
    // Shadow lines are grey rather than black; darker thin lines are treated as ink
    private const int SHADOW_MIN_LUMA = 90;
    // A darker region must be uniform for this fraction of the dimension to count as a new background region
    private const double MIN_BACKGROUND_RUN_FRACTION = 0.02;
    // Backgrounds discovered mid-walk must be at least this bright (prevents solid black panels counting as background)
    private const int MIN_INNER_BACKGROUND_LUMA = 120;
    // How far from the border (as a fraction of the dimension) to look for an edge
    private const double MAX_SEARCH_FRACTION = 0.45;
    // Events within this fraction of the dimension are clustered together
    private const double CLUSTER_FRACTION = 0.012;
    // A cluster needs at least this fraction of scan lines to be used
    private const double MIN_SUPPORT_FRACTION = 0.15;
    // The outermost cluster is preferred if it has at least this fraction of the best cluster's support
    private const double OUTERMOST_RELATIVE_SUPPORT = 0.35;
    // Content on a white background must cover at least this fraction of a band inside the edge to be trusted
    private const double MIN_CONTENT_DENSITY = 0.8;
    // The depth of the band used to measure content density, as a fraction of the dimension
    private const double CONTENT_BAND_FRACTION = 0.012;
    // Scan lines this close to the ends (as a fraction) are ignored, as they can be distorted by the corners
    private const double IGNORE_END_FRACTION = 0.03;
    // Crop slightly inside a detected edge (in working pixels) to avoid leaving a sliver of shadow/background
    private const int EDGE_INSET = 2;
    // Crops smaller than this fraction of the dimension are ignored
    private const double MIN_CROP_FRACTION = 0.003;
    // If the remaining page would be smaller than this fraction of a dimension, detection is treated as failed
    private const double MIN_REMAINING_FRACTION = 0.4;

    /// <summary>
    /// Gets a transform that crops the scanner background from the image, or null if no page edges were found.
    /// </summary>
    public static AutoCropTransform? GetAutoCropTransform(IMemoryImage image)
    {
        var result = Detect(image);
        if (!result.AnyEdgeFound)
        {
            return null;
        }
        var transform = new AutoCropTransform(result.Left, result.Right, result.Top, result.Bottom,
            image.Width, image.Height);
        return transform.IsNull ? null : transform;
    }

    /// <summary>
    /// Detects page edges. The result has crop amounts (in pixels of the provided image) for each side.
    /// </summary>
    public static AutoCropResult Detect(IMemoryImage image)
    {
        var pixels = PixelData.FromImage(image, WORKING_SIZE);
        int w = pixels.Width, h = pixels.Height;

        // First pass uses all scan lines (minus the ends)
        var sides = new SideResult[4];
        for (int i = 0; i < 4; i++)
        {
            sides[i] = DetectSide(pixels, (Side) i, null, null);
        }

        // Second pass limits scan lines to the page area found in the first pass
        var bounds = GetBounds(sides, w, h);
        sides[(int) Side.Left] = DetectSide(pixels, Side.Left, bounds.top, bounds.bottom);
        sides[(int) Side.Right] = DetectSide(pixels, Side.Right, bounds.top, bounds.bottom);
        sides[(int) Side.Top] = DetectSide(pixels, Side.Top, bounds.left, bounds.right);
        sides[(int) Side.Bottom] = DetectSide(pixels, Side.Bottom, bounds.left, bounds.right);

        // Ignore results that would leave too little of the image
        if (w - sides[(int) Side.Left].Crop - sides[(int) Side.Right].Crop < w * MIN_REMAINING_FRACTION)
        {
            sides[(int) Side.Left] = SideResult.NotFound;
            sides[(int) Side.Right] = SideResult.NotFound;
        }
        if (h - sides[(int) Side.Top].Crop - sides[(int) Side.Bottom].Crop < h * MIN_REMAINING_FRACTION)
        {
            sides[(int) Side.Top] = SideResult.NotFound;
            sides[(int) Side.Bottom] = SideResult.NotFound;
        }

        double xScale = image.Width / (double) w;
        double yScale = image.Height / (double) h;
        int Scale(SideResult side, double scale, int dim)
        {
            if (side.Crop < dim * MIN_CROP_FRACTION) return 0;
            return (int) Math.Round(side.Crop * scale);
        }
        return new AutoCropResult(
            Scale(sides[(int) Side.Left], xScale, w),
            Scale(sides[(int) Side.Right], xScale, w),
            Scale(sides[(int) Side.Top], yScale, h),
            Scale(sides[(int) Side.Bottom], yScale, h),
            sides[(int) Side.Left].Kind,
            sides[(int) Side.Right].Kind,
            sides[(int) Side.Top].Kind,
            sides[(int) Side.Bottom].Kind);
    }

    private static (int left, int right, int top, int bottom) GetBounds(SideResult[] sides, int w, int h)
    {
        int left = sides[(int) Side.Left].Crop;
        int right = w - sides[(int) Side.Right].Crop;
        int top = sides[(int) Side.Top].Crop;
        int bottom = h - sides[(int) Side.Bottom].Crop;
        return (left, right, top, bottom);
    }

    private static SideResult DetectSide(PixelData pixels, Side side, int? lineStart, int? lineEnd)
    {
        var view = new SideView(pixels, side);
        int depthLimit = (int) (view.Depth * MAX_SEARCH_FRACTION);
        int start = lineStart ?? 0;
        int end = lineEnd ?? view.Lines;
        int ignore = (int) ((end - start) * IGNORE_END_FRACTION);
        start += ignore;
        end -= ignore;
        int lineCount = end - start;
        if (lineCount <= 0)
        {
            return SideResult.NotFound;
        }

        var strong = new List<(int line, int pos)>();
        var weak = new List<(int line, int pos)>();
        for (int line = start; line < end; line++)
        {
            var ev = WalkLine(view, line, depthLimit, pixels.WhiteLevel);
            if (ev.Kind == EventKind.None) continue;
            if (ev.Kind == EventKind.Content && ev.OnWhite)
            {
                weak.Add((line, ev.Position));
            }
            else
            {
                strong.Add((line, ev.Position));
            }
        }

        int clusterSize = Math.Max(3, (int) (view.Depth * CLUSTER_FRACTION));
        var strongCluster = FindCluster(strong, clusterSize, lineCount);
        if (strongCluster != null)
        {
            var pos = OuterPosition(strongCluster);
            var kind = pos == 0 ? EdgeKind.None : EdgeKind.Edge;
            return new SideResult(pos == 0 ? 0 : pos + EDGE_INSET, kind);
        }

        var weakCluster = FindCluster(weak, clusterSize, lineCount);
        if (weakCluster != null)
        {
            var pos = OuterPosition(weakCluster);
            int band = Math.Max(4, (int) (view.Depth * CONTENT_BAND_FRACTION));
            if (ContentDensity(view, weakCluster, pos, band, pixels.WhiteLevel) >= MIN_CONTENT_DENSITY)
            {
                return new SideResult(pos + 1, EdgeKind.ContentEdge);
            }
        }

        return SideResult.NotFound;
    }

    private static int OuterPosition(List<(int line, int pos)> cluster)
    {
        // Use a low percentile rather than the minimum so a few outliers don't matter, while still keeping the whole
        // page if there's some leftover skew.
        var positions = cluster.Select(x => x.pos).OrderBy(x => x).ToList();
        return positions[(int) (positions.Count * 0.03)];
    }

    private static List<(int line, int pos)>? FindCluster(List<(int line, int pos)> events, int clusterSize,
        int lineCount)
    {
        if (events.Count == 0) return null;
        var sorted = events.OrderBy(x => x.pos).ToList();
        // For each event, count how many events are within the window starting at its position
        var windows = new List<(int startIndex, int count)>();
        int j = 0;
        for (int i = 0; i < sorted.Count; i++)
        {
            if (j < i) j = i;
            while (j + 1 < sorted.Count && sorted[j + 1].pos - sorted[i].pos <= clusterSize) j++;
            windows.Add((i, j - i + 1));
        }
        int best = windows.Max(x => x.count);
        int minSupport = (int) Math.Max(lineCount * MIN_SUPPORT_FRACTION, best * OUTERMOST_RELATIVE_SUPPORT);
        if (best < lineCount * MIN_SUPPORT_FRACTION) return null;
        // Windows are ordered by position, so the first qualifying window is the outermost one. Among overlapping
        // windows starting near it, pick the best supported one.
        var first = windows.First(x => x.count >= minSupport);
        var chosen = windows
            .Where(x => sorted[x.startIndex].pos - sorted[first.startIndex].pos <= clusterSize)
            .MaxBy(x => x.count);
        return sorted.Skip(chosen.startIndex).Take(chosen.count).ToList();
    }

    private static double ContentDensity(SideView view, List<(int line, int pos)> cluster, int pos, int band,
        int whiteLevel)
    {
        long total = 0, nonWhite = 0;
        foreach (var (line, _) in cluster)
        {
            for (int d = pos + 1; d <= pos + band && d < view.Depth; d++)
            {
                total++;
                if (!IsWhite(view.Luma(line, d), view.Chroma(line, d), whiteLevel)) nonWhite++;
            }
        }
        return total == 0 ? 0 : nonWhite / (double) total;
    }

    private static bool IsWhite(int luma, int chroma, int whiteLevel) =>
        luma >= whiteLevel - WHITE_MARGIN && chroma < NEUTRAL_CHROMA;

    private static LineEvent WalkLine(SideView view, int line, int depthLimit, int whiteLevel)
    {
        int minBackgroundRun = Math.Max(6, (int) (view.Depth * MIN_BACKGROUND_RUN_FRACTION));

        // The border pixels must be a uniform neutral region, otherwise content touches the border and there's
        // nothing to crop on this line.
        double level = (view.Luma(line, 0) + view.Luma(line, 1) + view.Luma(line, 2)) / 3.0;
        for (int d = 0; d < 3; d++)
        {
            if (view.Chroma(line, d) >= NEUTRAL_CHROMA || Math.Abs(view.Luma(line, d) - level) > UNIFORM_TOLERANCE)
            {
                return new LineEvent(EventKind.Content, 0, false);
            }
        }

        int i = 3;
        while (i < depthLimit)
        {
            int v = view.Luma(line, i);
            int c = view.Chroma(line, i);
            bool onWhite = level >= whiteLevel - WHITE_MARGIN;
            bool neutral = c < NEUTRAL_CHROMA;

            if (neutral && Math.Abs(v - level) <= UNIFORM_TOLERANCE)
            {
                // Still in the background; track slow drift (e.g. uneven lighting)
                level = level * 0.9 + v * 0.1;
                i++;
                continue;
            }

            if (neutral && v > level + UNIFORM_TOLERANCE)
            {
                if (onWhite)
                {
                    // Already white; a slightly brighter white isn't meaningful
                    level = v;
                    i++;
                    continue;
                }
                // The page is brighter than the background
                return new LineEvent(EventKind.StepUp, i, false);
            }

            // Darker or coloured. Measure how far the deviation extends.
            int j = i;
            int minLuma = 255;
            while (j < depthLimit && j - i <= MAX_LINE_WIDTH &&
                   (view.Luma(line, j) < level - UNIFORM_TOLERANCE || view.Chroma(line, j) >= NEUTRAL_CHROMA))
            {
                minLuma = Math.Min(minLuma, view.Luma(line, j));
                j++;
            }
            int width = j - i;
            if (width <= MAX_LINE_WIDTH && j < depthLimit)
            {
                int after = view.Luma(line, j);
                bool afterNeutral = view.Chroma(line, j) < NEUTRAL_CHROMA;
                bool coloured = Enumerable.Range(i, width).Any(k => view.Chroma(line, k) >= NEUTRAL_CHROMA);
                if (afterNeutral && after >= level - UNIFORM_TOLERANCE && !coloured)
                {
                    if (!onWhite && after > level + UNIFORM_TOLERANCE)
                    {
                        // A thin line followed by something brighter is the shadow at the edge of a page on a darker
                        // background
                        return new LineEvent(EventKind.StepUp, i, false);
                    }
                    if (minLuma >= SHADOW_MIN_LUMA && IsUniformRun(view, line, j, minBackgroundRun, depthLimit))
                    {
                        // A thin grey line followed by a uniform region (e.g. the page margin) is an edge shadow.
                        // Requiring the uniform region rules out the many thin grey strokes within page content.
                        return new LineEvent(EventKind.ShadowLine, i, false);
                    }
                    // Otherwise a thin line is ink (e.g. a panel border, text, screentone)
                    return new LineEvent(EventKind.Content, i, onWhite);
                }
            }

            // A sustained change. If it's a new uniform neutral region (e.g. a grey backing sheet beyond the edge of
            // a white lid), treat it as more background.
            if (neutral && v >= MIN_INNER_BACKGROUND_LUMA &&
                IsUniformRun(view, line, i + 1, minBackgroundRun, depthLimit))
            {
                level = view.Luma(line, i + 1);
                i += minBackgroundRun;
                continue;
            }

            return new LineEvent(EventKind.Content, i, onWhite);
        }

        return new LineEvent(EventKind.None, 0, false);
    }

    private static bool IsUniformRun(SideView view, int line, int start, int length, int depthLimit)
    {
        if (start + length > depthLimit) return false;
        int first = view.Luma(line, start);
        for (int d = start; d < start + length; d++)
        {
            if (view.Chroma(line, d) >= NEUTRAL_CHROMA || Math.Abs(view.Luma(line, d) - first) > UNIFORM_TOLERANCE)
            {
                return false;
            }
        }
        return true;
    }

    private enum Side
    {
        Left = 0,
        Right = 1,
        Top = 2,
        Bottom = 3
    }

    private enum EventKind
    {
        None,
        StepUp,
        ShadowLine,
        Content
    }

    private record struct LineEvent(EventKind Kind, int Position, bool OnWhite);

    private record struct SideResult(int Crop, EdgeKind Kind)
    {
        public static readonly SideResult NotFound = new(0, EdgeKind.None);
    }

    /// <summary>
    /// Accesses pixels from the perspective of one side: "depth" goes inward from that side's border, and "line"
    /// runs along it.
    /// </summary>
    private readonly struct SideView(PixelData pixels, Side side)
    {
        public int Depth => side is Side.Left or Side.Right ? pixels.Width : pixels.Height;
        public int Lines => side is Side.Left or Side.Right ? pixels.Height : pixels.Width;

        private int Index(int line, int depth) => side switch
        {
            Side.Left => line * pixels.Width + depth,
            Side.Right => line * pixels.Width + (pixels.Width - 1 - depth),
            Side.Top => depth * pixels.Width + line,
            _ => (pixels.Height - 1 - depth) * pixels.Width + line
        };

        public int Luma(int line, int depth) => pixels.Luma[Index(line, depth)];
        public int Chroma(int line, int depth) => pixels.Chroma[Index(line, depth)];
    }

    private class PixelData
    {
        public static PixelData FromImage(IMemoryImage image, int workingSize)
        {
            IMemoryImage? scaled = null;
            try
            {
                double scale = Math.Min(1.0, workingSize / (double) Math.Max(image.Width, image.Height));
                if (scale < 1.0)
                {
                    scaled = image.Clone().PerformTransform(new ScaleTransform(scale));
                }
                var source = scaled ?? image;
                int w = source.Width, h = source.Height;
                var luma = new byte[w * h];
                var chroma = new byte[w * h];
                using (var reader = new RgbPixelReader(source))
                {
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            var (r, g, b) = reader[y, x];
                            luma[y * w + x] = (byte) ((r * 299 + g * 587 + b * 114) / 1000);
                            chroma[y * w + x] = (byte) (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)));
                        }
                    }
                }
                return new PixelData(w, h, luma, chroma, EstimateWhiteLevel(luma));
            }
            finally
            {
                scaled?.Dispose();
            }
        }

        private static int EstimateWhiteLevel(byte[] luma)
        {
            // The paper/lid white is about the brightest common value
            var histogram = new int[256];
            foreach (var l in luma) histogram[l]++;
            long target = (long) (luma.Length * 0.02);
            long count = 0;
            for (int i = 255; i >= 0; i--)
            {
                count += histogram[i];
                if (count >= target) return Math.Max(i, 160);
            }
            return 255;
        }

        private PixelData(int width, int height, byte[] luma, byte[] chroma, int whiteLevel)
        {
            Width = width;
            Height = height;
            Luma = luma;
            Chroma = chroma;
            WhiteLevel = whiteLevel;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Luma { get; }
        public byte[] Chroma { get; }
        public int WhiteLevel { get; }
    }
}

/// <summary>
/// How a page edge was detected on one side.
/// </summary>
internal enum EdgeKind
{
    /// <summary>No edge was found, so that side isn't cropped.</summary>
    None,
    /// <summary>The page edge was found against the scanner background.</summary>
    Edge,
    /// <summary>Content (e.g. cover art) extends to the edge of the item and was found against a white background.</summary>
    ContentEdge
}

/// <summary>
/// The amount to crop from each side (in pixels) and how each edge was detected.
/// </summary>
internal record AutoCropResult(
    int Left, int Right, int Top, int Bottom,
    EdgeKind LeftKind, EdgeKind RightKind, EdgeKind TopKind, EdgeKind BottomKind)
{
    public bool AnyEdgeFound => LeftKind != EdgeKind.None || RightKind != EdgeKind.None ||
                                TopKind != EdgeKind.None || BottomKind != EdgeKind.None;
}
