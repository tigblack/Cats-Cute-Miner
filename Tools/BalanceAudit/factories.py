#!/usr/bin/env python3
"""Разбор баланса заводов ТЗ 2.2 §9 (Docs/Rework/BALANCE_FACTORIES.md).

Данные — JSON генераторов OutpostBalanceAuditTests:
  * калибровка PriceRate: Audit_TuneFactories → Docs/Reports/balance-factories/tune/<вариант>/campaign-free-seed*.json;
  * замер профиля b5: Audit_After → Docs/Reports/balance-factories/b5/campaign-*.json;
  * эталон b4 (ядро ТЗ 2.1, цепи с грузом): Docs/Reports/balance-audit/after/campaign-*.json.

Запуск из корня проекта:
  python3 Tools/BalanceAudit/factories.py calibrate   # таблица перебора → balance-factories/calibration.{json,md}
  python3 Tools/BalanceAudit/factories.py report      # таблицы отчёта → balance-factories/factories-{summary.json,tables.md}

Медиана калибровки (§9): медиана четырёх шахт 2–5 в каждом seed, затем медиана по seed (statistics.median).
Деньги — базовые монеты шахты: золото / (CoinBase · S(z)); золото товара — отдельно от монет (§6.4).
"""
import glob
import json
import math
import os
import re
import statistics
import sys
from fractions import Fraction

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
FACT = os.path.join(ROOT, 'Docs', 'Reports', 'balance-factories')
B4 = os.path.join(ROOT, 'Docs', 'Reports', 'balance-audit', 'after')
B5 = os.path.join(FACT, 'b5')
TUNE = os.path.join(FACT, 'tune')

TARGET = (4.22, 5.70)          # ±15 % от b4 4,96 мин (§9)
B4_MEDIAN = 4.96
# Ориентиры этапов — как в тесте Campaign_FreePlayer_PacingWithinStageTargets (у шахты 1 верхняя граница 5,5).
STAGES = [('1', 1, 1, 3.0, 5.5), ('2–10', 2, 10, 3.0, 7.0), ('11–30', 11, 30, 4.0, 9.0), ('31–60', 31, 60, 5.0, 11.0),
          ('61–100', 61, 100, 6.0, 13.0), ('101–144', 101, 144, 8.0, 15.0)]
# Заводы — с шахты 2; доли автодоставки и циклы считаются по этапам с заводами.
FSTAGES = [('2–10', 2, 10), ('11–30', 11, 30), ('31–60', 31, 60), ('61–100', 61, 100), ('101–144', 101, 144)]
# Приложение A ТЗ 2.2: тип → (завод, товар).
NAMES = {
    'amethyst': ('Dairy', 'Milk'), 'citrine': ('Sawmill', 'Wood'), 'agate': ('Fishery', 'Fish'), 'topaz': ('Yarn Mill', 'Yarn'),
    'opal': ('Cat Food Factory', 'Cat Food'), 'jade': ('Box Factory', 'Boxes'), 'onyx': ('Brickworks', 'Bricks'),
    'sapphire': ('Steel Mill', 'Steel'), 'tourmaline': ('Glassworks', 'Glass'), 'aquamarine': ('Catnip Farm', 'Catnip'),
    'emerald': ('Toy Factory', 'Toys'), 'diamond': ('Scratcher Works', 'Scratching Posts'), 'garnet': ('Cushion Factory', 'Cushions'),
    'heliodor': ('Bell Foundry', 'Bells'), 'ruby': ('Collar Works', 'Collars'), 'alexandrite': ('Gear Works', 'Gears'),
    'celestine': ('Lantern Works', 'Lanterns'), 'amber': ('Apiary', 'Honey'), 'moonstone': ('Silk Mill', 'Silk'),
    'sunstone': ('Caviar Cannery', 'Caviar'),
}


def med(xs):
    xs = [x for x in xs if x is not None and not (isinstance(x, float) and math.isnan(x))]
    return statistics.median(xs) if xs else float('nan')


def fmt(x, nd=2):
    if x is None or (isinstance(x, float) and math.isnan(x)):
        return '—'
    return ('%.' + str(nd) + 'f') % x


def pct(x, nd=0):
    return fmt(100 * x, nd) + ' %' if x == x else '—'


def load_dir(d):
    runs = {}
    for path in sorted(glob.glob(os.path.join(d, 'campaign-*.json'))):
        with open(path, encoding='utf-8') as f:
            a = json.load(f)
        runs[(a['Scenario'], int(a['Seed']))] = a
    return runs


def minutes(m):
    return (m.get('CompletedSec') or float('nan')) / 60.0


def by_zone(a):
    return {m['Zone']: m for m in a['Mines']}


def calib_median(campaigns):
    per_seed = []
    for a in campaigns:
        z = by_zone(a)
        four = [minutes(z[k]) for k in (2, 3, 4, 5) if k in z]
        if len(four) == 4:
            per_seed.append(statistics.median(four))
    return statistics.median(per_seed) if per_seed else float('nan'), per_seed


def frac_key(text):
    try:
        return Fraction(text)
    except (TypeError, ValueError, ZeroDivisionError):
        return Fraction(10 ** 9)


# ───────────────────────────── Калибровка ─────────────────────────────

def calibrate():
    rows = []
    for d in sorted(glob.glob(os.path.join(TUNE, '*'))):
        runs = load_dir(d)
        free = [a for (s, _), a in sorted(runs.items()) if s == 'free']
        if not free:
            continue
        median, per_seed = calib_median(free)
        zones = {k: med([minutes(by_zone(a)[k]) for a in free if k in by_zone(a)]) for k in (2, 3, 4, 5)}
        upto5 = [m for a in free for m in a['Mines'] if m['Zone'] <= 5]
        goods = sum(m.get('GoodsSoldBase') or 0 for m in upto5 if m['Zone'] >= 2)
        coins = sum(m.get('CreditedBase') or 0 for m in upto5 if m['Zone'] >= 2)
        rows.append({
            'variant': os.path.basename(d), 'priceRate': free[0].get('PriceRate'), 'seeds': len(free),
            'median2to5': median, 'perSeedMin': min(per_seed) if per_seed else None, 'perSeedMax': max(per_seed) if per_seed else None,
            'zones': zones, 'inTarget': TARGET[0] <= median <= TARGET[1],
            'deviation': median / B4_MEDIAN - 1 if median == median else None,
            'maxUnaffordable1to5': max((m.get('MaxUnaffordableSec') or 0) for m in upto5) if upto5 else None,
            'goodsShare2to5': goods / (goods + coins) if goods + coins > 0 else 0,
            'success': all(a['Success'] for a in free),
        })
    rows.sort(key=lambda r: frac_key(r['priceRate']))
    with open(os.path.join(FACT, 'calibration.json'), 'w', encoding='utf-8') as f:
        json.dump({'target': TARGET, 'b4Median': B4_MEDIAN, 'rows': rows}, f, ensure_ascii=False, indent=1)
    lines = ['| PriceRate | Seed | Медиана 2–5, мин | Разброс по seed | Шахта 2 | 3 | 4 | 5 | Отклонение от b4 | В пределе | Доля товара (2–5) | Макс. без покупки (1–5), с |',
             '|---|---:|---:|---|---:|---:|---:|---:|---:|---|---:|---:|']
    for r in rows:
        z = r['zones']
        lines.append('| %s | %d | %s | %s–%s | %s | %s | %s | %s | %s | %s | %s | %s |' % (
            r['priceRate'], r['seeds'], fmt(r['median2to5']), fmt(r['perSeedMin']), fmt(r['perSeedMax']),
            fmt(z[2]), fmt(z[3]), fmt(z[4]), fmt(z[5]), ('%+.1f %%' % (100 * r['deviation'])) if r['deviation'] is not None else '—',
            'да' if r['inTarget'] else 'нет', pct(r['goodsShare2to5']), fmt(r['maxUnaffordable1to5'], 0)))
    text = '\n'.join(lines) + '\n'
    with open(os.path.join(FACT, 'calibration.md'), 'w', encoding='utf-8') as f:
        f.write(text)
    print(text)
    best = min((r for r in rows if r['median2to5'] == r['median2to5']), key=lambda r: abs(r['median2to5'] - B4_MEDIAN), default=None)
    if best:
        print('closest to b4 %.2f: PriceRate %s → %.3f min (%s)' % (B4_MEDIAN, best['priceRate'], best['median2to5'],
                                                                  'in target' if best['inTarget'] else 'OUT of target'))


# ───────────────────────────── Отчёт ─────────────────────────────

def stage_of(z, stages):
    for st in stages:
        if st[1] <= z <= st[2]:
            return st[0]
    return None


def pacing(b5, b4):
    """Медианы этапов, отношения соседних шахт, условия теста темпа (отключённые до нового баланса)."""
    out = {}
    for key in sorted(b5):
        scen, seed = key
        a5 = b5[key]
        a4 = b4.get(key)
        z5 = by_zone(a5)
        z4 = by_zone(a4) if a4 else {}
        st = []
        prev = None
        for name, lo_z, hi_z, lo, hi in STAGES:
            m5 = [minutes(z5[z]) for z in range(lo_z, hi_z + 1) if z in z5]
            m4 = [minutes(z4[z]) for z in range(lo_z, hi_z + 1) if z in z4]
            # Как тест: «медиана» — элемент [n/2] отсортированного списка.
            t5 = sorted(m5)[len(m5) // 2] if m5 else float('nan')
            st.append({'stage': name, 'target': [lo, hi], 'b5': med(m5), 'b4': med(m4), 'testMedian': t5,
                       'inRange': lo <= t5 <= hi, 'curveOk': prev is None or lo_z <= 2 or t5 >= 0.95 * prev})
            prev = t5
        ratios = []
        zones = sorted(z5)
        for i in range(1, len(zones)):
            a, b = z5[zones[i - 1]], z5[zones[i]]
            ratios.append((zones[i], minutes(b) / minutes(a)))
        out['%s/seed%d' % key] = {
            'success': a5['Success'], 'mines': len(a5['Mines']), 'hours': a5['TotalSec'] / 3600,
            'hoursB4': a4['TotalSec'] / 3600 if a4 else None, 'stages': st,
            'maxNeighbourRatio': max((r for _, r in ratios), default=float('nan')),
            'jumps22': [(z, r) for z, r in ratios if r >= 2.2],
            'maxUnaffordable': max(((m.get('MaxUnaffordableSec') or 0), m['Zone']) for m in a5['Mines']),
            'unaffordable120': [m['Zone'] for m in a5['Mines'] if (m.get('MaxUnaffordableSec') or 0) >= 120],
        }
    return out


def gold(a):
    rows = []
    for m in a['Mines']:
        coins, goods = m.get('CreditedBase') or 0, m.get('GoodsSoldBase') or 0
        rows.append({'zone': m['Zone'], 'coins': coins, 'goods': goods, 'share': goods / (coins + goods) if coins + goods else 0,
                     'lost': m.get('GoodsLostBase') or 0, 'lostReady': m.get('GoodsLostReadyBase') or 0})
    stages = []
    for name, lo, hi in FSTAGES:
        sel = [r for r in rows if lo <= r['zone'] <= hi]
        c, g = sum(r['coins'] for r in sel), sum(r['goods'] for r in sel)
        stages.append({'stage': name, 'coins': c, 'goods': g, 'share': g / (c + g) if c + g else 0,
                       'medianShare': med([r['share'] for r in sel]), 'lost': sum(r['lost'] for r in sel),
                       'lostShareOfGoods': sum(r['lost'] for r in sel) / g if g else float('nan'),
                       'lostReady': sum(r['lostReady'] for r in sel)})
    return rows, stages


def cycles_by_type(a):
    """Циклы по типам: в шахте первого появления типа и в остальных; заводы и шахты без единого цикла."""
    first = {}
    for m in a['Mines']:
        for f in m.get('Factories') or []:
            first.setdefault(f['TypeId'], m['Zone'])
    types = {}
    zero_mines = []
    for m in a['Mines']:
        fs = m.get('Factories') or []
        if fs and sum(f.get('Cycles') or 0 for f in fs) == 0:
            zero_mines.append(m['Zone'])
        for f in fs:
            t = types.setdefault(f['TypeId'], {'type': f['TypeId'], 'number': f.get('TypeNumber'), 'cycleSec': f.get('CycleSec'),
                                                'firstZone': first[f['TypeId']], 'firstCycles': 0, 'firstWorkMin': None,
                                                'otherFactories': 0, 'otherCycles': 0, 'otherZero': 0, 'factories': 0, 'zero': 0,
                                                'otherWorkSec': 0.0})
            t['factories'] += 1
            c = f.get('Cycles') or 0
            work = (m.get('CompletedSec') or 0) - f['UnlockSec'] if f.get('UnlockSec') is not None else 0
            if c == 0:
                t['zero'] += 1
            if m['Zone'] == t['firstZone']:
                t['firstCycles'] += c
                t['firstWorkMin'] = work / 60
            else:
                t['otherFactories'] += 1
                t['otherCycles'] += c
                t['otherWorkSec'] += work
                if c == 0:
                    t['otherZero'] += 1
    rows = sorted(types.values(), key=lambda t: t['number'] or 0)
    for t in rows:
        t['otherMeanCycles'] = t['otherCycles'] / t['otherFactories'] if t['otherFactories'] else float('nan')
        t['otherMeanWorkMin'] = t['otherWorkSec'] / t['otherFactories'] / 60 if t['otherFactories'] else float('nan')
    return rows, zero_mines


def auto_running(m, f):
    """Автодоставка включилась, пока шахта шла (до завершения); карточка из сундука выхода этой шахте уже не помогает."""
    return f.get('AutoSec') is not None and m.get('CompletedSec') is not None and f['AutoSec'] < m['CompletedSec']


def automation(a):
    st = []
    for name, lo, hi in FSTAGES:
        fs = [(m, f) for m in a['Mines'] if lo <= m['Zone'] <= hi for f in (m.get('Factories') or [])]
        n = len(fs)
        st.append({'stage': name, 'factories': n,
                   'autoStart': sum(1 for _, f in fs if f.get('AutoAtStart')) / n if n else float('nan'),
                   'autoEnd': sum(1 for m, f in fs if auto_running(m, f)) / n if n else float('nan'),
                   'autoEndExit': sum(1 for _, f in fs if f.get('AutoAtEnd')) / n if n else float('nan'),
                   'common': sum(1 for _, f in fs if is_common(f)) / n if n else float('nan')})
    late = [(m, f) for m in a['Mines'] if m['Zone'] >= 31 for f in (m.get('Factories') or [])]
    late_end = sum(1 for m, f in late if auto_running(m, f)) / len(late) if late else float('nan')
    late_start = sum(1 for _, f in late if f.get('AutoAtStart')) / len(late) if late else float('nan')
    return st, late_start, late_end


def level_gaps(a, lo=31, hi=144):
    """Недостача уровня карточки требования в начале шахты: требование − уровень (≤ 0 — автомат) и доля «с +k уровнями»."""
    gaps = {}
    n = 0
    zero = 0
    for m in a['Mines']:
        if not lo <= m['Zone'] <= hi:
            continue
        for f in m.get('Factories') or []:
            n += 1
            g = max(0, (f.get('RequiredCardLevel') or 0) - (f.get('CardLevelStart') or 0))
            if (f.get('CardLevelStart') or 0) == 0:
                zero += 1
                g = max(g, 1)
            gaps[g] = gaps.get(g, 0) + 1
    within = {k: sum(c for g, c in gaps.items() if g <= k) / n if n else float('nan') for k in (0, 1, 2, 3)}
    elixir = [m.get('Elixir') for m in a['Mines'] if lo <= m['Zone'] <= hi]
    return {'factories': n, 'gaps': dict(sorted(gaps.items())), 'withPlusLevels': within, 'notCollected': zero,
            'elixirMedian': med(elixir), 'elixirEnd': elixir[-1] if elixir else None}


COMMON = {'ca%03d' % i for i in list(range(2, 14)) + [39, 41, 44, 46, 48, 56, 58, 60]}


def is_common(f):
    return f.get('RequiredCardId') in COMMON


def deliveries(a):
    rows = []
    for m in a['Mines']:
        mins = minutes(m)
        rows.append({'zone': m['Zone'], 'perMin': (m.get('ManualDeliveries') or 0) / mins if mins > 0 else 0,
                     'total': m.get('ManualDeliveries') or 0})
    st = []
    for name, lo, hi in [('2–30', 2, 30)] + FSTAGES:
        sel = [r for r in rows if lo <= r['zone'] <= hi]
        st.append({'stage': name, 'median': med([r['perMin'] for r in sel]), 'max': max((r['perMin'] for r in sel), default=0),
                   'maxZone': max(sel, key=lambda r: r['perMin'])['zone'] if sel else None,
                   'over12': [r['zone'] for r in sel if r['perMin'] > 12]})
    return rows, st


def offline(a):
    by_h = {}
    for m in a['Mines']:
        coins, goods = m.get('CreditedBase') or 0, m.get('GoodsSoldBase') or 0
        for ab in m.get('Absences') or []:
            by_h.setdefault(ab['Hours'], []).append((m, ab, coins + goods))
    out = {}
    for h, items in sorted(by_h.items()):
        st = []
        for name, lo, hi in [('все', 1, 144)] + FSTAGES:
            sel = [(m, ab, inc) for m, ab, inc in items if lo <= m['Zone'] <= hi]
            if not sel:
                continue
            fac = [(m, ab, inc) for m, ab, inc in sel if ab.get('Factories')]
            st.append({
                'stage': name, 'mines': len(sel),
                'returnedBefore': sum(1 for _, ab, _ in sel if ab['ReturnedBeforeComplete']) / len(sel),
                'warehouseAuto': sum(ab['WarehouseAutoBase'] for _, ab, _ in sel),
                'warehouseManual': sum(ab['WarehouseManualBase'] for _, ab, _ in sel),
                'readyManual': sum(ab['ReadyManualBase'] for _, ab, _ in sel),
                'pipes': sum(ab['PipesBase'] for _, ab, _ in sel),
                'lost': sum(ab['LostBase'] for _, ab, _ in sel),
                'lostReady': sum(ab['LostReadyBase'] for _, ab, _ in sel),
                'income': sum(inc for _, _, inc in sel),
                'medianWarehouseShare': med([(ab['WarehouseAutoBase'] + ab['WarehouseManualBase']) / inc for _, ab, inc in fac if inc > 0]),
                'medianLostShare': med([ab['LostBase'] / inc for _, ab, inc in fac if inc > 0]),
                'autoFactories': sum(ab['AutoFactories'] for _, ab, _ in sel), 'manualFactories': sum(ab['ManualFactories'] for _, ab, _ in sel),
                'locked': sum(ab['Locked'] for _, ab, _ in sel),
            })
        out[h] = st
    return out


def report():
    b5 = load_dir(B5)
    b4 = load_dir(B4)
    if not b5:
        raise SystemExit('нет данных %s — сначала Audit_After' % B5)
    summary = {'pacing': pacing(b5, b4)}
    free = [a for (s, _), a in sorted(b5.items()) if s == 'free']
    summary['calibration'] = dict(zip(('median', 'perSeed'), calib_median(free)))
    free4 = [a for (s, _), a in sorted(b4.items()) if s == 'free']
    summary['calibrationB4'] = dict(zip(('median', 'perSeed'), calib_median(free4)))
    models = {}
    for key in (('free', 1), ('active', 1), ('idle', 1)):
        if key not in b5:
            continue
        a = b5[key]
        rows, gst = gold(a)
        cyc, zero = cycles_by_type(a)
        ast, late_start, late_end = automation(a)
        drows, dst = deliveries(a)
        models[key[0]] = {'goldStages': gst, 'cycles': cyc, 'zeroCycleMines': zero, 'automation': ast, 'lateAutoStart': late_start,
                          'lateAutoEnd': late_end, 'deliveryStages': dst, 'goldRows': rows, 'deliveryRows': drows,
                          'levelGaps': level_gaps(a)}
    # Автодоставка по 12 seed free (разброс удачи сундуков).
    seeds_auto = []
    for a in free:
        _, s, e = automation(a)
        seeds_auto.append((int(a['Seed']), s, e))
    # Сводка по моделям и seed: время кампании, доля товара, автомат на 31–144, максимум без покупки.
    overview = []
    for (scen, seed), a in sorted(b5.items()):
        g = sum(m.get('GoodsSoldBase') or 0 for m in a['Mines'])
        c = sum(m.get('CreditedBase') or 0 for m in a['Mines'])
        _, s, e = automation(a)
        mu = max(((m.get('MaxUnaffordableSec') or 0), m['Zone']) for m in a['Mines'])
        overview.append({'scenario': scen, 'seed': seed, 'success': a['Success'], 'mines': len(a['Mines']), 'hours': a['TotalSec'] / 3600,
                         'hoursB4': b4[(scen, seed)]['TotalSec'] / 3600 if (scen, seed) in b4 else None, 'goodsShare': g / (g + c) if g + c else 0,
                         'lateAutoStart': s, 'lateAutoEnd': e, 'maxUnaffordable': mu[0], 'maxUnaffordableZone': mu[1],
                         'calib': calib_median([a])[0]})
    summary['overview'] = overview
    summary['models'] = models
    summary['lateAutoBySeed'] = seeds_auto
    if ('free', 1) in b5:
        summary['offline'] = offline(b5[('free', 1)])
    with open(os.path.join(FACT, 'factories-summary.json'), 'w', encoding='utf-8') as f:
        json.dump(summary, f, ensure_ascii=False, indent=1, default=str)
    write_tables(summary, b5, b4)


def write_tables(s, b5, b4):
    L = ['# Таблицы заводов (ТЗ 2.2 §9)', '', 'Сгенерировано `Tools/BalanceAudit/factories.py report`. Деньги — базовые монеты шахты.', '']
    c = s['calibration']
    L.append('## Калибровка (free, 12 seed)')
    L.append('')
    L.append('Медиана шахт 2–5: **%s мин** (по seed %s; b4 — %s мин; предел %.2f–%.2f).' % (
        fmt(c['median']), ', '.join(fmt(x) for x in c['perSeed']), fmt(s['calibrationB4']['median']), TARGET[0], TARGET[1]))
    L.append('')
    L.append('## Темп по этапам: медиана, мин (b4 → b5)')
    L.append('')
    L.append('| Модель | Кампания b4 → b5, ч | ' + ' | '.join(st[0] for st in STAGES) + ' | Макс. отношение соседних | Шахт с ×≥2,2 | Макс. без покупки, с |')
    L.append('|---|---|' + '---|' * len(STAGES) + '---:|---:|---|')
    for key, p in s['pacing'].items():
        L.append('| %s | %s → %s | %s | %s | %d | %s (шахта %d) |' % (
            key, fmt(p['hoursB4']), fmt(p['hours']), ' | '.join('%s → %s' % (fmt(x['b4'], 1), fmt(x['b5'], 1)) for x in p['stages']),
            fmt(p['maxNeighbourRatio']), len(p['jumps22']), fmt(p['maxUnaffordable'][0], 0), p['maxUnaffordable'][1]))
    L.append('')
    p = s['pacing'].get('free/seed1')
    if p:
        L.append('### Условия теста темпа, отключённые до нового баланса (free seed 1)')
        L.append('')
        L.append('| Этап | Ориентир ТЗ 2.1 §12.6, мин | Медиана теста, мин | В ориентире | Кривая ≥ 0,95 предыдущего |')
        L.append('|---|---|---:|---|---|')
        for x in p['stages']:
            L.append('| %s | %g–%g | %s | %s | %s |' % (x['stage'], x['target'][0], x['target'][1], fmt(x['testMedian']),
                                                        'да' if x['inRange'] else 'нет', 'да' if x['curveOk'] else 'нет'))
        L.append('')
        L.append('Соседние шахты с отношением ≥ 2,2: %s.' % (', '.join('%d (×%s)' % (z, fmt(r)) for z, r in p['jumps22']) or 'нет'))
        L.append('')
    for name, m in s['models'].items():
        L.append('## Модель %s (seed 1)' % name)
        L.append('')
        L.append('### Золото: монеты и товар')
        L.append('')
        L.append('| Этап | Монеты | Товар | Доля товара | Медиана доли по шахтам | Пропало при переходе (склад + трубы) | Доля пропавшего от продаж | Готовые партии, пропали |')
        L.append('|---|---:|---:|---:|---:|---:|---:|---:|')
        for x in m['goldStages']:
            L.append('| %s | %s | %s | %s | %s | %s | %s | %s |' % (x['stage'], fmt(x['coins'], 0), fmt(x['goods'], 0), pct(x['share'], 1),
                                                              pct(x['medianShare'], 1), fmt(x['lost'], 0), pct(x['lostShareOfGoods'], 1), fmt(x['lostReady'], 0)))
        L.append('')
        L.append('### Автодоставка: доля заводов')
        L.append('')
        L.append('| Этап | Заводов | В начале шахты | В конце шахты (включилась, пока шахта шла) | С картой сундука выхода | Требуют Common |')
        L.append('|---|---:|---:|---:|---:|---:|')
        for x in m['automation']:
            L.append('| %s | %d | %s | %s | %s | %s |' % (x['stage'], x['factories'], pct(x['autoStart'], 1), pct(x['autoEnd'], 1),
                                                      pct(x['autoEndExit'], 1), pct(x['common'], 0)))
        L.append('')
        L.append('Шахты 31–144: на автомате в начале %s, в конце %s (порог предложений §9 — 25 %%).' % (pct(m['lateAutoStart'], 1), pct(m['lateAutoEnd'], 1)))
        L.append('')
        g = m['levelGaps']
        L.append('Недостача уровня карточки требования в начале шахт 31–144 (заводов %d; уровней → заводов): %s; карточка не собрана — %d. '
                 'Автомат был бы у %s заводов при +1 уровне всех карточек заводов, %s при +2, %s при +3. Эликсир к концу шахт 31–144: медиана %s, '
                 'в конце кампании %s — копии, а не эликсир, ограничивают улучшение.' % (
                     g['factories'], ', '.join('%d → %d' % (k, v) for k, v in g['gaps'].items()), g['notCollected'],
                     pct(g['withPlusLevels'][1], 0), pct(g['withPlusLevels'][2], 0), pct(g['withPlusLevels'][3], 0),
                     fmt(g['elixirMedian'], 0), fmt(g['elixirEnd'], 0)))
        L.append('')
        L.append('### Циклы заводов по типам')
        L.append('')
        L.append('| № | Тип | Завод | Цикл, с | Первая шахта | Циклов там | Работал там, мин | Заводов в других шахтах | Циклов на завод в других | Работал в других (сред.), мин | Заводов без цикла |')
        L.append('|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|')
        for t in m['cycles']:
            L.append('| %s | %s | %s | %s | %d | %d | %s | %d | %s | %s | %d из %d |' % (
                t['number'], t['type'], NAMES.get(t['type'], ('?',))[0], t['cycleSec'], t['firstZone'], t['firstCycles'], fmt(t['firstWorkMin'], 1),
                t['otherFactories'], fmt(t['otherMeanCycles'], 1), fmt(t['otherMeanWorkMin'], 1), t['zero'], t['factories']))
        L.append('')
        L.append('Шахты, где заводы не закончили ни одного цикла: %s.' % (', '.join(map(str, m['zeroCycleMines'])) or 'нет'))
        L.append('')
        L.append('### Ручные доставки в минуту')
        L.append('')
        L.append('| Шахты | Медиана | Максимум | Шахта максимума | Шахт > 12 в минуту |')
        L.append('|---|---:|---:|---:|---|')
        for x in m['deliveryStages']:
            L.append('| %s | %s | %s | %s | %s |' % (x['stage'], fmt(x['median']), fmt(x['max']), x['maxZone'], ', '.join(map(str, x['over12'])) or 'нет'))
        L.append('')
    if s.get('overview'):
        L.append('## Все прогоны: итог')
        L.append('')
        L.append('| Модель | Seed | Успех | Шахт | Кампания b4 → b5, ч | Медиана 2–5, мин | Доля товара | Автомат 31–144 (начало / конец) | Макс. без покупки, с (шахта) |')
        L.append('|---|---:|---|---:|---|---:|---:|---|---|')
        for o in s['overview']:
            L.append('| %s | %d | %s | %d | %s → %s | %s | %s | %s / %s | %s (%d) |' % (
                o['scenario'], o['seed'], 'да' if o['success'] else 'НЕТ', o['mines'], fmt(o['hoursB4']), fmt(o['hours']), fmt(o['calib']),
                pct(o['goodsShare'], 1), pct(o['lateAutoStart'], 1), pct(o['lateAutoEnd'], 1), fmt(o['maxUnaffordable'], 1), o['maxUnaffordableZone']))
        L.append('')
    if s.get('lateAutoBySeed'):
        L.append('## Автодоставка на шахтах 31–144 по seed (free)')
        L.append('')
        L.append('| Seed | В начале | В конце |')
        L.append('|---:|---:|---:|')
        for seed, st, en in sorted(s['lateAutoBySeed']):
            L.append('| %d | %s | %s |' % (seed, pct(st, 1), pct(en, 1)))
        L.append('')
    if s.get('offline'):
        L.append('## Вклад офлайна (free seed 1, отсутствие с 50 % длительности шахты)')
        L.append('')
        L.append('| Отсутствие | Шахты | Шахт | Вернулся до завершения | Склад к возвращению: авто / ручные | Готовые партии ручных | В трубах | Пропало при переходе (склад + трубы) | Медиана: склад / доход шахты | Медиана: пропало / доход шахты | Заводы к возвращению: авто / ручные / в цепях |')
        L.append('|---|---|---:|---:|---|---:|---:|---:|---:|---:|---|')
        for h, rows in s['offline'].items():
            for x in rows:
                L.append('| %s ч | %s | %d | %s | %s / %s | %s | %s | %s | %s | %s | %d / %d / %d |' % (
                    fmt(float(h), 0), x['stage'], x['mines'], pct(x['returnedBefore'], 0), fmt(x['warehouseAuto'], 0), fmt(x['warehouseManual'], 0),
                    fmt(x['readyManual'], 0), fmt(x['pipes'], 0), fmt(x['lost'], 0), pct(x['medianWarehouseShare'], 1), pct(x['medianLostShare'], 1),
                    x['autoFactories'], x['manualFactories'], x['locked']))
        L.append('')
    # Все 144 шахты.
    f5, a5, i5 = b5.get(('free', 1)), b5.get(('active', 1)), b5.get(('idle', 1))
    f4, a4, i4 = b4.get(('free', 1)), b4.get(('active', 1)), b4.get(('idle', 1))
    if f5:
        L.append('## Все 144 шахты (seed 1)')
        L.append('')
        L.append('| Шахта | free b4 → b5, мин | × к соседней | Монеты / товар | Товар | Заводов | Авто нач./кон. | Циклов | Макс. без покупки, с | active b4 → b5 | Ручных/мин | idle b4 → b5 | Пропало (free) |')
        L.append('|---:|---|---:|---|---:|---:|---|---:|---:|---|---:|---|---:|')
        z5, za5, zi5 = by_zone(f5), by_zone(a5) if a5 else {}, by_zone(i5) if i5 else {}
        z4, za4, zi4 = by_zone(f4) if f4 else {}, by_zone(a4) if a4 else {}, by_zone(i4) if i4 else {}
        prev = None
        for z in sorted(z5):
            m = z5[z]
            coins, goods = m.get('CreditedBase') or 0, m.get('GoodsSoldBase') or 0
            fs = m.get('Factories') or []
            am = za5.get(z)
            per = (am.get('ManualDeliveries') or 0) / minutes(am) if am else float('nan')
            L.append('| %d | %s → %s | %s | %s / %s | %s | %d | %d/%d | %d | %s | %s → %s | %s | %s → %s | %s |' % (
                z, fmt(minutes(z4[z]) if z in z4 else None), fmt(minutes(m)), fmt(minutes(m) / minutes(prev)) if prev else '—',
                fmt(coins, 0), fmt(goods, 0), pct(goods / (coins + goods) if coins + goods else 0), len(fs),
                sum(1 for f in fs if f.get('AutoAtStart')), sum(1 for f in fs if auto_running(m, f)), sum(f.get('Cycles') or 0 for f in fs),
                fmt(m.get('MaxUnaffordableSec'), 0),
                fmt(minutes(za4[z]) if z in za4 else None), fmt(minutes(am) if am else None), fmt(per, 1),
                fmt(minutes(zi4[z]) if z in zi4 else None), fmt(minutes(zi5[z]) if z in zi5 else None), fmt(m.get('GoodsLostBase'), 0)))
            prev = m
        L.append('')
    with open(os.path.join(FACT, 'factories-tables.md'), 'w', encoding='utf-8') as f:
        f.write('\n'.join(L) + '\n')
    print('written', os.path.relpath(os.path.join(FACT, 'factories-tables.md'), ROOT))


def main():
    cmd = sys.argv[1] if len(sys.argv) > 1 else 'report'
    os.makedirs(FACT, exist_ok=True)
    if cmd == 'calibrate':
        calibrate()
    elif cmd == 'report':
        report()
    else:
        raise SystemExit(__doc__)


if __name__ == '__main__':
    main()
