#!/usr/bin/env python3
"""Темп b6: множитель темпа шахты K(z) (решение владельца В4, 15 октября 2026).

Шахта, где есть медленные заводы, длится около трёх циклов самого медленного завода: цель D(z) = 3 × C(z), C(z) — самый
длинный CycleSec заводов шахты z. K(z) = 1 там, где 3 × C(z) не длиннее длительности b5 (D0), и в шахтах 1–11 (playable).
Иначе K(z) = 3 × C(z) / D0(z) × corr(z): D0 и C — из замера b5 (free seed 1, Docs/Reports/balance-factories/b5),
corr — поправка калибровки (шахта с K не точно в K раз длиннее: доля ходьбы падает), кусочно-линейная по шахтам выборки.
K округляется до 1/100. Подробности — Docs/Rework/BALANCE_FACTORIES.md, «Темп b6: 3 цикла».

Команды (из корня проекта):
  python3 Tools/BalanceAudit/pace_scale.py variants [--zones=12-144] <имя>=<z:corr,...> ... — pace-variants.json для Audit_Pace
  python3 Tools/BalanceAudit/pace_scale.py apply <z:corr,...>                       — PaceScale и Version в outpost_balance.json
  python3 Tools/BalanceAudit/pace_scale.py report <вариант> [...]                   — таблица замера pace/<вариант>.json
  python3 Tools/BalanceAudit/pace_scale.py fit <вариант>                            — поправки corr по замеру варианта
  python3 Tools/BalanceAudit/pace_scale.py summary <вариант>                        — сводка по этапам и кампании
"""
import json
import os
import sys
from fractions import Fraction

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
B5 = os.path.join(ROOT, 'Docs', 'Reports', 'balance-factories', 'b5', 'campaign-free-seed1.json')
PACE = os.path.join(ROOT, 'Docs', 'Reports', 'balance-factories', 'pace')
BALANCE = os.path.join(ROOT, 'Assets', 'Game', 'Resources', 'Outpost', 'outpost_balance.json')
VERSION = 'outpost-2.2-b6'
CYCLES = 3
PLAYABLE_LAST_ZONE = 11
SAMPLE = [19, 24, 31, 47, 57, 68, 80, 93, 104, 114, 123, 134, 144]


def b5_mines():
    with open(B5, encoding='utf-8') as f:
        d = json.load(f)
    rows = {}
    for m in d['Mines']:
        c = max([fa['CycleSec'] for fa in m['Factories']], default=0)
        rows[m['Zone']] = {'D0': m['CompletedSec'], 'C': c}
    return rows


def parse_corr(text):
    pts = []
    for part in text.split(','):
        if not part:
            continue
        z, v = part.split(':')
        pts.append((int(z), float(v)))
    return sorted(pts)


def corr_at(pts, z):
    if not pts:
        return 1.0
    if z <= pts[0][0]:
        return pts[0][1]
    if z >= pts[-1][0]:
        return pts[-1][1]
    for (z0, v0), (z1, v1) in zip(pts, pts[1:]):
        if z0 <= z <= z1:
            return v0 + (v1 - v0) * (z - z0) / (z1 - z0)
    return 1.0


def table(pts):
    mines = b5_mines()
    out = []
    for z in range(1, 145):
        m = mines[z]
        target = CYCLES * m['C']
        if z <= PLAYABLE_LAST_ZONE or target <= m['D0']:
            out.append(Fraction(1))
            continue
        k = target / m['D0'] * corr_at(pts, z)
        out.append(max(Fraction(1), Fraction(round(k * 100), 100)))
    return out


def fmt(fr):
    return str(fr.numerator) if fr.denominator == 1 else '%d/%d' % (fr.numerator, fr.denominator)


def cmd_variants(args):
    os.makedirs(PACE, exist_ok=True)
    variants = []
    zones = SAMPLE
    if args and args[0].startswith('--zones='):
        a, b = args[0].split('=', 1)[1].split('-')
        zones = list(range(int(a), int(b) + 1))
        args = args[1:]
    for a in args:
        name, corr = a.split('=', 1)
        variants.append({'Name': name, 'Corr': corr, 'PaceScale': [fmt(k) for k in table(parse_corr(corr))]})
    spec = {'Model': 'free', 'Seed': 1, 'Zones': zones, 'Variants': variants}
    with open(os.path.join(PACE, 'pace-variants.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump(spec, f, ensure_ascii=False, indent=1)
        f.write('\n')
    print('written pace-variants.json:', ', '.join(v['Name'] for v in variants))


def cmd_apply(args):
    pts = parse_corr(args[0] if args else '')
    ks = table(pts)
    with open(BALANCE, encoding='utf-8', newline='') as f:
        text = f.read()
    lines = text.split('\n')
    out = []
    skip = False
    for ln in lines:
        if ln.startswith('  "PaceScale":'):
            skip = not ln.rstrip().endswith('],')
            continue
        if skip:
            skip = not ln.startswith('  ],')
            continue
        if ln.startswith('  "Version":'):
            ln = '  "Version": "%s",' % VERSION
        out.append(ln)
        if ln.startswith('  "CostZoneStep":'):
            # K(z) шахт 1..144 по 12 в строке (шахты 1–12, 13–24, …).
            out.append('  "PaceScale": [')
            rows = [ks[i:i + 12] for i in range(0, len(ks), 12)]
            for i, row in enumerate(rows):
                out.append('    ' + ', '.join('"%s"' % fmt(k) for k in row) + (',' if i < len(rows) - 1 else ''))
            out.append('  ],')
    with open(BALANCE, 'w', encoding='utf-8', newline='') as f:
        f.write('\n'.join(out))
    print('outpost_balance.json: Version %s, PaceScale corr %s' % (VERSION, args[0] if args else '-'))
    mines = b5_mines()
    for z in SAMPLE:
        print('  zone %3d: C %6.0f s, 3C %5.2f h, D0 %5.2f min, K %s' % (z, mines[z]['C'], 3 * mines[z]['C'] / 3600, mines[z]['D0'] / 60, fmt(ks[z - 1])))


def load(name):
    with open(os.path.join(PACE, name + '.json'), encoding='utf-8') as f:
        return json.load(f)


def summary(row):
    m = row['Mine']
    d = m['CompletedSec'] or 0
    c = max([fa['CycleSec'] for fa in m['Factories']], default=0)
    slow = [fa for fa in m['Factories'] if fa['CycleSec'] == c]
    last = max([p['Sec'] for p in (m.get('PurchaseLog') or [])], default=0)
    gold = (m['CreditedBase'] or 0) + (m['GoodsSoldBase'] or 0)
    return {
        'zone': row['Zone'], 'K': float(Fraction(row['K'])), 'C': c, 'D': d, 'D0': row['BaseSec'],
        'ratio': d / (CYCLES * c) if c else 0, 'eps': d / (float(Fraction(row['K'])) * row['BaseSec']),
        'last': last / d if d else 0, 'unaff': m['MaxUnaffordableSec'], 'gap': m['MaxPurchaseGapSec'],
        'goods': (m['GoodsSoldBase'] or 0) / gold if gold else 0, 'purchases': m['Purchases'],
        'slowCycles': [fa['Cycles'] for fa in slow], 'slowUnlock': [fa['UnlockSec'] / d if d else 0 for fa in slow],
        'outcome': m['Outcome'], 'wall': row['WallSec'], 'levels': m['Levels'],
        'cycles': sum(fa['Cycles'] for fa in m['Factories']), 'factories': len(m['Factories']),
        'zeroCycle': sum(1 for fa in m['Factories'] if fa['Cycles'] == 0),
        'lost': (m['GoodsLostBase'] or 0) + (m['GoodsLostReadyBase'] or 0), 'sold': m['GoodsSoldBase'] or 0,
    }


def cmd_report(args):
    for name in args:
        d = load(name)
        print('# %s (base campaign wall %.0f s)' % (name, d['BaseCampaignWallSec']))
        print('| Шахта | C(z) | Цель 3C | D0 b5, мин | K(z) | D, ч | D / 3C | Последняя покупка, % D | Макс. без покупки, % D '
              '| Доля товара | Циклы самого медленного (открыт на % D) | Заводы без цикла | Итог | Время счёта, с |')
        print('|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---|---:|')
        for row in sorted(d['Rows'], key=lambda r: r['Zone']):
            s = summary(row)
            print('| %d | %s | %.2f ч | %.1f | %.2f | %.2f | %.2f | %.0f | %.1f | %.0f %% | %s (%s) | %d из %d | %s | %.0f |' % (
                s['zone'], dur(s['C']), CYCLES * s['C'] / 3600, s['D0'] / 60, s['K'], s['D'] / 3600, s['ratio'],
                100 * s['last'], 100 * s['unaff'] / s['D'] if s['D'] else 0, 100 * s['goods'],
                '/'.join(str(x) for x in s['slowCycles']), '/'.join('%.0f' % (100 * x) for x in s['slowUnlock']),
                s['zeroCycle'], s['factories'], s['outcome'], s['wall']))


def dur(sec):
    if sec >= 3600:
        return ('%.1f ч' % (sec / 3600)).replace('.0 ', ' ')
    return '%d мин' % (sec // 60)


def cmd_fit(args):
    d = load(args[0])
    if any(r.get('Mine', {}).get('IntentionalFinalExcavation') for r in d['Rows']):
        raise SystemExit('Final excavations are deliberate difficulty spikes. Fit ordinary-phase reports made with WithFinalExcavations(null).')
    pts = []
    for row in sorted(d['Rows'], key=lambda r: r['Zone']):
        s = summary(row)
        if s['K'] <= 1 or not s['D']:
            continue
        # D ≈ K·D0·ε → нужный K' = K·3C/D, corr' = K'·D0/(3C) = K·D0/D (D0 — шахта той же кампании K = 1).
        pts.append('%d:%.3f' % (s['zone'], s['K'] * s['D0'] / s['D']))
    print(','.join(pts))


def cmd_summary(args):
    """Сводка варианта по всем шахтам: D/3C, покупки, товар, циклы по этапам, длительность кампании."""
    d = load(args[0])
    mines = b5_mines()
    rows = {r['Zone']: summary(r) for r in d['Rows']}
    total = sum(rows[z]['D'] if z in rows else mines[z]['D0'] for z in range(1, 145))
    print('campaign (free seed 1, zones 1-11 = b5): %.1f days (b5 %.2f h)' % (total / 86400, sum(m['D0'] for m in mines.values()) / 3600))
    paced = [s for s in rows.values() if s['K'] > 1]
    ratios = sorted(s['ratio'] for s in paced)
    intentional = {r['Zone'] for r in d['Rows'] if r.get('Mine', {}).get('IntentionalFinalExcavation')}
    out = [s['zone'] for s in paced if s['zone'] not in intentional and not 0.75 <= s['ratio'] <= 1.25]
    print('K>1 mines: %d; D/3C min %.2f median %.2f max %.2f; outside ±25%%: %s' % (len(paced), ratios[0], ratios[len(ratios) // 2], ratios[-1], out or '-'))
    print('last purchase: min %.0f%% D (zone %d); max unaffordable: max %.1f%% D (zone %d), max / (120*K) %.2f' % (
        100 * min(s['last'] for s in paced), min(paced, key=lambda s: s['last'])['zone'],
        100 * max(s['unaff'] / s['D'] for s in paced), max(paced, key=lambda s: s['unaff'] / s['D'])['zone'],
        max(s['unaff'] / (120 * s['K']) for s in paced)))
    print('outcomes: %s' % sorted(set(s['outcome'] for s in rows.values())))
    for a, b in [(12, 30), (31, 60), (61, 100), (101, 144)]:
        st = [rows[z] for z in range(a, b + 1) if z in rows]
        if not st:
            continue
        med = sorted(x['D'] for x in st)[len(st) // 2]
        goods = sum(x['goods'] for x in st) / len(st)
        fz = sum(x['factories'] for x in st)
        zero = sum(x['zeroCycle'] for x in st)
        slow0 = sum(1 for x in st if all(c == 0 for c in x['slowCycles']))
        lost = sum(x['lost'] for x in st) / max(1e-9, sum(x['sold'] for x in st))
        print('zones %d-%d: median D %.2f h; goods share %.0f%%; factories without a cycle %d of %d; slowest factory 0 cycles in %d of %d mines; lost/sold %.0f%%; max wall %.0f s' % (
            a, b, med / 3600, 100 * goods, zero, fz, slow0, len(st), 100 * lost, max(x['wall'] for x in st)))
    print('wall: sum %.0f s, max %.0f s' % (sum(s['wall'] for s in rows.values()), max(s['wall'] for s in rows.values())))


if __name__ == '__main__':
    cmd = sys.argv[1] if len(sys.argv) > 1 else ''
    {'variants': cmd_variants, 'apply': cmd_apply, 'report': cmd_report, 'fit': cmd_fit, 'summary': cmd_summary}.get(cmd, lambda a: print(__doc__))(sys.argv[2:])
