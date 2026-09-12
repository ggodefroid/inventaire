"""Lit le nom d'origine d'un assembly .NET dans sa table Module.

Le MSI stocke les fichiers sous des noms mutiles (F_MI09D7.1.DLL...), mais
chaque assembly porte son vrai nom dans ses metadonnees. La table Module est la
premiere du flux de tables, donc son offset se calcule sans avoir a connaitre
le schema complet des metadonnees.
"""
import struct
import sys


def module_name(path):
    d = open(path, "rb").read()
    if d[:2] != b"MZ":
        return None
    pe = struct.unpack_from("<I", d, 0x3C)[0]
    if d[pe:pe + 4] != b"PE\0\0":
        return None
    nsec = struct.unpack_from("<H", d, pe + 6)[0]
    opt_size = struct.unpack_from("<H", d, pe + 20)[0]
    opt = pe + 24
    magic = struct.unpack_from("<H", d, opt)[0]
    # Repertoire de donnees 14 = en-tete CLI.
    cli_rva, _ = struct.unpack_from("<II", d, opt + (208 if magic == 0x10B else 224))
    if not cli_rva:
        return None

    sec = opt + opt_size

    def rva_to_off(rva):
        for i in range(nsec):
            b = sec + i * 40
            # Table des sections : VirtualSize@8, VirtualAddress@12,
            # SizeOfRawData@16, PointerToRawData@20.
            vsz, va, rsz, praw = struct.unpack_from("<IIII", d, b + 8)
            if va <= rva < va + max(vsz, rsz):
                return praw + (rva - va)
        return None

    cli = rva_to_off(cli_rva)
    meta_rva, _ = struct.unpack_from("<II", d, cli + 8)
    root = rva_to_off(meta_rva)
    if d[root:root + 4] != b"BSJB":
        return None

    # Racine des metadonnees : BSJB(4) Major(2) Minor(2) Reserved(4)
    # Length(4) Version(Length) Flags(2) Streams(2) puis les en-tetes de flux.
    ver_len = struct.unpack_from("<I", d, root + 12)[0]
    ver_len = (ver_len + 3) & ~3
    p = root + 16 + ver_len + 2          # + Flags (2 octets)
    nstreams = struct.unpack_from("<H", d, p)[0]
    p += 2
    streams = {}
    for _ in range(nstreams):
        off, size = struct.unpack_from("<II", d, p)
        p += 8
        end = d.index(b"\0", p)
        name = d[p:end].decode("ascii")
        p += (end - p + 4) & ~3          # nom aligne sur 4 octets
        streams[name] = (root + off, size)

    tbl = streams.get("#~") or streams.get("#-")
    strings = streams.get("#Strings")
    if not tbl or not strings:
        return None
    tbl_off = tbl[0]

    heap_sizes = d[tbl_off + 6]
    str_idx = 4 if heap_sizes & 0x01 else 2
    guid_idx = 4 if heap_sizes & 0x02 else 2
    valid = struct.unpack_from("<Q", d, tbl_off + 8)[0]
    if not valid & 1:                    # pas de table Module : anormal
        return None
    nrows = bin(valid).count("1")
    rows_off = tbl_off + 24
    first_row = rows_off + 4 * nrows

    # Module : Generation(2) puis Name (index de chaine)
    name_idx = int.from_bytes(d[first_row + 2:first_row + 2 + str_idx], "little")
    base = strings[0] + name_idx
    return d[base:d.index(b"\0", base)].decode("utf-8", "replace")


if __name__ == "__main__":
    for path in sys.argv[1:]:
        n = module_name(path)
        if n:
            print(f"{n}\t{path}")
