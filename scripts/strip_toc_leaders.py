#!/usr/bin/env python3
"""
strip_toc_leaders.py - remove the dotted leader lines from the table of
contents, list of tables and list of figures in a .docx.

Word renders a TOC/LOT/LOF entry as a run of text followed by a tab that is
right-aligned to the page margin with a dotted leader (``w:leader="dot"``).
The leader is written into the cached field result in ``word/document.xml``
when the fields are updated, so it cannot be suppressed from the source alone.
This utility rewrites every tab whose leader is not already blank to
``w:leader="none"`` in the main document part (and any headers or footers),
leaving the field itself live so that the entries can still be updated.

Run after Word has updated the fields (scripts/render_docx.ps1 does this):

    python scripts/strip_toc_leaders.py --docx out/.../Report.docx
"""

from __future__ import annotations

import argparse
import re
import shutil
import sys
import zipfile
from pathlib import Path

TARGET_PARTS = re.compile(r"word/(document|header\d*|footer\d*)\.xml$")
LEADER_ATTR = re.compile(rb'w:leader="(?!none\b)[^"]*"')


def strip_docx(docx: Path) -> int:
    """Rewrite the docx in place; return the number of leader attributes fixed."""
    tmp = docx.with_suffix(".docx.leaders.tmp")
    changed = 0
    with zipfile.ZipFile(docx, "r") as zin:
        names = zin.namelist()
        with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as zout:
            for name in names:
                data = zin.read(name)
                if TARGET_PARTS.match(name):
                    data, n = LEADER_ATTR.subn(b'w:leader="none"', data)
                    changed += n
                zout.writestr(name, data)
    shutil.move(str(tmp), str(docx))
    return changed


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--docx", type=Path, required=True, help="the .docx to edit in place")
    args = ap.parse_args()
    if not args.docx.exists():
        print(f"not found: {args.docx}", file=sys.stderr)
        return 1
    changed = strip_docx(args.docx)
    print(f"strip_toc_leaders: set {changed} tab leader(s) to 'none' in {args.docx.name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
