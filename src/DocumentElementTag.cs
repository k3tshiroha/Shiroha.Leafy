using System;
using System.Buffers;
using System.Collections.Immutable;

namespace Shiroha.Leafy;

public sealed class DocumentElementTag
{
    public static ByteString DefaultSelfClosingEpilogue { get => field ??= new(" /"u8); }

    public bool IsClosingTag { get; }
    public ByteString ElementType { get; }
    public ImmutableArray<DocumentElementAttribute> Attributes { get; }
    public ByteString Epilogue { get; }
    public bool IsSelfClosing { get; }

    public DocumentElementTag(bool isClosing, ByteString elementType, ImmutableArray<DocumentElementAttribute> attributes, ByteString epilogue)
    {
        if (
            elementType.IsEmpty ||
            "+-.:"u8.Contains(elementType[0]) ||
            elementType.AsSpan().ContainsAnyInRange((byte)0, (byte)' ') || // allow '!' at the head
            elementType.AsSpan().ContainsAnyInRange((byte)'"', (byte)'*') || // allow '+', '-', '.'
            elementType.AsSpan().ContainsAnyInRange((byte)';', (byte)'@') || // allow ':'
            elementType.AsSpan().ContainsAnyInRange((byte)'[', (byte)'^') || // allow '_'
            elementType.AsSpan().ContainsAnyInRange((byte)'{', (byte)127) ||
            elementType.AsSpan().ContainsAny(",/<`"u8) ||
            elementType.AsSpan()[1..].Contains((byte)'!')
            )
            throw new ArgumentException(null, nameof(elementType));
        if (
            !epilogue.IsEmpty &&
            (epilogue[^1] == (byte)'/' ? epilogue.AsSpan()[..^1] : epilogue.AsSpan())
            .ContainsAnyExcept([(byte)' ', new ByteRange((byte)'\t', (byte)'\r')]) // ' ', '\t', '\n', '\v', '\f', '\r'
            )
            throw new ArgumentException(null, nameof(epilogue));

        this.IsClosingTag = isClosing;
        this.ElementType = elementType;
        this.Attributes = attributes;
        this.Epilogue = epilogue;
        this.IsSelfClosing = !epilogue.IsEmpty && epilogue[^1] == (byte)'/';
    }

    public void Serialize<TWriter>(TWriter dest)
    where TWriter : IBufferWriter<byte>
    {

        dest.Write(!this.IsClosingTag ? "<"u8 : "</"u8);
        dest.Write(this.ElementType);
        foreach (var a in this.Attributes)
        {
            a.Serialize(dest);
        }
        if (!this.Epilogue.IsEmpty) dest.Write(this.Epilogue);
        dest.Write(">"u8);
    }
}
