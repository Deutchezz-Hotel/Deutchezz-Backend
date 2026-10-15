#!/usr/bin/env python3
"""
build_docx.py - assemble the report chapter sources into a single Markdown
document and then build the final .docx deliverable.

Chapter sources live in docs/report/chapters/ as ordered Markdown files
(00-front-matter.md, 01-chapter-one.md, ... 99-references.md). They are
concatenated in filename order into docs/report/report.md, and the result is
rendered into out/Deutchezz_Final_Report_Revised/Deutchezz_Final_Report_Revised.docx
using the template for styles and page setup (see build_report.py for the source syntax).

Usage:
  python scripts/build_docx.py
  python scripts/build_docx.py --chapters docs/report/chapters --out out/Deutchezz_Final_Report_Revised/Deutchezz_Final_Report_Revised.docx
  python scripts/build_docx.py --no-docx        # only assemble report.md
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_report  # noqa: E402

DEFAULT_CHAPTERS = Path("docs/report/chapters")
DEFAULT_REFS = Path("docs/report/references.md")
DEFAULT_APPENDICES = Path("docs/report/appendices.md")
DEFAULT_SRC = Path("docs/report/report.md")
DEFAULT_TEMPLATE = Path("docs/PIPDC_Final_Project_Documentation.docx")
DEFAULT_OUT = Path("out/Deutchezz_Final_Report_Revised/Deutchezz_Final_Report_Revised.docx")


def assemble(chapters_dir: Path, refs: Path, out: Path,
             appendices: Path | None = None) -> str:
    files = sorted(p for p in chapters_dir.glob("*.md"))
    if refs.exists():
        files.append(refs)
    if appendices is not None and appendices.exists():
        files.append(appendices)
    if not files:
        raise SystemExit(f"No chapter sources found in {chapters_dir}")
    parts = [p.read_text(encoding="utf-8").strip("\n") for p in files]
    text = "\n\n".join(parts) + "\n"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(text, encoding="utf-8")
    print(f"Assembled {len(files)} sources -> {out}")
    for p in files:
        print(f"  - {p.name}")
    return text


def build(text: str, template: Path, out: Path, src_dir: Path) -> None:
    build_report.build_document(text, template, out, src_dir)
    print(f"Wrote {out}")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--chapters", type=Path, default=DEFAULT_CHAPTERS)
    ap.add_argument("--references", type=Path, default=DEFAULT_REFS)
    ap.add_argument("--appendices", type=Path, default=DEFAULT_APPENDICES)
    ap.add_argument("--src", type=Path, default=DEFAULT_SRC)
    ap.add_argument("--template", type=Path, default=DEFAULT_TEMPLATE)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    ap.add_argument("--no-docx", action="store_true", help="only assemble report.md")
    args = ap.parse_args()

    text = assemble(args.chapters, args.references, args.src, args.appendices)
    if not args.no_docx:
        build(text, args.template, args.out, args.src.parent)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
