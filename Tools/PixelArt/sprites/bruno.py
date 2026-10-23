"""Bruno: charcoal tabby blacksmith, leather apron/gloves, steel hammer and compact base anvil.

Uses the existing cat poses and palette. Service character only; never a mining worker.
The four portrait expressions match Mia's dialogue/Store portrait contract.
"""
from pxl import Canvas, C
from sprites.cats import CAT_FRAMES, POSES, Parts, body_master, marking, paint, fur_map, PIVOT, KEY
from sprites.firstsession import mia_head
from sprites.tutorial import m_disc


def body(frame):
    p = POSES[frame]
    parts = Parts()
    master = body_master(p, parts)
    palette = fur_map('gray')
    palette.update(bo='wood0', b1='wood1', b2='wood2', b3='wood3', b4='wood5',
                   sh='cream2', shs='cream0', btn='gold3', chk='fur_gray2')
    cv = paint(marking(master, 'gray', p, parts), palette)
    # Separate leather gloves follow the paws in every animation pose.
    for x, y in parts.paws:
        if master.opaque(x, y):
            cv.set(x, y, 'wood5' if master.get(x, y)[:3] in (KEY['f4'], KEY['f3']) else 'wood3')
    tx, ty = p.torso
    # Apron bib, seam, brass rivets and a practical front pocket.
    for y in range(ty + 2, min(cv.h, ty + 6)):
        for x in range(tx + 4, tx + 11):
            if cv.opaque(x, y):
                cv.set(x, y, 'wood4' if x == tx + 4 else 'wood3')
    cv.line(tx + 5, ty + 5, tx + 9, ty + 5, 'wood1')
    for x in (tx + 4, tx + 10):
        cv.set(x, ty + 2, 'gold4')
    # Steel smith's hammer at the belt; it is part of Bruno, not team equipment.
    hx, hy = tx + 11, ty + 3
    cv.line(hx, hy, hx, hy + 5, 'wood1')
    cv.line(hx + 1, hy, hx + 1, hy + 5, 'wood5')
    cv.rect(hx - 2, hy - 2, 6, 3, 'stone1')
    cv.line(hx - 1, hy - 2, hx + 2, hy - 2, 'stone6')
    cv.line(hx - 1, hy - 1, hx + 2, hy - 1, 'stone4')
    return cv


def portrait(expr):
    cv = Canvas(40, 40)
    # A broad leather apron with shoulder straps; warm rivets and a steel hammer.
    cv.ellipse(20, 40, 18, 13, 'fur_gray1')
    cv.ellipse(19, 39, 15, 11, 'fur_gray3')
    cv.polygon([(10, 29), (15, 29), (16, 33), (25, 33), (26, 29), (30, 30), (32, 39), (8, 39)], 'wood1')
    cv.polygon([(12, 29), (15, 30), (16, 33), (24, 33), (27, 29), (29, 30), (30, 39), (10, 39)], 'wood3')
    cv.line(15, 34, 15, 39, 'wood5')
    cv.rect(18, 36, 8, 3, 'wood2')
    cv.line(18, 36, 25, 36, 'wood5')
    cv.set(15, 33, 'gold4')
    cv.set(26, 33, 'gold3')
    # Same softly shaped face as the existing portraits, recolored to a silver tabby.
    head = mia_head(expr)
    mapping = {C('fur_white0'): 'fur_gray0', C('fur_white1'): 'fur_gray1', C('fur_white2'): 'fur_gray2',
               C('fur_white3'): 'fur_gray3', C('fur_white4'): 'fur_gray4',
               C('fur_ginger1'): 'fur_gray1', C('fur_ginger2'): 'fur_gray2', C('fur_ginger3'): 'fur_gray3'}
    for y in range(head.h):
        for x in range(head.w):
            value = head.get(x, y)
            if value in mapping:
                head.set(x, y, mapping[value])
    cv.blit(head, 0, 0)
    # Cat ears and forehead sit above the portrait face; no miner helmet.
    cv.polygon([(9, 15), (8, 5), (16, 12)], 'fur_gray0')
    cv.polygon([(10, 13), (10, 7), (14, 12)], 'fur_gray3')
    cv.line(11, 9, 12, 12, 'pink1')
    cv.polygon([(25, 12), (32, 5), (31, 16)], 'fur_gray0')
    cv.polygon([(27, 12), (30, 8), (30, 14)], 'fur_gray2')
    cv.ellipse(20, 13, 9, 5, 'fur_gray3')
    for x in (17, 21, 25):
        cv.line(x, 10, x - 1, 13, 'fur_gray1')
    # Leather collar and hammer raised beside the apron, clear of the face.
    cv.line(31, 30, 31, 38, 'wood0')
    cv.line(32, 30, 32, 38, 'wood5')
    cv.rect(28, 27, 9, 5, 'stone1')
    cv.line(29, 27, 35, 27, 'stone7')
    cv.rect(29, 28, 7, 2, 'stone4')
    disc = m_disc()
    for y in range(40):
        for x in range(40):
            if (x, y) not in disc: cv.set(x, y, None)
    return cv


def icon():
    cv = Canvas(16, 16)
    rows = ['G...........G', 'GG.........GG', 'GpGGGGGGGGGpG', 'GGGGgGgGgGGGG',
            'GGeGGGGGGGeGG', 'GecGGGGGGGceG', 'GGGGGnnGGGGGG', '.GGGGqqGGGGG.', '..GGGGGGGGG..', '...LLLLLLL...']
    face = Canvas.from_ascii(rows, {'G': 'fur_gray3', 'g': 'fur_gray1', 'p': 'pink1', 'e': 'ink1', 'c': 'cream4',
                                    'n': 'pink2', 'q': 'ink2', 'L': 'wood3'})
    cv.blit(face, 1, 2)
    cv.outline('fur_gray0')
    cv.line(12, 10, 12, 15, 'wood4')
    cv.rect(10, 9, 5, 3, 'stone2')
    cv.line(11, 9, 14, 9, 'stone6')
    return cv


def anvil():
    cv = Canvas(34, 30)
    # Walnut stump, end grain, leather tool roll, blued-steel horn and polished edges.
    cv.ellipse(17, 25, 12, 4, 'wood0')
    cv.rect(5, 15, 24, 10, 'wood2')
    cv.ellipse(17, 15, 12, 4, 'wood5')
    cv.ellipse(17, 15, 9, 2, 'wood3')
    for x in (7, 11, 23, 27): cv.line(x, 18, x - 1, 24, 'wood1')
    cv.line(7, 22, 27, 22, 'stone1')
    cv.line(7, 23, 27, 23, 'stone4')
    cv.polygon([(7, 5), (28, 5), (28, 10), (22, 11), (21, 15), (26, 16), (26, 19), (10, 19), (10, 16),
                (14, 15), (14, 11), (8, 10), (2, 7)], 'stone0')
    cv.polygon([(7, 6), (27, 6), (27, 9), (21, 10), (19, 15), (24, 17), (12, 17), (16, 15), (15, 10), (8, 9), (3, 7)], 'stone3')
    cv.line(7, 5, 27, 5, 'stone7')
    cv.line(8, 6, 25, 6, 'stone5')
    cv.line(17, 11, 17, 14, 'stone5')
    cv.set(24, 7, 'stone0')
    cv.line(28, 17, 30, 24, 'wood0')
    cv.line(29, 17, 31, 24, 'wood5')
    cv.rect(26, 14, 7, 4, 'stone1')
    cv.line(27, 14, 32, 14, 'stone6')
    return cv


def build(reg):
    for frame in CAT_FRAMES:
        reg.add('cat_body_bruno_' + frame, body(frame), pivot=PIVOT, note='Bruno: leather apron, gloves and hammer; service only')
    for i, expr in enumerate(('smile', 'talk', 'blink', 'worried')):
        reg.add('bruno_portrait_' + str(i), portrait(expr))
    reg.add('icon_bruno', icon())
    reg.add('bruno_anvil', anvil(), pivot=(17, 2))
