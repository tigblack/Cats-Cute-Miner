"""62 baked collection scenes, 48×44. Native cat rig; no runtime tint or rotation.

39 factory cards (ТЗ 2.2 §7.2, Docs/TZ/Cat_Miners_Factories_TZ_v2.2.md): Common "Foreman" — the cat worker busy at
its workshop; Uncommon "Manager" — the same cat with finished goods, another pose and scene. Both cards of a factory
share fur and helmet (FAMILIES). No gems, loupes or appraiser scales. ca001 and the 21 global cards (global_scene)
are unchanged: Tools/PixelArt/review_cards.py --check compares their pixels with the pre-2.2 atlas.
Props are drawn at authoring time only; all final pixels are opaque RAMPS colours. Review sheets are separate.
"""
import math
from collections import namedtuple
from copy import deepcopy
from pxl import C, Canvas
from sprites import cats, cards
from sprites.structures import rock, ROCK

RARITIES = ('common', 'uncommon', 'rare', 'epic', 'legendary', 'majestic')
Factory = namedtuple('Factory', 'type common manager name good short fur helmet')
# JSON type, Foreman id, Manager id, factory, good, card name stem (ТЗ 2.2 appendix A), coat, helmet (kept from 2.1).
FAMILIES = [Factory(*f) for f in (
 ('amethyst', 2, 14, 'Dairy', 'Milk', 'Dairy', 'gray', 'purple'),
 ('citrine', 3, 15, 'Sawmill', 'Wood', 'Sawmill', 'ginger', 'yellow'),
 ('agate', 4, 16, 'Fishery', 'Fish', 'Fishery', 'brown', 'teal'),
 ('topaz', 5, 17, 'Yarn Mill', 'Yarn', 'Yarn Mill', 'black', 'yellow'),
 ('opal', 6, 18, 'Cat Food Factory', 'Cat Food', 'Cat Food', 'white', 'pink'),
 ('jade', 7, 19, 'Box Factory', 'Boxes', 'Box', 'cream', 'green'),
 ('onyx', 8, 20, 'Brickworks', 'Bricks', 'Brickworks', 'white', 'red'),
 ('sapphire', 9, 21, 'Steel Mill', 'Steel', 'Steel Mill', 'ginger', 'blue'),
 ('tourmaline', 10, 22, 'Glassworks', 'Glass', 'Glassworks', 'black', 'pink'),
 ('aquamarine', 11, 23, 'Catnip Farm', 'Catnip', 'Catnip', 'brown', 'white'),
 ('emerald', 12, 24, 'Toy Factory', 'Toys', 'Toy', 'gray', 'green'),
 ('diamond', 13, 25, 'Scratcher Works', 'Scratching Posts', 'Scratcher', 'black', 'white'),
 ('garnet', 39, 40, 'Cushion Factory', 'Cushions', 'Cushion', 'cream', 'red'),
 ('heliodor', 41, 42, 'Bell Foundry', 'Bells', 'Foundry', 'brown', 'green'),
 ('ruby', 44, 45, 'Collar Works', 'Collars', 'Collar', 'gray', 'red'),
 ('alexandrite', 46, 47, 'Gear Works', 'Gears', 'Gear', 'ginger', 'teal'),
 ('celestine', 48, 49, 'Lantern Works', 'Lanterns', 'Lantern', 'black', 'blue'),
 ('amber', 56, 57, 'Apiary', 'Honey', 'Apiary', 'white', 'yellow'),
 ('moonstone', 58, 59, 'Silk Mill', 'Silk', 'Silk Mill', 'gray', 'white'),
 ('sunstone', 60, None, 'Caviar Cannery', 'Caviar', 'Cannery', 'cream', 'yellow'),
)]
GLOBALS = {
 1:(1,'Depot Master'),26:(3,"Master's Yarn"),27:(3,'Courier Cat'),
 28:(3,'Fish Ration'),29:(3,'Pocket Watch'),30:(3,'Milk Bowl'),
 31:(3,'Lucky Paw'),32:(3,'Gate Bell'),33:(3,'Catnip'),
 34:(5,'Golden Cart'),35:(5,'Sledgehammer'),36:(5,'Hiring Poster'),
 37:(5,'Gold Vein'),38:(5,'Golden Helmet'),43:(3,'Toy Mouse'),
 50:(4,'Bell Collar'),51:(4,'Cat Bed'),52:(4,'Scratching Post'),
 53:(4,"Miner's Lantern"),54:(4,'Rubble Blaster'),55:(4,'Lucky Clover'),
 62:(6,'Treasure Map'),63:(3,'Demolition Expert'),
}
CATALOG = dict(GLOBALS)
for _f in FAMILIES:
    CATALOG[_f.common] = (1, _f.short + ' Foreman')
    if _f.manager:
        CATALOG[_f.manager] = (2, _f.short + ' Manager')
# Arts that must stay pixel-identical to the pre-2.2 atlas (ТЗ 2.2 §7.2).
UNCHANGED = tuple(sorted(i for i in GLOBALS if i != 63))
FACTORY_IDS = tuple(sorted(i for f in FAMILIES for i in (f.common, f.manager) if i))


def layer(cv, draw, outline):
    a = Canvas(48,44)
    draw(a)
    a.outline(outline)
    cv.blit(a,0,0)


def sparkle(cv,x,y,col='gold5',r=2):
    cv.hline(x-r,x+r,y,col)
    cv.vline(x,y-r,y+r,col)
    cv.set(x+1,y+1,'gold2' if col.startswith('gold') else 'blue4')


def floor(cv,variant=0):
    cv.ellipse(24,40,22,3,'navy1')
    cv.hline(3,44,41,'slate3')
    cv.hline(7+variant%5,17+variant%5,42,'slate4')
    for x in (5,37,43):
        cv.rect(x,39-(x%3),2,1,'slate5')


def box(cv,x,y,w=13,h=11):
    cv.rect(x,y,w,h,'wood0'); cv.rect(x+1,y+1,w-2,h-2,'wood4')
    cv.hline(x+1,x+w-2,y+1,'wood6')
    cv.vline(x+w-2,y+2,y+h-2,'wood2')
    cv.line(x+2,y+h-3,x+w-3,y+2,'wood2')
    cv.line(x+2,y+h-4,x+w-3,y+1,'wood5')
    for xx in (x+2,x+w-3):
        cv.set(xx,y+2,'gold3');cv.set(xx,y+h-3,'gold2')


def bench(cv,y=35):
    cv.rect(2,y,44,3,'wood0');cv.hline(3,44,y,'wood6');cv.hline(3,44,y+1,'wood4')
    for x in (5,40): cv.rect(x,y+3,3,6,'wood2');cv.vline(x,y+3,43,'wood5')


def cat(cv,fr='idle0',fur='ginger',helmet='yellow',at=(-4,2),tool=False,pack=None,flip=False):
    p=deepcopy(cats.POSES[fr]);parts=cats.Parts()
    b=cats.body_master(p,parts)
    out=cats.paint(cats.marking(b,fur,p,parts),cats.fur_map(fur))
    if pack:
        out.blit(cats.pack_layer(p,pack,cats.mask_of(b),parts),0,0)
    if helmet:
        out.blit(cats.paint(cats.helmet_master(p),cats.helmet_map(helmet)),0,0)
    if tool:
        behind=parts.head if p.pick_behind_head else set()
        out.blit(cats.paint(cats.pick_master(p,parts.paws,behind,scale=1.3,thick=3.3),cats.PICK_IRON),0,0)
    if flip: out=out.flipped()
    cv.blit(out,*at)
    return p


def prop(cv,key,x,y,size=None):
    """Reuse legacy material/detail design; crop padding before composing."""
    a=getattr(cards,'item_'+key)();bbox=a.img.getbbox()
    a=a.crop(bbox[0],bbox[1],bbox[2]-bbox[0],bbox[3]-bbox[1])
    if size:
        from PIL import Image
        k=min(size[0]/a.w,size[1]/a.h)
        im=a.img.resize((max(1,round(a.w*k)),max(1,round(a.h*k))),Image.Resampling.NEAREST)
        a=Canvas(*im.size);a.img=im;a.px=im.load()
    cv.blit(a,x,y)


# ───────────────────────── factory cards (ТЗ 2.2 §7.2) ─────────────────────────
# Foreman (Common): the cat worker busy at its workshop. Manager (Uncommon): the same cat (fur and helmet of the pair)
# with finished goods, in another pose and scene. The goods stay secondary: never alone and large in the centre, so
# the scenes never read like the item cards ca026, ca028, ca030, ca032, ca033, ca043, ca050–ca053.

def pose(name, **kw):
    """Rig frame with overrides (paws, arms, eyes…); arms behind the head follow the arm kind, as in cats.Pose."""
    p = deepcopy(cats.POSES[name])
    for k, v in kw.items():
        setattr(p, k, v)
    p.arms_behind_head = p.arms in ('up', 'back')
    return p


class Worker:
    """Cat on the cats.py rig at scene offset `at`. The paws are kept as a separate layer: a held tool is drawn
    between the body and the gripping paws."""

    def __init__(self, fam, p, at=(-5, 1), pack=None):
        self.p, self.at = p, at
        parts = cats.Parts()
        master = cats.body_master(p, parts)
        self.body = cats.paint(cats.marking(master, fam.fur, p, parts), cats.fur_map(fam.fur))
        self.paws = Canvas(cats.CW, cats.CH)
        for x, y in parts.paws:
            if self.body.opaque(x, y):
                self.paws.set(x, y, self.body.get(x, y))
        if pack:
            self.body.blit(cats.pack_layer(p, pack, cats.mask_of(master), parts), 0, 0)
        self.body.blit(cats.paint(cats.helmet_master(p), cats.helmet_map(fam.helmet)), 0, 0)

    def xy(self, x, y):
        """Rig point → scene point."""
        return x + self.at[0], y + self.at[1]

    def draw(self, cv):
        cv.blit(self.body, *self.at)

    def grip(self, cv):
        cv.blit(self.paws, *self.at)


def lay(cv, outline, draw):
    """Prop on its own layer, outlined by the darkest tone of its material."""
    layer(cv, draw, outline)


def orb(a, cx, cy, rx, ry, tones):
    """Rounded volume: shadow, base, light, highlight (light from the top left)."""
    dark, base, light, hi = tones
    for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
        for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
            dx, dy = (x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry
            if dx * dx + dy * dy > 1.0:
                continue
            lum = -0.6 * dx - 0.8 * dy
            a.set(x, y, hi if lum > 0.75 else light if lum > 0.3 else dark if lum < -0.45 else base)


def slab(a, x, y, w, h, tones):
    """Box face with a light top-left edge and a dark bottom-right edge: tones = (dark, base, light)."""
    dark, base, light = tones
    a.rect(x, y, w, h, base)
    a.hline(x, x + w - 1, y, light)
    a.vline(x, y, y + h - 1, light)
    a.hline(x + 1, x + w - 1, y + h - 1, dark)
    a.vline(x + w - 1, y + 1, y + h - 1, dark)


def table(cv, x0, x1, y, legs=None):
    """Workshop bench top with plank edge and legs to the floor."""
    def d(a):
        a.rect(x0, y, x1 - x0 + 1, 3, 'wood4')
        a.hline(x0, x1, y, 'wood6')
        a.hline(x0, x1, y + 2, 'wood2')
        for x in legs or (x0 + 2, x1 - 3):
            a.rect(x, y + 3, 2, 39 - y - 2, 'wood2')
            a.vline(x, y + 3, 39, 'wood3')
    lay(cv, 'wood0', d)


def dust(cv, pts, col):
    for x, y in pts:
        cv.set(x, y, col)


def fish(a, x, y, left=True, tones=('navy4', 'navy6', 'navy8', 'cream3')):
    """Small fish 9×5 around (x, y): silver back, light belly, forked tail."""
    dark, base, light, belly = tones
    a.ellipse(x, y, 3.6, 1.7, base)
    a.hline(x - 2, x + 1, y - 1, light)
    a.hline(x - 1, x + 2, y + 1, belly)
    d = 1 if left else -1
    tx = x + d * 4
    a.set(tx, y, dark)
    a.set(tx + d, y - 1, base)
    a.set(tx + d, y + 1, base)
    a.set(tx + d, y, dark)
    a.set(x - d * 2, y, 'ink1')


def crate(cv, x, y, w, h):
    """Slatted wooden crate (lighter than box(): goods crates)."""
    def d(a):
        slab(a, x, y, w, h, ('wood3', 'wood5', 'wood6'))
        for yy in range(y + 3, y + h - 1, 3):
            a.hline(x + 1, x + w - 2, yy, 'wood3')
        a.vline(x + 2, y + 1, y + h - 2, 'wood4')
        a.vline(x + w - 3, y + 1, y + h - 2, 'wood4')
    lay(cv, 'wood1', d)


# ── 1 Dairy (Milk) ──
def churn(a, x, y, h=11):
    """Steel milk churn 7 px wide: lid, neck, shoulder, band; side handles."""
    a.rect(x + 1, y, 5, 1, 'fur_white3')
    a.rect(x + 2, y + 1, 3, 2, 'fur_white1')
    a.rect(x, y + 3, 7, h - 3, 'fur_white2')
    a.vline(x + 1, y + 3, y + h - 1, 'fur_white4')
    a.vline(x + 2, y + 4, y + h - 2, 'fur_white3')
    a.vline(x + 5, y + 3, y + h - 1, 'fur_white1')
    a.vline(x + 6, y + 3, y + h - 1, 'fur_white0')
    a.hline(x, x + 6, y + 3, 'fur_white3')
    a.hline(x, x + 6, y + 6, 'fur_gray2')
    a.set(x - 1, y + 4, 'fur_gray2')
    a.set(x + 7, y + 4, 'fur_gray2')


def barn(cv):
    def d(a):
        a.polygon([(27, 17), (38, 5), (49, 17)], 'red2')
        a.polygon([(28.5, 16), (38, 6.5), (47.5, 16)], 'red3')
        a.rect(30, 16, 18, 24, 'red3')
        for x in range(32, 48, 3):
            a.vline(x, 17, 38, 'red2')
        a.hline(30, 47, 16, 'cream3')
        a.line(27, 17, 38, 5, 'cream3')
        a.line(38, 5, 48, 16, 'cream2')
        a.rect(36, 9, 5, 4, 'ink2')
        a.hline(36, 40, 12, 'gold4')
        a.set(37, 11, 'gold3')
        a.frame(35, 8, 7, 6, 'cream3')
        a.rect(34, 24, 10, 16, 'wood1')
        a.frame(34, 24, 10, 16, 'cream3')
        a.line(35, 25, 42, 38, 'cream2')
        a.line(42, 25, 35, 38, 'cream2')
        a.vline(38, 25, 38, 'cream1')
    lay(cv, 'red0', d)
    for x, h in ((29, 3), (31, 2), (45, 3), (47, 2)):
        cv.vline(x, 40 - h, 39, 'green4')
        cv.set(x, 40 - h, 'green6')


def dairy_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 0)
    barn(cv)
    p = cats.base(0, lfoot=(1, 0), rfoot=(-1, -1), arms='reach', paws=((25, 29), (29, 30)), eyes='look_r', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    lay(cv, 'fur_gray0', lambda a: (churn(a, 20, 31, 9), a.line(20, 30, 23, 28, 'fur_gray3'),
                                     a.line(23, 28, 26, 30, 'fur_gray3')))
    for x, y in ((22, 27), (24, 25), (21, 25)):
        cv.set(x, y, 'cream4')
    w.grip(cv)
    return cv


def clipboard(a, x, y, w=8, h=10, ticks=3):
    a.rect(x, y, w, h, 'wood3')
    a.rect(x + 1, y + 2, w - 2, h - 3, 'cream3')
    a.rect(x + 2, y, w - 4, 2, 'gold3')
    a.hline(x + 2, x + w - 3, y, 'gold5')
    for k in range(ticks):
        yy = y + 4 + k * 2
        a.hline(x + 2, x + 3, yy, 'green3')
        a.hline(x + 4, x + w - 3, yy, 'navy4')


def dairy_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 1)
    # Back row of churns behind the front row: a full dairy store.
    lay(cv, 'fur_gray0', lambda a: [churn(a, x, 23, 10) for x in (31, 39)])
    lay(cv, 'fur_gray0', lambda a: [churn(a, x, 29, 11) for x in (27, 35, 43)])
    cv.hline(28, 32, 29, 'cream4')
    p = cats.base(0, arms='hold', arms_dy=0, paws=((18, 29), (24, 28)), eyes='look_r', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    lay(cv, 'wood1', lambda a: clipboard(a, 13, 26, 9, 10))
    w.grip(cv)
    return cv


# ── 2 Sawmill (Wood) ──
def log(a, x0, x1, y, r=3):
    """Horizontal log: rounded bark (light top, dark belly) with knots, light end grain with rings at the right end."""
    a.rect(x0, y, x1 - x0 + 1, 2 * r + 1, 'wood3')
    a.hline(x0, x1, y, 'wood5')
    a.hline(x0, x1, y + 1, 'wood4')
    a.hline(x0, x1, y + 2 * r - 1, 'wood2')
    a.hline(x0, x1, y + 2 * r, 'wood1')
    for x in range(x0 + 2, x1 - 2, 5):
        a.set(x, y + 2, 'wood2')
        a.set(x + 1, y + 2, 'wood5')
        a.set(x + 3, y + 4, 'wood2')
    a.ellipse(x1 + 0.5, y + r + 0.5, 1.8, r + 0.5, 'wood6')
    a.ellipse(x1 + 0.5, y + r + 0.5, 0.9, r - 0.8, 'wood5')
    a.set(x1, y + r, 'wood4')


def blade(a, cx, cy, r):
    for k in range(14):
        t = 2 * math.pi * k / 14
        a.set(round(cx + math.cos(t) * (r + 0.6)), round(cy + math.sin(t) * (r + 0.6)), 'stone6')
    a.ellipse(cx + 0.5, cy + 0.5, r, r, 'stone6')
    a.ellipse(cx, cy, r - 1.5, r - 1.5, 'stone7')
    a.ellipse(cx + 0.5, cy + 0.5, r - 3, r - 3, 'stone5')
    a.ellipse(cx + 0.5, cy + 0.5, 1.5, 1.5, 'navy2')
    a.set(int(cx), int(cy), 'stone7')


def sawmill_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 2)
    lay(cv, 'stone1', lambda a: blade(a, 38, 27, 7))
    table(cv, 22, 47, 32, legs=(25, 43))
    p = cats.base(0, arms='strike', paws=((23, 25), (26, 27)), eyes='focus', mouth='grit', head=(9, 6))
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    lay(cv, 'wood0', lambda a: log(a, 19, 33, 25, 3))
    dust(cv, ((40, 18), (43, 20), (45, 17), (42, 15), (46, 22), (39, 16)), 'wood6')
    dust(cv, ((44, 19), (41, 21), (47, 19)), 'wood7')
    w.grip(cv)
    return cv


def planks(cv, x0, x1, y0, rows):
    def d(a):
        for k in range(rows):
            y = y0 + k * 4
            a.rect(x0 + (k % 2), y, x1 - x0 + 1 - (k % 2), 3, 'wood5')
            a.hline(x0 + (k % 2), x1, y, 'wood6')
            a.hline(x0 + (k % 2), x1, y + 2, 'wood3')
            a.rect(x0 + (k % 2), y, 2, 3, 'wood6')
            a.set(x0 + (k % 2), y + 1, 'wood4')
            if k < rows - 1:
                for x in (x0 + 4, x1 - 4):
                    a.set(x, y + 3, 'wood1')
                    a.set(x + 1, y + 3, 'wood1')
    lay(cv, 'wood1', d)


def sawmill_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 3)
    planks(cv, 27, 47, 19, 5)
    p = cats.base(0, arms='hold', arms_dy=0, paws=((19, 29), (24, 27)), eyes='look_r', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    def tape(a):
        a.rect(15, 26, 7, 7, 'gold3')
        a.hline(15, 21, 26, 'gold5')
        a.vline(15, 26, 32, 'gold4')
        a.hline(16, 21, 32, 'gold2')
        a.ellipse(18.5, 29.5, 1.5, 1.5, 'stone5')
        a.set(18, 29, 'stone7')
        a.hline(22, 28, 30, 'gold5')
        a.hline(22, 28, 31, 'gold4')
        for x in range(23, 29, 2):
            a.set(x, 31, 'ink2')
    lay(cv, 'gold0', tape)
    w.grip(cv)
    cv.set(40, 18, 'cream4')
    return cv


# ── 3 Fishery (Fish) ──
def water(cv, x0):
    def d(a):
        a.polygon([(x0, 35), (48, 33), (48, 44), (x0 - 2, 44)], 'blue3')
        a.polygon([(x0 + 1, 36), (48, 34.5), (48, 37), (x0, 38)], 'blue4')
        for x, y in ((x0 + 3, 40), (x0 + 9, 42), (x0 + 13, 39), (x0 + 6, 37)):
            a.hline(x, x + 3, y, 'blue5')
    lay(cv, 'blue1', d)


NAVY3 = C('navy3')[:3]


def net(cv, x0, y0, x1, y1, fishes):
    def d(a):
        a.polygon([(x0 + 2, y0), (x1 - 1, y0 + 1), (x1, y1 - 4), (x1 - 4, y1), (x0 + 3, y1), (x0, y1 - 5)], 'navy3')
        for fx, fy, left in fishes:
            fish(a, fx, fy, left, ('blue2', 'blue4', 'blue6', 'cream3'))
        for k in range(-20, 30, 4):
            for t in range(0, 30):
                x, y = x0 + k + t, y0 + t
                if a.opaque(x, y) and a.get(x, y)[:3] == NAVY3:
                    a.set(x, y, 'cream1')
                x, y = x0 + k + t, y1 - t
                if a.opaque(x, y) and a.get(x, y)[:3] == NAVY3:
                    a.set(x, y, 'cream1')
    lay(cv, 'navy1', d)


def fishery_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 4)
    water(cv, 30)
    net(cv, 29, 21, 46, 38, ((33, 26, True), (39, 25, False), (36, 30, True), (42, 31, True), (34, 34, False)))
    p = pose('tool_recoil', head=(7, 6), torso=(13, 25), paws=((22, 24), (25, 26)), eyes='focus', mouth='grit',
             tail='low', tail_at=(5, 26))
    w = Worker(fam, p, at=(-5, 1))
    w.draw(cv)
    cv.line(19, 26, 30, 22, 'wood4')
    cv.line(19, 27, 30, 23, 'wood2')
    for x, y in ((33, 19), (40, 18), (45, 20), (28, 31)):
        cv.set(x, y, 'blue7')
    cv.set(37, 17, 'cream4')
    w.grip(cv)
    return cv


def hanging_fish(a, x, y, h=8):
    """Fish hanging by the tail from a hook: tail at the top, head down."""
    a.set(x - 1, y, 'blue4')
    a.set(x + 1, y, 'blue4')
    a.set(x, y + 1, 'blue3')
    a.ellipse(x + .5, y + 2 + h / 2, 2.2, h / 2, 'blue4')
    a.vline(x - 1, y + 3, y + h, 'blue6')
    a.vline(x + 1, y + 4, y + h, 'cream3')
    a.set(x, y + h, 'ink1')


def fishery_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 5)
    lay(cv, 'navy1', lambda a: [fish(a, x, 23, left, ('blue2', 'blue4', 'blue6', 'cream3'))
                                for x, left in ((36, True), (41, False))])
    crate(cv, 33, 24, 13, 7)
    lay(cv, 'navy1', lambda a: [fish(a, x, 31, left, ('blue2', 'blue4', 'blue6', 'cream3'))
                                for x, left in ((33, False), (39, True), (44, False))])
    crate(cv, 29, 32, 18, 8)
    p = cats.base(0, arms='strike', arms_dy=-3, paws=((23, 21), (26, 23)), eyes='look_r', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    def scale(a):
        a.vline(23, 15, 17, 'stone6')
        a.rect(22, 18, 4, 8, 'gold3')
        a.vline(22, 18, 25, 'gold5')
        a.vline(25, 19, 25, 'gold2')
        a.rect(23, 20, 2, 4, 'cream3')
        a.hline(23, 24, 22, 'red3')
        a.vline(24, 26, 27, 'stone6')
        hanging_fish(a, 24, 28, 8)
    lay(cv, 'navy1', scale)
    w.grip(cv)
    return cv


# ── 4 Yarn Mill (Yarn) ──
def spinning_wheel(cv):
    def d(a):
        a.line(38, 22, 33, 39, 'wood2')
        a.line(38, 22, 44, 39, 'wood2')
        a.line(29, 30, 44, 30, 'wood3')
        a.rect(28, 25, 4, 3, 'wood4')
        a.set(28, 25, 'wood6')
        for k in range(8):
            t = math.pi * k / 4
            a.line(38, 20, round(38 + math.cos(t) * 7), round(20 + math.sin(t) * 7), 'wood4')
        for k in range(64):
            t = 2 * math.pi * k / 64
            for rr in (7.5, 8.3):
                rim = 'wood5' if math.sin(t + .8) < 0 else 'wood3'
                a.set(round(38 + math.cos(t) * rr), round(20 + math.sin(t) * rr), rim)
        a.ellipse(38.5, 20.5, 1.5, 1.5, 'gold3')
    lay(cv, 'wood0', d)
    cv.line(31, 25, 38, 12, 'cream2')
    cv.line(31, 27, 38, 28, 'cream2')


def yarn_ball(cv, x, y, r=3, tones=('pink1', 'pink2', 'pink3', 'pink4')):
    def d(a):
        orb(a, x, y, r, r, tones)
        a.line(x - r + 1, y + 1, x + 1, y - r + 1, tones[0])
        a.line(x - 1, y + r - 1, x + r - 1, y - 1, tones[0])
    lay(cv, 'pink0', d)


def yarnmill_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 6)
    spinning_wheel(cv)
    yarn_ball(cv, 28.5, 37.5, 3)
    p = cats.base(0, arms='strike', arms_dy=-1, paws=((23, 24), (26, 26)), eyes='focus', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    lay(cv, 'cream1', lambda a: (orb(a, 21, 26, 2.5, 2, ('cream2', 'cream3', 'cream4', 'white'))))
    cv.line(23, 26, 29, 26, 'pink3')
    cv.line(29, 28, 28, 34, 'pink3')
    w.grip(cv)
    return cv


SPOOL_TONES = [('pink1', 'pink2', 'pink3'), ('purple3', 'purple4', 'purple5'), ('teal2', 'teal4', 'teal5'),
               ('gold2', 'gold3', 'gold4'), ('red2', 'red3', 'red4'), ('blue3', 'blue4', 'blue5'),
               ('green3', 'green4', 'green5'), ('pink2', 'pink3', 'pink4'), ('amber1', 'amber2', 'amber3')]


def spool(a, x, y, tones, h=6):
    """Cone of yarn on a wooden core 4 px wide."""
    dark, base, light = tones
    a.hline(x, x + 3, y, 'wood6')
    a.rect(x, y + 1, 4, h - 2, base)
    a.vline(x, y + 1, y + h - 2, light)
    a.vline(x + 3, y + 1, y + h - 2, dark)
    a.set(x + 1, y + 2, light)
    a.hline(x, x + 3, y + h - 1, 'wood4')


def shelf(cv, x0, x1, y0, ys):
    def d(a):
        for x in (x0, x1 - 1):
            a.rect(x, y0, 2, 40 - y0, 'wood2')
            a.vline(x, y0, 39, 'wood4')
        for y in ys:
            a.rect(x0, y, x1 - x0 + 1, 2, 'wood4')
            a.hline(x0, x1, y, 'wood6')
    lay(cv, 'wood0', d)


def yarnmill_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 7)
    shelf(cv, 29, 47, 5, (15, 26, 37))
    lay(cv, 'wood0', lambda a: [spool(a, 31 + c * 5, 9 + r * 11, SPOOL_TONES[(r * 3 + c) % 9])
                                for r in range(3) for c in range(3)])
    p = cats.base(0, arms='hold', arms_dy=0, paws=((17, 29), (23, 29)), eyes='happy', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    lay(cv, 'wood0', lambda a: spool(a, 15, 25, SPOOL_TONES[0], 8))
    w.grip(cv)
    return cv


# ── 5 Cat Food Factory (Cat Food) ──
def sack(a, x, y, w, h, open_top=False):
    """Cat food bag in the goods colour (amber) with a paw print, as goods_catfood."""
    a.polygon([(x + 1, y), (x + w - 2, y), (x + w, y + h), (x - 1, y + h)], 'amber2')
    a.vline(x, y + 1, y + h - 1, 'amber3')
    a.line(x + w - 2, y + 1, x + w - 1, y + h - 1, 'amber1')
    a.hline(x, x + w - 1, y + h - 1, 'amber1')
    if open_top:
        a.ellipse(x + w / 2, y + 1, w / 2 - 1, 1.6, 'wood1')
        for k in range(0, w - 3, 2):
            a.set(x + 2 + k, y, 'wood4')
            a.set(x + 3 + k, y + 1, 'wood3')
    px, py = x + w // 2 - 1, y + h // 2
    a.rect(px, py + 1, 3, 2, 'cream3')
    for dx in (-1, 1, 3):
        a.set(px + dx, py, 'cream3')


def catfood_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 8)
    lay(cv, 'amber0', lambda a: (sack(a, 39, 22, 9, 10), sack(a, 37, 31, 10, 9)))
    lay(cv, 'amber0', lambda a: sack(a, 27, 29, 11, 11, True))
    p = cats.base(0, arms='reach', arms_dy=-1, paws=((25, 27), (28, 28)), eyes='focus', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    def scoop(a):
        a.rect(17, 27, 5, 2, 'wood3')
        a.polygon([(22, 24), (29, 23), (32, 27), (23, 29)], 'stone6')
        a.line(23, 24, 29, 23, 'stone7')
        a.line(24, 28, 31, 27, 'stone4')
        a.hline(25, 29, 24, 'amber3')
    lay(cv, 'navy1', scoop)
    dust(cv, ((32, 27), (33, 28), (32, 29), (33, 30)), 'amber3')
    dust(cv, ((31, 28), (34, 29)), 'wood3')
    w.grip(cv)
    return cv


def tin(a, x, y, w=5, h=6, label='amber2', lid='stone7'):
    a.rect(x, y, w, h, 'stone6')
    a.hline(x, x + w - 1, y, lid)
    a.rect(x, y + 2, w, h - 3, label)
    a.vline(x, y + 1, y + h - 1, 'stone7')
    a.vline(x + w - 1, y + 1, y + h - 1, 'stone4')
    a.set(x + w // 2, y + 3, 'blue5')


def conveyor(cv, x0, x1, y):
    def d(a):
        a.rect(x0, y, x1 - x0 + 1, 4, 'navy3')
        a.hline(x0, x1, y, 'stone4')
        a.hline(x0, x1, y + 3, 'navy2')
        for x in range(x0 + 2, x1, 5):
            a.ellipse(x + 0.5, y + 2, 1.2, 1.2, 'stone5')
            a.set(x, y + 1, 'stone7')
        for x in (x0 + 2, x1 - 3):
            a.rect(x, y + 4, 2, 39 - y - 3, 'navy3')
            a.vline(x, y + 4, 39, 'navy5')
    lay(cv, 'navy1', d)


def catfood_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 9)
    conveyor(cv, 25, 47, 31)
    lay(cv, 'navy1', lambda a: [tin(a, x, 25) for x in (30, 37, 44)])
    def lamp(a):
        a.vline(45, 11, 22, 'stone4')
        a.ellipse(45.5, 9.5, 2.5, 2.5, 'green5')
        a.set(44, 8, 'green6')
    lay(cv, 'navy1', lamp)
    p = cats.base(0, head=(8, 6), arms='strike', arms_dy=-3, paws=((24, 21), (26, 23)), eyes='focus', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    lay(cv, 'navy1', lambda a: tin(a, 21, 16, 5, 6))
    w.grip(cv)
    return cv


# ── 6 Box Factory (Boxes) ──
CARD = ('fur_cream1', 'fur_cream2', 'fur_cream3')


def carton(a, x, y, w, h, top=3, tape=True, stamp=False):
    """Closed cardboard box in 3/4: top face, front face, tape."""
    a.polygon([(x + 1, y), (x + w, y), (x + w - 1, y + top), (x, y + top)], 'fur_cream3')
    slab(a, x, y + top, w, h - top, CARD)
    if tape:
        a.line(x + w // 2 + 1, y, x + w // 2, y + top, 'cream2')
        a.vline(x + w // 2, y + top, y + top + 2, 'cream2')
    if stamp:
        sx, sy = x + 2, y + top + 2
        a.rect(sx, sy + 1, 3, 2, 'red3')
        a.set(sx, sy, 'red3')
        a.set(sx + 2, sy, 'red3')


def boxes_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 10)
    table(cv, 22, 47, 33, legs=(25, 44))
    def open_box(a):
        a.polygon([(26, 25), (24, 18), (28, 19), (29, 25)], 'fur_cream3')
        a.polygon([(40, 25), (43, 18), (46, 20), (43, 25)], 'fur_cream2')
        a.polygon([(29, 25), (41, 25), (39, 22), (31, 22)], 'fur_cream1')
        slab(a, 27, 25, 15, 8, CARD)
        a.hline(28, 40, 25, 'fur_cream0')
        a.hline(32, 37, 28, 'cream2')
    lay(cv, 'fur_cream0', open_box)
    lay(cv, 'gold0', lambda a: (a.ellipse(45.5, 30.5, 2.5, 2.5, 'cream2'), a.ellipse(45.5, 30.5, 1, 1, 'gold1')))
    p = cats.base(0, arms='reach', arms_dy=-3, paws=((26, 24), (29, 25)), eyes='focus', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    w.grip(cv)
    return cv


def boxes_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 11)
    lay(cv, 'fur_cream0', lambda a: (carton(a, 29, 27, 18, 13, 3, stamp=True), carton(a, 31, 17, 14, 10, 3, stamp=True),
                                     carton(a, 33, 9, 10, 8, 2)))
    p = cats.base(0, arms='strike', arms_dy=-1, paws=((24, 23), (26, 25)), eyes='focus', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    def stamp(a):
        a.ellipse(19, 23.5, 1.5, 1.5, 'wood5')
        a.rect(19, 23, 5, 2, 'wood3')
        a.rect(24, 21, 2, 6, 'wood2')
        a.rect(26, 21, 2, 6, 'red3')
        a.vline(26, 21, 26, 'red4')
    lay(cv, 'wood0', stamp)
    w.grip(cv)
    return cv


# ── 7 Brickworks (Bricks) ──
def bricks(a, x, y, cols, rows, w=4):
    for r in range(rows):
        off = (r % 2) * (w // 2)
        for c in range(cols):
            bx = x + c * (w + 1) + off
            a.rect(bx, y + r * 3, w, 2, 'red3')
            a.hline(bx, bx + w - 1, y + r * 3, 'red4')
            a.set(bx + w - 1, y + r * 3 + 1, 'red2')


def brickworks_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 12)
    def barrow(a):
        a.rect(25, 30, 2, 2, 'wood2')
        a.line(21, 30, 30, 32, 'wood3')
        a.line(26, 33, 27, 39, 'wood2')
        a.polygon([(28, 27), (44, 27), (41, 35), (31, 35)], 'stone4')
        a.hline(28, 44, 27, 'stone6')
        a.line(31, 35, 41, 35, 'stone2')
        a.line(41, 35, 44, 27, 'stone3')
    lay(cv, 'navy1', barrow)
    lay(cv, 'red0', lambda a: (bricks(a, 30, 21, 3, 2), a.rect(30, 25, 13, 2, 'red3'), a.hline(30, 42, 25, 'red4')))
    lay(cv, 'ink1', lambda a: (orb(a, 42.5, 37.5, 3, 3, ('ink3', 'navy2', 'navy3', 'navy4')),
                                a.ellipse(42.5, 37.5, 1, 1, 'stone6')))
    p = cats.base(1, head=(9, 7), lfoot=(1, 0), rfoot=(-1, -1), arms='strike', arms_dy=1,
                  paws=((23, 27), (26, 29)), eyes='focus', mouth='grit')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    w.grip(cv)
    return cv


def kiln(cv):
    def d(a):
        a.polygon([(27, 40), (27, 18), (31, 11), (37, 8), (43, 11), (47, 18), (47, 40)], 'red2')
        for y in range(10, 40, 3):
            off = (y // 3) % 2 * 2
            for x in range(27 + off, 48, 5):
                a.set(x, y, 'red1')
            a.hline(27, 47, y + 2, 'red1')
        a.rect(43, 1, 4, 10, 'red2')
        a.hline(43, 46, 1, 'red3')
        a.polygon([(31, 40), (31, 28), (34, 24), (39, 24), (42, 28), (42, 40)], 'ink2')
        a.polygon([(32, 40), (32, 29), (34, 26), (39, 26), (41, 29), (41, 40)], 'amber1')
        a.polygon([(33, 40), (33, 31), (35, 28), (38, 28), (40, 31), (40, 40)], 'amber2')
        a.rect(34, 33, 6, 7, 'amber3')
        a.rect(35, 36, 4, 4, 'amber4')
        bricks(a, 33, 34, 2, 2, 3)
        a.hline(28, 31, 18, 'red3')
        a.line(28, 18, 31, 12, 'red3')
    lay(cv, 'red0', d)
    for x, y, r in ((42, -1, 2), (38, 0, 1.5)):
        cv.ellipse(x, y, r, r, 'stone5')


def brickworks_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 13)
    kiln(cv)
    lay(cv, 'red0', lambda a: bricks(a, 21, 36, 2, 2))
    p = cats.base(0, arms='hold', arms_dy=-1, paws=((18, 26), (23, 26)), eyes='happy', mouth='w')
    w = Worker(fam, p, at=(-7, 1))
    w.draw(cv)
    lay(cv, 'red0', lambda a: (a.rect(12, 26, 8, 3, 'red3'), a.hline(12, 19, 26, 'red4'), a.vline(19, 27, 28, 'red2')))
    w.grip(cv)
    return cv


# ── 8 Steel Mill (Steel) ──
def goggles(cv, w):
    hx, hy = w.xy(*w.p.head)
    cv.rect(hx + 3, hy + 11, 19, 3, 'navy1')
    for ex in (hx + 7, hx + 14):
        cv.rect(ex, hy + 11, 5, 3, 'teal2')
        cv.set(ex + 1, hy + 11, 'teal5')
        cv.set(ex, hy + 12, 'teal4')
    cv.hline(hx + 3, hx + 21, hy + 14, 'ink1')


def ladle(cv):
    def d(a):
        a.vline(39, 0, 9, 'stone4')
        for y in range(0, 9, 3):
            a.set(38, y + 1, 'stone6')
            a.set(40, y + 2, 'stone3')
        a.line(32, 17, 39, 9, 'stone3')
        a.line(46, 17, 39, 9, 'stone3')
        a.polygon([(30, 17), (48, 17), (46, 33), (32, 33)], 'navy3')
        a.vline(31, 18, 31, 'navy5')
        a.vline(32, 19, 31, 'navy4')
        a.line(46, 18, 45, 32, 'navy2')
        a.hline(32, 46, 32, 'navy2')
        a.rect(30, 21, 18, 2, 'navy4')
        a.hline(31, 46, 21, 'navy6')
        a.ellipse(39, 18, 8.5, 2, 'amber3')
        a.ellipse(38, 17.6, 6, 1.2, 'gold5')
        a.hline(29, 30, 18, 'navy3')
    lay(cv, 'ink1', d)
    dust(cv, ((34, 13), (43, 12), (37, 11), (46, 14), (31, 15)), 'gold5')
    dust(cv, ((41, 14), (35, 15), (44, 10)), 'amber4')


def steel_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 14)
    ladle(cv)
    p = cats.base(0, arms='strike', arms_dy=-1, paws=((22, 24), (25, 26)), eyes='focus', mouth='grit')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    goggles(cv, w)
    cv.line(16, 30, 33, 18, 'navy4')
    cv.line(17, 30, 33, 19, 'navy2')
    cv.set(33, 18, 'amber4')
    cv.set(34, 18, 'gold5')
    w.grip(cv)
    return cv


def ingot(a, x, y, w=8):
    a.polygon([(x + 1, y), (x + w - 1, y), (x + w, y + 3), (x, y + 3)], 'stone6')
    a.hline(x + 1, x + w - 1, y, 'stone7')
    a.set(x + 2, y + 1, 'cream4')
    a.hline(x, x + w, y + 3, 'stone4')


def steel_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 15)
    def pile(a):
        for k in range(2):
            ingot(a, 28 + k * 10, 36)
        for k in range(2):
            ingot(a, 33 + k * 7, 32, 6)
        ingot(a, 30, 28)
        ingot(a, 39, 28)
        ingot(a, 34, 24)
    lay(cv, 'navy1', pile)
    p = cats.base(0, arms='hold', arms_dy=-1, paws=((18, 25), (23, 25)), eyes='happy', mouth='w')
    w = Worker(fam, p, at=(-7, 1))
    w.draw(cv)
    lay(cv, 'navy1', lambda a: ingot(a, 11, 23, 10))
    w.grip(cv)
    sparkle(cv, 40, 19, 'cream4', 1)
    return cv


# ── 9 Glassworks (Glass) ──
def furnace(cv):
    def d(a):
        a.rect(33, 4, 15, 22, 'stone3')
        for y in range(5, 26, 3):
            a.hline(33, 47, y, 'stone2')
            for x in range(34 + (y // 3) % 2 * 2, 48, 4):
                a.set(x, y + 1, 'stone2')
        a.ellipse(41, 13, 5, 5, 'amber1')
        a.ellipse(41, 13.5, 4, 4, 'amber3')
        a.ellipse(41, 14, 2.5, 2.5, 'gold5')
        a.rect(35, 18, 12, 8, 'stone3')
        a.hline(33, 47, 18, 'stone5')
    lay(cv, 'stone1', d)


def glass_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 16)
    furnace(cv)
    p = cats.base(0, arms='strike', arms_dy=-2, paws=((22, 23), (25, 25)), eyes='blink', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    cv.line(16, 23, 34, 28, 'stone6')
    cv.line(16, 24, 34, 29, 'navy2')
    lay(cv, 'amber0', lambda a: orb(a, 37, 29, 3.5, 3, ('amber2', 'amber3', 'amber4', 'gold5')))
    w.grip(cv)
    dust(cv, ((41, 26), (40, 33), (43, 30)), 'amber4')
    return cv


def bottle(a, x, y, h, tones):
    dark, base, light = tones
    a.rect(x, y + 4, 4, h - 4, base)
    a.vline(x, y + 4, y + h - 1, light)
    a.vline(x + 3, y + 4, y + h - 1, dark)
    a.rect(x + 1, y + 1, 2, 3, base)
    a.rect(x + 1, y, 2, 1, 'wood5')
    a.set(x + 1, y + 5, 'cream4')


def glass_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 17)
    lay(cv, 'teal1', lambda a: (bottle(a, 37, 29, 11, ('teal3', 'teal4', 'teal5')),
                                 bottle(a, 42, 25, 15, ('green3', 'green4', 'green5')),
                                 bottle(a, 33, 32, 8, ('blue3', 'blue4', 'blue6'))))
    p = cats.base(0, arms='hold', arms_dy=0, paws=((19, 29), (24, 26)), eyes='happy', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    def pane(a):
        a.rect(22, 11, 9, 28, 'blue6')
        a.vline(22, 11, 38, 'blue7')
        a.hline(22, 30, 11, 'blue7')
        a.line(24, 20, 28, 14, 'cream4')
        a.line(24, 24, 29, 17, 'cream4')
        a.line(25, 33, 29, 27, 'blue7')
    lay(cv, 'blue3', pane)
    w.grip(cv)
    return cv


# ── 10 Catnip Farm (Catnip) ──
def bed(cv, x0, x1, y):
    def d(a):
        a.rect(x0, y, x1 - x0 + 1, 39 - y, 'wood3')
        a.hline(x0, x1, y, 'wood5')
        a.hline(x0, x1, y + 3, 'wood2')
        a.rect(x0 + 1, y - 2, x1 - x0 - 1, 2, 'wood1')
    lay(cv, 'wood0', d)


def mint(cv, x, y, h=10):
    def d(a):
        a.vline(x, y, y + h, 'green3')
        for k, (dx, dy) in enumerate(((-2, 1), (2, 3), (-2, 5), (2, 7), (0, -1))):
            if dy > h:
                continue
            a.ellipse(x + dx + .5, y + dy + .5, 1.8, 1.2, 'green5' if k % 2 else 'green4')
            a.set(x + dx, y + dy, 'green6')
    lay(cv, 'green1', d)


def catnip_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 18)
    bed(cv, 28, 47, 33)
    for x, y, h in ((31, 23, 8), (37, 20, 11), (43, 22, 9)):
        mint(cv, x, y, h)
    p = cats.base(0, arms='strike', arms_dy=-2, paws=((22, 23), (25, 25)), eyes='happy', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    def can(a):
        a.rect(17, 22, 8, 7, 'blue4')
        a.hline(17, 24, 22, 'blue5')
        a.vline(17, 22, 28, 'blue5')
        a.vline(24, 23, 28, 'blue3')
        a.line(24, 25, 30, 20, 'blue4')
        a.rect(30, 18, 2, 3, 'blue5')
        a.line(18, 21, 21, 19, 'blue3')
        a.line(21, 19, 23, 21, 'blue3')
    lay(cv, 'blue1', can)
    dust(cv, ((33, 20), (34, 22), (32, 23), (35, 24), (33, 25), (36, 21)), 'blue6')
    dust(cv, ((34, 19), (32, 21)), 'blue7')
    w.grip(cv)
    return cv


def greenhouse(cv):
    def d(a):
        a.polygon([(30, 40), (30, 14), (34, 7), (39, 4), (44, 7), (48, 14), (48, 40)], 'blue6')
        for x in (30, 36, 42, 47):
            a.vline(x, 9, 39, 'cream2')
        for y in (14, 24):
            a.hline(30, 47, y, 'cream2')
        a.line(30, 14, 34, 7, 'cream2')
        a.line(34, 7, 39, 4, 'cream2')
        a.line(39, 4, 44, 7, 'cream2')
        a.line(44, 7, 48, 14, 'cream2')
        a.line(32, 20, 34, 16, 'cream4')
        a.line(38, 20, 40, 16, 'cream4')
        for x in (33, 39, 44):
            a.ellipse(x, 33, 2.5, 3, 'green4')
            a.set(x - 1, 31, 'green6')
        a.rect(30, 36, 18, 4, 'wood3')
    lay(cv, 'navy2', d)


def catnip_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 19)
    greenhouse(cv)
    p = cats.base(0, arms='hold', arms_dy=-1, paws=((18, 27), (25, 27)), eyes='happy', mouth='w')
    w = Worker(fam, p, at=(-7, 1))
    w.draw(cv)
    def tray(a):
        a.rect(10, 28, 18, 4, 'wood4')
        a.hline(10, 27, 28, 'wood6')
        a.hline(11, 26, 29, 'wood1')
        for x in range(12, 27, 3):
            a.vline(x, 25, 28, 'green4')
            a.set(x - 1, 25, 'green6')
            a.set(x + 1, 26, 'green5')
    lay(cv, 'wood0', tray)
    w.grip(cv)
    return cv


# ── 11 Toy Factory (Toys) ──
def mouse_toy(a, x, y, tones=('purple3', 'purple4', 'purple5', 'purple6')):
    orb(a, x, y, 3.5, 2.2, tones)
    a.set(x - 2, y - 2, 'pink3')
    a.set(x - 3, y - 2, 'pink2')
    a.set(x - 3, y, 'ink1')
    a.line(x + 3, y + 1, x + 6, y - 1, 'pink2')


def toy_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 20)
    table(cv, 22, 47, 33, legs=(25, 44))
    def machine(a):
        a.rect(27, 30, 19, 3, 'navy3')
        a.hline(27, 45, 30, 'navy5')
        a.rect(40, 17, 5, 13, 'navy2')
        a.rect(29, 16, 16, 5, 'navy2')
        a.rect(29, 21, 4, 4, 'navy2')
        a.hline(29, 44, 16, 'navy4')
        a.vline(40, 21, 29, 'navy4')
        a.hline(31, 38, 18, 'gold4')
        a.set(35, 19, 'gold3')
        a.vline(42, 22, 27, 'gold4')
        a.vline(30, 25, 27, 'stone7')
        a.rect(35, 13, 3, 3, 'pink3')
        a.hline(35, 37, 12, 'wood5')
        a.vline(36, 11, 15, 'wood5')
    lay(cv, 'ink1', machine)
    lay(cv, 'stone1', lambda a: (a.ellipse(46, 19.5, 2, 3, 'stone5'), a.set(46, 19, 'stone7')))
    cv.line(35, 14, 31, 21, 'pink3')
    lay(cv, 'purple1', lambda a: mouse_toy(a, 31, 29))
    p = cats.base(0, arms='strike', arms_dy=0, paws=((24, 26), (27, 28)), eyes='focus', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    w.grip(cv)
    return cv


def toy_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 21)
    shelf(cv, 29, 47, 6, (16, 28))
    def toys(a):
        mouse_toy(a, 34, 13)
        orb(a, 42.5, 12.5, 3, 3, ('red2', 'red3', 'red4', 'red5'))
        a.hline(40, 45, 12, 'cream3')
        fish(a, 34, 25, True, ('blue3', 'blue4', 'blue6', 'blue7'))
        a.line(41, 26, 45, 19, 'wood4')
        a.ellipse(45.5, 18, 1.5, 2, 'pink3')
        a.ellipse(44, 17, 1, 1.5, 'gold4')
        orb(a, 37, 37, 3, 2.5, ('teal2', 'teal3', 'teal4', 'teal5'))
        mouse_toy(a, 44, 37, ('fur_gray1', 'fur_gray2', 'fur_gray3', 'fur_gray4'))
    lay(cv, 'ink1', toys)
    p = pose('joy3', eyes='happy', mouth='open')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    w.grip(cv)
    return cv


# ── 12 Scratcher Works (Scratching Posts) ──
def rope(a, x0, x1, y0, y1):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            a.set(x, y, 'cream1' if (x + y * 2) % 4 < 2 else 'cream2')
        a.set(x0, y, 'cream2')
        a.set(x1, y, 'cream0')


def scratcher_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 22)
    def post(a):
        a.rect(27, 37, 20, 3, 'wood4')
        a.hline(27, 46, 37, 'wood6')
        a.rect(33, 7, 6, 30, 'wood5')
        a.vline(33, 7, 36, 'wood6')
        a.vline(38, 7, 36, 'wood3')
        for y in range(9, 22, 4):
            a.set(35, y, 'wood4')
        rope(a, 32, 39, 22, 36)
    lay(cv, 'wood0', post)
    def rope_spool(a):
        a.ellipse(44, 32, 3, 4, 'cream1')
        a.hline(41, 47, 28, 'wood3')
        a.hline(41, 47, 36, 'wood3')
        a.line(42, 31, 46, 31, 'cream0')
        a.line(42, 33, 46, 33, 'cream0')
    lay(cv, 'wood0', rope_spool)
    p = cats.base(0, arms='strike', arms_dy=-2, paws=((23, 23), (26, 25)), eyes='focus', mouth='grit')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    cv.line(21, 25, 32, 22, 'cream2')
    cv.line(21, 26, 32, 23, 'cream0')
    w.grip(cv)
    return cv


def mini_post(a, x, h, top_tones=('fur_cream1', 'fur_cream2', 'fur_cream3')):
    """Finished scratching post 9 px wide: base, rope column, top pad."""
    a.rect(x, 37, 9, 3, 'wood4')
    a.hline(x, x + 8, 37, 'wood6')
    rope(a, x + 3, x + 5, 39 - h, 36)
    slab(a, x, 37 - h - 2, 9, 2, top_tones)


def scratcher_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 23)
    shelf(cv, 28, 47, 4, (14,))
    lay(cv, 'wood0', lambda a: [slab(a, x, 8, 5, 6, ('fur_cream1', 'fur_cream2', 'fur_cream3')) for x in (30, 36, 42)])
    def posts(a):
        mini_post(a, 26, 13)
        mini_post(a, 37, 18, ('red2', 'red3', 'red4'))
    lay(cv, 'wood0', posts)
    lay(cv, 'red1', lambda a: (a.vline(33, 24, 27, 'cream1'),
                                orb(a, 33.5, 29, 1.6, 1.6, ('red2', 'red3', 'red4', 'red5'))))
    p = pose('joy4', eyes='happy', mouth='w')
    w = Worker(fam, p, at=(-7, 1))
    w.draw(cv)
    w.grip(cv)
    return cv


# ── 13 Cushion Factory (Cushions) ──
def cushion(a, x, y, w, h, tones):
    dark, base, light = tones
    a.ellipse(x + w / 2, y + h / 2, w / 2, h / 2, base)
    a.rect(x + 1, y + 1, w - 2, h - 2, base)
    a.hline(x + 2, x + w - 3, y + 1, light)
    a.hline(x + 2, x + w - 3, y + h - 2, dark)
    for k in range(1, 3):
        a.set(x + k * w // 3, y + h // 2, dark)


def stuffing(a, pts):
    for x, y, r in pts:
        a.ellipse(x, y, r, r * 0.85, 'cream3')
        a.ellipse(x - .5, y - .5, r - 1, r * 0.85 - 1, 'cream4')


def pillow(a, x0, y0, x1, y1, tones):
    """Square cushion: pinched corners, puffed faces, centre button, light from the top left."""
    dark, base, light, hi = tones
    h, w = y1 - y0, x1 - x0
    for y in range(y0, y1 + 1):
        iy = round(1.2 * math.sin(math.pi * (y - y0) / h))
        for x in range(x0 + iy, x1 - iy + 1):
            ix = round(1.2 * math.sin(math.pi * (x - x0) / w))
            if y0 + ix <= y <= y1 - ix:
                u, v = (x - x0) / w, (y - y0) / h
                a.set(x, y, light if u + v < 0.55 else dark if u + v > 1.45 else base)
    a.set((x0 + x1) // 2, (y0 + y1) // 2, dark)
    a.set((x0 + x1) // 2 - 1, (y0 + y1) // 2 - 1, hi)
    for x, y in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):
        a.set(x, y, 'gold4')


def cushion_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 24)
    def basket(a):
        a.polygon([(37, 28), (48, 28), (47, 40), (38, 40)], 'wood4')
        for y in range(30, 40, 2):
            a.hline(38, 47, y, 'wood3')
        for x in range(40, 47, 3):
            a.vline(x, 29, 39, 'wood5')
        stuffing(a, ((40, 27, 2.5), (44, 26, 3), (47, 27, 2)))
    lay(cv, 'wood1', basket)
    lay(cv, 'pink0', lambda a: (pillow(a, 25, 28, 37, 39, ('pink1', 'pink2', 'pink3', 'pink4')),
                                 stuffing(a, ((26, 29, 2.5), (29, 28, 2)))))
    p = cats.base(0, arms='reach', paws=((25, 27), (28, 28)), eyes='focus', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    w.grip(cv)
    for x, y in ((33, 22), (37, 20), (30, 19)):
        cv.set(x, y, 'cream4')
    return cv


def cushion_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 25)
    lay(cv, 'purple1', lambda a: pillow(a, 34, 33, 46, 39, ('purple3', 'purple4', 'purple5', 'purple6')))
    lay(cv, 'pink0', lambda a: pillow(a, 35, 27, 45, 32, ('pink1', 'pink2', 'pink3', 'pink4')))
    lay(cv, 'gold0', lambda a: pillow(a, 36, 21, 44, 26, ('gold2', 'gold3', 'gold4', 'gold5')))
    lay(cv, 'red1', lambda a: (cushion(a, 2, 33, 30, 7, ('red2', 'red3', 'red4')),
                                cushion(a, 4, 27, 26, 7, ('pink2', 'pink3', 'pink4'))))
    p = cats.base(0, arms='hold', paws=((18, 29), (24, 27)), eyes='happy', mouth='w')
    w = Worker(fam, p, at=(-3, -7))
    w.draw(cv)
    w.grip(cv)
    return cv


# ── 14 Bell Foundry (Bells) ──
def bell(a, x, y, h, tones=('gold2', 'gold3', 'gold4', 'gold5')):
    dark, base, light, hi = tones
    w = h
    a.polygon([(x - w * 0.3, y + 1), (x + w * 0.3, y + 1), (x + w * 0.45, y + h - 2), (x + w * 0.55, y + h),
               (x - w * 0.55, y + h), (x - w * 0.45, y + h - 2)], base)
    a.ellipse(x, y + 1.5, w * 0.3, 1.5, base)
    a.line(int(x - w * 0.25), y + 2, int(x - w * 0.38), y + h - 2, light)
    a.set(int(x - w * 0.2), y + 2, hi)
    a.hline(int(x - w * 0.5), int(x + w * 0.5), y + h, dark)
    a.set(int(x), y - 1, dark)


def foundry_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 26)
    def mold(a):
        a.rect(29, 31, 18, 9, 'wood3')
        a.rect(30, 32, 16, 3, 'fur_cream1')
        a.hline(29, 46, 31, 'wood5')
        a.ellipse(35, 32, 2, 1, 'amber3')
        a.set(35, 32, 'gold5')
        a.hline(29, 46, 35, 'wood1')
        a.vline(29, 32, 39, 'wood5')
    lay(cv, 'wood0', mold)
    lay(cv, 'gold0', lambda a: bell(a, 42, 23, 7))
    p = cats.base(0, arms='strike', arms_dy=-2, paws=((22, 23), (25, 25)), eyes='focus', mouth='grit')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    def crucible(a):
        a.line(14, 26, 25, 23, 'navy5')
        a.line(14, 27, 25, 24, 'navy3')
        a.polygon([(25, 20), (33, 22), (32, 28), (26, 27)], 'stone3')
        a.line(26, 23, 26, 26, 'stone5')
        a.line(25, 20, 33, 22, 'amber4')
        a.hline(27, 31, 21, 'gold5')
    lay(cv, 'ink1', crucible)
    cv.vline(34, 23, 30, 'amber4')
    cv.vline(35, 24, 30, 'gold5')
    dust(cv, ((37, 27), (32, 30), (38, 30), (36, 21)), 'gold5')
    w.grip(cv)
    return cv


def dial_scale(cv):
    def d(a):
        a.rect(28, 36, 19, 3, 'stone4')
        a.hline(28, 46, 36, 'stone6')
        a.rect(44, 18, 2, 18, 'stone4')
        a.ellipse(44.5, 15.5, 4, 4, 'stone5')
        a.ellipse(44.5, 15.5, 3, 3, 'cream3')
        a.line(45, 15, 46, 13, 'red3')
        a.set(44, 15, 'ink2')
    lay(cv, 'navy1', d)


def foundry_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 27)
    dial_scale(cv)
    lay(cv, 'gold0', lambda a: bell(a, 35, 27, 9))
    p = cats.base(0, arms='hold', arms_dy=-1, paws=((18, 27), (23, 27)), eyes='happy', mouth='open')
    w = Worker(fam, p, at=(-7, 1))
    w.draw(cv)
    lay(cv, 'gold0', lambda a: bell(a, 16, 25, 6))
    for x, y in ((21, 24), (22, 26), (10, 25)):
        cv.set(x, y, 'cream3')
    w.grip(cv)
    return cv


# ── 15 Collar Works (Collars) ──
def collar(a, x, y, r, tones):
    dark, base, light = tones
    a.ellipse(x, y, r, r * 0.6, base)
    a.ellipse(x, y, r - 1.5, r * 0.6 - 1.2, None)
    a.set(int(x - r + 1), int(y - 1), light)
    a.rect(int(x) - 1, int(y + r * 0.6) - 1, 2, 2, 'gold4')


def collar_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 28)
    table(cv, 22, 47, 33, legs=(25, 44))
    def hide(a):
        a.polygon([(29, 33), (31, 29), (37, 30), (41, 28), (46, 29), (48, 33)], 'red3')
        a.hline(31, 46, 31, 'red4')
        a.line(37, 30, 46, 29, 'red4')
        a.polygon([(24, 33), (26, 31), (31, 31), (30, 33)], 'red3')
    lay(cv, 'red1', hide)
    for x in range(33, 47, 2):
        cv.set(x, 32, 'cream2')
    lay(cv, 'wood0', lambda a: (a.rect(38, 25, 9, 2, 'wood5'), a.hline(38, 46, 25, 'wood6'),
                                 [a.set(x, 26, 'wood2') for x in range(39, 46, 2)]))
    p = cats.base(0, arms='strike', arms_dy=0, paws=((21, 24), (23, 29)), eyes='focus', mouth='grit')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    def shears(a):
        a.line(26, 28, 36, 24, 'stone7')
        a.line(26, 29, 36, 25, 'stone5')
        a.line(26, 30, 36, 33, 'stone6')
        a.line(26, 31, 35, 33, 'stone4')
        a.rect(25, 29, 2, 2, 'navy2')
        for y in (26, 32):
            a.ellipse(21.5, y, 2.5, 2, 'red3')
            a.ellipse(21.5, y, 1, 0.8, None)
            a.set(20, y - 1, 'red4')
        a.line(23, 27, 25, 29, 'red3')
        a.line(23, 31, 25, 30, 'red3')
    lay(cv, 'navy1', shears)
    w.grip(cv)
    return cv


def collar_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 29)
    def stand(a):
        a.rect(35, 7, 3, 31, 'wood3')
        a.vline(35, 7, 37, 'wood5')
        a.rect(29, 37, 15, 3, 'wood3')
        a.hline(29, 43, 37, 'wood5')
        for y in (12, 21, 30):
            a.hline(30, 43, y, 'wood4')
    lay(cv, 'wood0', stand)
    def collars(a):
        for (x, y, t) in ((32, 15, ('red2', 'red4', 'red5')), (41, 15, ('blue3', 'blue5', 'blue6')),
                          (32, 24, ('green3', 'green5', 'green6')), (41, 24, ('purple3', 'purple4', 'purple6')),
                          (41, 33, ('teal2', 'teal4', 'teal5'))):
            collar(a, x, y, 3.5, t)
    lay(cv, 'ink1', collars)
    p = cats.base(0, arms='hold', arms_dy=-1, paws=((17, 27), (24, 27)), eyes='happy', mouth='w')
    w = Worker(fam, p, at=(-8, 1))
    w.draw(cv)
    lay(cv, 'red1', lambda a: collar(a, 15.5, 29, 4.5, ('red2', 'red4', 'red5')))
    w.grip(cv)
    return cv


# ── 16 Gear Works (Gears) ──
def gear(cv, cx, cy, r, teeth, tones, outline):
    dark, base, light, hi = tones
    a = Canvas(48, 44)
    a.ellipse(cx, cy, r - 0.5, r - 0.5, base)
    for k in range(teeth):
        t = 2 * math.pi * (k + 0.5) / teeth
        for rr in (r - 0.2, r + 0.9):
            x, y = cx + math.cos(t) * rr, cy + math.sin(t) * rr
            a.rect(int(math.floor(x - 0.5)), int(math.floor(y - 0.5)), 2, 2, base)
    for y in range(44):
        for x in range(48):
            if a.opaque(x, y):
                lum = -(x + .5 - cx) * 0.6 - (y + .5 - cy) * 0.8
                if lum > r * 0.45:
                    a.set(x, y, light)
                elif lum < -r * 0.45:
                    a.set(x, y, dark)
    a.ellipse(cx, cy, r * 0.5, r * 0.5, dark)
    a.ellipse(cx, cy, r * 0.5 - 1, r * 0.5 - 1, base)
    a.ellipse(cx, cy, 1.5, 1.5, 'navy2')
    a.set(int(cx - r * 0.5), int(cy - r * 0.3), hi)
    a.outline(outline)
    cv.blit(a, 0, 0)


BRASS = ('gold1', 'gold2', 'gold3', 'gold5')
COPPER = ('fur_ginger1', 'fur_ginger2', 'fur_ginger3', 'fur_ginger4')


def gear_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 30)
    gear(cv, 32, 35, 4.5, 8, COPPER, 'fur_ginger0')
    gear(cv, 41, 22, 9, 12, BRASS, 'gold0')
    p = cats.base(0, arms='strike', arms_dy=-2, paws=((22, 23), (25, 25)), eyes='focus', mouth='grit')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    def wrench(a):
        a.line(17, 26, 37, 22, 'stone6')
        a.line(17, 27, 37, 23, 'stone4')
        a.polygon([(36, 19), (40, 20), (40, 21), (38, 22), (38, 23), (40, 24), (40, 25), (36, 26)], 'stone6')
        a.set(37, 20, 'stone7')
    lay(cv, 'navy1', wrench)
    w.grip(cv)
    return cv


def gear_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 31)
    def panel(a):
        a.rect(27, 6, 21, 33, 'navy3')
        a.frame(27, 6, 21, 33, 'navy4')
        for x, y in ((29, 8), (45, 8), (29, 36), (45, 36)):
            a.set(x, y, 'stone6')
    lay(cv, 'navy1', panel)
    gear(cv, 34, 16, 6, 10, BRASS, 'gold0')
    gear(cv, 42.5, 25, 5, 9, COPPER, 'fur_ginger0')
    gear(cv, 34.5, 31, 4, 8, BRASS, 'gold0')
    p = cats.base(0, arms='hold', arms_dy=-1, paws=((18, 26), (23, 26)), eyes='happy', mouth='w')
    w = Worker(fam, p, at=(-7, 1))
    w.draw(cv)
    gear(cv, 16, 26, 3.5, 7, BRASS, 'gold0')
    w.grip(cv)
    return cv


# ── 17 Lantern Works (Lanterns) ──
def lantern_body(a, x, y, h=8, lit=True):
    """Small workshop lantern 6 px wide: cap, glass with flame, base."""
    a.rect(x + 1, y, 4, 1, 'stone5')
    a.rect(x, y + 1, 6, 1, 'stone4')
    a.rect(x, y + 2, 6, h - 3, 'amber3' if lit else 'blue6')
    a.vline(x, y + 2, y + h - 2, 'stone4')
    a.vline(x + 5, y + 2, y + h - 2, 'stone4')
    if lit:
        a.rect(x + 2, y + 3, 2, h - 5, 'gold5')
        a.set(x + 2, y + 2, 'amber4')
    a.rect(x, y + h - 1, 6, 1, 'stone4')


def lantern_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 32)
    table(cv, 22, 47, 33, legs=(25, 44))
    def parts(a):
        a.rect(30, 23, 7, 1, 'stone4')
        a.rect(30, 24, 7, 8, 'amber2')
        a.vline(30, 24, 31, 'stone4')
        a.vline(36, 24, 31, 'stone4')
        a.rect(32, 26, 3, 5, 'gold5')
        a.rect(30, 32, 7, 1, 'stone4')
        a.rect(39, 28, 5, 4, 'blue6')
        a.set(40, 29, 'cream4')
        a.rect(42, 25, 4, 3, 'blue6')
        a.line(44, 31, 47, 29, 'red3')
    lay(cv, 'navy1', parts)
    p = cats.base(0, arms='strike', arms_dy=-3, paws=((24, 19), (27, 21)), eyes='focus', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    lay(cv, 'navy1', lambda a: (a.polygon([(28, 21), (33, 17), (38, 21)], 'stone5'), a.hline(29, 37, 21, 'stone6'),
                                 a.set(33, 16, 'stone6')))
    w.grip(cv)
    return cv


def lantern_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 33)
    p = cats.base(0, arms='carry', paws=((15, 26), (21, 25)), eyes='happy', mouth='w')
    w = Worker(fam, p, at=(-7, 1))
    w.draw(cv)
    cv.line(8, 30, 46, 9, 'wood3')
    cv.line(8, 31, 46, 10, 'wood1')
    def bunch(a):
        for x, y, l in ((25, 20, 5), (33, 16, 8), (41, 11, 6)):
            a.vline(x + 3, y, y + l, 'stone4')
            lantern_body(a, x, y + l, 8)
    lay(cv, 'navy1', bunch)
    w.grip(cv)
    for x, y in ((25, 37), (33, 38), (41, 33)):
        cv.set(x, y, 'amber4')
    return cv


# ── 18 Apiary (Honey) ──
def bee(cv, x, y):
    cv.set(x, y, 'gold4')
    cv.set(x + 1, y, 'ink1')
    cv.set(x + 2, y, 'gold4')
    cv.set(x + 1, y - 1, 'cream4')


def veil(cv, w):
    hx, hy = w.xy(*w.p.head)
    cv.hline(hx - 1, hx + 24, hy + 9, 'gold4')
    cv.hline(hx - 2, hx + 25, hy + 10, 'gold2')
    for x in list(range(hx - 1, hx + 3)) + list(range(hx + 21, hx + 25)):
        for y in range(hy + 11, hy + 19):
            if (x + y) % 2 == 0:
                cv.set(x, y, 'cream2')


def hive(cv):
    def d(a):
        for k, y in enumerate((30, 22)):
            slab(a, 33, y, 14, 8, ('fur_white0', 'fur_white2', 'fur_white4'))
            a.hline(34, 45, y + 4, 'fur_white1')
        a.polygon([(31, 22), (40, 15), (49, 22)], 'red3')
        a.hline(31, 47, 22, 'red2')
        a.rect(37, 36, 5, 2, 'ink2')
        a.rect(33, 38, 14, 2, 'wood3')
    lay(cv, 'fur_gray0', d)


def comb(a, x, y, w, h):
    a.rect(x, y, w, h, 'wood4')
    a.rect(x + 1, y + 1, w - 2, h - 2, 'amber3')
    for yy in range(y + 1, y + h - 1):
        for xx in range(x + 1, x + w - 1):
            if (xx + (yy // 2) % 2 * 1) % 2 == 0 and yy % 2 == 0:
                a.set(xx, yy, 'gold4')
            elif yy % 2 == 1 and xx % 3 == 0:
                a.set(xx, yy, 'amber2')
    a.hline(x, x + w - 1, y, 'wood6')


def apiary_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 34)
    hive(cv)
    p = cats.base(0, arms='strike', arms_dy=-2, paws=((22, 22), (25, 25)), eyes='happy', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    veil(cv, w)
    lay(cv, 'wood1', lambda a: (comb(a, 18, 21, 12, 10), a.set(22, 31, 'amber3'), a.set(22, 32, 'amber3')))
    w.grip(cv)
    for x, y in ((31, 12), (37, 9), (44, 12), (29, 18), (41, 35)):
        bee(cv, x, y)
    return cv


def apiary_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 35)
    def barrel(a):
        a.polygon([(30, 23), (44, 23), (45, 31), (44, 39), (30, 39), (29, 31)], 'wood4')
        for x in range(31, 44, 3):
            a.vline(x, 24, 38, 'wood3')
        a.vline(30, 24, 38, 'wood5')
        for y in (26, 35):
            a.hline(29, 45, y, 'stone4')
        a.ellipse(37, 23, 7, 1.6, 'amber3')
        a.hline(33, 40, 23, 'gold4')
        a.vline(32, 24, 28, 'amber3')
        a.vline(42, 24, 30, 'amber3')
        a.set(42, 31, 'amber4')
    lay(cv, 'wood0', barrel)
    def jar(a):
        a.rect(42, 34, 5, 6, 'amber3')
        a.hline(42, 46, 33, 'blue6')
        a.vline(42, 34, 39, 'amber4')
        a.set(43, 35, 'cream4')
    lay(cv, 'blue2', jar)
    p = cats.base(0, arms='hold', arms_dy=-1, paws=((18, 27), (23, 26)), eyes='happy', mouth='open')
    w = Worker(fam, p, at=(-7, 1))
    w.draw(cv)
    def dipper(a):
        a.line(14, 30, 19, 22, 'wood4')
        a.rect(18, 18, 4, 5, 'amber3')
        a.hline(18, 21, 19, 'gold4')
        a.hline(18, 21, 21, 'amber2')
        a.set(18, 18, 'gold5')
        a.set(21, 23, 'amber3')
        a.set(21, 24, 'amber4')
    lay(cv, 'wood0', dipper)
    w.grip(cv)
    for x, y in ((28, 12), (35, 16), (41, 14)):
        bee(cv, x, y)
    return cv


# ── 19 Silk Mill (Silk) ──
def loom(cv):
    def d(a):
        for x in (28, 45):
            a.rect(x, 6, 3, 34, 'wood3')
            a.vline(x, 6, 39, 'wood5')
        a.rect(28, 6, 20, 3, 'wood4')
        a.hline(28, 47, 6, 'wood6')
        for x in range(32, 45, 2):
            a.vline(x, 9, 26, 'cream3' if x % 4 else 'purple6')
        a.rect(31, 17, 14, 2, 'wood5')
        a.hline(31, 44, 17, 'wood6')
        a.rect(31, 27, 14, 7, 'purple5')
        for y in range(27, 34, 2):
            a.hline(31, 44, y, 'purple6')
        a.rect(28, 34, 20, 3, 'wood4')
        a.hline(28, 47, 34, 'wood6')
    lay(cv, 'wood0', d)


def silk_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 36)
    loom(cv)
    p = cats.base(0, arms='strike', arms_dy=-1, paws=((24, 24), (27, 26)), eyes='focus', mouth='w')
    w = Worker(fam, p, at=(-6, 1))
    w.draw(cv)
    lay(cv, 'wood0', lambda a: (a.polygon([(21, 25), (25, 23), (33, 23), (36, 25), (33, 27), (25, 27)], 'wood5'),
                                 a.hline(25, 33, 24, 'wood6'), a.rect(28, 25, 3, 1, 'purple6')))
    w.grip(cv)
    return cv


def silk_m(fam):
    cv = Canvas(48, 44)
    floor(cv, 37)
    shelf(cv, 33, 47, 4, (17,))
    def rolls(a):
        for c in range(3):
            x = 36 + c * 4
            t = SPOOL_TONES[(c + 1) % 9]
            a.ellipse(x + .5, 12.5, 1.8, 3, t[1])
            a.set(x, 11, t[2])
            a.set(x + 1, 13, t[0])
    lay(cv, 'wood0', rolls)
    table(cv, 26, 47, 34, legs=(29, 44))
    def roll(a):
        a.rect(38, 22, 10, 11, 'purple5')
        a.hline(38, 47, 22, 'purple6')
        a.hline(39, 47, 23, 'cream4')
        a.hline(38, 47, 32, 'purple4')
        a.polygon([(33, 33), (38, 33), (39, 39), (37, 40), (35, 39), (33, 40), (32, 38)], 'purple5')
        a.vline(35, 34, 38, 'cream4')
        a.vline(37, 34, 38, 'purple4')
        a.ellipse(36.5, 27.5, 5.5, 5.5, 'purple5')
        for r, c in ((4.5, 'purple6'), (3.5, 'purple4'), (2.5, 'purple6'), (1.5, 'purple4')):
            a.ellipse(36.5, 27.5, r, r, c)
        a.ellipse(36.5, 27.5, 0.8, 0.8, 'wood3')
        a.set(33, 24, 'cream4')
        a.set(34, 23, 'cream4')
    lay(cv, 'purple2', roll)
    p = cats.base(0, arms='hold', arms_dy=-1, paws=((18, 27), (23, 26)), eyes='happy', mouth='w')
    w = Worker(fam, p, at=(-7, 1))
    w.draw(cv)
    def swatch(a):
        a.polygon([(11, 26), (19, 25), (20, 31), (12, 32)], 'purple5')
        a.line(12, 27, 18, 26, 'cream4')
        a.line(13, 29, 19, 28, 'purple6')
        a.line(12, 31, 19, 30, 'purple4')
    lay(cv, 'purple2', swatch)
    w.grip(cv)
    sparkle(cv, 44, 18, 'cream4', 1)
    return cv


# ── 20 Caviar Cannery (Caviar) ──
def caviar_f(fam):
    cv = Canvas(48, 44)
    floor(cv, 38)
    conveyor(cv, 25, 47, 32)
    def line(a):
        a.rect(36, 4, 10, 8, 'navy3')
        a.hline(36, 45, 4, 'navy5')
        a.vline(36, 5, 11, 'navy4')
        a.hline(37, 44, 8, 'gold4')
        a.rect(39, 12, 4, 4, 'navy4')
        a.rect(40, 16, 2, 2, 'stone5')
        a.set(40, 16, 'stone7')
        for x in (28, 39):
            a.ellipse(x + 3, 29, 3, 1.4, 'stone6')
            a.rect(x, 29, 7, 3, 'stone5')
            a.hline(x, x + 6, 31, 'stone4')
            a.ellipse(x + 3, 29, 2.2, 0.8, 'red4')
    lay(cv, 'navy1', line)
    dust(cv, ((41, 20), (40, 23), (41, 26)), 'red5')
    p = cats.base(0, arms='hold', arms_dy=0, paws=((18, 28), (25, 28)), eyes='happy', mouth='w')
    w = Worker(fam, p, at=(-7, 1))
    w.draw(cv)
    def tin_open(a):
        a.ellipse(17.5, 27, 6, 2, 'stone6')
        a.rect(12, 27, 12, 3, 'stone5')
        a.hline(12, 23, 29, 'stone4')
        a.vline(12, 27, 29, 'stone7')
        a.ellipse(17.5, 27, 5, 1.4, 'red4')
        for x in range(14, 22, 2):
            a.set(x, 27, 'red5')
            a.set(x + 1, 26, 'red6')
    lay(cv, 'navy1', tin_open)
    w.grip(cv)
    return cv


SCENES = {
    'amethyst': (dairy_f, dairy_m), 'citrine': (sawmill_f, sawmill_m), 'agate': (fishery_f, fishery_m),
    'topaz': (yarnmill_f, yarnmill_m), 'opal': (catfood_f, catfood_m), 'jade': (boxes_f, boxes_m),
    'onyx': (brickworks_f, brickworks_m), 'sapphire': (steel_f, steel_m), 'tourmaline': (glass_f, glass_m),
    'aquamarine': (catnip_f, catnip_m), 'emerald': (toy_f, toy_m), 'diamond': (scratcher_f, scratcher_m),
    'garnet': (cushion_f, cushion_m), 'heliodor': (foundry_f, foundry_m), 'ruby': (collar_f, collar_m),
    'alexandrite': (gear_f, gear_m), 'celestine': (lantern_f, lantern_m), 'amber': (apiary_f, apiary_m),
    'moonstone': (silk_f, silk_m), 'sunstone': (caviar_f, None),
}


def manager(fam, master=False):
    """Factory card scene (ТЗ 2.2 §7.2): Foreman (Common) or, with master=True, Manager (Uncommon)."""
    return SCENES[fam.type][1 if master else 0](fam)


def chest(cv,x=27,y=29,elixir=False):
    cv.rect(x,y,19,12,'wood0');cv.rect(x+1,y+1,17,10,'wood4')
    cv.hline(x+1,x+17,y+1,'wood6')
    for xx in (x+3,x+14):cv.rect(xx,y+1,2,10,'gold2');cv.set(xx,y+1,'gold5')
    cv.polygon([(x,y-1),(x+3,y-9),(x+18,y-8),(x+19,y-1)],'wood0')
    cv.polygon([(x+2,y-2),(x+4,y-7),(x+16,y-6),(x+17,y-2)],'wood3')
    cv.hline(x+4,x+15,y-7,'gold4');cv.rect(x+8,y+3,3,3,'gold4')
    for xx in (x+4,x+10,x+15):
        if elixir:
            cv.rect(xx,y-1,4,5,'purple0');cv.rect(xx+1,y,2,3,'purple4');cv.set(xx+1,y-2,'cream3');cv.set(xx+1,y,'purple6')
        else:cv.ellipse(xx,y+1,3,2,'gold3');cv.set(xx-1,y,'gold5')


def little_cat(cv,x,y,fur='cream'):
    # Native tiny crew silhouette, using the rig's material colours.
    cv.polygon([(x,y+4),(x,y),(x+4,y+3),(x+8,y),(x+9,y+5),(x+8,y+10),(x+1,y+10)],'fur_'+fur+'0')
    cv.rect(x+1,y+4,7,5,'fur_'+fur+'3');cv.rect(x+2,y+9,5,5,'blue3')
    cv.set(x+2,y+6,'ink1');cv.set(x+6,y+6,'ink1');cv.set(x+4,y+7,'pink2')
    cv.hline(x+1,x+8,y+3,'gold4');cv.set(x+4,y+2,'amber5')
    cv.set(x+2,y+14,'cream3');cv.set(x+6,y+14,'cream3')


def global_scene(i):
    cv=Canvas(48,44);floor(cv,i)
    if i==1:
        cv.rect(35,4,10,4,'wood0');cv.rect(36,5,8,2,'wood5')
        for x in (37,41):cv.rect(x,10,3,24,'wood2');cv.vline(x,10,33,'wood5')
        box(cv,31,29,15,12);cat(cv,'unload1','brown','yellow',at=(-5,1))
        prop(cv,'golden_cart',0,30,(19,13));cv.ellipse(28,31,5,5,'cream1');cv.hline(25,30,27,'wood2');cv.set(27,29,'gold4')
    elif i==26:
        cat(cv,'pickup0','white',None,at=(-5,0));bench(cv)
        prop(cv,'yarn',25,22,(19,18))
        for x,y in ((30,10),(40,15),(23,37)):
            cv.ellipse(x,y,4,4,'fur_ginger0');cv.ellipse(x,y,3,3,'fur_ginger3');cv.ellipse(x,y,1,1,'navy1')
            for dx,dy in ((-4,0),(4,0),(0,-4),(0,4)):cv.set(x+dx,y+dy,'fur_ginger3')
        cv.line(26,34,33,17,'stone6');cv.line(28,34,37,21,'stone4')
    elif i==27:
        for y in (15,22,29):cv.hline(1,8,y,'blue5');cv.hline(2,6,y+1,'navy5')
        cat(cv,'walk6','ginger',None,at=(0,0));prop(cv,'fish_parcel',27,23,(20,19))
        cv.line(33,26,42,26,'cream2');cv.vline(38,26,40,'cream1')
    elif i==28:
        # Oversized supply backpack behind the cat, fish protrude above its rim.
        box(cv,0,21,18,18);prop(cv,'fish',0,12,(20,12));prop(cv,'fish',2,22,(16,10))
        cv.rect(2,31,5,6,'cream3');cv.hline(2,6,32,'red3')
        cat(cv,'carry3','cream','green',at=(8,1),pack='legendary')
        cv.line(21,28,19,37,'wood4');cv.line(26,28,25,36,'wood4')
    elif i==29:
        box(cv,29,3,16,18);cv.rect(31,5,12,14,'cream2')
        little_cat(cv,32,5,'ginger');cv.hline(33,41,19,'green4')
        cat(cv,'idle5','gray',None,at=(-6,2));prop(cv,'pocket_watch',28,24,(18,18))
        cv.line(26,29,30,24,'gold3')
    elif i==30:
        cat(cv,'pickup1','white',None,at=(-3,1));prop(cv,'milk_bowl',25,29,(21,13))
        for x,y in ((10,40),(20,38),(32,41),(41,26)):
            cv.ellipse(x,y,3,2,'gold1');cv.hline(x-1,x+1,y-1,'gold5')
        cv.line(22,33,29,36,'cream3');cv.line(24,31,31,34,'cream2')
    elif i==31:
        cat(cv,'joy2','black',None,at=(-6,0));prop(cv,'lucky_paw',29,19,(17,21))
        cv.line(31,21,31,14,'gold3')
        for x,y in ((35,10),(42,17),(26,36),(8,9)):sparkle(cv,x,y)
    elif i==32:
        for x in (29,43):cv.rect(x,8,4,34,'wood1');cv.vline(x,8,41,'wood5')
        cv.rect(28,6,19,4,'wood4');cv.hline(29,45,6,'wood6')
        for x in (34,38):cv.rect(x,25,3,16,'wood3')
        prop(cv,'gate_bell',33,12,(12,17));cv.line(37,28,29,33,'cream2')
        cat(cv,'mine_raise','cream','blue',at=(-7,2));cv.line(26,27,32,30,'cream2')
        for x,y in ((32,13),(44,12)):cv.line(x,y,x-2,y+4,'gold4')
    elif i==33:
        cat(cv,'mine_strike','ginger','green',at=(-5,1),tool=True)
        prop(cv,'catnip',30,4,(16,22));rock(cv,37,37,9,5,ROCK,seed=2,moss=False,n=6)
        sparkle(cv,34,34,'gold4');cv.line(31,16,42,23,'green5')
    elif i==34:
        cat(cv,'joy0','ginger','gold',at=(3,-2));prop(cv,'golden_cart',4,23,(40,21))
        cv.hline(1,46,42,'stone5');cv.hline(3,44,43,'wood3')
        for x,y in ((5,18),(41,15),(36,25)):sparkle(cv,x,y)
    elif i==35:
        cat(cv,'mine_raise','brown','red',at=(-4,2));prop(cv,'sledgehammer',25,5,(21,33))
        cv.line(23,29,29,24,'fur_brown3');sparkle(cv,38,37,'gold4',4)
        cv.polygon([(33,39),(35,32),(39,37),(45,33),(43,40)],'amber3')
    elif i==36:
        prop(cv,'recruit_poster',29,2,(17,22));cat(cv,'joy3','white',None,at=(-7,2))
        little_cat(cv,28,27,'cream');little_cat(cv,38,27,'gray')
        cv.line(28,23,32,20,'green5')
    elif i==37:
        prop(cv,'gold_vein',24,20,(23,22));cat(cv,'mine_recoil','black','yellow',at=(-5,1),tool=True)
        for x,y in ((35,16),(43,26),(30,35)):sparkle(cv,x,y)
    elif i==38:
        little_cat(cv,28,27,'gray');little_cat(cv,38,27,'brown')
        cat(cv,'unload1','cream','gold',at=(-5,1));cv.line(24,30,35,22,'wood3')
        cv.polygon([(35,22),(35,12),(44,16)],'red3');cv.vline(35,12,22,'gold4')
        sparkle(cv,22,4)
    elif i==43:
        cat(cv,'pickup0','brown',None,at=(-5,1));bench(cv)
        prop(cv,'toy_mouse',27,24,(18,12));cv.rect(32,6,13,12,'wood0');cv.rect(33,7,11,10,'cream3')
        cv.line(35,15,42,9,'red3');cv.ellipse(35,9,1,1,'red3');cv.ellipse(42,15,1,1,'red3')
        cv.line(31,30,40,22,'wood4');cv.rect(38,19,7,5,'cream2');cv.set(41,21,'red3')
    elif i==50:
        cat(cv,'joy1','white',None,at=(-5,1));cv.rect(9,26,19,3,'red2');cv.hline(10,27,26,'red4')
        cv.ellipse(21,30,3,3,'gold2');cv.set(20,29,'gold5');cv.set(21,32,'wood0')
        # Flying golden pick is drawn along its own axis, never rotated by code.
        p=deepcopy(cats.POSES['mine_ready']);p.pick=((30,27),(37,10))
        m=dict(cats.PICK_IRON);m.update(po='gold0',p1='gold1',p2='gold3',p3='gold4',p4='gold5')
        cv.blit(cats.paint(cats.pick_master(p,set(),set(),scale=1.25),m),0,0)
        for x,y in ((40,20),(43,26),(33,6)):sparkle(cv,x,y,'gold5',1)
    elif i==51:
        prop(cv,'cat_bed',2,24,(44,19))
        # Sleeping cat uses the rig's closed-eyed head and marked fur.
        p=deepcopy(cats.POSES['blink']);p.head=(8,6)
        head=cats.paint(cats.head_layer(p),cats.fur_map('ginger'))
        cv.ellipse(30,29,10,5,'fur_ginger0');cv.ellipse(29,28,9,4,'fur_ginger3')
        cv.blit(head.crop(8,6,24,20),7,12)
        cv.ellipse(35,31,6,2,'fur_ginger2')
        cv.ellipse(39,7,4,5,'cream3');cv.ellipse(41,6,3,4,'navy2')
        for x,y in ((29,8),(33,3)):cv.hline(x,x+3,y,'cream3');cv.line(x+3,y,x,y+3,'cream3');cv.hline(x,x+3,y+3,'cream3')
    elif i==52:
        prop(cv,'scratcher',29,4,(18,36));cat(cv,'mine_strike','gray',None,at=(-5,1))
        rock(cv,36,38,9,5,ROCK,seed=11,moss=False,n=6)
        for x in (32,35,38):cv.line(x,29,x-2,35,'cream3');cv.line(x,14,x-2,20,'wood1')
        cv.line(26,29,33,24,'fur_gray3')
    elif i==53:
        chest(cv,27,30,True);cat(cv,'unload1','black','purple',at=(-6,0))
        prop(cv,'lantern',22,20,(10,18));cv.line(29,24,35,22,'amber4');cv.line(29,29,37,28,'amber3')
        for xx,yy in ((34,28),(41,27)):
            cv.rect(xx,yy,5,8,'purple0');cv.rect(xx+1,yy+1,3,5,'purple4')
            cv.rect(xx+1,yy-2,3,3,'cream1');cv.set(xx+1,yy+1,'purple6')
        sparkle(cv,42,21,'purple5',1)
    elif i==54:
        for x,y,r in ((35,31,9),(43,36,6),(28,38,6)):
            rock(cv,x,y,r,r*.75,ROCK,seed=x,moss=False,n=6)
        cat(cv,'unload1','gray','red',at=(-6,1));prop(cv,'big_dynamite',26,21,(12,20))
        cv.line(39,26,42,30,'stone0');cv.line(35,34,31,38,'stone0')
    elif i==55:
        chest(cv,27,30);cat(cv,'idle5','cream','green',at=(-7,1));prop(cv,'clover',25,12,(18,24))
        for x,y in ((43,11),(38,23),(25,8)):sparkle(cv,x,y)
    elif i==63:
        # Distant blasted rock and soft, stepped dust puffs behind the proud miner.
        rock(cv,38,34,8,5,ROCK,seed=63,moss=False,n=6)
        for x,y,r in ((35,21,4),(41,17,4),(43,25,3)):
            cv.ellipse(x,y,r+1,r,'stone1')
            cv.ellipse(x,y-1,r,r-1,'stone4')
            cv.hline(x-2,x,y-r+1,'stone6')
        for x,y in ((30,17),(43,10),(45,31)):
            cv.rect(x,y,2,2,'stone5')
        cv.line(35,29,39,32,'stone0')
        cat(cv,'unload1','ginger','red',at=(-6,1))
        # Three bright sticks held against the chest, with a rope binding.
        for x,y in ((25,25),(29,23),(33,25)):
            cv.rect(x,y,4,12,'red0')
            cv.rect(x+1,y+1,2,10,'red3')
            cv.vline(x+1,y+1,y+9,'red5')
            cv.hline(x+1,x+2,y,'red4')
        cv.rect(25,30,12,2,'wood1')
        cv.hline(26,35,30,'cream1')
        cv.line(31,23,33,20,'wood1')
        cv.line(33,20,36,20,'cream2')
        sparkle(cv,38,20,'gold5',2)
        cv.set(41,18,'amber3')
        cv.ellipse(25,33,3,2,'fur_ginger0')
        cv.ellipse(25,32,2,1,'fur_ginger3')
        sparkle(cv,8,9,'gold4',1)
    elif i==62:
        chest(cv,25,29);cat(cv,'unload1','brown','gold',at=(-6,0),pack='legendary')
        prop(cv,'treasure_map',19,25,(21,18))
        for x,y,col in ((32,25,'purple4'),(40,27,'blue5'),(44,24,'pink3')):cv.rect(x,y,3,4,col);cv.set(x,y,'cream4')
        for x,y in ((35,11),(44,16),(25,8)):sparkle(cv,x,y,'gold5',2)
    return cv


def build(reg):
    scenes={i:global_scene(i) for i in GLOBALS}
    for fam in FAMILIES:
        scenes[fam.common]=manager(fam)
        if fam.manager:scenes[fam.manager]=manager(fam,True)
    assert set(scenes)==set(range(1,61))|{62,63}
    for i in sorted(scenes):
        reg.add(f'card_art_ca{i:03}',scenes[i],note=CATALOG[i][1])
