using System;
using System.Text;

namespace Shiroha.Leafy;

public sealed class ByteString : IEquatable<ByteString>
{
    private readonly byte[] _bytes;

    public int Length => this._bytes.Length;
    public bool IsEmpty => this.Length == 0;
    public byte this[int i] => this._bytes[i];

    public static ByteString Empty { get; } = new ByteString([]);

    public ByteString(ReadOnlySpan<byte> bytes)
    {
        this._bytes = bytes.ToArray(); // returns Array.Empty<byte>() for an empty span.
    }

    public ReadOnlySpan<byte> AsSpan()
    {
        return this._bytes;
    }

    public static implicit operator ReadOnlySpan<byte>(ByteString str)
    {
        return str.AsSpan();
    }

    public static bool operator ==(ByteString? a, ByteString? b)
    {
        if (object.ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return a.AsSpan().SequenceEqual(b.AsSpan());
    }

    public static bool operator !=(ByteString? a, ByteString? b) => !(a == b);

    public override bool Equals(object? obj) => obj is ByteString str && this == str;
    public bool Equals(ByteString? other) => this == other;

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.AddBytes(this.AsSpan());
        return h.ToHashCode();
    }

    public override string ToString()
    {
        return Encoding.UTF8.GetString(this.AsSpan());
    }
}
