#!/usr/bin/env python3
"""
build_report.py - build the Deutchezz Hotel Ispahan final-year report .docx from
docs/report/report.md, using the project template for styles and page setup.

The template (docs/PIPDC_Final_Project_Documentation.docx) supplies STRUCTURE AND
FORMATTING ONLY: its styles, section properties and page setup are reused, and its
body content is discarded. No template prose is copied.

Formatting applied on top of the template:
  * Times New Roman throughout; Normal body text 12 pt, justified, 1.5 line spacing.
  * Word Heading 1..4 styles used for the automatic table of contents.
  * APA-style hanging-indent reference list.
  * Automatic Table of Contents, List of Tables and List of Figures (Word fields).

Source syntax (docs/report/report.md):
  # CHAPTER ONE            -> chapter banner (Normal, bold, centred, uppercase)
  ## PRELIMINARY TITLE     -> Heading 1, centred (front matter only)
  ### 1.0 Section          -> Heading 1
  #### 1.1 Subsection      -> Heading 2
  ##### 1.1.1 Sub-sub      -> Heading 3
  ###### ...               -> Heading 4
  plain text               -> Normal paragraph
  | a | b |                -> table row (first row = header)
  [[CENTER: text]]         -> centred paragraph
  [[BOLD: text]]           -> bold paragraph
  [[CBOLD: text]]          -> centred bold paragraph
  [[BLANK]]                -> empty paragraph
  [[PAGEBREAK]]            -> page break
  [[TOC]]                  -> Word table-of-contents field
  [[LOT]]                  -> Word list-of-tables field (Table Caption style)
  [[LOF]]                  -> Word list-of-figures field (Figure Caption style)
  [[FIG: text]]            -> Figure Caption paragraph
  [[TBL: text]]            -> Table Caption paragraph
  [[CHTITLE: text]]        -> chapter title line (Normal, centred, bold)
  [[IMG: path|width_in]]   -> centred inline image (path relative to report.md)
  [[REF: text]]            -> reference paragraph (hanging indent)
  [[LIST: text]]           -> list paragraph
  [[TO BE PROVIDED: text]] -> clearly marked author placeholder

Usage:
  python scripts/build_report.py
  python scripts/build_report.py --src docs/report/report.md --out out/Deutchezz_Final_Report.docx
"""

from __future__ import annotations

import argparse
import re
from pathlib import Path

from docx import Document
from docx.enum.style import WD_STYLE_TYPE
from docx.enum.text import WD_ALIGN_PARAGRAPH, WD_BREAK, WD_TAB_ALIGNMENT, WD_TAB_LEADER
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Emu, Inches, Pt, RGBColor

DEFAULT_SRC = Path("docs/report/report.md")
DEFAULT_TEMPLATE = Path("docs/PIPDC_Final_Project_Documentation.docx")
DEFAULT_OUT = Path("out/Deutchezz_Final_Report.docx")

BODY_FONT = "Times New Roman"
PLACEHOLDER_RE = re.compile(r"\[\[TO BE PROVIDED:\s*(.*?)\]\]")
LINE_MARKER = re.compile(r"^\[\[([A-Z0-9 ]+?)(?::\s*(.*?))?\]\]$", re.S)
HEADING_RE = re.compile(r"^(#{1,6})\s+(.*)$")


# --------------------------------------------------------------------------- #
# low-level helpers
# --------------------------------------------------------------------------- #
def clear_body(doc: Document) -> None:
    body = doc.element.body
    for child in list(body):
        if child.tag == qn("w:sectPr"):
            continue
        body.remove(child)


def ensure_styles(doc: Document) -> None:
    """Create any paragraph styles the build relies on but that a blank
    python-docx document does not define (Figure/Table Caption)."""
    existing = {s.name for s in doc.styles}
    for name in ("Figure Caption", "Table Caption", "List Paragraph"):
        if name not in existing:
            st = doc.styles.add_style(name, WD_STYLE_TYPE.PARAGRAPH)
            try:
                st.base_style = doc.styles["Normal"]
            except KeyError:  # pragma: no cover
                pass


def set_page(doc: Document) -> None:
    """US Letter page with the margins used by the project documentation
    template (following the institution's own sample document)."""
    for section in doc.sections:
        section.page_width = Emu(7772400)   # 8.5 in
        section.page_height = Emu(10058400)  # 11 in
        section.left_margin = Emu(1141095)   # 1.25 in
        section.right_margin = Emu(914400)   # 1.0 in
        section.top_margin = Emu(514985)     # 0.56 in
        section.bottom_margin = Emu(687705)  # 0.75 in


def set_style_font(style, name=None, size=None, bold=None, italic=None,
                   color=None) -> None:
    if name is not None:
        style.font.name = name
        rpr = style.element.get_or_add_rPr()
        rfonts = rpr.find(qn("w:rFonts"))
        if rfonts is None:
            rfonts = OxmlElement("w:rFonts")
            rpr.insert(0, rfonts)
        for attr in ("w:ascii", "w:hAnsi", "w:cs", "w:eastAsia"):
            rfonts.set(qn(attr), name)
    if size is not None:
        style.font.size = Pt(size)
    if bold is not None:
        style.font.bold = bold
    if italic is not None:
        style.font.italic = italic
    if color is not None:
        style.font.color.rgb = color


def configure_styles(doc: Document) -> None:
    normal = doc.styles["Normal"]
    set_style_font(normal, BODY_FONT, 12, bold=False, italic=False,
                   color=RGBColor(0, 0, 0))
    pf = normal.paragraph_format
    pf.alignment = WD_ALIGN_PARAGRAPH.JUSTIFY
    pf.line_spacing = 1.5
    pf.space_before = Pt(0)
    pf.space_after = Pt(8)

    specs = {
        "Heading 1": (14, True, False, 14, 8),
        "Heading 2": (13, True, False, 12, 6),
        "Heading 3": (12, True, False, 10, 6),
        "Heading 4": (12, True, True, 10, 6),
    }
    for name, (size, bold, italic, before, after) in specs.items():
        try:
            st = doc.styles[name]
        except KeyError:  # pragma: no cover
            continue
        set_style_font(st, BODY_FONT, size, bold=bold, italic=italic,
                       color=RGBColor(0, 0, 0))
        spf = st.paragraph_format
        spf.alignment = WD_ALIGN_PARAGRAPH.LEFT
        spf.line_spacing = 1.5
        spf.space_before = Pt(before)
        spf.space_after = Pt(after)
        spf.keep_with_next = True

    for name in ("Figure Caption", "Table Caption"):
        try:
            st = doc.styles[name]
        except KeyError:  # pragma: no cover
            continue
        set_style_font(st, BODY_FONT, 11, bold=False, italic=False,
                       color=RGBColor(0, 0, 0))
        spf = st.paragraph_format
        spf.alignment = WD_ALIGN_PARAGRAPH.CENTER
        spf.line_spacing = 1.0
        spf.space_before = Pt(4)
        spf.space_after = Pt(6)
        spf.keep_with_next = True

    for name in ("toc 1", "toc 2", "toc 3", "toc 4"):
        try:
            st = doc.styles[name]
        except KeyError:  # pragma: no cover
            continue
        set_style_font(st, BODY_FONT, 12, bold=False, italic=False,
                       color=RGBColor(0, 0, 0))
        # Entry leaders: replace the default dotted leader with a blank one so
        # the table of contents, list of tables and list of figures render with
        # no dot leader lines. Word regenerates the field result from this style
        # when the fields are updated (see scripts/strip_toc_leaders.py).
        try:
            tabs = st.paragraph_format.tab_stops
            tabs.clear_all()
            tabs.add_tab_stop(Inches(6.5), WD_TAB_ALIGNMENT.RIGHT,
                              WD_TAB_LEADER.SPACES)
        except Exception:  # pragma: no cover
            pass


def add_page_break(doc: Document) -> None:
    p = doc.add_paragraph()
    p.add_run().add_break(WD_BREAK.PAGE)


def add_page_number_footer(doc: Document) -> None:
    for section in doc.sections:
        footer = section.footer
        footer.is_linked_to_previous = False
        p = footer.paragraphs[0] if footer.paragraphs else footer.add_paragraph()
        p.text = ""
        p.alignment = WD_ALIGN_PARAGRAPH.CENTER
        run = p.add_run()
        f1 = OxmlElement("w:fldChar"); f1.set(qn("w:fldCharType"), "begin")
        it = OxmlElement("w:instrText"); it.set(qn("xml:space"), "preserve"); it.text = "PAGE"
        f2 = OxmlElement("w:fldChar"); f2.set(qn("w:fldCharType"), "end")
        run._r.append(f1); run._r.append(it); run._r.append(f2)


def add_toc_field(doc: Document, instr: str, cached: str) -> None:
    p = doc.add_paragraph()
    run = p.add_run()
    b = OxmlElement("w:fldChar"); b.set(qn("w:fldCharType"), "begin")
    i = OxmlElement("w:instrText"); i.set(qn("xml:space"), "preserve"); i.text = instr
    s = OxmlElement("w:fldChar"); s.set(qn("w:fldCharType"), "separate")
    t = OxmlElement("w:t"); t.text = cached
    e = OxmlElement("w:fldChar"); e.set(qn("w:fldCharType"), "end")
    run._r.append(b); run._r.append(i); run._r.append(s); run._r.append(t); run._r.append(e)


def add_runs_with_placeholders(p, text: str) -> None:
    pos = 0
    for m in PLACEHOLDER_RE.finditer(text):
        if m.start() > pos:
            p.add_run(text[pos:m.start()])
        r = p.add_run("[TO BE PROVIDED: " + m.group(1).strip() + "]")
        r.bold = True
        r.italic = True
        pos = m.end()
    if pos < len(text):
        p.add_run(text[pos:])


def add_table(doc: Document, rows: list[list[str]]) -> None:
    if not rows:
        return
    cols = max(len(r) for r in rows)
    table = doc.add_table(rows=len(rows), cols=cols)
    try:
        table.style = "Table Grid"
    except KeyError:
        pass
    table.autofit = True
    for i, row in enumerate(rows):
        for j in range(cols):
            text = row[j] if j < len(row) else ""
            cell = table.cell(i, j)
            cell.text = ""
            para = cell.paragraphs[0]
            para.paragraph_format.space_after = Pt(2)
            para.paragraph_format.line_spacing = 1.0
            para.paragraph_format.alignment = WD_ALIGN_PARAGRAPH.LEFT
            run = para.add_run(text)
            run.font.size = Pt(11)
            run.font.name = BODY_FONT
            if i == 0:
                run.bold = True


# --------------------------------------------------------------------------- #
# marker handling
# --------------------------------------------------------------------------- #
LINE_MARKER = re.compile(r"^\[\[([A-Z0-9 ]+?)(?::\s*(.*?))?\]\]$", re.S)
HEADING_RE = re.compile(r"^(#{1,6})\s+(.*)$")


def emit_heading(doc: Document, text: str, level: int) -> None:
    if level == 0:  # chapter / references banner
        p = doc.add_paragraph(style="Normal")
        p.alignment = WD_ALIGN_PARAGRAPH.CENTER
        p.paragraph_format.space_before = Pt(12)
        p.paragraph_format.space_after = Pt(12)
        p.paragraph_format.keep_with_next = True
        run = p.add_run(text.upper())
        run.bold = True
        run.font.size = Pt(14)
        run.font.name = BODY_FONT
        return
    style = f"Heading {min(level, 4)}"
    doc.add_paragraph(text, style=style)


def emit(doc: Document, src: str, base_dir: Path) -> None:
    lines = src.splitlines()
    i = 0
    in_refs = False
    seen_chapter = False
    # Body line spacing is 1.5 in the front matter and 2.0 from Chapter One
    # onward (Chapters One to Five and the reference list).
    double_space = False
    while i < len(lines):
        line = lines[i].rstrip()
        stripped = line.strip()

        if not stripped or stripped.startswith("<!--"):
            i += 1
            continue

        # tables
        if stripped.startswith("|"):
            block = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                cells = [c.strip() for c in lines[i].strip().strip("|").split("|")]
                if not re.fullmatch(r"[-: ]+", "".join(cells)):
                    block.append(cells)
                i += 1
            if block:
                add_table(doc, block)
                doc.add_paragraph().paragraph_format.space_after = Pt(4)
            continue

        # headings
        m = HEADING_RE.match(stripped)
        if m:
            hashes, text = m.group(1), m.group(2).strip()
            level = len(hashes) - 1  # 0 for "#"
            if level == 0 and text.upper().startswith("REFERENCES"):
                emit_heading(doc, text, 0)
                in_refs = True
                double_space = True
            elif level == 0:
                seen_chapter = True
                emit_heading(doc, text, 0)
                double_space = True
            elif level == 1:  # front-matter preliminary title
                p = doc.add_paragraph(text, style="Heading 1")
                p.alignment = WD_ALIGN_PARAGRAPH.CENTER
            elif level == 2:  # numbered section, e.g. 1.0
                doc.add_paragraph(text, style="Heading 1")
            elif level == 3:  # subsection, e.g. 1.2.1
                doc.add_paragraph(text, style="Heading 2")
            elif level == 4:  # sub-subsection
                doc.add_paragraph(text, style="Heading 3")
            else:
                doc.add_paragraph(text, style="Heading 4")
            i += 1
            continue

        # inline markers
        mm = LINE_MARKER.match(stripped)
        if mm:
            _emit_marker(doc, mm.group(1).strip(), (mm.group(2) or "").strip(), base_dir)
            i += 1
            continue

        # plain paragraph
        p = doc.add_paragraph(style="Normal")
        if in_refs:
            p.paragraph_format.left_indent = Inches(0.5)
            p.paragraph_format.first_line_indent = Inches(-0.5)
            p.paragraph_format.line_spacing = 2.0 if double_space else 1.5
            p.alignment = WD_ALIGN_PARAGRAPH.LEFT
        elif double_space:
            p.paragraph_format.line_spacing = 2.0
        add_runs_with_placeholders(p, stripped)
        i += 1


def _emit_image(doc: Document, arg: str, base_dir: Path) -> None:
    parts = arg.split("|")
    rel = parts[0].strip()
    width = parts[1].strip() if len(parts) > 1 and parts[1].strip() else None
    path = (base_dir / rel).resolve()
    p = doc.add_paragraph(style="Normal")
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    p.paragraph_format.space_after = Pt(2)
    p.paragraph_format.keep_with_next = True
    if not path.exists():
        r = p.add_run("[TO BE PROVIDED: figure image " + rel + "]")
        r.bold = True
        r.italic = True
        return
    run = p.add_run()
    run.add_picture(str(path), width=Inches(float(width)) if width else None)


def _emit_marker(doc: Document, name: str, arg: str, base_dir: Path) -> None:
    if name == "BLANK":
        doc.add_paragraph()
    elif name == "PAGEBREAK":
        add_page_break(doc)
    elif name == "TOC":
        add_toc_field(doc, 'TOC \\o "1-3" \\h \\z \\u',
                      "Right-click and choose Update Field to build the table of contents.")
    elif name == "LOT":
        add_toc_field(doc, 'TOC \\h \\z \\t "Table Caption,1"',
                      "Right-click and choose Update Field to build the list of tables.")
    elif name == "LOF":
        add_toc_field(doc, 'TOC \\h \\z \\t "Figure Caption,1"',
                      "Right-click and choose Update Field to build the list of figures.")
    elif name == "CENTER":
        p = doc.add_paragraph(style="Normal")
        p.alignment = WD_ALIGN_PARAGRAPH.CENTER
        add_runs_with_placeholders(p, arg)
    elif name == "CBOLD":
        p = doc.add_paragraph(style="Normal")
        p.alignment = WD_ALIGN_PARAGRAPH.CENTER
        add_runs_with_placeholders(p, arg)
        for r in p.runs:
            r.bold = True
    elif name == "CHTITLE":
        p = doc.add_paragraph(style="Normal")
        p.alignment = WD_ALIGN_PARAGRAPH.CENTER
        p.paragraph_format.space_after = Pt(12)
        p.paragraph_format.keep_with_next = True
        r = p.add_run(arg.upper())
        r.bold = True
        r.font.size = Pt(13)
        r.font.name = BODY_FONT
    elif name == "BOLD":
        p = doc.add_paragraph(style="Normal")
        add_runs_with_placeholders(p, arg)
        for r in p.runs:
            r.bold = True
    elif name == "FIG":
        doc.add_paragraph(arg, style="Figure Caption")
    elif name == "TBL":
        doc.add_paragraph(arg, style="Table Caption")
    elif name == "IMG":
        _emit_image(doc, arg, base_dir)
    elif name == "REF":
        p = doc.add_paragraph(style="Normal")
        p.paragraph_format.left_indent = Inches(0.5)
        p.paragraph_format.first_line_indent = Inches(-0.5)
        p.paragraph_format.line_spacing = 1.5
        p.alignment = WD_ALIGN_PARAGRAPH.LEFT
        add_runs_with_placeholders(p, arg)
    elif name == "LIST":
        doc.add_paragraph(arg, style="List Paragraph")
    elif name == "TO BE PROVIDED":
        p = doc.add_paragraph(style="Normal")
        r = p.add_run("[TO BE PROVIDED: " + arg + "]")
        r.bold = True
        r.italic = True
    else:
        p = doc.add_paragraph(style="Normal")
        add_runs_with_placeholders(p, arg)


# --------------------------------------------------------------------------- #
# public entry point
# --------------------------------------------------------------------------- #
def build_document(src_text: str, template: Path, out: Path, src_dir: Path) -> None:
    # A blank document is used for the package; the project template is only a
    # formatting reference. This avoids carrying the template's unused media.
    doc = Document()
    ensure_styles(doc)
    set_page(doc)
    configure_styles(doc)
    emit(doc, src_text, base_dir=src_dir)
    add_page_number_footer(doc)
    out.parent.mkdir(parents=True, exist_ok=True)
    doc.save(str(out))


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--src", type=Path, default=DEFAULT_SRC)
    ap.add_argument("--template", type=Path, default=DEFAULT_TEMPLATE)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    args = ap.parse_args()
    text = Path(args.src).read_text(encoding="utf-8")
    build_document(text, args.template, args.out, Path(args.src).parent)
    print(f"Wrote {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
