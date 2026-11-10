using System;

namespace Shiroha.Leafy;

public static class SpanExtensions
{
    public static bool ContainsAnyExcept(this ReadOnlySpan<byte> span, ReadOnlySpan<ByteRange> ranges)
    {
        for (int j = 0; j < ranges.Length; ++j)
        {
            var range = ranges[j];
            int c = span.IndexOfAnyExceptInRange(range.LowInclusive, range.HighInclusive);
            if (c < 0) return true;
            if (c > 0)
            {
                span = span[c..];
                j = j == 0 ? 1 : 0;
            }
        }
        return false;
    }
}
