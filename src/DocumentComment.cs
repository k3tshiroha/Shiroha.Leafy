using System;
using System.Buffers;

namespace Shiroha.Leafy;

public sealed class DocumentComment : DocumentNode
{
    public ByteString RawText { get; }

    public DocumentComment(ByteString rawText)
    {
        if (rawText.AsSpan().StartsWith("<!--"u8))
        {
            if (rawText.Length < 7 || !rawText.AsSpan().EndsWith("-->"u8))
                throw new ArgumentException(null, nameof(rawText));
        }
        else if (
            rawText.AsSpan().StartsWith("<!DOCTYPE"u8) ||
            rawText.AsSpan().StartsWith("<!doctype"u8)
            )
        {
            if (!rawText.AsSpan().EndsWith(">"u8))
                throw new ArgumentException(null, nameof(rawText));
        }
        else if (rawText.AsSpan().StartsWith("<?"u8))
        {
            if (rawText.Length < 4 || !rawText.AsSpan().EndsWith("?>"u8))
                throw new ArgumentException(null, nameof(rawText));
        }
        else
        {
            throw new ArgumentException(null, nameof(rawText));
        }

        this.RawText = rawText;
    }

    public override void Serialize<TWriter>(TWriter dest)
    {
        dest.Write(RawText);
    }
}
