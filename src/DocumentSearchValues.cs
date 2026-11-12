using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Shiroha.Leafy;

internal static class DocumentSearchValues
{
    public static SearchValues<byte> WhiteSpaces { get; }
    public static SearchValues<byte> ControllChars { get; }
    public static SearchValues<byte> AllowedInHeadOfElementType { get; }
    public static SearchValues<byte> AllowedInElementType { get; }
    public static SearchValues<byte> DisallowedInAttributeName { get; }
    public static SearchValues<byte> DisallowedInAttributeValueSinglequoted { get; }
    public static SearchValues<byte> DisallowedInAttributeValueDoublequoted { get; }
    public static SearchValues<byte> DisallowedInAttributeValueUnquoted { get; }

    private ref struct SpanList(Span<byte> span)
    {
        private Span<byte> _span = span;
        private int _cnt = 0;

        public readonly ReadOnlySpan<byte> Span => this._span[..this._cnt];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AppendAll(ReadOnlySpan<byte> values)
        {
            values.CopyTo(this._span[this._cnt..]);
            this._cnt += values.Length;
        }
    }

    static DocumentSearchValues()
    {
        ReadOnlySpan<byte> whitespaces = " \n\r\t\v\f"u8;
        ReadOnlySpan<byte> controlChars = [
            0, 1, 2, 3, 4, 5, 6, 7, 8, 14, 15, 16, 17, 18, 19, 20,
            21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 127
            ];
        Span<byte> whiteSpacesAndControlChars = stackalloc byte[33];
        for (int i = 0; i < 32; ++i) whiteSpacesAndControlChars[i] = (byte)i;
        whiteSpacesAndControlChars[32] = 127;

        ReadOnlySpan<byte> alphabetsAndNumbers = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"u8;
        Span<byte> outOfASCII = stackalloc byte[128];
        for (int i = 0; i < 128; ++i) outOfASCII[i] = (byte)(128 + i);

        WhiteSpaces = SearchValues.Create(whitespaces);
        ControllChars = SearchValues.Create(controlChars);

        SpanList headOfElementType = new(stackalloc byte[256]);
        SpanList elementType = new(stackalloc byte[256]);
        headOfElementType.AppendAll(alphabetsAndNumbers);
        headOfElementType.AppendAll(outOfASCII);
        elementType.AppendAll(alphabetsAndNumbers);
        elementType.AppendAll(outOfASCII);
        headOfElementType.AppendAll("_"u8);
        elementType.AppendAll("+-.:_"u8);
        AllowedInHeadOfElementType = SearchValues.Create(headOfElementType.Span);
        AllowedInElementType = SearchValues.Create(elementType.Span);

        SpanList attName = new(stackalloc byte[40]);
        SpanList attValueS = new(stackalloc byte[30]);
        SpanList attValueD = new(stackalloc byte[30]);
        SpanList attValueU = new(stackalloc byte[40]);
        attName.AppendAll(whiteSpacesAndControlChars);
        attName.AppendAll([.. "<=>\"\'"u8, 127]);
        attValueS.AppendAll(controlChars);
        attValueS.AppendAll("\'"u8);
        attValueD.AppendAll(controlChars);
        attValueD.AppendAll("\""u8);
        attValueU.AppendAll(whiteSpacesAndControlChars);
        attValueU.AppendAll("<>"u8);
        DisallowedInAttributeName = SearchValues.Create(attName.Span);
        DisallowedInAttributeValueSinglequoted = SearchValues.Create(attValueS.Span);
        DisallowedInAttributeValueDoublequoted = SearchValues.Create(attValueD.Span);
        DisallowedInAttributeValueUnquoted = SearchValues.Create(attValueU.Span);
    }
}
