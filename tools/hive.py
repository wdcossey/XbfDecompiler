"""Prints the keys and values of a registry hive file (regf) whose path matches a regex, and everything under them.
Read-only; needs no admin rights and no `reg load`. Handles REG_SZ, REG_EXPAND_SZ, REG_MULTI_SZ and REG_DWORD; other
types print as hex.

Usage: python hive.py <hive file> <regex on the key path>
Example: python hive.py flash-update1/config/SOFTWARE "Control Panel.Theme"   (the 20 accents, both themes)"""
import re, struct, sys

data = open(sys.argv[1], "rb").read()
pattern = re.compile(sys.argv[2], re.I)
BASE = 0x1000

def cell(off):
    return BASE + off + 4

def nk(off):
    p = cell(off)
    assert data[p:p+2] == b"nk", hex(off)
    flags = struct.unpack_from("<H", data, p + 2)[0]
    nsub = struct.unpack_from("<I", data, p + 0x14)[0]
    subs = struct.unpack_from("<I", data, p + 0x1C)[0]
    nval = struct.unpack_from("<I", data, p + 0x24)[0]
    vals = struct.unpack_from("<I", data, p + 0x28)[0]
    nlen = struct.unpack_from("<H", data, p + 0x48)[0]
    raw = data[p + 0x4C:p + 0x4C + nlen]
    name = raw.decode("latin-1") if flags & 0x20 else raw.decode("utf-16-le")
    return name, nsub, subs, nval, vals

def subkeys(off):
    p = cell(off)
    sig = data[p:p+2]
    n = struct.unpack_from("<H", data, p + 2)[0]
    if sig in (b"lf", b"lh"):
        for i in range(n):
            yield struct.unpack_from("<I", data, p + 4 + i * 8)[0]
    elif sig == b"li":
        for i in range(n):
            yield struct.unpack_from("<I", data, p + 4 + i * 4)[0]
    elif sig == b"ri":
        for i in range(n):
            yield from subkeys(struct.unpack_from("<I", data, p + 4 + i * 4)[0])

def values(nval, vals):
    if nval == 0 or vals == 0xFFFFFFFF:
        return
    p = cell(vals)
    for i in range(nval):
        v = cell(struct.unpack_from("<I", data, p + i * 4)[0])
        nlen, dlen, doff, vtype, flags = struct.unpack_from("<HIIIH", data, v + 2)
        raw = data[v + 0x14:v + 0x14 + nlen]
        name = (raw.decode("latin-1") if flags & 1 else raw.decode("utf-16-le")) or "(default)"
        size = dlen & 0x7FFFFFFF
        if dlen & 0x80000000:
            blob = struct.pack("<I", doff)[:size]
        else:
            blob = data[cell(doff):cell(doff) + min(size, 4096)]
        if vtype in (1, 2):
            val = blob.decode("utf-16-le", "replace").rstrip("\0")
        elif vtype == 4:
            d = struct.unpack("<I", blob[:4])[0]
            val = f"0x{d:08X}"
        elif vtype == 7:
            val = blob.decode("utf-16-le", "replace").rstrip("\0").split("\0")
        else:
            val = blob[:64].hex()
        yield name, vtype, val

root = struct.unpack_from("<I", data, 0x24)[0]

def walk(off, path, printing, depth):
    name, nsub, subs, nval, vals = nk(off)
    path = f"{path}\\{name}" if path else name
    hit = printing or bool(pattern.search(path))
    if hit:
        print(f"[{path}]")
        for vn, vt, vv in values(nval, vals):
            print(f"  {vn} = {vv}")
    if nsub and subs != 0xFFFFFFFF and depth < 40:
        for s in subkeys(subs):
            walk(s, path, hit, depth + 1)

walk(root, "", False, 0)
