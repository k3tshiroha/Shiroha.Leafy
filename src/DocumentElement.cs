using System;
using System.Collections.Immutable;

namespace Shiroha.Leafy;

public sealed class DocumentElement : DocumentNode
{
    public DocumentElementTag OpeningTag { get; }
    public DocumentElementTag ClosingTag { get; }
    public ImmutableArray<DocumentNode> SubNodes { get; }

    public ByteString NodeType => this.OpeningTag.NodeType;

    public DocumentElement(DocumentElementTag openingTag, DocumentElementTag closingTag, ImmutableArray<DocumentNode> subNodes)
    {
        if (openingTag.NodeType != closingTag.NodeType) throw new ArgumentException(null, nameof(closingTag));

        this.OpeningTag = openingTag;
        this.ClosingTag = closingTag;
        this.SubNodes = subNodes;
    }

    public override void Serialize<TWriter>(TWriter dest)
    {
        this.OpeningTag.Serialize(dest);
        foreach (var c in this.SubNodes)
        {
            c.Serialize(dest);
        }
        this.ClosingTag.Serialize(dest);
    }
}
