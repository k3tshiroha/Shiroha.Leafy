using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Shiroha.Leafy;

/*
    ラウンドトリップテスト用。
    構文木を構築し、そのままバイトストリームに再シリアライズし標準出力に書き出す。
    差分を取る:
    $ dotnet run <file> | diff <file> -
    ※Powershllで実行してはいけない。
    BOMはテキストノードになるはず？
*/


string? path = null;
bool printSyntaxTree = false;

foreach (var arg in args)
{
    if (arg == "-s")
    {
        printSyntaxTree = true;
    }
    else if (arg.StartsWith('-'))
    {
        Console.Error.WriteLine($"Error: unrecognized option '{arg}'");
        return 1;
    }
    else
    {
        path = arg;
    }
}

if (path == null)
{
    Console.Error.WriteLine("Error: source file was unspecified.");
    return 1;
}

List<DocumentNode> document = new();
using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
{
    DocumentParser<HTMLDocumentParserSettings> parser = new(default);
    LineCounter lineCounter = new();
    byte[] buf = new byte[1024];
    int cnt = 0;
    while (true)
    {
        // ストリームを読み進める
        bool isFinal = false;
        if (cnt != buf.Length)
        {
            int c = stream.Read(buf.AsSpan(cnt));
            isFinal = c == 0;
            cnt += c;
        }
        var fragment = buf.AsSpan(0, cnt);

        // パース
        var status = parser.Parse(fragment, isFinal, out int bytesConsumed);
        lineCounter.AppendUtf8(fragment[..bytesConsumed]);

        // バッファを詰める
        fragment[bytesConsumed..].CopyTo(buf);
        cnt = fragment.Length - bytesConsumed;

        // エラー処理
        switch (status)
        {
            case DocumentParserStatus.Continue:
                if (isFinal) throw new UnreachableException();
                break;
            case DocumentParserStatus.FinishedSuccessfully:
                document.Add(parser.Result);
                break;
            case DocumentParserStatus.FinishedWithNoResult:
                if (!isFinal || fragment.Length != 0) throw new UnreachableException();
                break;
            case DocumentParserStatus.UnexpectedChar:
                Console.Error.WriteLine($"Error: unexpected char\n    at {path}:({lineCounter.Line}, {lineCounter.Column})");
                return 1;
            case DocumentParserStatus.Unterminated:
                Console.Error.WriteLine($"Error: unterminated\n    at {path}:({lineCounter.Line}, {lineCounter.Column})");
                break;
            default:
                throw new UnreachableException();
        }
        if (isFinal) break;
    }
}

if (printSyntaxTree)
{
    foreach (var node in document)
    {
        printSyntaxNode(node, 0);
    }
}
else
{
    var destStream = Console.OpenStandardOutput();
    var destWriter = new StreamBufferWriter(destStream);
    foreach (var node in document)
    {
        node.Serialize(destWriter);
    }
}
return 0;

void printIndent(int indent)
{
    Span<char> chars = stackalloc char[4 * indent];
    chars.Fill(' ');
    Console.Write(chars);
}

void printTag(DocumentElementTag? tag)
{
    if (tag == null) return;
    Console.Write('<');
    if (tag.IsClosingTag) Console.Write('/');
    Console.Write(tag.ElementType);
    if (!tag.Attributes.IsEmpty)
    {
        Console.Write(" $numAttributes$=\"");
        Console.Write(tag.Attributes.Length);
        Console.Write("\"");
    }
    if (tag.IsSelfClosing)
    {
        Console.Write(" /");
    }
    Console.Write('>');
}

void printSyntaxNode(DocumentNode node, int indent)
{
    printIndent(indent);
    Console.Write("- ");
    switch (node)
    {
        case DocumentElement element:
            Console.Write("Element ");
            printTag(element.OpeningTag);
            printTag(element.ClosingTag);
            Console.WriteLine();
            foreach (var subnode in element.SubNodes)
            {
                printSyntaxNode(subnode, indent + 1);
            }
            break;
        case DocumentText text:
            Console.Write("Text Length = ");
            Console.WriteLine(text.Text.Length);
            break;
        case DocumentComment comment:
            Console.Write("Comment Length = ");
            Console.WriteLine(comment.RawText.Length);
            break;
        default:
            Console.WriteLine(node);
            break;
    }
}

struct HTMLDocumentParserSettings : IDocumentParserSettings
{
    public readonly bool IsRawTextElement(ByteString elementType)
    {
        if (elementType.IsEmpty) return false;
        switch (elementType[0])
        {
            case (byte)'s':
                return
                    elementType.AsSpan().SequenceEqual("script"u8) ||
                    elementType.AsSpan().SequenceEqual("style"u8);
            case (byte)'t':
                return
                    elementType.AsSpan().SequenceEqual("title"u8) ||
                    elementType.AsSpan().SequenceEqual("textarea"u8);
            case (byte)'p':
                return
                    elementType.AsSpan().SequenceEqual("plaintext"u8);
            default:
                return false;
        }
    }

    public readonly bool IsVoidElement(ByteString elementType)
    {
        if (elementType.IsEmpty) return false;
        switch (elementType[0])
        {
            case (byte)'a':
                return
                    elementType.AsSpan().SequenceEqual("area"u8);
            case (byte)'b':
                return
                    elementType.AsSpan().SequenceEqual("base"u8) ||
                    elementType.AsSpan().SequenceEqual("br"u8);
            case (byte)'c':
                return
                    elementType.AsSpan().SequenceEqual("col"u8);
            case (byte)'e':
                return
                    elementType.AsSpan().SequenceEqual("embed"u8);
            case (byte)'h':
                return
                    elementType.AsSpan().SequenceEqual("hr"u8);
            case (byte)'i':
                return
                    elementType.AsSpan().SequenceEqual("img"u8) ||
                    elementType.AsSpan().SequenceEqual("input"u8);
            case (byte)'l':
                return
                    elementType.AsSpan().SequenceEqual("link"u8);
            case (byte)'m':
                return
                    elementType.AsSpan().SequenceEqual("meta"u8);
            case (byte)'p':
                return
                    elementType.AsSpan().SequenceEqual("param"u8);
            case (byte)'s':
                return
                    elementType.AsSpan().SequenceEqual("source"u8);
            case (byte)'t':
                return
                    elementType.AsSpan().SequenceEqual("track"u8);
            case (byte)'w':
                return
                    elementType.AsSpan().SequenceEqual("wbr"u8);
            default:
                return false;
        }
    }
}

struct LineCounter
{
    public int Line { get; private set; }
    public int Column { get; private set; }
    private bool _lastIsCR;

    public void AppendUtf8(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            int c = bytes.IndexOfAny((byte)'\n', (byte)'\r');
            if (c == 0)
            {
                switch (bytes[0])
                {
                    case (byte)'\r':
                        ++this.Line;
                        this._lastIsCR = true;
                        break;
                    case (byte)'\n':
                        if (!this._lastIsCR) ++this.Line;
                        this._lastIsCR = false;
                        break;
                    default:
                        throw new UnreachableException();
                }
                this.Column = 0;
                bytes = bytes[1..];
            }
            else
            {
                if (c < 0) c = bytes.Length;
                this.Column += c;
                this._lastIsCR = false;
                bytes = bytes[c..];
            }
        }
    }
}

sealed class StreamBufferWriter(Stream stream) : IBufferWriter<byte>
{
    private Stream _stream = stream;
    private byte[] _buf = new byte[256];

    public void Dispose()
    {
        this._stream.Dispose();
    }

    public void Advance(int count)
    {
        this._stream.Write(this._buf.AsSpan(0, count));
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        if (this._buf.Length < sizeHint) this._buf = new byte[sizeHint];
        return this._buf;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        throw new NotImplementedException();
    }
}
