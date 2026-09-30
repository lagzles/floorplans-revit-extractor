namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>
    /// Which pixels a set of shapes covers. Built by the rasterizers and
    /// read-only to everyone else.
    /// </summary>
    public sealed class Coverage
    {
        private readonly bool[] _cells;

        internal Coverage(int width, int height)
        {
            _cells = new bool[PixelSpans.RequireArea(width, height)];
            Width = width;
            Height = height;
        }

        public int Width { get; }

        public int Height { get; }

        public int Count { get; private set; }

        public bool this[int x, int y] => _cells[PixelSpans.IndexOf(x, y, Width, Height)];

        internal void SetSpan(int row, int first, int last)
        {
            int rowStart = row * Width;
            for (int x = first; x <= last; x++)
            {
                if (!_cells[rowStart + x])
                {
                    _cells[rowStart + x] = true;
                    Count++;
                }
            }
        }
    }
}
