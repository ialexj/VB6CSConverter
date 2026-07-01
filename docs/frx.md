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

> **Pitfall — do not read "to EOF" for every item.**  Naively dumping from `startOffset` to the
> end of the file (instead of to the *next sorted offset*) will silently swallow every subsequent
> property's data too.  Adjacent items are frequently empty `StringList`s (`00 00`, byteLength 2)
> for design-time-empty `ComboBox`/`ListBox` controls, so a wrongly-bounded dump looks like a run
> of "padding" zero bytes followed by unrelated (but individually valid) structures — e.g. a
> `List` blob immediately followed by a completely unrelated grid control's `OleObjectBlob`. Always
> bound reads with the next sorted offset, never EOF, unless the item genuinely is the last one.

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

> **Confirmed stale in practice:** for a `ComboBox`'s `ItemData` property (which stores the
> per-item `Long` value as its decimal string, e.g. `"0"`), `maxItemLength` has been observed to
> read `3` in multiple instances within the same form even though every current item was only
> 1 byte long (`"0"`). The field is written once and not necessarily recomputed when items are
> edited down, so it must never be trusted as authoritative — always derive item boundaries by
> walking the length-prefixed items themselves.

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

### Bindings — `C5/C6 FA 01/02/… 00` magic

Used by data-aware controls to record which ADO/DAO recordset (and optionally which field) each
control is bound to. The 3rd magic byte is an entry **count** `N` — the item is `N` consecutive
`(flags, name)` tuples followed by a fixed 6-byte zero trailer. `N == 1` is by far the most common
case (a single DataSource binding); `N == 2` has been observed for controls that also persist a
bound field name (e.g. a grid column's `DataField` alongside its `DataSource`).

```
Offset      Size  Field
──────────  ────  ──────────────────────────────────────────────────────────────────
+0          4     C6 FA 0N 00  magic (C6 or C5 in low byte; N = entry count, usually 1)
+4          …     entry × N    see below
+4+Σentries 6     0x00 × 6     fixed reserved trailer, always zero (not variable padding)

each entry:
+0      4     int32   flags / binding type  (observed: 3, 9 = 0x09, 16 = 0x10)
+4      1     byte    nameLen — byte length of the name
+5      N     CP1252  name (no null terminator)
```

**Validation:** `byteLength == 4 + 6 + N×5 + Σ(nameLen)`. For the common `N == 1` case this
simplifies to `byteLength == 15 + nameLen`. Confirmed across 182 real-world instances scanned
from a single project's exported `.frx` blobs (180 with `N == 1`, 2 with `N == 2`) — every one
satisfied the formula with zero mismatches. Observed flag values across that scan: `3` (1
instance), `9` (101 instances), `16` (80 instances); the trailing 6 zero bytes were present and
zero in all 182 instances.

### ClsidStream — bare CLSID header, no length-prefix wrapper

Used by some third-party ActiveX controls to persist a collection-valued property (e.g. a tab
control's per-tab caption/icon data, or a Coolbar-style control's `Bands` collection). Unlike the
CLSID-prefixed BinaryBlob payload described above, this item has **no leading 4-byte
`payloadLength` field** — the CLSID sits directly at `startOffset`, and the size field comes
*after* it instead of before it.

```
Offset  Size  Field
──────  ────  ──────────────────────────────────────────────────────────────────
+0      16    guid (little-endian)   CLSID of the component/collection type
+16     4     int32                  contentSize == byteLength − 20
+20     …     byte[]                 component-specific content (may itself embed
                                      further structures, e.g. raw BMP images)
```

**Validation:** `contentSize == byteLength − 20` exactly. Confirmed across 34 real-world
instances (sizes ranging from 380 bytes to 121,444 bytes) with zero mismatches — including the
largest instance, whose content embeds multiple raw BMP images (`42 4D` / "BM" magic) back to
back, one per collection element.

The same CLSID (`{F6F07540-42EC-11CE-8135-00AA004BB851}`) was observed for two structurally
different properties (a tab control's `TabCaption` and an unrelated control's `Bands` property),
suggesting this is a generic collection-persistence stream shared by multiple third-party
controls rather than a format tied to one specific `ProgID`. The CLSID was not resolvable via the
local COM registry (control not installed on the analysis machine), so its owning component could
not be identified by name.

### RTF text — `{\rtf1` magic

Used by `RichTextBox`-style controls to persist a formatted-text (`TextRTF`) property. The item
is simply the literal RTF document (per the public RTF spec — 7-bit-clean, so CP1252/ASCII
decoding is safe) with **no length prefix and no wrapper** — `byteLength` is the exact size of the
RTF document, which is self-delimiting via its own balanced `{ … }` braces.

```
Offset  Size          Field
──────  ────────────  ──────────────────────────────────────────────────────
+0      byteLength    CP1252/ASCII text, starting with the literal `{\rtf1`
```

Confirmed across 10 real-world instances (byteLength 121–135), all well-formed RTF documents
ending in a balanced `\par }`.

### Other Item Types

There are other item types that don't follow any of the patterns described here, but of
which not enough examples have been found to determine a structure or a magic marker. 
For the purposes of an agnostic reader, they should be preserved as raw bytes.

---

## Disambiguation

When writing a component-agnostic parser, the following heuristic can be used:

Given a blob at a FRM-provided offset with a known `byteLength`:

1. Scan for known magic bytes at `startOffset` (`4C 42` → OleObjectBlob, `C5/C6 FA` → Bindings,
   a known CLSID → ClsidStream, `{\rtf1` → RTF text). If any match, parse the special type.
2. Otherwise, read 4 bytes at `startOffset` as int32 `candidate`.
3. If `candidate == byteLength − 4` → parse as **BinaryBlob**.
4. Otherwise → attempt **StringList**: validate that `count` is plausible and all items fit within
   `byteLength` without overflow.
5. Otherwise → preserve as raw bytes (unknown item type).

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

### Picture — BinaryBlob / ImagePayload, no CLSID, BMP

(`frmPosMain.frx`, offset `0x14A2`, `CommandButton.Picture`, byteLength = 1090):

```
3E 04 00 00   payloadLen = 1086  (1090 − 4 = 1086 ✓)
6C 74 00 00   magic
36 04 00 00   imageLen  = 1078  (1090 − 12 = 1078 ✓)
42 4D 36 04 00 00   ← BMP header "BM", file size 0x436 = 1078
…
```

### ClsidStream — `TabproLib.vaTabPro` tab-caption data, no length-prefix wrapper

(`frmEncomendasInserir.frx`, offset `0x0019`, `tabEncomendasInserir.TabCaption`, byteLength = 380):

```
40 75 F0 F6
EC 42 CE 11   CLSID {F6F07540-42EC-11CE-8135-00AA004BB851}   ← no leading length prefix
81 35 00 AA
00 4B B8 51
68 01 00 00   contentSize = 360  (380 − 20 = 360 ✓)
CA 00 FF FF   control-specific header
A0 5B 02 00   ← trailing int16 = 2 (plausibly a tab/item count)
…             opaque per-tab caption data
```

### ClsidStream — Coolbar-style `Bands` collection with embedded BMPs

(`mdiOptiware98.frx`, offset `0xE2E70`, `<coolbar>.Bands`, byteLength = 121444 — the *same* CLSID
as the example above, on an entirely different property, supporting the theory that this is a
shared generic collection-persistence format rather than one tied to a specific control):

```
40 75 F0 F6
EC 42 CE 11   CLSID {F6F07540-42EC-11CE-8135-00AA004BB851}
81 35 00 AA
00 4B B8 51
50 DA 01 00   contentSize = 121424  (121444 − 20 = 121424 ✓)
FF FF FF FF   control-specific header (differs from the TabCaption example above)
00 00 00 00
B7 B0 00 00
…
42 4D 36 30 00 00   ← embedded "BM" BMP header for the first band's image, further in
…                      (additional BMPs follow back-to-back for subsequent bands)
```

### RTF text — `RichTextBox`-style `TextRTF` property

(byteLength = 121, multiple independent instances across different forms — always ends in a
balanced `\par }`):

```
7B 5C 72 74 66 31 5C 61 6E 73 69 5C 61 6E 73 69   "{\rtf1\ansi\ansi…"
…                                                  full RTF document, no wrapper/prefix
64 5C 66 30 5C 66 73 31 37 20 0A 5C 70 61 72 20 7D   "…d\f0\fs17\n\par }"
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

Real-world `ItemData` example (`frmPosMain.frx`, offset `0x134E`, `cboVenda(1).ItemData`,
byteLength = 13) — every item's `Long` value is the unset default of `0`, and `maxItemLength`
is stale at `3` even though every current item is 1 byte:

```
03 00         count = 3          (matches the paired List property's item count)
03 00         maxItemLength = 3  (STALE — every item below is only 1 byte long)
01 00  30                    "0"
01 00  30                    "0"
01 00  30                    "0"
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
06            nameLen = 6
64 61 74 44 6F 63   "datDoc" (6 bytes, CP1252, no terminator)
00 00 00 00 00 00   fixed 6-byte reserved trailer (always zero)
```

Three more instances from the same file (`dbcVenda` DBCombo controls), all satisfying
`byteLength == 15 + nameLen`:

```
offset 0x1305, byteLength 22:  C6 FA 01 00  10 00 00 00  07 "datUser"      00×6
offset 0x131B, byteLength 25:  C6 FA 01 00  10 00 00 00  0A "datArmazem"   00×6
offset 0x1334, byteLength 26:  C6 FA 01 00  10 00 00 00  0B "datClientes"  00×6
```

A fourth independent instance from a different file/form (`frmPOSDocumento.frx`, offset `0x980D`,
`dbcCliente.Bindings`, byteLength 26 — confirmed via the next FRM-referenced offset `0x9827`,
`0x9827 − 0x980D == 15 + 11`):

```
offset 0x980D, byteLength 26:  C6 FA 01 00  10 00 00 00  0B "datClientes"  00×6
```

### Bindings — two-entry (`N == 2`) variant

Two independent instances (`frmclientesmain.frx`, offsets `0x15B69` and `0xE2F7`, byteLength 38
each), showing a `DataField`-like name (`"Defs"`) paired with a `DataSource`-like name
(`"datClientes(7)"`, referencing element 7 of a control array) inside a single item:

```
C6 FA 02 00        magic, N = 2 entries
02 00 00 00        entry 1: flags = 2
04                 entry 1: nameLen = 4
44 65 66 73        entry 1: "Defs"
10 00 00 00        entry 2: flags = 16       (same flag value seen in single-entry Bindings)
0E                 entry 2: nameLen = 14
64 61 74 43 6C 69 65 6E 74 65 73 28 37 29   entry 2: "datClientes(7)"
00 00 00 00 00 00  fixed 6-byte reserved trailer
```

`byteLength == 4 + 6 + 2×5 + (4 + 14) == 38` ✓ — matches the generalised formula.

### StringList + adjacent OleObjectBlob — `frmPOSDocumento.frx`

A `ComboBox` pair (`lstField` / `cboCopias`) illustrating an empty `StringList`, a stale
`ItemData` `maxItemLength`, a populated `List`, and an unrelated grid blob sitting immediately
afterward — all four are separate, correctly-bounded items once offsets are sorted properly:

```
offset 0x26BC, byteLength 2   lstField.List        00 00                        (empty, count=0)
offset 0x26BE, byteLength 16  cboCopias.ItemData    count=4  maxItemLength=3 (STALE)
                                                     "0" "0" "0" "0"
offset 0x26CE, byteLength 52  cboCopias.List        count=4  maxItemLength=13
                                                     "Original" "Duplicado" "Triplicado" "Quadriplicado"
offset 0x2702, byteLength 6777  tdbgTotais.OleObjectBlob   "LB" v13, contentSize=6753
```
