#!/usr/bin/env python3
"""
check_doc.py - integrity checks for the Deutchezz Hotel Ispahan final-year report.

Checks against the project template (docs/PIPDC_Final_Project_Documentation.docx):

1. OVERLAP CHECK
   Every n-word (default 8) sequence of the NEW document's body text is compared
   with the template's body text. Headings and captions are excluded on BOTH
   sides, because the standing rules permit structural similarity in headings
   only. Any shared n-gram outside headings is a failure.

2. CITATION CHECK
   Every in-text citation (Author, Year) must appear in the REFERENCES list, and
   every reference entry must be cited at least once.

Inputs may be a .docx file or a Markdown/.txt source.

Exit code 0 when all checks pass, 1 otherwise.

Usage:
  python scripts/check_doc.py --doc docs/report/report.md
  python scripts/check_doc.py --doc out/Deutchezz_Final_Report.docx
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

try:
    from docx import Document
    from docx.oxml.ns import qn
    from docx.table import Table
    from docx.text.paragraph import Paragraph
except ImportError:  # pragma: no cover
    Document = None

DEFAULT_TEMPLATE = Path("docs/PIPDC_Final_Project_Documentation.docx")
CAPTION_STYLES = {"Figure Caption", "Table Caption"}
HEADING_PREFIXES = ("Heading", "Title", "TOC", "toc", "Subtitle")


def _is_heading(style_name: str) -> bool:
    if not style_name:
        return False
    if style_name in CAPTION_STYLES:
        return True
    return style_name.startswith(HEADING_PREFIXES)


class Source:
    """Body text (no headings), full text (with headings) and heading list."""

    def __init__(self):
        self.body: list[str] = []
        self.full: list[str] = []
        self.headings: list[str] = []

    def add(self, text: str, heading: bool) -> None:
        text = text.strip()
        if not text:
            return
        self.full.append(text)
        (self.headings if heading else self.body).append(text)

    @property
    def body_text(self) -> str:
        return "\n".join(self.body)

    @property
    def full_text(self) -> str:
        return "\n".join(self.full)


def _iter_block_items(doc: "Document"):
    """Yield paragraphs and tables in document order (python-docx keeps them separate)."""
    body = doc.element.body
    for child in body.iterchildren():
        if child.tag == qn("w:p"):
            yield Paragraph(child, doc)
        elif child.tag == qn("w:tbl"):
            yield Table(child, doc)


def extract_docx(path: Path) -> Source:
    doc = Document(str(path))
    src = Source()
    for block in _iter_block_items(doc):
        if isinstance(block, Paragraph):
            src.add(block.text, _is_heading(block.style.name if block.style else ""))
        else:
            for row in block.rows:
                for cell in row.cells:
                    for p in cell.paragraphs:
                        if p.text.strip():
                            src.add(p.text, False)
    return src


_MD_STRIP = [
    (re.compile(r"<!--.*?-->", re.S), " "),
    (re.compile(r"```.*?```", re.S), " "),
    (re.compile(r"\[\[[^\]]*\]\]"), " "),
    (re.compile(r"`([^`]+)`"), r"\1"),
    (re.compile(r"!\[[^\]]*\]\([^)]*\)"), " "),
    (re.compile(r"\[([^\]]+)\]\([^)]*\)"), r"\1"),
    (re.compile(r"\*\*([^*]+)\*\*"), r"\1"),
    (re.compile(r"\*([^*]+)\*"), r"\1"),
]
_MD_HEADING = re.compile(r"^\s{0,3}(#{1,6})\s*(.+?)\s*$")
_MD_TABLE_ROW = re.compile(r"^\s*\|.*\|\s*$")
_MD_TABLE_SEP = re.compile(r"^\s*[-:| ]{3,}\s*$")
_MD_LIST = re.compile(r"^\s{0,3}(?:[-*+]|\d+\.)\s+")


def _strip_inline(text: str) -> str:
    for pat, repl in _MD_STRIP:
        text = pat.sub(repl, text)
    return text.strip()


def extract_markdown(path: Path) -> Source:
    src = Source()
    for line in path.read_text(encoding="utf-8").splitlines():
        if _MD_TABLE_SEP.match(line):
            continue
        m = _MD_HEADING.match(line)
        if m:
            src.add(_strip_inline(m.group(2)), True)
            continue
        if _MD_TABLE_ROW.match(line):
            for c in line.strip().strip("|").split("|"):
                src.add(_strip_inline(c), False)
            continue
        src.add(_strip_inline(_MD_LIST.sub("", line)), False)
    return src


def load_source(path: Path) -> Source:
    suffix = path.suffix.lower()
    if suffix == ".docx":
        return extract_docx(path)
    if suffix in {".md", ".markdown", ".txt"}:
        return extract_markdown(path)
    raise SystemExit(f"Unsupported source type: {path.suffix}")


_WORD_RE = re.compile(r"[a-z0-9]+")


def words(text: str) -> list[str]:
    return _WORD_RE.findall(text.lower())


def ngrams(ws: list[str], n: int) -> list[str]:
    return [" ".join(ws[i:i + n]) for i in range(len(ws) - n + 1)]


# ------------------------------------------------------------------------- #
# Citations (APA 6th heuristic)
# ------------------------------------------------------------------------- #
_YEAR = r"(?:19|20)\d{2}[a-z]?|n\.d\."


def _norm(s: str) -> str:
    return re.sub(r"[^a-z0-9]", "", s.lower())


def _author_candidates(name: str) -> set[str]:
    name = re.sub(r"\bet al\.?", "", name, flags=re.I)
    name = re.sub(r"\band\b|&", " ", name, flags=re.I)
    parts = [p.strip() for p in re.split(r",", name) if p.strip()]
    if not parts:
        return set()
    first = parts[0]
    fw = first.split()
    cands = {_norm(name), _norm(first)}
    if fw:
        cands.add(_norm(fw[-1]))
    return {c for c in cands if c}


def _year_of(chunk: str):
    ym = re.search(r"\b(" + _YEAR + r")", chunk)
    return ym.group(1).rstrip(".").lower() if ym else None


def in_text_citations(text: str):
    """Return list of (frozenset(candidate-surnames), year, display) groups."""
    groups = []
    for m in re.finditer(r"\(([^()]+)\)", text):
        for chunk in m.group(1).split(";"):
            chunk = chunk.strip()
            year = _year_of(chunk)
            if not year:
                continue
            ym = re.search(r"\b(" + _YEAR + r")", chunk)
            name = chunk[: ym.start()].strip(" ,")
            cands = _author_candidates(name)
            if cands:
                groups.append((frozenset(cands), year, f"{name} {year}".strip()))
    for m in re.finditer(
        r"([A-Z][A-Za-z'\-\.]+(?:(?:\s+(?:and|&|et al\.?))?\s+[A-Z][A-Za-z'\-\.]+)*)\s+\(("
        + _YEAR + r")\)", text):
        year = m.group(2).rstrip(".").lower()
        cands = _author_candidates(m.group(1))
        if cands:
            groups.append((frozenset(cands), year, f"{m.group(1)} {year}".strip()))
    # dedupe
    seen, out = set(), []
    for c, y, d in groups:
        key = (c, y)
        if key not in seen:
            seen.add(key)
            out.append((c, y, d))
    return out


_REF_HEADING = re.compile(r"^\s*(?:#+\s*)?REFERENCES\s*$", re.I)


def reference_entries(source: Source) -> list[str]:
    idx = next((i for i, e in enumerate(source.full) if _REF_HEADING.match(e)), None)
    if idx is None:
        return []
    entries = []
    for e in source.full[idx + 1:]:
        if re.match(r"^\s*(?:#+\s*)?APPENDIX\b", e, re.I):
            break
        if len(e) > 20 and not e.startswith("[["):
            entries.append(e)
    return entries


def reference_key(entry: str):
    year = _year_of(entry)
    name = re.split(r"\(\s*(?:" + _YEAR + r")\s*\)", entry)[0]
    return frozenset(_author_candidates(name)), year, name.strip(" .")


def _match(a: frozenset, b: frozenset) -> bool:
    if a & b:
        return True
    for x in a:
        for y in b:
            if x and y and (x in y or y in x):
                return True
    return False


# ------------------------------------------------------------------------- #
def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--template", type=Path, default=DEFAULT_TEMPLATE)
    ap.add_argument("--doc", type=Path, required=True)
    ap.add_argument("--min-match", type=int, default=8)
    ap.add_argument("--max-report", type=int, default=40)
    args = ap.parse_args()

    if Document is None:
        print("python-docx is required", file=sys.stderr)
        return 2
    for p in (args.template, args.doc):
        if not p.exists():
            print(f"Not found: {p}", file=sys.stderr)
            return 2

    tmpl = extract_docx(args.template)
    new = load_source(args.doc)

    tmpl_grams = set(ngrams(words(tmpl.body_text), args.min_match))
    new_grams = ngrams(words(new.body_text), args.min_match)
    overlaps = sorted({g for g in new_grams if g in tmpl_grams})

    print("=" * 72)
    print("OVERLAP CHECK")
    print("=" * 72)
    print(f"template body words : {len(words(tmpl.body_text)):>8}")
    print(f"new body words      : {len(words(new.body_text)):>8}")
    print(f"n-gram size         : {args.min_match}")
    print(f"shared {args.min_match}-grams   : {len(overlaps)}")
    for g in overlaps[: args.max_report]:
        print(f"  MATCH: {g}")
    if len(overlaps) > args.max_report:
        print(f"  ... and {len(overlaps) - args.max_report} more")
    overlap_ok = not overlaps

    print()
    print("=" * 72)
    print("CITATION CHECK")
    print("=" * 72)
    cites = in_text_citations(new.body_text)
    refs = [reference_key(e) for e in reference_entries(new)]
    cites = [(c, y, d) for (c, y, d) in cites if c]
    refs = [r for r in refs if r[0]]

    cited_not_listed = [
        d for (c, y, d) in cites
        if not any(ry == y and _match(c, rc) for (rc, ry, _) in refs)
    ]
    listed_not_cited = [
        disp for (rc, ry, disp) in refs
        if not any(cy == ry and _match(c, rc) for (c, cy, _) in cites)
    ]
    print(f"in-text citations   : {len(cites)}")
    print(f"reference entries   : {len(refs)}")
    for d in cited_not_listed:
        print(f"  CITED BUT NOT IN REFERENCES: {d}")
    for d in listed_not_cited:
        print(f"  IN REFERENCES BUT NOT CITED: {d}")
    citation_ok = not cited_not_listed and not listed_not_cited

    print()
    print("=" * 72)
    print(f"RESULT: overlaps={'PASS' if overlap_ok else 'FAIL'}  citations={'PASS' if citation_ok else 'FAIL'}")
    print("=" * 72)
    return 0 if (overlap_ok and citation_ok) else 1


if __name__ == "__main__":
    raise SystemExit(main())
