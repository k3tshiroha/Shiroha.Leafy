using System;

namespace Shiroha.Leafy;

public readonly record struct ByteRange(byte LowInclusive, byte HighInclusive)
{
    public static implicit operator ByteRange(byte value) => new(value, value);
}

public static class ByteRangeExtensions
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
