using System;

namespace Shiroha.Leafy;

public readonly record struct ByteRange(byte LowInclusive, byte HighInclusive)
{
    public static implicit operator ByteRange(byte value) => new(value, value);
}
