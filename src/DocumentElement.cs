using System;
using System.Collections.Immutable;

namespace Shiroha.Leafy;

public sealed class DocumentElement : DocumentNode
{
    public DocumentElementTag OpeningTag { get; } // Note: `OpeningTag` can be a closing tag. This occurs when an orphan closing tag is found in the document.
    public DocumentElementTag? ClosingTag { get; }
    public ImmutableArray<DocumentNode> SubNodes { get; }

    public ByteString ElementType => this.OpeningTag.ElementType;

    public DocumentElement(DocumentElementTag openingTag, DocumentElementTag? closingTag, ImmutableArray<DocumentNode> subNodes)
    {
        if (closingTag != null && openingTag.ElementType != closingTag.ElementType) throw new ArgumentException(null, nameof(closingTag));
        if (closingTag != null && !closingTag.IsClosingTag) throw new ArgumentException(null, nameof(closingTag));

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
        this.ClosingTag?.Serialize(dest);
    }
}
