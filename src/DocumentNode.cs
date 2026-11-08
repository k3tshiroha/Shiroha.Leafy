using System.Buffers;

namespace Shiroha.Leafy;

public abstract class DocumentNode
{
    internal DocumentNode() { }

    public abstract void Serialize<TWriter>(TWriter dest) where TWriter : IBufferWriter<byte>;
}
