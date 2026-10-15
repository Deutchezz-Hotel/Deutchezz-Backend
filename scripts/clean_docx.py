#!/usr/bin/env python3
"""Remove media parts that are not referenced by document.xml (template leftovers)."""
from __future__ import annotations

import posixpath
import re
import shutil
import sys
import zipfile
from pathlib import Path


def clean(path: Path) -> None:
    zin = zipfile.ZipFile(path, "r")
    names = zin.namelist()
    doc = zin.read("word/document.xml").decode("utf-8", "ignore")
    rels = zin.read("word/_rels/document.xml.rels").decode("utf-8", "ignore")

    used_rids = set(re.findall(r'r:(?:embed|link)="([^"]+)"', doc))
    rel_map = dict(re.findall(
        r'<Relationship[^>]*Id="([^"]+)"[^>]*Target="([^"]+)"', rels))

    keep_media = set()
    for rid in used_rids:
        target = rel_map.get(rid, "")
        if target:
            keep_media.add(posixpath.normpath(posixpath.join("word", target)))

    drop = {n for n in names
            if n.startswith("word/media/") and n not in keep_media}

    tmp = path.with_suffix(path.suffix + ".tmp")
    zout = zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED)
    for item in zin.infolist():
        if item.filename in drop:
            continue
        data = zin.read(item.filename)
        if item.filename == "word/_rels/document.xml.rels":
            kept = [ln for ln in data.decode("utf-8").splitlines(keepends=True)
                    if not any(f'Target="{posixpath.relpath(d, "word")}"' in ln
                               for d in drop)]
            data = "".join(kept).encode("utf-8")
        zout.writestr(item, data)
    zout.close()
    zin.close()
    shutil.move(str(tmp), str(path))
    print(f"removed {len(drop)} unused media part(s); kept {len(keep_media)}")


if __name__ == "__main__":
    clean(Path(sys.argv[1]))
