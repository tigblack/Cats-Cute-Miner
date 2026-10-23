"""Edit decision lists of the Cat Miners Meta ads (128 BPM grid: bar = 1.875 s): four 45 s ads plus the cut-downs
and extensions ad1_30 (30 s 9:16), ad3_30 (30 s 4:5) and ad4_60 (60 s 9:16) on music_cut.py beds.

Scripts and rationale: Docs/Marketing/AD_SCRIPTS.md. Times are output seconds; source times come from the shot logs
(marks, taps, core events) recorded by AdCaptureTests, so re-shooting the footage keeps the edit in sync.
Captions sit in the Reels safe zone (y 269..1247) and inside a centred 4:5 crop (y 285..1635); the game's own HUD
stays visible as proof of real gameplay. Every caption claim matches what the footage shows (QA 2026-10-13).
"""
from __future__ import annotations

from typing import List

from compose import Ad, Ov, Seg, log, mark, taps, zoom_to

BAR = 1.875
CAP_Y = 400          # caption centre: below the game's top bar and the Reels top margin
CAP_HI = 332         # one-line captions over full-screen windows (plank 288..376)
CAP_LOW = 1140       # lower slot (above the Reels bottom margin)
LEGAL = 'In-game purchases (incl. random items)'
LEGAL_Y = 1212       # in-ad disclosure while chests are on screen (inside the safe zone)


def cap(t0: float, t1: float, text: str, style: str = 'plank', cy: int = CAP_Y, k: int = 6, **kw) -> Ov:
    return Ov('caption', t0, t1, dict(text=text, style=style, cy=cy, k=k, **kw))


def legal(t0: float, t1: float, cy: int = LEGAL_Y) -> Ov:
    return Ov('caption', t0, t1, dict(text=LEGAL, style='dark', cy=cy, k=4, width=1000, exit=False))


def logo_bug(t0: float, t1: float, cy: int = 1100) -> Ov:
    """Small game logo early in the ad (brand before the end card)."""
    return Ov('logo', t0, t1, dict(cy=cy, k=2))


def core(shot: str, name: str, nth: int = 0) -> float:
    """Seconds of the nth core event `name` in the shot."""
    return [e['f'] / 30 for e in log(shot)['events'] if e['kind'] == 'core' and e['name'] == name][nth]


def end_card(tagline: str, length: float = 45.0) -> List[Ov]:
    """The last two bars."""
    return [Ov('end', length - 2 * BAR, length, dict(tagline=tagline, legal=LEGAL))]


def pops(ovs, gain=0.45):
    """A soft pop on every caption start (not on the disclosure)."""
    return [(o.t0, 'pop', gain) for o in ovs if o.kind == 'caption' and o.a.get('text') != LEGAL]


def flash(t: float, color: str = 'cream4') -> Ov:
    return Ov('flash', t, t + 0.3, dict(color=color, peak=0.5))


def b(n: float) -> float:
    return n * BAR


# ───────────────────────── Ad 1: Level 1 -> Level 100 (progression) ─────────────────────────
def ad1() -> Ad:
    hire0 = mark('m1_hire', 'hire') - 0.5
    gate = mark('m13_gate', 'gate_open')
    results = mark('m1_clear', 'results')
    chest_window = taps('m100_store_chest')[0][0] + 1 / 30
    segs = [
        # Hook: Mine 100 (12 cats in legendary gear, trillions of gold) over Mine 1 (2 cats, 0 gold), both HUDs visible.
        Seg(0.0, b(2), 'm100_swarm', 1.0, split=('m1_start', 1.5, 1180, 1120)),
        Seg(b(2), b(4), 'm1_start', 5.25, view=zoom_to(1.0, (540, 960), 2.0, (480, 820))),
        Seg(b(4), b(6), 'm1_hire', hire0),
        Seg(b(6), b(8), 'm1_base', 1.2),
        Seg(b(8), b(10), 'm1_clear', results - 1.1),
        Seg(b(10), b(12), 'm13_glowup', 0.6),
        Seg(b(12), b(14), 'm13_gate', max(0.0, gate - 2.6)),
        Seg(b(14), b(16), 'm16_swarm', 0.5),
        Seg(b(16), b(18), 'm100_swarm', 5.0),
        Seg(b(18), b(20), 'm100_offline', 0.0, paws=False),
        Seg(b(20), b(22), 'm100_store_chest', chest_window, paws=False,
            view=zoom_to(1.0, (540, 960), 1.25, (540, 1000))),
    ]
    ovs = [
        Ov('split_labels', 0.0, b(2), dict(top='LEVEL 100', top_style='gold', top_icon='icon_item_hat_legendary',
                                           top_cy=860, bottom='LEVEL 1', bottom_style='blue', bottom_icon='icon_cat',
                                           bottom_cy=1150)),
        cap(b(2), b(4), 'It starts with *2 cats*...'),
        logo_bug(b(2) + 0.3, b(4), cy=1150),
        cap(b(4), b(6), 'Hire *more cats*!'),
        cap(b(6), b(8), 'Upgrade *everything*', cy=CAP_HI),
        cap(b(8), b(10), 'Mine *cleared*!', cy=CAP_HI),
        cap(b(10), b(12), 'Equip *legendary* gear'),
        cap(b(12), b(14), 'Break into *new tunnels*'),
        cap(b(14), b(16), 'Deeper and *deeper*...'),
        cap(b(16), b(18), '...all the way to *Mine 100*!'),
        cap(b(18), b(20), 'Earn *trillions* of gold!', cy=CAP_HI),
        cap(b(20), b(22), 'Chests full of *cat cards*', cy=CAP_HI),
        legal(b(20), b(22)),
        flash(b(2)), flash(b(10), 'gold4'), flash(b(16), 'gold4'),
        *end_card('How deep can *YOUR* cats dig?'),
    ]
    sfx = [(0.0, 'impact', 0.5), (b(2) - 0.2, 'whoosh', 0.45), (b(10) - 0.2, 'whoosh', 0.45),
           (b(16) - 0.2, 'whoosh', 0.45), (b(18) + 1.5, 'cash', 0.5), *pops(ovs)]
    return Ad('CatMiners_Ad1_Level1toLevel100_45s', 't1_progress', segs, ovs, sfx)


# ───────────────────────── Ad 2: Stop tapping! (fail -> fix, idle) ─────────────────────────
def ad2() -> Ad:
    hire0 = mark('m1_hire', 'hire') - 0.3
    off = mark('m100_offline', 'offline_summary')
    skip_tap = taps('m100_skip')[0][0]
    results = mark('m1_clear', 'results')
    tap_x, tap_y = taps('m1_tap')[0][1:3]
    segs = [
        # 1.9 s in: no reward toast in this stretch; the far rock only cracks under the lone paw.
        Seg(0.0, 3.30, 'm1_tap', 1.9, paws=False, audio=2.0),
        Seg(3.30, b(3), 'm1_tap', 5.2, speed=0.0, paws=False, audio=0.0,
            view=zoom_to(1.0, (540, 960), 1.25, (tap_x, tap_y))),
        Seg(b(3), b(6), 'm1_hire', hire0),
        Seg(b(6), b(8), 'm16_swarm', 1.0, audio=1.8),
        Seg(b(8), b(10), 'm1_base', 1.2),
        Seg(b(10), b(12), 'm100_swarm', 4.0, audio=0.3),
        Seg(b(12), b(14), 'm100_offline', max(0.0, off - 0.4)),
        Seg(b(14), b(16), 'm100_skip', skip_tap - 0.5),
        Seg(b(16), b(18), 'm100_swarm', 0.3),
        Seg(b(18), b(20), 'm100_overview', 1.5, audio=0.0),
        Seg(b(20), b(22), 'm1_clear', results - 1.1),
    ]
    ovs = [
        Ov('burst', 0.0, 3.30, dict(x=tap_x, y=tap_y, rate=7.0)),
        cap(0.0, 3.30, 'Mining *by hand* be like...'),
        Ov('wrong', 3.30, b(3), dict(text='WRONG WAY!')),
        cap(b(3), b(6), 'Let the *cats* do it!', style='green'),
        logo_bug(b(3) + 0.4, b(6), cy=1130),
        cap(b(6), b(8), 'They carry the *gold* home'),
        cap(b(8), b(10), 'You just *upgrade*', cy=CAP_HI),
        Ov('zzz', b(10) + 0.2, b(12), dict(origin=(420, 1150))),
        cap(b(10), b(12), 'Close the game...', style='dark', cy=CAP_HI),
        cap(b(12), b(14), '...they keep *digging*!', cy=CAP_HI),
        cap(b(14), b(16), '*Skip time*, get gold', cy=CAP_HI),
        legal(b(14) + 0.6, b(16)),
        cap(b(16), b(18), 'Mine after *mine*'),
        cap(b(18), b(20), '*144 mines* to dig!'),
        cap(b(20), b(22), 'Mine *cleared*!', cy=CAP_HI),
        flash(b(3), 'gold4'), flash(b(12), 'gold4'),
        *end_card('Let your cats *dig for you*!'),
    ]
    ticks = [(i / 7.0, 'tick', 0.22) for i in range(23)]
    sfx = [*ticks, (3.30, 'scratch', 0.6), (b(3) + 0.02, 'impact', 0.35),
           (b(10) + 0.3, 'zzz', 0.45), (b(12) + 0.1, 'cash', 0.5), (b(14) - 0.2, 'whoosh', 0.45),
           *pops(ovs[1:])]
    return Ad('CatMiners_Ad2_StopTapping_45s', 't2_idle', segs, ovs, sfx)


# ───────────────────────── Ad 3: Satisfying loot + collection ─────────────────────────
def ad3() -> Ad:
    t_open, tx, ty, _ = taps('m100_store_chest')[0]            # the paw opens the Wooden Chest (store tile)
    chest_window = t_open + 1 / 30
    upgrade2 = taps('cards_upgrade')[1][0]
    segs = [
        # Hook: push-in on the chest tile (frozen store, so no window pops in), the paw presses it just before the
        # reveal hit at 3.75 s; then the real chest window with a slow push-in on the cards.
        Seg(0.0, b(2), 'm100_store_chest', 0.4, speed=0.0, paws=False, audio=0.0, hover=(tx, ty, b(2) - 0.15),
            view=zoom_to(1.0, (540, 960), 1.8, (tx, ty))),
        Seg(b(2), b(4), 'm100_store_chest', chest_window, paws=False,
            view=zoom_to(1.0, (540, 960), 1.15, (540, 1000))),
        Seg(b(4), b(6), 'm13_asmr', 0.5, audio=2.5),
        Seg(b(6), b(8), 'm100_swarm', 6.2, audio=1.5),
        Seg(b(8), b(10), 'cards_scroll', 0.4, audio=0.0),
        Seg(b(10), b(12), 'cards_upgrade', upgrade2 - 1.0, audio=0.0),
        Seg(b(12), b(14), 'equip_upgrade', 0.6, audio=0.0),
        Seg(b(14), b(16), 'm13_pan', 0.5),
        Seg(b(16), b(18), 'm16_swarm', 1.0, audio=1.5),
        Seg(b(18), b(20), 'm100_swarm', 1.5),
        Seg(b(20), b(22), 'm13_gate', max(0.0, mark('m13_gate', 'gate_open') - 2.2)),
    ]
    ovs = [
        cap(0.0, b(2), 'What\'s *inside*?'),
        legal(0.0, b(4)),
        cap(b(2), b(4), 'Full of *cat cards*!', cy=CAP_HI),
        cap(b(4), b(6), 'Every hit = *gold*'),
        logo_bug(b(4) + 0.3, b(6), cy=1130),
        cap(b(6), b(8), 'So *satisfying*...'),
        cap(b(8), b(10), 'Collect *61* cat cards', cy=CAP_LOW),
        cap(b(10), b(12), 'Level them *up*', cy=CAP_LOW),
        cap(b(12), b(14), 'Upgrade your *gear*', cy=CAP_LOW),
        cap(b(14), b(16), 'Deeper and *deeper*'),
        cap(b(16), b(18), 'They carry the *gold* home'),
        cap(b(18), b(20), 'All the way to *Mine 100*!'),
        cap(b(20), b(22), 'Open *new tunnels*!'),
        flash(b(2), 'gold4'),
        *end_card('Which cat will *you* find?'),
    ]
    sfx = [(b(2), 'sparkle', 0.55), (b(10) + 1.0, 'ding', 0.45), *pops(ovs)]
    return Ad('CatMiners_Ad3_ChestsCollection_45s', 't3_chest', segs, ovs, sfx)


# ───────────────────────── Ad 4: Cat factories (TZ 2.2: chains, Deliver, pipes, Market, automation) ─────────────────────────
def first_core(shot: str, name: str, after: float = 0.0, default: float = 0.0) -> float:
    """Seconds of the first core event `name` at or after `after` (default if none)."""
    ts = [e['f'] / 30 for e in log(shot)['events'] if e['kind'] == 'core' and e['name'] == name and e['f'] / 30 >= after]
    return ts[0] if ts else default


def ad4() -> Ad:
    unlocked = mark('f10_chains', 'unlocked')                 # the chains snap
    deliver = taps('f10_deliver')[0][0]                       # the paw presses "Deliver"
    trio = taps('f30_deliver')[0][0]                          # first of three "Deliver" presses
    m30_sell = taps('f30_market')[-1][0]                      # "Sell all" in Mine 30
    upgrade = taps('f3_card')[0][0]                           # "Upgrade" on the Dairy Foreman card
    auto_send = first_core('f3_auto', 'GoodsDispatched', after=0.8, default=1.8)
    m140_send = first_core('f140_factories', 'GoodsDispatched', default=6.0)
    m140_sell = taps('f140_market')[-1][0]
    # The batch rides the pipe for only ~2 s, so tap and ride share one take; the caption switches as the camera
    # starts following the pipe to the warehouse ("+24" pops there).
    pipe = b(5.25)
    segs = [
        # Hook: cats and the paw hammer a chained factory; the chains snap right on the first bar line (3.75 s).
        Seg(0.0, b(2), 'f10_chains', max(0.0, unlocked - b(2) + 0.05)),
        Seg(b(2), b(4), 'f10_chains', unlocked + 0.05),
        Seg(b(4), b(6.5), 'f10_deliver', max(0.0, deliver - 1.7)),
        Seg(b(6.5), b(8), 'f30_deliver', max(0.0, trio - 0.6)),
        Seg(b(8), b(10), 'f30_market', max(0.0, m30_sell - 1.9)),
        Seg(b(10), b(12), 'f3_card', max(0.0, upgrade - 1.5), audio=0.8),
        # Push-ins make the small AUTO badges readable (the Mine 3 one ends below the HUD bar, no half-cut HUD).
        Seg(b(12), b(14), 'f3_auto', max(0.0, auto_send - 1.2), view=zoom_to(1.0, (540, 960), 1.4, (600, 820))),
        Seg(b(14), b(16), 'f140_factories', max(0.0, m140_send - 1.5), view=zoom_to(1.0, (540, 960), 1.25, (550, 700))),
        Seg(b(16), b(18), 'f140_overview', 0.8, audio=0.3),
        Seg(b(18), b(20), 'f140_yard', 1.0),
        Seg(b(20), b(22), 'f140_market', max(0.0, m140_sell - 1.6)),
    ]
    # CAP_LOW wherever the top slot would hide the proof: the Deliver buttons, the AUTO badges, the Foreman card text.
    ovs = [
        cap(0.0, b(2), 'Break the *chains*!', cy=CAP_LOW),
        cap(b(2), b(4), 'Unlock a *cat factory*!'),
        logo_bug(b(2) + 0.3, b(4), cy=1150),
        cap(b(4), pipe, 'Tap *Deliver*!', cy=CAP_LOW),
        cap(pipe, b(6.5), 'Pipes carry the *goods*'),
        cap(b(6.5), b(8), 'More factories, more *goods*!', cy=CAP_LOW),
        cap(b(8), b(10), 'Sell goods for *gold*!'),
        cap(b(10), b(12), 'Level up the *Foreman*...', cy=1090),     # hides the card counter, clears the % rows
        cap(b(12), b(14), '...and it runs *itself*!', style='green', cy=CAP_LOW),
        cap(b(14), b(16), 'Every factory on *AUTO*!', cy=CAP_LOW),
        cap(b(16), b(18), 'Build a factory *empire*'),
        cap(b(18), b(20), 'Cute cats, *busy* factories'),
        cap(b(20), b(22), 'Sell for *huge* profits!', cy=CAP_LOW),
        flash(b(2), 'gold4'), flash(b(12), 'gold4'), flash(b(16), 'gold4'),
        *end_card('Run your own *cat factory*!'),
    ]
    sfx = [(0.0, 'whoosh', 0.35), (b(2) - 0.02, 'impact', 0.5), (b(6.5) - 0.2, 'whoosh', 0.45),
           (b(8) + 2.1, 'cash', 0.5), (b(10) + 1.75, 'ding', 0.45), (b(16) - 0.2, 'whoosh', 0.45),
           (b(20) + 1.8, 'cash', 0.55), *pops(ovs)]
    return Ad('CatMiners_Ad4_CatFactories_45s', 't1_progress', segs, ovs, sfx)


# ───────────────────────── Cut-downs and extensions (other lengths and 4:5) ─────────────────────────
# 30 s beds (16 bars): hook 0-2, verse 2-6, build 6-8, drop 8-14, end card 14-16.
# 60 s bed (32 bars): hook 0-2, verse 2-10, build 10-12, drop 12-20, breakdown 20-22, build 22-24, drop 24-30, end 30-32.
def ad1_30() -> Ad:
    """Ad 1 in 30 s: 2 cats -> more cats -> upgrades -> legendary gear -> new tunnels -> Mine 100."""
    hire0 = mark('m1_hire', 'hire') - 0.5
    gate = mark('m13_gate', 'gate_open')
    segs = [
        Seg(0.0, b(2), 'm100_swarm', 1.0, split=('m1_start', 1.5, 1180, 1120)),
        Seg(b(2), b(4), 'm1_start', 5.25, view=zoom_to(1.0, (540, 960), 2.0, (480, 820))),
        Seg(b(4), b(6), 'm1_hire', hire0),
        Seg(b(6), b(8), 'm1_base', 1.2),
        Seg(b(8), b(10), 'm13_glowup', 0.6),
        Seg(b(10), b(12), 'm13_gate', max(0.0, gate - 2.6)),
        Seg(b(12), b(14), 'm100_swarm', 5.0),
    ]
    ovs = [
        Ov('split_labels', 0.0, b(2), dict(top='LEVEL 100', top_style='gold', top_icon='icon_item_hat_legendary',
                                           top_cy=860, bottom='LEVEL 1', bottom_style='blue', bottom_icon='icon_cat',
                                           bottom_cy=1150)),
        cap(b(2), b(4), 'It starts with *2 cats*...'),
        logo_bug(b(2) + 0.3, b(4), cy=1150),
        cap(b(4), b(6), 'Hire *more cats*!'),
        cap(b(6), b(8), 'Upgrade *everything*', cy=CAP_HI),
        cap(b(8), b(10), 'Equip *legendary* gear'),
        cap(b(10), b(12), 'Break into *new tunnels*'),
        cap(b(12), b(14), '...all the way to *Mine 100*!'),
        flash(b(2)), flash(b(8), 'gold4'), flash(b(12), 'gold4'),
        *end_card('How deep can *YOUR* cats dig?', 30.0),
    ]
    sfx = [(0.0, 'impact', 0.5), (b(2) - 0.2, 'whoosh', 0.45), (b(8) - 0.2, 'whoosh', 0.45),
           (b(12) - 0.2, 'whoosh', 0.45), *pops(ovs)]
    return Ad('CatMiners_Ad1_Level1toLevel100_30s_1080x1920', 't1_progress_30', segs, ovs, sfx, length=30.0)


def ad3_30() -> Ad:
    """Ad 3 in 30 s, 4:5: chest -> cards -> collection -> level up -> gear -> Mine 100 -> new tunnels.
    Each segment picks its 1350 px window of the 9:16 frame so the proof stays in shot: "Collected 61 of 61" above
    the cards grid, the HUD's "Mine 100" pinned on top of the field."""
    t_open, tx, ty, _ = taps('m100_store_chest')[0]
    chest_window = t_open + 1 / 30
    upgrade2 = taps('cards_upgrade')[1][0]
    segs = [
        Seg(0.0, b(2), 'm100_store_chest', 0.4, speed=0.0, paws=False, audio=0.0, hover=(tx, ty, b(2) - 0.15),
            view=zoom_to(1.0, (540, 960), 1.8, (tx, ty))),
        Seg(b(2), b(4), 'm100_store_chest', chest_window, paws=False,
            view=zoom_to(1.0, (540, 960), 1.15, (540, 1000)), win_y=250),
        Seg(b(4), b(6), 'cards_scroll', 0.4, audio=0.0, win_y=150),
        # The Upgrade button sits below the window: the level ticks up (Lv 2 -> 3, toast) without the paw.
        Seg(b(6), b(8), 'cards_upgrade', upgrade2 - 1.0, audio=0.0, paws=False, win_y=100),
        Seg(b(8), b(10), 'equip_upgrade', 0.6, audio=0.0, win_y=250),
        Seg(b(10), b(12), 'm100_swarm', 1.5, hud_pin=True, win_y=330),
        Seg(b(12), b(14), 'm13_gate', max(0.0, mark('m13_gate', 'gate_open') - 2.2), win_y=240),
    ]
    ovs = [
        cap(0.0, b(2), 'What\'s *inside*?'),
        legal(0.0, b(4)),
        cap(b(2), b(4), 'Full of *cat cards*!', cy=CAP_HI),
        logo_bug(b(2) + 0.3, b(4), cy=1530),
        cap(b(4), b(6), 'Collect *61* cat cards', cy=CAP_LOW),
        cap(b(6), b(8), 'Level them *up*', cy=1090),
        cap(b(8), b(10), 'Upgrade your *gear*', cy=440),
        cap(b(10), b(12), 'All the way to *Mine 100*!', cy=520),
        cap(b(12), b(14), 'Open *new tunnels*!'),
        flash(b(2), 'gold4'), flash(b(8), 'gold4'),
        *end_card('Which cat will *you* find?', 30.0),
    ]
    sfx = [(b(2), 'sparkle', 0.55), (b(6) + 1.0, 'ding', 0.45), *pops(ovs)]
    return Ad('CatMiners_Ad3_ChestsCollection_30s_1080x1350', 't3_chest_30', segs, ovs, sfx, length=30.0, out_h=1350)


def ad4_60() -> Ad:
    """Ad 4 in 60 s: the 45 s story plus goods arriving on their own, the pan into the base, Mine 10 vs Mine 140
    and the market's goods list before the big sale."""
    unlocked = mark('f10_chains', 'unlocked')
    deliver = taps('f10_deliver')[0][0]
    trio = taps('f30_deliver')[0][0]
    m30_sell = taps('f30_market')[-1][0]
    upgrade = taps('f3_card')[0][0]
    auto_send = first_core('f3_auto', 'GoodsDispatched', after=0.8, default=1.8)
    auto_arrive = first_core('f3_auto', 'GoodsArrived', after=8.0, default=13.0)    # camera is at the base by then
    m140_send = first_core('f140_factories', 'GoodsDispatched', default=6.0)
    m140_sell = taps('f140_market')[-1][0]
    market0 = max(0.0, m140_sell - 1.6 - b(2))             # the paw opens the Market, the list scrolls to Caviar
    pipe = b(5.25)
    segs = [
        Seg(0.0, b(2), 'f10_chains', max(0.0, unlocked - b(2) + 0.05)),
        Seg(b(2), b(4), 'f10_chains', unlocked + 0.05),
        Seg(b(4), b(6.5), 'f10_deliver', max(0.0, deliver - 1.7)),
        Seg(b(6.5), b(8), 'f30_deliver', max(0.0, trio - 0.6)),
        Seg(b(8), b(10), 'f30_market', max(0.0, m30_sell - 1.9)),
        Seg(b(10), b(12), 'f3_card', max(0.0, upgrade - 1.5), audio=0.8),
        Seg(b(12), b(14), 'f3_auto', max(0.0, auto_send - 1.2), view=zoom_to(1.0, (540, 960), 1.4, (600, 820))),
        Seg(b(14), b(16), 'f3_auto', max(0.0, auto_arrive - 2.8), view=zoom_to(1.0, (540, 960), 1.3, (760, 1200))),
        Seg(b(16), b(18), 'f140_factories', max(0.0, m140_send - 1.5), view=zoom_to(1.0, (540, 960), 1.25, (550, 700))),
        Seg(b(18), b(20), 'f140_overview', 0.5, audio=0.3),
        Seg(b(20), b(22), 'f140_overview', 0.5 + b(2), audio=0.3),
        Seg(b(22), b(24), 'f10_chains', unlocked + 0.3, split=('f140_factories', 0.5, 640, 520)),
        Seg(b(24), b(26), 'f140_market', market0),
        Seg(b(26), b(28), 'f140_market', market0 + b(2)),
        Seg(b(28), b(30), 'f140_yard', 1.0),
    ]
    ovs = [
        cap(0.0, b(2), 'Break the *chains*!', cy=CAP_LOW),
        cap(b(2), b(4), 'Unlock a *cat factory*!'),
        logo_bug(b(2) + 0.3, b(4), cy=1150),
        cap(b(4), pipe, 'Tap *Deliver*!', cy=CAP_LOW),
        cap(pipe, b(6.5), 'Pipes carry the *goods*'),
        cap(b(6.5), b(8), 'More factories, more *goods*!', cy=CAP_LOW),
        cap(b(8), b(10), 'Sell goods for *gold*!'),
        cap(b(10), b(12), 'Level up the *Foreman*...', cy=1090),
        cap(b(12), b(14), '...and it runs *itself*!', style='green', cy=CAP_LOW),
        cap(b(14), b(16), 'Goods arrive *on their own*'),
        cap(b(16), b(18), 'Every factory on *AUTO*!', cy=CAP_LOW),
        cap(b(18), b(20), 'Build a factory *empire*'),
        cap(b(20), b(22), 'All pipes lead to *your base*'),
        Ov('split_labels', b(22), b(24), dict(top='MINE 10', top_style='blue', top_icon='icon_cat', top_cy=860,
                                             bottom='MINE 140', bottom_style='gold',
                                             bottom_icon='icon_item_hat_legendary', bottom_cy=1150)),
        cap(b(24), b(26), 'Wood, fish, yarn... *caviar*!', cy=CAP_LOW),
        cap(b(26), b(28), 'Sell for *huge* profits!', cy=CAP_LOW),
        cap(b(28), b(30), 'Cute cats, *busy* factories'),
        flash(b(2), 'gold4'), flash(b(12), 'gold4'), flash(b(18), 'gold4'), flash(b(24), 'gold4'),
        *end_card('Run your own *cat factory*!', 60.0),
    ]
    sell = b(24) + (m140_sell - market0)
    sfx = [(0.0, 'whoosh', 0.35), (b(2) - 0.02, 'impact', 0.5), (b(6.5) - 0.2, 'whoosh', 0.45),
           (b(8) + 2.1, 'cash', 0.5), (b(10) + 1.75, 'ding', 0.45), (b(18) - 0.2, 'whoosh', 0.45),
           (b(22) - 0.2, 'whoosh', 0.45), (b(24) - 0.2, 'whoosh', 0.45), (sell + 0.2, 'cash', 0.55), *pops(ovs)]
    return Ad('CatMiners_Ad4_CatFactories_60s_1080x1920', 't1_progress_60', segs, ovs, sfx, length=60.0)


ADS = {'ad1': ad1, 'ad2': ad2, 'ad3': ad3, 'ad4': ad4, 'ad1_30': ad1_30, 'ad3_30': ad3_30, 'ad4_60': ad4_60}
