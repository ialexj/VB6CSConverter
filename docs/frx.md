# VB6 FRX Binary File Format

## Overview

VB6 stores binary form property values — image data, serialised component state, and string arrays
(list items, item data) — in a sidecar `.frx` file that has the same base name as the `.frm` source
file.  The FRX file is a flat, sequential byte stream with no global header or directory.

## Parsing Requirements

**FRX files cannot be parsed in isolation.**  There is no structural signature that allows reliable
sequential enumeration.  Every item must be located using an offset obtained from the corresponding
FRM file.

### FRM syntax

A FRM property that has a binary value encodes the **FRX offset** as a bare hexadecimal literal
within its form designer code (not visible in the VB6 IDE):

```
Begin VB.ComboBox Combo1 
    ItemData        =   "Form1.frx":0000
    ...
    List            =   "Form1.frx":0002    
End
```

The FRM supplies the `startOffset` only.  The `byteLength` of each item must be derived by
sorting all referenced offsets in ascending order and computing the difference to the next offset.
The last item extends to end-of-file.

(The VB6 IDE writes the FRX in order of appearance in the FRM, but this shouldn't be assumed.)

```
offsets (sorted): 0x0000, 0x0046, 0x0056, 0x0093, …, EOF

byteLength[i] = offset[i+1] − offset[i]
byteLength[last] = fileSize − offset[last]
```

---

## Item Types

The best way to determine the item type is by looking at the component that produced it, 
as each component can produce a serialized object in whatever way it pleases. 

However, there are some generalizations and common patterns.

### BinaryBlob (length prefixed)

The first 4 bytes of the item are a little-endian **int32** `payloadLength`, which matches the
expected size of the item, minus 4 bytes (the size of the length field).

**Validation:** `payloadLength` must equal `byteLength − 4`.  If it does not, the item is not a
BinaryBlob.

```
Offset  Size  Field
──────  ────  ──────────────────────────────────────────────────────
+0      4     int32   payloadLength   == byteLength − 4
+4      …     byte[]  payload         see below
```

Payload offsets specified beloware calculated from the start of the payload data.

#### ImagePayload

The image payload type is detected by the presence of the magic marker `6C 74 00 00`.

```
Offset  Size  Field
──────  ────  ──────────────────────────────────────────────────────
+0      4     6C 74 00 00    magic bytes
+4      4     int32          imageLength
+4      …     byte[]         image data
```

The image payload MAY have a CLSID before the magic marker. For example, many Picture
properties will have:

`CLSID {0BE35204-8F91-11CE-9DE3-00AA004BB851} (OLE StdPicture)`

In this case, all the offsets are shifted to make room:

```
Offset  Size  Field
──────  ────  ──────────────────────────────────────────────────────
+0      16    guid (little-endian)   CLSID
+16     4     6C 74 00 00    magic bytes
+20     4     int32          imageLength
+24     …     byte[]         image data
```

`imageLength` is the exact byte count of the image data.  It must satisfy:

| CLSID present | Formula                           |
|:-------------:|-----------------------------------|
| No            | `imageLength == byteLength − 12`  |
| Yes           | `imageLength == byteLength − 28`  |

The image data can be further classified by its own magic bytes as they would appear in their file format.
A few common formats follow:

| Format | Magic bytes (hex) |
|--------|-------------------|
| BMP    | `42 4D`           |
| ICO    | `00 00 01 00`     |
| CUR    | `00 00 02 00`     |
| WMF    | `D7 CD C6 9A`     |
| EMF    | `01 00 00 00`     |
| GIF    | `47 49 46`        |
| JPEG   | `FF D8`           |

#### Other Payloads

There may be other payloads that follow this basic structure. The first 16 bytes of a payload 
*may* be a little-endian CLSID identifying the component that wrote the data (this has been
observed in practice), however without some other marker, or a lookup of the CLSID against the 
registry, this cannot be determined structurally.

For the purposes of a component-agnostic reader, an unknown payload should be preserved raw.

---

### String List

The first 2 bytes are a little-endian **int16** `count` giving the number of string items.
If there are any items, the next 2 bytes will contain the greatest length of any given item.
Then follow the strings, each prefixed by their length. 

```
Offset  Size  Field
──────  ────  ──────────────────────────────────────────────────────────────────────────
+0      2     int16   count           number of items; 0 for empty list
+2      2     int16   maxItemLength   max(byte length of any item); present iff count > 0
+4      …     items   [int16 len | CP1252 bytes] × count
```

`maxItemLength` equals the byte length of the longest string in the list.  It is a
pre-allocation hint written by the VB6 IDE; it carries no type information and should not be used
for item enumeration.

Each item is a 2-byte little-endian length followed by exactly that many bytes of CP1252-encoded
text.  There is no null terminator and no padding between items.

**Validation:** the entire structure must fit within `byteLength`.  Parsing must not read beyond
`startOffset + byteLength`.

### OleObjectBlob — `4C 42` ("LB") magic

Used by grid controls (e.g. VSFlexGrid) to serialise column definitions and display state.

```
Offset  Size  Field
──────  ────  ────────────────────────────────────────────────────────────────────
+0      2     4C 42       magic bytes "LB"
+2      2     0D 00       version = 13 (observed; may vary for other control versions)
+4      4     int32       contentSize = byteLength − 24
+8      16    varies      control-specific header (position/size fields; differ per instance)
+24     …     FE FF …     OLE Property Set stream (MS-OLEPS / IPropertyStorage)
                          starts with ByteOrder=0xFFFE, Version, SystemIdentifier, CLSID
```

> The `FE FF` byte order mark is the standard header for the OLE Property Set binary format
> (`IPropertyStorage` / MS-OLEPS). 

### Bindings — `C5/C6 FA 01 00` magic

Used by data-aware controls to record which ADO/DAO recordset each control is bound to.

```
Offset  Size  Field
──────  ────  ──────────────────────────────────────────────────────────────────
+0      4     C6 FA 01 00  magic (C6 or C5 in low byte; exact significance unknown)
+4      4     int32        flags / binding type  (observed values: 9 = 0x09, 16 = 0x10)
+8      1     byte         nameLen — byte length of the datasource name
+9      N     CP1252       datasource name (no null terminator)
+9+N    …     0x00…        null padding to the next blob boundary
```

### Other Item Types

There are other item types that don't follow any of the patterns described here, but of
which not enough examples have been found to determine a structure or a magic marker. 
For the purposes of an agnostic reader, they should be preserved as raw bytes.

---

## Disambiguation

When writing a component-agnostic parser, the following heuristic can be used:

Given a blob at a FRM-provided offset with a known `byteLength`:

1. Read 4 bytes at `startOffset` as int32 `candidate`.
2. If `candidate == byteLength − 4` → parse as **BinaryBlob**.
3. Otherwise → attempt **StringList**: validate that `count` is plausible and all items fit within
   `byteLength` without overflow.
4. Scan for known magic bytes. If any match, parse the special type (if relevant)

> **Edge case:** a zero-length BinaryBlob (`payloadLen == 0`, `byteLength == 4`) and an empty
> StringList (`count == 0`, `byteLength == 2`) are structurally distinct only by `byteLength`.
> These could be both represented as a null object.

---

## Observed Examples

### Icon — BinaryBlob / ImagePayload, no CLSID

```
C6 0E 00 00   payloadLen = 3782  (byteLength = 3786 → 3786 − 4 = 3782 ✓)
6C 74 00 00   magic
BE 0E 00 00   imageLen  = 3774  (3786 − 12 = 3774 ✓)
00 00 01 00   ← ICO header: type=1 (icon), count=1
01 00 30 30   ← ICONDIRENTRY: width=48, height=48 …
…
```

### Picture — BinaryBlob / ImagePayload, with CLSID

```
XX XX XX XX   payloadLen  (byteLength − 4 ✓)
04 52 E3 0B
91 8F CE 11   CLSID {0BE35204-8F91-11CE-9DE3-00AA004BB851} (OLE StdPicture)
9D E3 00 AA
00 4B B8 51
6C 74 00 00   magic
XX XX XX XX   imageLen  (byteLength − 28 ✓)
…             image payload
```

### Opaque component blob — BinaryBlob / OpaquePayload

(Sheridan SSTab / vaTabPro, tab-caption data):

```
XX XX XX XX   payloadLen
40 75 F0 F6
EC 42 CE 11   CLSID {F6F07540-42EC-11CE-8135-00AA004BB851} (Sheridan vaTabPro / SSTab)
81 35 00 AA
00 4B B8 51
…             opaque serialised component state (no magic marker)
```

### StringList

ListBox / ComboBox `ItemData` property, 3 items:

```
03 00         count = 3
03 00         maxItemLength = 3
01 00  31                    "1"
02 00  32 32                 "22"
03 00  33 33 33              "333"
```

ListBox / ComboBox `List` property, 3 items with varying length:

```
03 00         count = 3
08 00         maxItemLength = 8  (length of longest item "ZZZZZZZZ")
04 00  58 58 58 58              "XXXX"
06 00  59 59 59 59 59 59        "YYYYYY"
08 00  5A 5A 5A 5A 5A 5A 5A 5A  "ZZZZZZZZ"
```

### OleObjectBlob — grid control serialised state

(VSFlexGrid `OleObjectBlob` property, byteLength = 16140):

```
4C 42 0D 00   magic "LB", version = 13
F4 3E 00 00   contentSize = 16116  (16140 − 24 = 16116 ✓)
65 9C FF FF
3F B3 FF FF   16 bytes of control-specific position/size fields
8C 28 00 00
59 10 00 00
FE FF 00 00   ← OLE Property Set header (MS-OLEPS): ByteOrder = 0xFFFE
06 02 02 00   version + system identifier
49 8C 02 00
00 00 00 00
00 00 00 00   CLSID of the property-set schema (16 bytes)
00 00 00 46
…             property set sections
```

### Bindings — data-source binding descriptor

`frmPosMain.frx`, offset `0x18E4` (data-aware control `Bindings` property, byteLength = 21):

```
C6 FA 01 00   magic
09 00 00 00   flags / binding type
0B            nameLen = 11
64 61 74 44 6F 63   "datDoc" … (11 bytes total, CP1252, no terminator)
00 00 00 00 00 00   null padding to next blob boundary
```
