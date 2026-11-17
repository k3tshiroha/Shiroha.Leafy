using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Shiroha.Leafy;


public enum DocumentParserStatus
{
    Continue,
    FinishedSuccessfully,
    FinishedWithNoResult,
    UnexpectedChar,
    Unterminated,
}

public interface IDocumentParserSettings
{
    bool IsVoidElement(ByteString elementType);
    bool IsRawTextElement(ByteString elementType);
}

public sealed class DocumentParser<TSettings>
where TSettings : IDocumentParserSettings
{
    private readonly TSettings _settings;

    public DocumentNode Result { get => field ?? throw new InvalidOperationException(); private set; }

    public DocumentParser(TSettings settings)
    {
        this._settings = settings;
    }

    private readonly struct StackFrame(DocumentElementTag openingTag, ImmutableArray<DocumentNode>.Builder subnodes)
    {
        public readonly DocumentElementTag openingTag = openingTag;
        public readonly ImmutableArray<DocumentNode>.Builder subnodes = subnodes;
    }

    private readonly Stack<ImmutableArray<DocumentNode>.Builder> _pool_nodeArrayBuilder = new();
    private readonly Stack<StackFrame> _stack = new();
    private readonly ImmutableArray<DocumentElementAttribute>.Builder _atts = ImmutableArray.CreateBuilder<DocumentElementAttribute>();
    private readonly ArrayBufferWriter<byte> _text = new();

    private bool _isClosingTag;
    private ByteString? _elementType;
    private ByteString? _attSeparator;
    private ByteString? _attName;
    private byte _quotationChar = 127;

    private ByteString? _endOfRawTextSection;

    private enum State
    {
        ReadNode,
        ReadElementType,
        ReadAttributeSeparator,
        ReadAttributeName,
        ReadAttributeValue,
        ReadTagEpilogue,
        ReadRawTextElement,
        ReadRawTextElementClosingTag,
        ReadDeclaration,
        ReadDoctype,
        ReadComment,
        ReadCData,
        ReadInstruction,
        Error,
    }
    private State _state;

    private ref struct Input
    {
        public ReadOnlySpan<byte> Span { get; private set; }
        public int BytesConsumed { get; private set; }

        public Input(ReadOnlySpan<byte> span)
        {
            this.Span = span;
            this.BytesConsumed = 0;
        }

        public void Consume(int count)
        {
            this.Span = this.Span[count..];
            this.BytesConsumed += count;
        }

        public void ConsumeAll()
        {
            this.BytesConsumed += this.Span.Length;
            this.Span = default;
        }
    }

    public DocumentParserStatus Parse(ReadOnlySpan<byte> bytes, bool isFinal, out int bytesConsumed)
    {
        this.Result = null!;

        Input input = new(bytes);
        while (true)
        {
            switch (this._state)
            {
                case State.ReadNode:
                    if (input.Span.IsEmpty)
                    {
                        bytesConsumed = input.BytesConsumed;
                        if (isFinal)
                        {
                            bool hasResult = rollup();
                            return hasResult ? DocumentParserStatus.FinishedSuccessfully : DocumentParserStatus.FinishedWithNoResult;
                        }
                        else
                        {
                            return DocumentParserStatus.Continue;
                        }
                    }
                    if (input.Span[0] != (byte)'<')
                    {
                        collectTextUntil(ref input, (byte)'<');
                        continue;
                    }
                    if (input.Span.Length == 1)
                    {
                        if (isFinal)
                        {
                            this._text.Write("<"u8);
                            input.Consume(1);
                            continue;
                        }
                        else
                        {
                            bytesConsumed = input.BytesConsumed;
                            return DocumentParserStatus.Continue;
                        }
                    }
                    switch (input.Span[1])
                    {
                        case byte x when DocumentSearchValues.AllowedInHeadOfElementType.Contains(x):
                            if (pushTextNode())
                            {
                                bytesConsumed = input.BytesConsumed;
                                return DocumentParserStatus.FinishedSuccessfully;
                            }
                            this._isClosingTag = false;
                            input.Consume(1);
                            this._state = State.ReadElementType;
                            continue;
                        case (byte)'/':
                            if (pushTextNode())
                            {
                                bytesConsumed = input.BytesConsumed;
                                return DocumentParserStatus.FinishedSuccessfully;
                            }
                            this._isClosingTag = true;
                            input.Consume(2);
                            this._state = State.ReadElementType;
                            continue;
                        case (byte)'!':
                            if (pushTextNode())
                            {
                                bytesConsumed = input.BytesConsumed;
                                return DocumentParserStatus.FinishedSuccessfully;
                            }
                            this._state = State.ReadDeclaration;
                            continue;
                        case (byte)'?':
                            if (pushTextNode())
                            {
                                bytesConsumed = input.BytesConsumed;
                                return DocumentParserStatus.FinishedSuccessfully;
                            }
                            this._state = State.ReadInstruction;
                            continue;
                        default:
                            this._text.Write("<"u8);
                            input.Consume(1);
                            continue;
                    }
                case State.ReadElementType:
                    if (input.Span.IsEmpty)
                    {
                        bytesConsumed = input.BytesConsumed;
                        return !isFinal ? DocumentParserStatus.Continue : DocumentParserStatus.Unterminated;
                    }
                    if (this._text.WrittenCount == 0)
                    {
                        if (DocumentSearchValues.AllowedInHeadOfElementType.Contains(input.Span[0]))
                        {
                            this._text.Write(input.Span[0..1]);
                            input.Consume(1);
                        }
                        else
                        {
                            bytesConsumed = input.BytesConsumed;
                            return DocumentParserStatus.UnexpectedChar;
                        }
                    }
                    collectText(ref input, DocumentSearchValues.AllowedInElementType);
                    if (input.Span.IsEmpty)
                    {
                        bytesConsumed = input.BytesConsumed;
                        return !isFinal ? DocumentParserStatus.Continue : DocumentParserStatus.Unterminated;
                    }
                    else
                    {
                        this._elementType = new ByteString(this._text.WrittenSpan);
                        this._text.ResetWrittenCount();
                        this._state = State.ReadAttributeSeparator;
                        continue;
                    }
                case State.ReadAttributeSeparator:
                    collectText(ref input, DocumentSearchValues.WhiteSpaces);
                    if (input.Span.IsEmpty)
                    {
                        bytesConsumed = input.BytesConsumed;
                        return !isFinal ? DocumentParserStatus.Continue : DocumentParserStatus.Unterminated;
                    }
                    if (input.Span[0] is (byte)'/' or (byte)'>')
                    {
                        this._state = State.ReadTagEpilogue;
                        continue;
                    }
                    if (this._text.WrittenCount == 0)
                    {
                        bytesConsumed = input.BytesConsumed;
                        return DocumentParserStatus.UnexpectedChar;
                    }
                    this._attSeparator = takeText();
                    this._state = State.ReadAttributeName;
                    continue;
                case State.ReadAttributeName:
                    collectTextUntil(ref input, DocumentSearchValues.DisallowedInAttributeName);
                    if (input.Span.IsEmpty)
                    {
                        bytesConsumed = input.BytesConsumed;
                        return !isFinal ? DocumentParserStatus.Continue : DocumentParserStatus.Unterminated;
                    }
                    if (this._text.WrittenCount == 0)
                    {
                        bytesConsumed = input.BytesConsumed;
                        return DocumentParserStatus.UnexpectedChar;
                    }
                    this._attName = takeText();
                    if (input.Span[0] == (byte)'=')
                    {
                        input.Consume(1);
                        this._state = State.ReadAttributeValue;
                        continue;
                    }
                    else if (
                        input.Span[0] is (byte)'/' or (byte)'>' ||
                        DocumentSearchValues.WhiteSpaces.Contains(input.Span[0])
                        )
                    {
                        if (this._attSeparator == null) throw new UnreachableException();
                        var att = new DocumentElementAttribute(this._attSeparator, this._attName, null, 0);
                        this._atts.Add(att);
                        this._attSeparator = null;
                        this._attName = null;
                        this._state = State.ReadAttributeSeparator;
                        continue;
                    }
                    else
                    {
                        bytesConsumed = input.BytesConsumed;
                        this._state = State.Error;
                        return DocumentParserStatus.UnexpectedChar;
                    }
                case State.ReadAttributeValue:
                    if (input.Span.IsEmpty)
                    {
                        bytesConsumed = input.BytesConsumed;
                        return !isFinal ? DocumentParserStatus.Continue : DocumentParserStatus.Unterminated;
                    }
                    if (this._quotationChar == 127)
                    {
                        if (this._text.WrittenCount != 0) throw new UnreachableException();
                        if (input.Span[0] is (byte)'"' or (byte)'\'')
                        {
                            this._quotationChar = input.Span[0];
                            input.Consume(1);
                        }
                        else
                        {
                            this._quotationChar = 0;
                        }
                    }
                    {
                        SearchValues<byte> searchValues = this._quotationChar switch
                        {
                            (byte)'"' => DocumentSearchValues.DisallowedInAttributeValueDoublequoted,
                            (byte)'\'' => DocumentSearchValues.DisallowedInAttributeValueSinglequoted,
                            0 => DocumentSearchValues.DisallowedInAttributeValueUnquoted,
                            _ => throw new UnreachableException(),
                        };
                        collectTextUntil(ref input, searchValues);
                    }
                    if (input.Span.IsEmpty)
                    {
                        bytesConsumed = input.BytesConsumed;
                        return !isFinal ? DocumentParserStatus.Continue : DocumentParserStatus.Unterminated;
                    }
                    if (this._quotationChar != 0)
                    {
                        if (input.Span[0] == this._quotationChar)
                        {
                            input.Consume(1);
                        }
                        else
                        {
                            bytesConsumed = input.BytesConsumed;
                            return DocumentParserStatus.UnexpectedChar;
                        }
                    }
                    {
                        var attValue = takeText();
                        if (this._attSeparator == null || this._attName == null) throw new UnreachableException();
                        var att = new DocumentElementAttribute(this._attSeparator, this._attName, attValue, this._quotationChar);
                        this._atts.Add(att);
                        this._attSeparator = null;
                        this._attName = null;
                        this._quotationChar = 127;
                    }
                    this._state = State.ReadAttributeSeparator;
                    continue;
                case State.ReadTagEpilogue:
                    if (input.Span.IsEmpty) throw new UnreachableException();
                    {
                        bool isSelfClosing = false;
                        if (input.Span[0] == (byte)'/')
                        {
                            if (input.Span.Length == 1)
                            {
                                bytesConsumed = input.BytesConsumed;
                                return !isFinal ? DocumentParserStatus.Continue : DocumentParserStatus.Unterminated;
                            }
                            this._text.Write("/"u8);
                            isSelfClosing = true;
                            input.Consume(1);
                        }
                        if (input.Span[0] != (byte)'>')
                        {
                            bytesConsumed = input.BytesConsumed;
                            this._state = State.Error;
                            return DocumentParserStatus.UnexpectedChar;
                        }
                        input.Consume(1);
                        if (this._elementType == null) throw new UnreachableException();
                        isSelfClosing |= this._settings.IsVoidElement(this._elementType);
                        var atts = this._atts.DrainToImmutable();
                        var epilogue = this.takeText();
                        var tag = new DocumentElementTag(this._isClosingTag, this._elementType, atts, epilogue);
                        this._elementType = null;
                        if (isSelfClosing)
                        {
                            var element = new DocumentElement(tag, null, []);
                            this._state = State.ReadNode;
                            if (pushNode(element))
                            {
                                bytesConsumed = input.BytesConsumed;
                                return DocumentParserStatus.FinishedSuccessfully;
                            }
                            continue;
                        }
                        else if (tag.IsClosingTag)
                        {
                            if (endElement(tag))
                            {
                                bytesConsumed = input.BytesConsumed;
                                this._state = State.ReadNode;
                                return DocumentParserStatus.FinishedSuccessfully;
                            }
                            this._state = State.ReadNode;
                            continue;
                        }
                        else
                        {
                            beginElement(tag);
                            if (this._settings.IsRawTextElement(tag.ElementType))
                            {
                                this._text.Write("</"u8);
                                this._text.Write(tag.ElementType);
                                this._endOfRawTextSection = takeText();
                                this._state = State.ReadRawTextElement;
                            }
                            else
                            {
                                this._state = State.ReadNode;
                            }
                            continue;
                        }
                    }
                case State.ReadRawTextElement:
                    if (this._endOfRawTextSection == null) throw new UnreachableException();
                    collectTextUntil(ref input, this._endOfRawTextSection);
                    if (input.Span.Length <= this._endOfRawTextSection.Length)
                    {
                        if (isFinal)
                        {
                            // this is part of the raw text
                            this._text.Write(input.Span);
                            input.ConsumeAll();
                            if (pushTextNode()) throw new UnreachableException();
                            if (!rollup()) throw new UnreachableException();
                            this._endOfRawTextSection = null;
                            this._state = State.ReadNode;
                            bytesConsumed = input.BytesConsumed;
                            return DocumentParserStatus.FinishedSuccessfully;
                        }
                        else
                        {
                            bytesConsumed = input.BytesConsumed;
                            return DocumentParserStatus.Continue;
                        }
                    }
                    if (
                        input.Span[this._endOfRawTextSection.Length] is (byte)'>' or (byte)'/' ||
                        DocumentSearchValues.WhiteSpaces.Contains(input.Span[this._endOfRawTextSection.Length])
                        )
                    {
                        if (pushTextNode()) throw new UnreachableException();
                        input.Consume(this._endOfRawTextSection.Length);
                        this._state = State.ReadRawTextElementClosingTag;
                        continue;
                    }
                    else
                    {
                        this._text.Write(this._endOfRawTextSection);
                        input.Consume(this._endOfRawTextSection.Length);
                        continue;
                    }
                case State.ReadRawTextElementClosingTag:
                    if (this._endOfRawTextSection == null) throw new UnreachableException();
                    collectText(ref input, DocumentSearchValues.WhiteSpaces);
                    if (input.Span.IsEmpty)
                    {
                        bytesConsumed = input.BytesConsumed;
                        return !isFinal ? DocumentParserStatus.Continue : DocumentParserStatus.Unterminated;
                    }
                    if (input.Span[0] == '/')
                    {
                        if (input.Span.Length == 1)
                        {
                            bytesConsumed = input.BytesConsumed;
                            if (isFinal)
                            {
                                input.Consume(1);
                                this._state = State.Error;
                                return DocumentParserStatus.Unterminated;
                            }
                            else
                            {
                                return DocumentParserStatus.Continue;
                            }
                        }
                        this._text.Write("/"u8);
                        input.Consume(1);
                    }
                    if (input.Span[0] == '>')
                    {
                        input.Consume(1);
                        ByteString elementType = new(this._endOfRawTextSection.AsSpan()[2..]);
                        ByteString epilogue = takeText();
                        DocumentElementTag closingTag = new(true, elementType, [], epilogue);
                        bool hasResult = endElement(closingTag);
                        this._endOfRawTextSection = null;
                        this._state = State.ReadNode;
                        if (hasResult)
                        {
                            bytesConsumed = input.BytesConsumed;
                            return DocumentParserStatus.FinishedSuccessfully;
                        }
                        continue;
                    }
                    else
                    {
                        bytesConsumed = input.BytesConsumed;
                        this._state = State.Error;
                        return DocumentParserStatus.UnexpectedChar;
                    }
                case State.ReadDeclaration:
                    if (input.Span.Length < 3)
                    {
                        bytesConsumed = input.BytesConsumed;
                        return !isFinal ? DocumentParserStatus.Continue : DocumentParserStatus.Unterminated;
                    }
                    if (input.Span[0] != '<' || input.Span[1] != '!') throw new UnreachableException();
                    if (input.Span[2] is (byte)'-')
                    {
                        this._state = State.ReadComment;
                        continue;
                    }
                    if (input.Span[2] is (byte)'d' or (byte)'D')
                    {
                        this._state = State.ReadDoctype;
                        continue;
                    }
                    if (input.Span[2] is (byte)'[')
                    {
                        this._state = State.ReadCData;
                        continue;
                    }
                    bytesConsumed = input.BytesConsumed;
                    return DocumentParserStatus.UnexpectedChar;
                case State.ReadDoctype:
                    {
                        ReadOnlySpan<byte> doctypeOpen;
                        if (this._text.WrittenCount == 0)
                        {
                            if (input.Span.Length < 3) throw new UnreachableException();
                            doctypeOpen = input.Span[2] switch
                            {
                                (byte)'D' => "<!DOCTYPE"u8,
                                (byte)'d' => "<!doctype"u8,
                                _ => throw new UnreachableException(),
                            };
                        }
                        else
                        {
                            doctypeOpen = default;
                        }
                        switch (collectRawTextSection(ref input, isFinal, doctypeOpen, ">"u8))
                        {
                            case -1:
                                bytesConsumed = input.BytesConsumed;
                                this._state = State.Error;
                                return input.Span.IsEmpty ? DocumentParserStatus.Unterminated : DocumentParserStatus.UnexpectedChar;
                            case 0:
                                bytesConsumed = input.BytesConsumed;
                                return DocumentParserStatus.Continue;
                            case 1:
                                break;
                        }
                        var doctype = takeText();
                        var node = new DocumentComment(doctype);
                        this._state = State.ReadNode;
                        if (pushNode(node))
                        {
                            bytesConsumed = input.BytesConsumed;
                            return DocumentParserStatus.FinishedSuccessfully;
                        }
                        continue;
                    }
                case State.ReadComment:
                    switch (collectRawTextSection(ref input, isFinal, "<!--"u8, "-->"u8))
                    {
                        case -1:
                            bytesConsumed = input.BytesConsumed;
                            this._state = State.Error;
                            return input.Span.IsEmpty ? DocumentParserStatus.Unterminated : DocumentParserStatus.UnexpectedChar;
                        case 0:
                            bytesConsumed = input.BytesConsumed;
                            return DocumentParserStatus.Continue;
                        case 1:
                            break;
                    }
                    {
                        var comment = takeText();
                        var node = new DocumentComment(comment);
                        this._state = State.ReadNode;
                        if (pushNode(node))
                        {
                            bytesConsumed = input.BytesConsumed;
                            return DocumentParserStatus.FinishedSuccessfully;
                        }
                        continue;
                    }
                case State.ReadCData:
                    switch (collectRawTextSection(ref input, isFinal, "<![CDATA["u8, "]]>"u8))
                    {
                        case -1:
                            bytesConsumed = input.BytesConsumed;
                            this._state = State.Error;
                            return input.Span.IsEmpty ? DocumentParserStatus.Unterminated : DocumentParserStatus.UnexpectedChar;
                        case 0:
                            bytesConsumed = input.BytesConsumed;
                            return DocumentParserStatus.Continue;
                        case 1:
                            break;
                    }
                    {
                        var cdata = takeText();
                        var node = new DocumentText(cdata, false);
                        this._state = State.ReadNode;
                        if (pushNode(node))
                        {
                            bytesConsumed = input.BytesConsumed;
                            return DocumentParserStatus.FinishedSuccessfully;
                        }
                        continue;
                    }
                case State.ReadInstruction:
                    switch (collectRawTextSection(ref input, isFinal, "<?"u8, "?>"u8))
                    {
                        case -1:
                            bytesConsumed = input.BytesConsumed;
                            this._state = State.Error;
                            return input.Span.IsEmpty ? DocumentParserStatus.Unterminated : DocumentParserStatus.UnexpectedChar;
                        case 0:
                            bytesConsumed = input.BytesConsumed;
                            return DocumentParserStatus.Continue;
                        case 1:
                            break;
                    }
                    {
                        var instruction = takeText();
                        var node = new DocumentComment(instruction);
                        this._state = State.ReadNode;
                        if (pushNode(node))
                        {
                            bytesConsumed = input.BytesConsumed;
                            return DocumentParserStatus.FinishedSuccessfully;
                        }
                        continue;
                    }
                case State.Error:
                    throw new InvalidOperationException();
                default:
                    throw new UnreachableException();
            }
        }
    }

    void collectText(ref Input input, SearchValues<byte> acceptChars)
    {
        int c = input.Span.IndexOfAnyExcept(acceptChars);
        if (c < 0)
        {
            this._text.Write(input.Span);
            input.ConsumeAll();
        }
        else
        {
            this._text.Write(input.Span[..c]);
            input.Consume(c);
        }
    }

    void collectTextUntil(ref Input input, byte stopChar)
    {
        int c = input.Span.IndexOf(stopChar);
        if (c < 0)
        {
            this._text.Write(input.Span);
            input.ConsumeAll();
        }
        else
        {
            this._text.Write(input.Span[..c]);
            input.Consume(c);
        }
    }

    void collectTextUntil(ref Input input, SearchValues<byte> stopChars)
    {
        int c = input.Span.IndexOfAny(stopChars);
        if (c < 0)
        {
            this._text.Write(input.Span);
            input.ConsumeAll();
        }
        else
        {
            this._text.Write(input.Span[..c]);
            input.Consume(c);
        }
    }

    void collectTextUntil(ref Input input, ReadOnlySpan<byte> stopKeyword)
    {
        if (stopKeyword.IsEmpty) throw new UnreachableException();
        int c = input.Span.IndexOf(stopKeyword);
        if (c < 0)
        {
            var lastSpan =
                input.Span.Length < stopKeyword.Length ?
                    input.Span :
                    input.Span[^stopKeyword.Length..];
            int step = 1 + stopKeyword[1..].IndexOf(stopKeyword[0]);
            while (true)
            {
                int d = lastSpan.IndexOf(stopKeyword[0]);
                if (d == -1)
                {
                    lastSpan = default;
                    break;
                }
                lastSpan = lastSpan[d..];
                int common = lastSpan.CommonPrefixLength(stopKeyword);
                if (common == 0) throw new UnreachableException();
                if (common == lastSpan.Length) break;
                lastSpan = step == 0 ? lastSpan[common..] : lastSpan[Math.Min(common, step)..];
            }
            c = input.Span.Length - lastSpan.Length;
        }
        this._text.Write(input.Span[..c]);
        input.Consume(c);
    }

    bool collectKeyword(ref Input input, bool isFinal, ReadOnlySpan<byte> keyword, out int status)
    {
        // status
        //  -1 : unexpected char
        //   0 : continue
        //   1 : end of section

        if (!isFinal)
        {
            if (input.Span.Length < keyword.Length)
            {
                status = 0;
                return false;
            }
            if (!input.Span.StartsWith(keyword))
            {
                input.Consume(input.Span.CommonPrefixLength(keyword));
                status = -1;
                return false;
            }
            this._text.Write(keyword);
            input.Consume(keyword.Length);
        }
        else
        {
            int c = input.Span.CommonPrefixLength(keyword);
            input.Consume(c);
            if (c != keyword.Length)
            {
                status = -1;
                return false;
            }
            this._text.Write(keyword);
        }
        status = 1;
        return true;
    }

    int collectRawTextSection(ref Input input, bool isFinal, ReadOnlySpan<byte> beginning, ReadOnlySpan<byte> end)
    {
        int status;
        if (this._text.WrittenCount < beginning.Length)
        {
            if (this._text.WrittenCount != 0) throw new UnreachableException();
            if (!collectKeyword(ref input, isFinal, beginning, out status)) return status;
        }
        collectTextUntil(ref input, end);
        if (input.Span.Length < end.Length)
        {
            if (isFinal)
            {
                input.ConsumeAll();
                return -1; // unterminated
            }
            else
            {
                return 0; // continue
            }
        }
        collectKeyword(ref input, isFinal, end, out status);
        return status;
    }

    ByteString takeText()
    {
        if (this._text.WrittenCount == 0)
        {
            return ByteString.Empty;
        }
        else
        {
            var result = new ByteString(this._text.WrittenSpan);
            this._text.ResetWrittenCount();
            return result;
        }
    }

    bool pushTextNode()
    {
        if (this._text.WrittenCount == 0) return false;
        var text = takeText();
        var node = new DocumentText(text, false);
        return pushNode(node);
    }

    bool pushNode(DocumentNode node)
    {
        if (this._stack.TryPeek(out var frame))
        {
            frame.subnodes.Add(node);
            return false;
        }
        else
        {
            this.Result = node;
            return true;
        }
    }

    void beginElement(DocumentElementTag openingTag)
    {
        if (!this._pool_nodeArrayBuilder.TryPop(out var subnodes)) subnodes = ImmutableArray.CreateBuilder<DocumentNode>();
        this._stack.Push(new StackFrame(openingTag, subnodes));
    }

    bool endElement(DocumentElementTag? closingTag)
    {
        DocumentElement node;
        if (this._stack.TryPeek(out var frame))
        {
            DocumentElementTag openingTag = frame.openingTag;
            if (closingTag == null || openingTag.ElementType == closingTag.ElementType)
            {
                ImmutableArray<DocumentNode> subnodes = frame.subnodes.DrainToImmutable();
                this._pool_nodeArrayBuilder.Push(frame.subnodes);
                node = new(openingTag, closingTag, subnodes);
                this._stack.Pop();
            }
            else
            {
                node = new(closingTag, null, []);
            }
        }
        else
        {
            if (closingTag == null) throw new UnreachableException();
            node = new(closingTag, null, []);
        }
        if (this._stack.Count == 0)
        {
            this.Result = node;
            return true;
        }
        else
        {
            this._stack.Peek().subnodes.Add(node);
            return false;
        }
    }

    bool rollup()
    {
        if (this._stack.Count == 0 && this._text.WrittenCount == 0)
        {
            return false;
        }
        if (pushTextNode()) return true;
        while (this._stack.Count != 0)
        {
            endElement(null);
        }
        return true;
    }
}
