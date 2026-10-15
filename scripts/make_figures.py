#!/usr/bin/env python3
"""
make_figures.py - generate the Chapter Three design diagrams for the Deutchezz
report from the actual implementation evidence.

The diagrams are genuine design models derived from the codebase and from the
project source of truth (docs/source-of-truth/SOURCE_OF_TRUTH.md): one backend
assembly with four namespace layers and enforced dependency directions, ten
controllers grouped by actor, fifteen domain entities with the reservation-line
/ physical-assignment split, and the transactional, idempotent booking flow.

Output (docs/report/figures/):
    fig3-1.png  system architecture (client-server, four layers, externals)
    fig3-2.png  backend namespaces and allowed dependency directions
    fig3-3.png  use case diagram (customer, receptionist, administrator)
    fig3-4.png  entity-relationship diagram (enforced vs logical links)
    fig3-5.png  activity diagram for the booking process
    fig3-6.png  sequence diagram for booking creation

These are design diagrams, not screenshots. Run:  python scripts/make_figures.py
"""

from __future__ import annotations

from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Circle, Ellipse, FancyBboxPatch, Polygon, Rectangle

FIG_DIR = Path(r"C:\Users\OYALE\Desktop\Project\Deutchezz_Hotel\docs\report\figures")

EDGE = "#33475b"
ENFORCED = "#b3261e"
LOGICAL = "#5f6b7a"
INK = "#1f2a37"
MUTED = "#6b7280"


# --------------------------------------------------------------------------- #
# primitives
# --------------------------------------------------------------------------- #
def newfig(w, h):
    fig, ax = plt.subplots(figsize=(w, h))
    ax.set_xlim(0, 100)
    ax.set_ylim(0, 100)
    ax.axis("off")
    return fig, ax


def save(fig, name):
    FIG_DIR.mkdir(parents=True, exist_ok=True)
    out = FIG_DIR / name
    fig.savefig(out, dpi=200, bbox_inches="tight", facecolor="white")
    plt.close(fig)
    print("wrote", out)


def box(ax, x, y, w, h, text, fc="#eef3fb", ec=EDGE, fontsize=9,
        bold=False, lw=1.2, style="round,pad=0.02,rounding_size=0.6",
        color=INK, align="center"):
    ax.add_patch(FancyBboxPatch(
        (x, y), w, h, boxstyle=style, linewidth=lw, edgecolor=ec,
        facecolor=fc, zorder=2))
    if text:
        va = {"center": "center", "top": "top"}[align]
        ty = y + h / 2 if align == "center" else y + h - 1.6
        ax.text(x + w / 2, ty, text, ha="center", va=va, fontsize=fontsize,
                zorder=3, color=color, fontweight="bold" if bold else "normal")


def ellipse(ax, cx, cy, w, h, text, fc="#e8f5e9", ec="#2e7d32", fontsize=7.4):
    ax.add_patch(Ellipse((cx, cy), w, h, fc=fc, ec=ec, lw=1.1, zorder=2))
    ax.text(cx, cy, text, ha="center", va="center", fontsize=fontsize,
            zorder=3, color=INK)


def line(ax, p1, p2, color=EDGE, lw=1.2, dashed=False, rad=0.0, zorder=1,
         arrow=True, arrowstyle="-|>"):
    ax.annotate("", xy=p2, xytext=p1,
                arrowprops=dict(arrowstyle=arrowstyle if arrow else "-",
                                color=color, lw=lw,
                                linestyle="--" if dashed else "-",
                                connectionstyle=f"arc3,rad={rad}",
                                shrinkA=0, shrinkB=0),
                zorder=zorder)


def label(ax, p, text, color=EDGE, fontsize=7, ha="center", va="center",
          weight="normal"):
    ax.text(*p, text, ha=ha, va=va, fontsize=fontsize, color=color, zorder=6,
            fontweight=weight,
            bbox=dict(fc="white", ec="none", pad=0.6, alpha=0.9))


def anchors(x, y, w, h):
    return {
        "top": (x + w / 2, y + h), "bottom": (x + w / 2, y),
        "left": (x, y + h / 2), "right": (x + w, y + h / 2),
        "tl": (x, y + h), "tr": (x + w, y + h),
        "bl": (x, y), "br": (x + w, y),
    }


# --------------------------------------------------------------------------- #
# figure 3.1 - system architecture
# --------------------------------------------------------------------------- #
def fig3_1():
    fig, ax = newfig(9.6, 7.0)
    ax.text(50, 98, "Deutchezz Hotel Management and Booking System",
            ha="center", fontsize=9.5, fontweight="bold", color=INK)
    ax.text(50, 94.5, "deployment view: two separately deployed applications",
            ha="center", fontsize=8, style="italic", color=MUTED)

    ax.text(50, 90.5, "Presentation tier  (browser)", ha="center", fontsize=8.5,
            style="italic", color=MUTED)
    box(ax, 3, 82, 28, 7.5, "Guest / Customer", fc="#eef3fb", fontsize=8.5)
    box(ax, 36, 82, 28, 7.5, "Receptionist", fc="#eef3fb", fontsize=8.5)
    box(ax, 69, 82, 28, 7.5, "Administrator", fc="#eef3fb", fontsize=8.5)
    box(ax, 18, 70, 64, 8, "React single-page application\n(TypeScript · Vite · Tailwind)",
        fc="#dbe7f6", bold=True, fontsize=8.5)
    for cx in (17, 50, 83):
        line(ax, (cx, 82), (cx, 78), lw=1.0, arrow=False)
    line(ax, (50, 70), (50, 64.5), lw=1.5)
    label(ax, (50, 67.2), "HTTPS / JSON   ·   /api/v1", fontsize=8)

    # application tier
    ax.add_patch(Rectangle((11, 30), 78, 33, fill=True, fc="#f7faff",
                           ec=EDGE, lw=1.4, zorder=1))
    ax.text(50, 60.5, "Application tier  -  ASP.NET Core Web API (.NET 9)",
            ha="center", fontsize=9, fontweight="bold", color=INK)
    layers = [
        (54.5, "Api layer   (controllers · routing · auth policies)", "#dbe7f6"),
        (48.7, "Application layer   (use-case services · validation)", "#dff0d8"),
        (42.9, "Domain layer   (entities · business rules · pricing)", "#fde9d9"),
        (37.1, "Infrastructure layer   (EF Core · Identity · JWT · mail · media)", "#ece3f5"),
    ]
    for y, t, c in layers:
        box(ax, 14, y, 72, 5.1, t, fc=c, fontsize=8)
    for y in (54.5, 48.7, 42.9):
        line(ax, (50, y), (50, y - 0.8), lw=0.8, arrow=False, color=MUTED)
    line(ax, (50, 37.1), (50, 30 - 1.5), lw=1.3)

    box(ax, 33, 20.5, 34, 7, "PostgreSQL database", fc="#e2e8f0", bold=True,
        fontsize=9)
    ax.text(50, 17, "persistent store (EF Core, Npgsql)", ha="center",
            fontsize=7.5, style="italic", color=MUTED)

    box(ax, 0.5, 37.1, 13, 9, "SMTP mail\nserver", fc="#eef3fb", fontsize=7.8)
    box(ax, 86.5, 37.1, 13, 9, "Cloudinary\nimage store", fc="#eef3fb",
        fontsize=7.8)
    line(ax, (13.5, 41.6), (14, 41.6), lw=1.0, arrow=False)
    line(ax, (86, 41.6), (86.5, 41.6), lw=1.0, arrow=False)
    ax.text(6.8, 34.5, "transactional\nemail", ha="center", fontsize=6.6,
            color=MUTED)
    ax.text(93.2, 34.5, "room images", ha="center", fontsize=6.6, color=MUTED)
    save(fig, "fig3-1.png")


# --------------------------------------------------------------------------- #
# figure 3.2 - backend namespaces and dependency direction
# --------------------------------------------------------------------------- #
def fig3_2():
    fig, ax = newfig(9.4, 6.2)
    ax.text(50, 96, "Backend namespaces and the dependency direction",
            ha="center", fontsize=11, fontweight="bold", color=INK)
    ax.text(50, 92, "the three architecture tests fail the build if an arrow is reversed",
            ha="center", fontsize=8.5, style="italic", color=MUTED)

    box(ax, 37, 77, 26, 11, "Api\nDeutchezz.Api", fc="#dbe7f6", bold=True)
    box(ax, 37, 57, 26, 11, "Application\nDeutchezz.Application", fc="#dff0d8",
        bold=True)
    box(ax, 37, 37, 26, 11, "Domain\nDeutchezz.Domain", fc="#fde9d9", bold=True)
    box(ax, 4, 57, 24, 11, "Infrastructure\nDeutchezz.Infrastructure",
        fc="#ece3f5", bold=True)

    line(ax, (50, 77), (50, 68), lw=1.4)
    label(ax, (58.5, 72.5), "may depend on", fontsize=8)
    line(ax, (50, 57), (50, 48), lw=1.4)
    label(ax, (58.5, 52.5), "may depend on", fontsize=8)
    line(ax, (30, 62.5), (37, 62.5), lw=1.4)
    label(ax, (33.5, 66), "implements\ninterfaces", fontsize=7.5)
    line(ax, (28, 59), (37, 42), lw=1.4, rad=-0.2)
    label(ax, (30, 50), "depends on", fontsize=8)

    box(ax, 4, 16, 92, 16,
        "Enforced by architecture tests (NetArchTest):\n"
        "1.  Domain depends on no outer layer.\n"
        "2.  Application does not depend on Infrastructure or Api.\n"
        "3.  Infrastructure does not depend on Api.",
        fc="#fdeaea", ec=ENFORCED, fontsize=8.6)
    ax.text(50, 8, "One production assembly; the layers are namespaces, not separate projects.",
            ha="center", fontsize=8, style="italic", color=MUTED)
    save(fig, "fig3-2.png")


# --------------------------------------------------------------------------- #
# figure 3.3 - use case diagram
# --------------------------------------------------------------------------- #
def fig3_3():
    fig, ax = newfig(11.0, 8.6)
    ax.add_patch(Rectangle((17, 5), 66, 88, fill=True, fc="#fbfdff",
                           ec=EDGE, lw=1.5, zorder=1))
    ax.text(50, 89.5, "Deutchezz Hotel Management and Booking System",
            ha="center", fontsize=10, fontweight="bold", color=INK)
    ax.text(50, 86.3, "system boundary", ha="center", fontsize=7.5,
            style="italic", color=MUTED)

    def actor(x, y, text, fc="#fff4d6", ec="#bf9000"):
        box(ax, x, y, 15, 9, text, fc=fc, ec=ec, bold=True, fontsize=8.2)

    actor(0, 57, "Guest /\nCustomer")
    actor(85, 62, "Receptionist")
    actor(85, 26, "Administrator")

    rows = [84, 74, 64, 54, 44, 34, 24, 14]
    left = {  # customer use cases (left column)
        84: "Register and verify account",
        74: "Sign in / sign out",
        64: "Search availability and view quote",
        54: "Create booking",
        44: "Look up or cancel own booking",
        34: "Manage own profile and notifications",
    }
    right = {  # reception and administration use cases (right column)
        84: "Search and view bookings",
        74: "Create walk-in booking",
        64: "Assign / reassign / unassign room",
        54: "Change booked room type",
        44: "Check in / check out",
        34: "Record payment",
        24: "Manage rooms, room types,\nstaff and settings",
        14: "View dashboard, reports\nand activity log",
    }
    cx_l, cx_r = 37, 63
    ew, eh = 27, 6.6
    for y, t in left.items():
        ellipse(ax, cx_l, y, ew, eh, t, fontsize=7.1)
    for y, t in right.items():
        ellipse(ax, cx_r, y, ew, eh, t, fontsize=7.1)

    # associations: customer -> left column
    for y in left:
        line(ax, (15, 61.5), (cx_l - ew / 2, y), lw=0.7, arrow=False,
             color=LOGICAL, zorder=0)
    # receptionist -> first six of right column
    for y in (84, 74, 64, 54, 44, 34):
        line(ax, (85, 66.5), (cx_r + ew / 2, y), lw=0.7, arrow=False,
             color=LOGICAL, zorder=0)
    # administrator -> lower right column
    for y in (24, 14):
        line(ax, (85, 30.5), (cx_r + ew / 2, y), lw=0.7, arrow=False,
             color=LOGICAL, zorder=0)
    # supporting actors
    ellipse(ax, 8, 24, 14, 7, "Identity\nprovider", fc="#eef3fb", ec=EDGE,
            fontsize=6.8)
    ellipse(ax, 8, 10, 14, 7, "Automated\nclock", fc="#eef3fb", ec=EDGE,
            fontsize=6.8)
    ax.text(8, 1.5, "supporting actors", ha="center", fontsize=6.6,
            style="italic", color=MUTED)
    save(fig, "fig3-3.png")


# --------------------------------------------------------------------------- #
# figure 3.4 - entity-relationship diagram
# --------------------------------------------------------------------------- #
def fig3_4():
    fig, ax = newfig(11.6, 9.2)
    ax.text(50, 98.5, "Entity-relationship model of the persistent store",
            ha="center", fontsize=11, fontweight="bold", color=INK)
    ax.text(50, 95.5, "fifteen entity types; the two link kinds are distinguished",
            ha="center", fontsize=8, style="italic", color=MUTED)

    ent = {}

    def entity(name, x, y, w, h, title, lines, fc="#f7faff"):
        box(ax, x, y, w, h, "", fc=fc, ec=EDGE, lw=1.1, style="square,pad=0")
        ax.add_patch(Rectangle((x, y + h - 3.0), w, 3.0, fc="#e7eef8",
                               ec="none", zorder=3))
        ax.plot([x, x + w], [y + h - 3.0, y + h - 3.0], color=EDGE, lw=1.0,
                zorder=4)
        ax.text(x + w / 2, y + h - 1.5, title, ha="center", va="center",
                fontsize=8.0, fontweight="bold", color=INK, zorder=5)
        ax.text(x + 1.0, y + h - 4.6, lines, ha="left", va="top", fontsize=6.2,
                color=INK, zorder=5)
        ent[name] = anchors(x, y, w, h)

    # identity / audit column
    entity("User", 1, 80, 26, 13, "User",
           "Id (PK)\nEmail (unique)\nFirstName, LastName\nPhoneNumber\nIsActive, Role")
    entity("PasswordResetCode", 1, 64, 26, 11, "PasswordResetCode",
           "Id (PK)\nUserId  (FK)\nCodeHash, ExpiresAt\nFailedAttempts")
    entity("PendingRegistration", 1, 50, 26, 9, "PendingRegistration",
           "Id (PK) · Email (unique)\nPasswordHash, CodeHash\nExpiresAt")
    entity("Notification", 1, 36, 26, 10, "Notification",
           "Id (PK)\nUserId? · GuestId?\nType, Body, IsRead")
    entity("ActivityLog", 1, 22, 26, 10, "ActivityLog",
           "Id (PK)\nUserId (logical)\nAction, EntityType, EntityId")

    # booking core column
    entity("Guest", 33, 84, 28, 9, "Guest",
           "Id (PK)\nFirstName, LastName\nEmail? · PhoneNumber\nUserId (logical)")
    entity("Booking", 33, 65, 28, 16, "Booking",
           "Id (PK) · BookingReference (unique)\nGuestId  (FK)\nCheckInAt, CheckOutAt\nCheckInDate, CheckOutDate · Nights\nStatus, PaymentStatus, Source\nTotalAmount, ExtraChargeAmount\nIdempotencyKey (unique)\nConcurrencyToken")
    entity("BookingRoomType", 33, 47, 28, 14, "BookingRoomType",
           "Id (PK)\nBookingId (logical)\nRoomTypeId (logical)\nQuantity, NumberOfNights\nRatePerNight, Subtotal\nExtraCharge, SurchargeTier")
    entity("RoomAssignment", 33, 29, 28, 14, "RoomAssignment",
           "Id (PK)\nBookingId (logical)\nBookingRoomTypeId (logical)\nRoomId (logical)\nAssignedAt, AssignedByUserId\nReleasedAt")
    entity("Payment", 33, 12, 28, 13, "Payment",
           "Id (PK)\nBookingId (logical)\nAmount, Method, Status\nReference, Notes\nRecordedByUserId, PaidAt")

    # inventory / settings column
    entity("RoomType", 67, 80, 32, 13, "RoomType",
           "Id (PK)\nName, Description, BasePrice\nMaximumOccupancy, BedType\nImageUrls, Features\nIsActive")
    entity("RoomTypeSurchargeTier", 67, 63, 32, 11, "RoomTypeSurchargeTier",
           "Id (PK)\nRoomTypeId  (FK)\nFromHours, ToHours\nAmount")
    entity("Room", 67, 47, 32, 12, "Room",
           "Id (PK) · RoomNumber (unique)\nRoomTypeId (logical)\nStatus, Floor, IsActive")
    entity("Setting", 67, 34, 32, 9, "Setting",
           "Id (PK) · Key (unique)\nValue")

    def rel(a, aa, b, ba, ca, cb, enforced=False, rad=0.0, lab_off=(0, 0)):
        p1, p2 = ent[a][aa], ent[b][ba]
        col = ENFORCED if enforced else LOGICAL
        line(ax, p1, p2, color=col, lw=1.6 if enforced else 1.1,
             dashed=not enforced, rad=rad, arrow=False)
        mx = (p1[0] + p2[0]) / 2 + lab_off[0]
        my = (p1[1] + p2[1]) / 2 + lab_off[1]
        label(ax, (mx, my), f"{ca}   {cb}", color=col, fontsize=6.8)

    rel("Booking", "top", "Guest", "bottom", "N", "1", enforced=True, lab_off=(5, 0))
    rel("User", "bottom", "PasswordResetCode", "top", "1", "N", enforced=True, lab_off=(7, 0))
    rel("RoomType", "bottom", "RoomTypeSurchargeTier", "top", "1", "N", enforced=True, lab_off=(9, 0))

    rel("Guest", "left", "User", "right", "N", "1", rad=-0.22, lab_off=(0, 3))
    rel("Booking", "bottom", "BookingRoomType", "top", "1", "N", lab_off=(4, 0))
    rel("BookingRoomType", "bottom", "RoomAssignment", "top", "1", "N", lab_off=(4, 0))
    rel("Booking", "bl", "RoomAssignment", "left", "1", "N", rad=-0.3, lab_off=(-2, 6))
    rel("Booking", "bl", "Payment", "left", "1", "N", rad=0.32, lab_off=(-9, 4))
    rel("RoomType", "left", "BookingRoomType", "right", "1", "N", rad=-0.18, lab_off=(0, 5))
    rel("RoomType", "bottom", "Room", "top", "1", "N", lab_off=(6, 2))
    rel("Room", "left", "RoomAssignment", "right", "1", "N", rad=0.22, lab_off=(0, 5))
    rel("User", "right", "Notification", "right", "1", "N", rad=-0.35, lab_off=(9, 0))
    rel("User", "right", "ActivityLog", "right", "1", "N", rad=-0.7, lab_off=(15, -6))

    ax.plot([34, 39], [7.5, 7.5], color=ENFORCED, lw=1.8)
    ax.text(40.5, 7.5, "database-enforced foreign key", fontsize=7.4, va="center",
            color=INK)
    ax.plot([34, 39], [4.2, 4.2], color=LOGICAL, lw=1.3, linestyle="--")
    ax.text(40.5, 4.2, "logical link (Guid column, no database constraint)",
            fontsize=7.4, va="center", color=INK)
    save(fig, "fig3-4.png")


# --------------------------------------------------------------------------- #
# figure 3.5 - activity diagram for the booking process
# --------------------------------------------------------------------------- #
def fig3_5():
    fig, ax = newfig(9.6, 10.6)
    ax.text(50, 98.5, "Activity diagram: online booking creation",
            ha="center", fontsize=11, fontweight="bold", color=INK)
    ax.text(50, 95.7, "actions, decisions and the transaction boundary",
            ha="center", fontsize=8, style="italic", color=MUTED)

    MAIN = 36          # x-centre of the main (happy-path) column
    RIGHT = 79         # x-centre of the early-exit / error column

    def action(cx, cy, text, w=38, h=6.0, fc="#e8f0fb"):
        box(ax, cx - w / 2, cy - h / 2, w, h, text, fc=fc, ec=EDGE,
            fontsize=7.6, lw=1.1)

    def decision(cx, cy, text, hw=10, hh=4.6):
        ax.add_patch(Polygon([[cx, cy + hh], [cx + hw, cy], [cx, cy - hh],
                              [cx - hw, cy]], closed=True, fc="#fff4d6",
                             ec="#bf9000", lw=1.1, zorder=2))
        ax.text(cx, cy, text, ha="center", va="center", fontsize=6.8,
                zorder=3, color=INK)

    def start(cx, cy):
        ax.add_patch(Circle((cx, cy), 1.9, fc=INK, ec=INK, zorder=3))

    def end(cx, cy):
        ax.add_patch(Circle((cx, cy), 2.6, fc="white", ec=INK, lw=1.6, zorder=3))
        ax.add_patch(Circle((cx, cy), 1.5, fc=INK, ec=INK, zorder=4))

    def arrow(p1, p2, t=None, toff=(2.4, 0)):
        line(ax, p1, p2, lw=1.1, arrow=True)
        if t:
            label(ax, ((p1[0] + p2[0]) / 2 + toff[0],
                       (p1[1] + p2[1]) / 2 + toff[1]), t, fontsize=6.8)

    def branch(cx, cy, tag, action_text, end_y):
        """Draw a right-hand early exit from a decision at (cx, cy)."""
        line(ax, (cx + 10, cy), (RIGHT, cy), lw=1.1)
        label(ax, ((cx + 10 + RIGHT) / 2, cy + 1.9), tag, fontsize=6.8)
        action(RIGHT, cy, action_text, w=36, h=6.2, fc="#fdeaea")
        line(ax, (RIGHT, cy - 3.1), (RIGHT, end_y + 2.6), lw=1.1)
        end(RIGHT, end_y)

    start(MAIN, 92)
    arrow((MAIN, 90.1), (MAIN, 87.4))
    action(MAIN, 84.2, "Receive booking request\n(Idempotency-Key header)")
    arrow((MAIN, 81.2), (MAIN, 78.6))
    decision(MAIN, 74, "key already\nseen?")
    branch(MAIN, 74, "yes", "Return the previously\ncreated booking", 66)

    arrow((MAIN, 69.4), (MAIN, 66.6))
    action(MAIN, 63.2, "Validate stay dates, guest\nand room-type requests")
    arrow((MAIN, 60.2), (MAIN, 57.6))
    decision(MAIN, 53, "valid?")
    branch(MAIN, 53, "no", "Return 400\nvalidation error", 45)

    arrow((MAIN, 48.4), (MAIN, 45.6))
    action(MAIN, 42.2, "Begin transaction;\nlock room-type rows", fc="#dff0d8")
    arrow((MAIN, 39.2), (MAIN, 36.6))
    action(MAIN, 33.2, "Recompute availability\nfor the stay", fc="#dff0d8")
    arrow((MAIN, 30.2), (MAIN, 27.6))
    decision(MAIN, 23, "rooms\nenough?")
    branch(MAIN, 23, "no", "Roll back; return\n409 conflict", 15)

    arrow((MAIN, 18.4), (MAIN, 15.6))
    action(MAIN, 12.2,
           "Create guest, booking and lines;\nsnapshot rates and surcharge tier",
           fc="#dff0d8")
    arrow((MAIN, 9.2), (MAIN, 6.6))
    action(MAIN, 3.6, "Commit transaction; send\nconfirmation email",
           fc="#dff0d8")

    ax.add_patch(Rectangle((MAIN - 24, 0.4), 24 + 21, 50.5, fill=False,
                           ec="#2e7d32", lw=1.3, linestyle="--", zorder=0))
    ax.text(MAIN - 25.5, 25, "transaction boundary", rotation=90, ha="center",
            va="center", fontsize=6.8, color="#2e7d32")
    save(fig, "fig3-5.png")


# --------------------------------------------------------------------------- #
# figure 3.6 - sequence diagram for booking creation
# --------------------------------------------------------------------------- #
def fig3_6():
    fig, ax = newfig(10.6, 8.0)
    ax.text(50, 98, "Sequence diagram: creating a booking",
            ha="center", fontsize=11, fontweight="bold", color=INK)
    ax.text(50, 94.8, "successful path, with the idempotency pre-check and the lock",
            ha="center", fontsize=8, style="italic", color=MUTED)

    parts = [
        (8, "Customer", "#eef3fb"),
        (25, "React SPA", "#dbe7f6"),
        (42, "Bookings\nController", "#dbe7f6"),
        (59, "Booking\nService", "#dff0d8"),
        (76, "Availability\nService", "#dff0d8"),
        (92, "PostgreSQL", "#e2e8f0"),
    ]
    top, bottom = 87, 10
    for x, name, fc in parts:
        box(ax, x - 7, top, 14, 7, name, fc=fc, fontsize=6.8, bold=True)
        line(ax, (x, top), (x, bottom), color=MUTED, lw=0.8, dashed=True,
             arrow=False)

    y = 81.0

    def msg(x1, x2, text, ret=False, dy=4.7):
        nonlocal y
        line(ax, (x1, y), (x2, y), lw=1.0, dashed=ret)
        label(ax, ((x1 + x2) / 2, y + 1.0), text, fontsize=6.3,
              ha="center", va="bottom")
        y -= dy

    msg(8, 25, "submit booking (dates, rooms, guest)")
    msg(25, 42, "POST /api/v1/bookings  (Idempotency-Key)")
    msg(42, 59, "CreateAsync(request)")
    msg(59, 92, "SELECT Booking WHERE IdempotencyKey = @key")
    msg(92, 59, "no existing booking", ret=True)
    msg(59, 76, "check availability for the stay")
    msg(76, 92, "lock room-type rows; count conflicts")
    msg(92, 76, "available quantity", ret=True)
    msg(76, 59, "sufficient", ret=True)
    msg(59, 92, "BEGIN; insert Guest, Booking and lines; COMMIT")
    msg(92, 59, "booking + reference (DHI-YYYY-XXXXXX)", ret=True)
    msg(59, 42, "BookingResponse (201 Created)", ret=True)
    msg(42, 25, "reference and stay details", ret=True)
    msg(25, 8, "confirmation shown to the guest", ret=True)

    ax.text(50, 3.8,
            "A replay returns the earlier booking from the idempotency pre-check; "
            "availability is re-checked under row locks inside the transaction.",
            ha="center", fontsize=6.9, color=MUTED)
    save(fig, "fig3-6.png")


def main():
    fig3_1(); fig3_2(); fig3_3(); fig3_4(); fig3_5(); fig3_6()


if __name__ == "__main__":
    main()
