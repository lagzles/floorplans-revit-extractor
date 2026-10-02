using System.Collections.Generic;

namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>
    /// Connected regions of one class in the mask, 8-connected (pixels that
    /// touch at a corner belong together, as the edge of a rotated opening
    /// does). Used by the gate's count check: elements extracted against blobs
    /// painted.
    /// </summary>
    public static class BlobCounter
    {
        public static int Count(SemanticMask mask, StructureClass cls)
        {
            byte[] pixels = mask.Pixels;
            int width = mask.Width;
            int height = mask.Height;
            var seen = new bool[pixels.Length];
            var stack = new Stack<int>();
            int blobs = 0;

            for (int start = 0; start < pixels.Length; start++)
            {
                if (seen[start] || pixels[start] != (byte)cls)
                {
                    continue;
                }

                blobs++;
                seen[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    int x = i % width;
                    int y = i / width;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                            {
                                continue;
                            }

                            int n = (ny * width) + nx;
                            if (!seen[n] && pixels[n] == (byte)cls)
                            {
                                seen[n] = true;
                                stack.Push(n);
                            }
                        }
                    }
                }
            }

            return blobs;
        }
    }
}
