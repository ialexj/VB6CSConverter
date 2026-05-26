using System;
using System.Collections.Generic;
using System.IO;

namespace VB6Parser;

/// <summary>Identifies the type of data stored in an FRX blob.</summary>
public enum FrxFormat
{
    Unknown,
    Bmp,
    Gif,
    Jpeg,
    Wmf,
    Emf,
    Ico,
    Cur,
    StringBlob,
    OleObject,
}

/// <summary>A single resource blob extracted from a VB6 .frx file.</summary>
public sealed record FrxBlob(int Offset, FrxFormat Format, byte[] Data, string[]? Strings = null);

/// <summary>
/// Reads binary resource blobs from VB6 .frx sidecar files.
/// The FRX format is a flat, sequential stream of size-prefixed blobs with no global header.
/// </summary>
public static class FrxReader
{
    // Magic key present in VB5/VB6 image blob headers (bytes: 6C 74 00 00)
    private const uint FrxKey = 0x0000746C;

    /// <summary>
    /// Reads a single blob from <paramref name="frxData"/> at the byte offset encoded in
    /// <paramref name="hexOffset"/> (e.g. <c>"0000"</c>, <c>"1A40"</c>).
    /// Returns <see langword="null"/> if the offset is out of range or the data is malformed.
    /// </summary>
    public static FrxBlob? Read(byte[] frxData, string hexOffset)
    {
        if (!TryParseOffset(hexOffset, out int offset))
            return null;

        return TryReadAt(frxData, offset);
    }

    /// <summary>
    /// Walks <paramref name="frxData"/> sequentially from offset 0, yielding every blob
    /// in the order they appear. Stops at EOF or on the first malformed blob.
    /// </summary>
    public static IEnumerable<FrxBlob> ReadAll(byte[] frxData)
    {
        int pos = 0;
        while (pos < frxData.Length) {
            var blob = TryReadAt(frxData, pos);
            if (blob is null)
                yield break;

            yield return blob;

            // Advance past this blob
            pos = NextOffset(frxData, pos);
            if (pos < 0)
                yield break;
        }
    }

    // ── internal helpers ──────────────────────────────────────────────────

    private static FrxBlob? TryReadAt(byte[] data, int offset)
    {
        if (offset < 0 || offset + 4 > data.Length)
            return null;

        // Try Variant A / B: check key at offset+4
        if (offset + 8 <= data.Length) {
            uint key = ReadUInt32(data, offset + 4);
            if (key == FrxKey && offset + 12 <= data.Length) {
                uint dwSizeImageEx = ReadUInt32(data, offset + 0);
                uint dwSizeImage   = ReadUInt32(data, offset + 8);

                int headerLen;
                if (dwSizeImageEx == dwSizeImage + 8) {
                    headerLen = 12;       // Variant A
                }
                else if (offset + 28 <= data.Length) {
                    // Variant B: image size is at offset+24, not offset+8
                    uint dwSizeImage24 = ReadUInt32(data, offset + 24);
                    if (dwSizeImageEx == dwSizeImage24 + 24) {
                        dwSizeImage = dwSizeImage24;
                        headerLen = 28;   // Variant B
                    }
                    else
                        goto variantC;
                }
                else
                    goto variantC;

                int dataStart = offset + headerLen;
                int dataLen   = (int)dwSizeImage;
                if (dataLen < 0 || (long)dataStart + dataLen > data.Length)
                    return null;

                var imageBytes = new byte[dataLen];
                Buffer.BlockCopy(data, dataStart, imageBytes, 0, dataLen);
                return new FrxBlob(offset, DetectImageFormat(imageBytes), imageBytes);
            }
        }

        variantC:
        {
            // Variant C: plain DWORD length
            uint dataSize = ReadUInt32(data, offset);
            if (dataSize == 0)
                return new FrxBlob(offset, FrxFormat.StringBlob, [], []);

            int dataStart = offset + 4;
            int dataLen   = (int)dataSize;
            if (dataLen < 0 || (long)dataStart + dataLen > data.Length)
                return null;

            var blob = new byte[dataLen];
            Buffer.BlockCopy(data, dataStart, blob, 0, dataLen);

            var imageFormat = DetectImageFormat(blob);
            if (imageFormat != FrxFormat.Unknown)
                return new FrxBlob(offset, imageFormat, blob);

            // No image magic — treat as string blob
            var strings = SplitNullTerminated(blob);
            return new FrxBlob(offset, FrxFormat.StringBlob, blob, strings);
        }
    }

    /// <summary>Computes the offset immediately after the blob at <paramref name="offset"/>.</summary>
    private static int NextOffset(byte[] data, int offset)
    {
        if (offset + 8 > data.Length)
            return -1;

        uint key = ReadUInt32(data, offset + 4);
        if (key == FrxKey && offset + 12 <= data.Length) {
            uint dwSizeImageEx = ReadUInt32(data, offset + 0);
            uint dwSizeImage   = ReadUInt32(data, offset + 8);

            if (dwSizeImageEx == dwSizeImage + 8)
                return offset + 12 + (int)dwSizeImage;

            if (offset + 28 <= data.Length) {
                uint dwSizeImage24 = ReadUInt32(data, offset + 24);
                if (dwSizeImageEx == dwSizeImage24 + 24)
                    return offset + 28 + (int)dwSizeImage24;
            }
        }

        // Variant C
        uint varCSize = ReadUInt32(data, offset);
        return offset + 4 + (int)varCSize;
    }

    private static FrxFormat DetectImageFormat(byte[] blob)
    {
        if (blob.Length < 2) return FrxFormat.Unknown;

        // BMP: "BM"
        if (blob[0] == 0x42 && blob[1] == 0x4D) return FrxFormat.Bmp;

        // GIF: "GIF"
        if (blob.Length >= 3 && blob[0] == 0x47 && blob[1] == 0x49 && blob[2] == 0x46) return FrxFormat.Gif;

        // JPEG: SOI marker FF D8
        if (blob[0] == 0xFF && blob[1] == 0xD8) return FrxFormat.Jpeg;

        // ICO: 00 00 01 00
        if (blob.Length >= 4 && blob[0] == 0x00 && blob[1] == 0x00 && blob[2] == 0x01 && blob[3] == 0x00) return FrxFormat.Ico;

        // CUR: 00 00 02 00
        if (blob.Length >= 4 && blob[0] == 0x00 && blob[1] == 0x00 && blob[2] == 0x02 && blob[3] == 0x00) return FrxFormat.Cur;

        // Aldus Placeable WMF: D7 CD C6 9A
        if (blob.Length >= 4 && blob[0] == 0xD7 && blob[1] == 0xCD && blob[2] == 0xC6 && blob[3] == 0x9A) return FrxFormat.Wmf;

        // EMF: signature "EMF" at offset 40 (0x28) — bytes 20 45 4D 46
        if (blob.Length >= 44 && blob[40] == 0x20 && blob[41] == 0x45 && blob[42] == 0x4D && blob[43] == 0x46) return FrxFormat.Emf;

        return FrxFormat.Unknown;
    }

    private static string[] SplitNullTerminated(byte[] data)
    {
        var list = new List<string>();
        int start = 0;
        for (int i = 0; i <= data.Length; i++) {
            if (i == data.Length || data[i] == 0x00) {
                if (i > start) {
                    list.Add(System.Text.Encoding.Default.GetString(data, start, i - start));
                }
                start = i + 1;
            }
        }
        return list.ToArray();
    }

    private static bool TryParseOffset(string hexOffset, out int offset)
    {
        try {
            offset = Convert.ToInt32(hexOffset.TrimStart(':'), 16);
            return true;
        }
        catch {
            offset = 0;
            return false;
        }
    }

    private static uint ReadUInt32(byte[] data, int offset)
        => BitConverter.ToUInt32(data, offset);

    /// <summary>Returns the file extension (without leading dot) for a given format.</summary>
    public static string GetExtension(FrxFormat format) => format switch {
        FrxFormat.Bmp       => "bmp",
        FrxFormat.Gif       => "gif",
        FrxFormat.Jpeg      => "jpg",
        FrxFormat.Wmf       => "wmf",
        FrxFormat.Emf       => "emf",
        FrxFormat.Ico       => "ico",
        FrxFormat.Cur       => "cur",
        FrxFormat.StringBlob => "strings",
        _                   => "bin",
    };
}
