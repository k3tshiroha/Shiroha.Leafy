using System;
using System.Buffers;
using System.Diagnostics;

namespace Shiroha.Leafy;

public sealed class DocumentTextNode : DocumentNode
{
    public ByteString Text { get; }

    public DocumentTextNode(ReadOnlySpan<byte> bytes, bool doEscape)
    {
        if (bytes.Length > 1 << 20) throw new ArgumentException("The input was too long.", nameof(bytes));

        byte[]? ar = null;
        int cnt = 0;

        if (doEscape)
        {
            var span = bytes;
            while (!span.IsEmpty)
            {
                int c = span.IndexOfAny((byte)'&', (byte)'<');
                int r; // count of bytes required to write escaped substring
                ReadOnlySpan<byte> escapedChar;
                if (c < 0)
                {
                    if (ar == null) break;
                    r = c = span.Length;
                    escapedChar = default;
                }
                else
                {
                    r = c;
                    switch (span[c])
                    {
                        case (byte)'&':
                            r += 5;
                            escapedChar = "&amp;"u8;
                            break;
                        case (byte)'<':
                            r += 4;
                            escapedChar = "&lt;"u8;
                            break;
                        default:
                            throw new UnreachableException();
                    }
                }
                if (ar == null)
                {
                    // create buffer
                    ar = new byte[bytes.Length + 30];
                }
                else if (ar.Length - cnt < r)
                {
                    // expand buffer
                    var oldar = ar;
                    ar = new byte[r + (oldar.Length << 1) - bytes.Length]; // a + 2 * b = 2 * (a + b) - a
                    oldar.AsSpan().CopyTo(ar);
                }
                // write raw substring
                span[..c].CopyTo(ar.AsSpan(cnt));
                span = span[c..];
                cnt += c;
                if (escapedChar.IsEmpty) break; // end of text
                // write escaped char
                escapedChar.CopyTo(ar.AsSpan(cnt));
                span = span[1..];
                ++cnt;
            }
        }

        if (ar == null)
        {
            this.Text = new(bytes);
        }
        else
        {
            this.Text = new(ar.AsSpan(0, cnt));
        }
    }

    public override void Serialize<TWriter>(TWriter dest)
    {
        dest.Write(this.Text);
    }
}
