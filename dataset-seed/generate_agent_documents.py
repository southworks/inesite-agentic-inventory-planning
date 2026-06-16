#!/usr/bin/env python3
"""
Generate PDF and PNG agent inputs from the Retail dataset-seed Raw layer (00_raw/).

Output: dataset-seed/00_agent_inputs/pdf/<source_type>/... and .../png/<source_type>/...

Mirrors the FSI dataset-seed's generate_agent_documents.py (PDF via reportlab,
PNG via Pillow) so both reference implementations expose the same agent-input
formats for OCR/vision/document-pipeline demos. Where FSI renders per loan
application, this renders per the natural reporting unit for each retail source
system: a store's weekly POS/inventory report, a supplier's profile, a single
promo event, a single shipment.

Categories:
  pos_transactions/      - weekly store sales report (PDF only; 22 = 2 stores x 11 weeks)
  supplier_data/          - supplier profile (PDF, 6) + shipment receiving report
                            (PDF, 6) + packing slip scan (PNG, 6 - the one category
                            with a believable physical-scan equivalent, like FSI's
                            ID/paystub PNGs)
  promotions/             - promotional event brief (PDF only; 4)
  inventory_snapshots/    - weekly store inventory status report (PDF only; 22)

00_agent_inputs/ is gitignored (zero new information vs 00_raw/ - regenerate after
clone, same treatment as FSI's 00_raw/{pdf,png}).

Usage:
  pip install -r requirements.txt
  python3 generate_agent_documents.py
  python3 generate_agent_documents.py --formats pdf
  python3 generate_agent_documents.py --formats png
"""

from __future__ import annotations

import argparse
from datetime import datetime
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from reportlab.lib import colors
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import inch
from reportlab.platypus import Paragraph, SimpleDocTemplate, Spacer, Table, TableStyle

from generate_raw_layer import PRODUCT_NAMES, STORE_NAMES, week_batches, load_extract
from generate_normalized_layers import (
    parse_pos,
    parse_supplier_master,
    parse_supplier_shipments,
    parse_promo_calendar,
    parse_inventory,
    cat_of,
)

BASE = Path(__file__).resolve().parent
OUT = BASE / "00_agent_inputs"

HEADER_BG = colors.HexColor("#1e3a5f")
ACCENT = colors.HexColor("#2c5282")
LIGHT_ROW = colors.HexColor("#f7fafc")


def out_dir(fmt: str, source_type: str) -> Path:
    return OUT / fmt / source_type


def fmt_money(v) -> str:
    return f"${float(v):,.2f}"


def fmt_date(d: str) -> str:
    try:
        return datetime.strptime(d, "%Y-%m-%d").strftime("%B %d, %Y")
    except (ValueError, TypeError):
        return str(d)


def ensure_dir(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)


# --- Generic PDF builders (mirrors FSI's generate_agent_documents.py) ---------

def _field_value_style() -> TableStyle:
    return TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), HEADER_BG),
        ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
        ("FONTNAME", (0, 0), (-1, 0), "Helvetica-Bold"),
        ("FONTSIZE", (0, 0), (-1, -1), 8),
        ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, LIGHT_ROW]),
        ("GRID", (0, 0), (-1, -1), 0.25, colors.lightgrey),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 5),
        ("RIGHTPADDING", (0, 0), (-1, -1), 5),
        ("TOPPADDING", (0, 0), (-1, -1), 4),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
    ])


def write_pdf(path: Path, title: str, subtitle: str, sections: list) -> None:
    """Field/value PDF - one or more titled sections of label/value rows."""
    ensure_dir(path.parent)
    doc = SimpleDocTemplate(
        str(path), pagesize=letter,
        leftMargin=0.75 * inch, rightMargin=0.75 * inch,
        topMargin=0.75 * inch, bottomMargin=0.75 * inch,
    )
    styles = getSampleStyleSheet()
    title_style = ParagraphStyle("DocTitle", parent=styles["Heading1"], fontSize=16, textColor=HEADER_BG, spaceAfter=4)
    subtitle_style = ParagraphStyle("DocSubtitle", parent=styles["Normal"], fontSize=9, textColor=colors.grey, spaceAfter=14)
    section_style = ParagraphStyle("Section", parent=styles["Heading2"], fontSize=11, textColor=ACCENT, spaceBefore=10, spaceAfter=6)
    footer_style = ParagraphStyle("Footer", parent=styles["Normal"], fontSize=8, textColor=colors.grey, spaceBefore=20)

    story: list = [Paragraph(title, title_style), Paragraph(subtitle, subtitle_style)]
    for section_title, rows in sections:
        story.append(Paragraph(section_title, section_style))
        if not rows:
            continue
        table = Table([["Field", "Value"]] + [[k, v] for k, v in rows], colWidths=[2.2 * inch, 4.3 * inch])
        table.setStyle(_field_value_style())
        story.append(table)
        story.append(Spacer(1, 6))

    story.append(Paragraph(
        "SYNTHETIC DOCUMENT — Generated for agentic retail inventory planning demo. Not for production use.",
        footer_style,
    ))
    doc.build(story)


def write_pdf_datatable(path: Path, title: str, subtitle: str, headers: list, rows: list, note: str | None = None) -> None:
    """Multi-row data-table PDF - one wide table, e.g. a weekly sales report."""
    ensure_dir(path.parent)
    doc = SimpleDocTemplate(
        str(path), pagesize=letter,
        leftMargin=0.6 * inch, rightMargin=0.6 * inch,
        topMargin=0.75 * inch, bottomMargin=0.75 * inch,
    )
    styles = getSampleStyleSheet()
    title_style = ParagraphStyle("DocTitle", parent=styles["Heading1"], fontSize=16, textColor=HEADER_BG, spaceAfter=4)
    subtitle_style = ParagraphStyle("DocSubtitle", parent=styles["Normal"], fontSize=9, textColor=colors.grey, spaceAfter=14)
    note_style = ParagraphStyle("Note", parent=styles["Normal"], fontSize=9, textColor=colors.black, spaceBefore=10)
    footer_style = ParagraphStyle("Footer", parent=styles["Normal"], fontSize=8, textColor=colors.grey, spaceBefore=20)

    story: list = [Paragraph(title, title_style), Paragraph(subtitle, subtitle_style)]
    table = Table([headers] + rows, repeatRows=1)
    table.setStyle(_field_value_style())
    story.append(table)
    if note:
        story.append(Paragraph(note, note_style))
    story.append(Paragraph(
        "SYNTHETIC DOCUMENT — Generated for agentic retail inventory planning demo. Not for production use.",
        footer_style,
    ))
    doc.build(story)


# --- PNG builder (mirrors FSI's write_id_scan_png / write_paystub_png) --------

def _font(size: int, bold: bool = False):
    candidates = [
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf" if bold else "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "arial.ttf",
    ]
    for p in candidates:
        try:
            return ImageFont.truetype(p, size)
        except OSError:
            continue
    return ImageFont.load_default()


def write_packing_slip_png(path: Path, shipment: dict, supplier_name: str) -> None:
    """Render a warehouse-dock-scanned packing slip / delivery receipt."""
    ensure_dir(path.parent)
    w, h = 900, 620
    img = Image.new("RGB", (w, h), color=(252, 252, 248))
    draw = ImageDraw.Draw(img)
    title_font = _font(24, bold=True)
    body_font = _font(16)
    bold_font = _font(18, bold=True)
    small_font = _font(13)

    draw.rectangle([30, 30, w - 30, h - 30], outline=(30, 64, 110), width=3)
    draw.text((50, 50), "DELIVERY RECEIPT / PACKING SLIP", fill=(30, 64, 110), font=title_font)
    draw.text((50, 88), f"Shipment {shipment['shipment_id']}", fill=(80, 80, 80), font=small_font)

    lines = [
        f"Supplier:      {supplier_name} ({shipment['supplier_id']})",
        f"SKU:           {shipment['sku_id']}",
        f"Ordered Date:  {fmt_date(shipment['ordered_date'])}",
        f"Expected Date: {fmt_date(shipment['expected_date'])}",
        f"Actual Date:   {fmt_date(shipment['actual_date'])}",
        "",
        f"Ordered Qty:   {shipment['ordered_qty']}",
        f"Received Qty:  {shipment['received_qty']}",
        f"Fill Rate:     {shipment['fill_rate_pct']}%",
    ]
    y = 140
    for line in lines:
        font = bold_font if line.startswith(("Received", "Fill Rate")) else body_font
        draw.text((50, y), line, fill=(30, 30, 30), font=font)
        y += 36

    if shipment.get("disrupted"):
        y += 10
        draw.rectangle([45, y, w - 45, y + 70], outline=(150, 30, 30), width=2)
        draw.text((55, y + 10), "*** DISRUPTION NOTED AT RECEIVING ***", fill=(150, 30, 30), font=bold_font)
        reason = (shipment.get("disruption_reason") or "")[:78]
        draw.text((55, y + 38), reason, fill=(120, 30, 30), font=small_font)

    draw.text((50, h - 55), "WAREHOUSE DOCK SCAN — VendorHub ERP Receiving Module", fill=(130, 130, 130), font=small_font)
    img.save(path, format="PNG")


# --- Category builders ---------------------------------------------------------

def generate_pos_reports(pos: dict, dates: list, formats: set) -> int:
    if "pdf" not in formats:
        return 0
    count = 0
    batches = week_batches(dates)
    stores = sorted({store for (_, store) in pos})
    for store in stores:
        for batch in batches:
            week_start, week_end = batch[0], batch[-1]
            skus = sorted({sku for (sku, st) in pos if st == store})
            rows = []
            total_units, total_revenue = 0, 0.0
            for sku in skus:
                by_date = pos[(sku, store)]
                units = sum(by_date[d]["units"] for d in batch)
                revenue = round(sum(by_date[d]["revenue"] for d in batch), 2)
                promo_days = sum(1 for d in batch if by_date[d]["promo"])
                total_units += units
                total_revenue += revenue
                rows.append([sku, PRODUCT_NAMES[sku], cat_of(sku), str(units), fmt_money(revenue),
                             "Yes" if promo_days else "No"])
            rows.append(["", "TOTAL", "", str(total_units), fmt_money(total_revenue), ""])
            write_pdf_datatable(
                out_dir("pdf", "pos_transactions") / f"{store}_{week_start}_sales_report.pdf",
                f"Weekly Store Sales Report — {STORE_NAMES[store]}",
                f"Week of {fmt_date(week_start)} – {fmt_date(week_end)}  |  Source: POS export",
                ["SKU", "Description", "Category", "Units Sold", "Net Sales", "Any Promo Day"],
                rows,
            )
            count += 1
    return count


def generate_inventory_reports(inventory_rows: list, formats: set) -> int:
    if "pdf" not in formats:
        return 0
    count = 0
    by_store_date: dict = {}
    for row in inventory_rows:
        by_store_date.setdefault((row["STORE_ID"], row["SNAPSHOT_DATE"]), []).append(row)

    for (store, date), rows_for_date in sorted(by_store_date.items()):
        rows = []
        for row in sorted(rows_for_date, key=lambda r: r["SKU"]):
            rows.append([
                row["SKU"], PRODUCT_NAMES[row["SKU"]], row["ON_HAND_UNITS"], row["IN_TRANSIT_UNITS"],
                row["SAFETY_STOCK_UNITS"], row["STATUS"],
            ])
        write_pdf_datatable(
            out_dir("pdf", "inventory_snapshots") / f"{store}_{date}_inventory_report.pdf",
            f"Weekly Inventory Status Report — {STORE_NAMES[store]}",
            f"Snapshot date: {fmt_date(date)}  |  Source: inventory management system",
            ["SKU", "Description", "On Hand", "In Transit", "Safety Stock", "Status"],
            rows,
        )
        count += 1
    return count


def generate_supplier_profiles(suppliers: list, formats: set) -> int:
    if "pdf" not in formats:
        return 0
    count = 0
    for s in suppliers:
        write_pdf(
            out_dir("pdf", "supplier_data") / f"{s['supplier_id']}_profile.pdf",
            "Approved Supplier Profile",
            f"{s['name']}  |  Source: VendorHub ERP — Procurement Module",
            [(
                "Supplier", [
                    ("Supplier ID", s["supplier_id"]),
                    ("Name", s["name"]),
                    ("SKU Supplied", f"{s['sku_id']} ({PRODUCT_NAMES[s['sku_id']]})"),
                    ("Nominal Lead Time", f"{s['lead_time_days']} days"),
                    ("MOQ", str(s["moq"])),
                    ("Nominal Fill Rate", f"{s['fill_rate_pct']}%"),
                    ("Reliability Score", str(s["reliability_score"])),
                ],
            )],
        )
        count += 1
    return count


def generate_shipment_documents(shipments: list, suppliers: list, formats: set) -> int:
    count = 0
    name_by_supplier = {s["supplier_id"]: s["name"] for s in suppliers}
    for sh in shipments:
        supplier_name = name_by_supplier[sh["supplier_id"]]
        if "pdf" in formats:
            sections = [(
                "Shipment", [
                    ("Shipment ID", sh["shipment_id"]),
                    ("Supplier", f"{supplier_name} ({sh['supplier_id']})"),
                    ("SKU", f"{sh['sku_id']} ({PRODUCT_NAMES[sh['sku_id']]})"),
                    ("Ordered Date", fmt_date(sh["ordered_date"])),
                    ("Expected Date", fmt_date(sh["expected_date"])),
                    ("Actual Date", fmt_date(sh["actual_date"])),
                    ("Ordered Qty", str(sh["ordered_qty"])),
                    ("Received Qty", str(sh["received_qty"])),
                    ("Fill Rate", f"{sh['fill_rate_pct']}%"),
                    ("Disruption Noted", sh["disruption_reason"] or "None"),
                ],
            )]
            write_pdf(
                out_dir("pdf", "supplier_data") / f"{sh['shipment_id']}_receiving_report.pdf",
                "Shipment Receiving Report",
                f"Received from {supplier_name}  |  Source: VendorHub ERP — Receiving Module",
                sections,
            )
            count += 1
        if "png" in formats:
            write_packing_slip_png(
                out_dir("png", "supplier_data") / f"{sh['shipment_id']}_packing_slip.png",
                sh, supplier_name,
            )
            count += 1
    return count


def generate_promotion_briefs(promos: list, formats: set) -> int:
    if "pdf" not in formats:
        return 0
    count = 0
    for p in promos:
        write_pdf(
            out_dir("pdf", "promotions") / f"{p['EVENT_ID']}.pdf",
            "Promotional Event Brief",
            f"{p['EVENT_ID']}  |  Source: pricing calendar system",
            [(
                "Promotion", [
                    ("SKU", f"{p['SKU']} ({PRODUCT_NAMES[p['SKU']]})"),
                    ("Store", STORE_NAMES[p["STORE_ID"]]),
                    ("Start Date", fmt_date(p["START_DATE"])),
                    ("End Date", fmt_date(p["END_DATE"])),
                    ("Discount", f"{p['DISCOUNT_PCT']}%"),
                    ("Expected Uplift", f"{p['EXPECTED_UPLIFT_PCT']}%"),
                ],
            )],
        )
        count += 1
    return count


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate PDF/PNG agent inputs from dataset-seed 00_raw/.")
    parser.add_argument("--formats", default="pdf,png", help="Comma-separated output formats: pdf, png (default: pdf,png)")
    args = parser.parse_args()
    formats = {f.strip().lower() for f in args.formats.split(",") if f.strip()}
    if not formats <= {"pdf", "png"}:
        raise SystemExit("Supported formats: pdf, png")

    extract = load_extract()
    dates = extract["dates"]
    pos = parse_pos()
    suppliers = parse_supplier_master()
    shipments = parse_supplier_shipments()
    promos = parse_promo_calendar()
    inventory_rows = parse_inventory()

    print(f"Generating agent inputs -> {OUT.relative_to(BASE.parent)}/{{pdf,png}}")
    print(f"Formats: {', '.join(sorted(formats))}\n")

    counts = {
        "pos_transactions (weekly sales reports)": generate_pos_reports(pos, dates, formats),
        "inventory_snapshots (weekly status reports)": generate_inventory_reports(inventory_rows, formats),
        "supplier_data (supplier profiles)": generate_supplier_profiles(suppliers, formats),
        "supplier_data (shipment receiving reports + packing slips)": generate_shipment_documents(shipments, suppliers, formats),
        "promotions (event briefs)": generate_promotion_briefs(promos, formats),
    }
    for name, n in counts.items():
        print(f"  {name}: {n} files")
    print(f"\nDone - {sum(counts.values())} files written.")


if __name__ == "__main__":
    main()
