using System;
using System.Buffers;
using System.Collections.Immutable;

namespace Shiroha.Leafy;

public sealed class DocumentElementTag
{
    public static ByteString DefaultSelfClosingEpilogue { get => field ??= new(" /"u8); }

    public bool IsClosingTag { get; }
    public ByteString NodeType { get; }
    public ImmutableArray<DocumentElementAttribute> Attributes { get; }
    public ByteString Epilogue { get; }
    public bool IsSelfClosing { get; }

    public DocumentElementTag(bool isClosing, ByteString nodeType, ImmutableArray<DocumentElementAttribute> attributes, ByteString epilogue)
    {
        if (
            nodeType.IsEmpty ||
            "+-.:"u8.Contains(nodeType[0]) ||
            nodeType.AsSpan().ContainsAnyInRange((byte)0, (byte)'*') ||
            nodeType.AsSpan().ContainsAnyInRange((byte)';', (byte)'@') ||
            nodeType.AsSpan().ContainsAnyInRange((byte)'[', (byte)'^') ||
            nodeType.AsSpan().ContainsAnyInRange((byte)'{', (byte)127) ||
            nodeType.AsSpan().ContainsAny(",/<`"u8)
            )
            throw new ArgumentException(null, nameof(nodeType));
        if (
            !epilogue.IsEmpty &&
            (epilogue[^1] == (byte)'/' ? epilogue.AsSpan()[..^1] : epilogue.AsSpan())
            .ContainsAnyExcept([(byte)' ', new ByteRange((byte)'\t', (byte)'\r')])
            )
            throw new ArgumentException(null, nameof(epilogue));

        this.IsClosingTag = isClosing;
        this.NodeType = nodeType;
        this.Attributes = attributes;
        this.Epilogue = epilogue;
        this.IsSelfClosing = !epilogue.IsEmpty && epilogue[^1] == (byte)'/';
    }

    public void Serialize<TWriter>(TWriter dest)
    where TWriter : IBufferWriter<byte>
    {

        dest.Write(!this.IsClosingTag ? "<"u8 : "</"u8);
        dest.Write(this.NodeType);
        foreach (var a in this.Attributes)
        {
            a.Serialize(dest);
        }
        if (!this.Epilogue.IsEmpty) dest.Write(this.Epilogue);
        dest.Write(">"u8);
    }
}
