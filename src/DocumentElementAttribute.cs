using System;
using System.Buffers;
using System.Collections.Immutable;

namespace Shiroha.Leafy;

public sealed class DocumentElementAttribute
{
    private static ByteString _WhiteSpace { get => field ??= new(" "u8); }

    public ByteString Separator { get; }
    public ByteString Key { get; }
    public ByteString? Value { get; }
    public byte QuotationChar { get; }

    public DocumentElementAttribute(ByteString key)
     : this(_WhiteSpace, key, null!, (byte)'@') // 「@」is meaningless, set to this's property but never appears on output.
    { }

    public DocumentElementAttribute(ByteString key, ByteString value)
     : this(_WhiteSpace, key, value, value.AsSpan().Contains((byte)'"') ? (byte)'\'' : (byte)'"')
    { }

    public DocumentElementAttribute(ByteString key, ByteString value, byte quotationChar)
     : this(_WhiteSpace, key, value, quotationChar)
    { }

    public DocumentElementAttribute(ByteString separator, ByteString key, ByteString value, byte quotationChar)
    {
        if (separator.IsEmpty || separator.AsSpan().ContainsAnyExcept([(byte)' ', new ByteRange((byte)'\t', (byte)'\r')]))
            throw new ArgumentException(null, nameof(separator));
        if (
            key.IsEmpty ||
            key.AsSpan().ContainsAnyInRange((byte)0, (byte)32) || // disallow control chars and white space
            key.AsSpan().ContainsAnyInRange((byte)'<', (byte)'>') || // disallow '<', '=', '>'
            key.AsSpan().ContainsAny((byte)'"', (byte)'\'')
            )
            throw new ArgumentException(null, nameof(key));
        if (value != null)
        {
            switch (quotationChar)
            {
                case (byte)'"':
                case (byte)'\'':
                    if (
                        value.AsSpan().ContainsAnyInRange((byte)0, (byte)8) || // allow '\t', '\n', '\v', '\f', '\r'
                        value.AsSpan().ContainsAnyInRange((byte)14, (byte)31) || // allow white space, alphabets, and symbols
                        value.AsSpan().ContainsAny((byte)127, quotationChar)
                        )
                        throw new ArgumentException(null, nameof(value));
                    break;
                case 0:
                    if (
                        !value.IsEmpty && (
                            value.AsSpan().ContainsAnyInRange((byte)0, (byte)32) || // disallow control chars and white space
                            value.AsSpan().ContainsAny((byte)127, (byte)'<', (byte)'>') || // disallow '<', '>'
                            value[0] is (byte)'"' or (byte)'\''
                            )
                        )
                        throw new ArgumentException(null, nameof(value));
                    break;
                default:
                    throw new ArgumentException(null, nameof(quotationChar));
            }
        }

        this.Separator = separator;
        this.Key = key;
        this.Value = value;
        this.QuotationChar = quotationChar;
    }

    public void Serialize<TWriter>(TWriter dest)
    where TWriter : IBufferWriter<byte>
    {
        dest.Write(this.Separator);
        dest.Write(this.Key);
        if (this.Value != null)
        {
            dest.Write("="u8);
            if (this.QuotationChar != 0) dest.Write([this.QuotationChar]);
            dest.Write(this.Value);
            if (this.QuotationChar != 0) dest.Write([this.QuotationChar]);
        }
    }
}
