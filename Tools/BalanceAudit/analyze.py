#!/usr/bin/env python3
"""Разбор аудита баланса CatOutpostV2 (Docs/Rework/BALANCE_AUDIT.md).

Читает JSON генератора OutpostBalanceAuditTests (Docs/Reports/balance-audit/<tag>/), считает сводки по шахтам,
участкам кампании и стратегиям, ищет выбросы и рисует графики «до/после».

Запуск из корня проекта:
  Tools/.venv/bin/python Tools/BalanceAudit/analyze.py before [after]
Выход: Docs/Reports/balance-audit/summary-<tag>.json, outliers-<tag>.json, charts/*.png, tables-<tag>.md.

Тег — имя каталога в Docs/Reports/balance-audit/ или «тег=путь» (путь от корня проекта); --out DIR — куда писать сводки и
графики. Заводы ТЗ 2.2 (профиль b5 против эталона b4, Docs/Rework/BALANCE_FACTORIES.md):
  Tools/.venv/bin/python Tools/BalanceAudit/analyze.py b4=Docs/Reports/balance-audit/after \
      b5=Docs/Reports/balance-factories/b5 --out Docs/Reports/balance-factories
Таблицы заводов (товар, циклы, автодоставка, офлайн) — Tools/BalanceAudit/factories.py.
"""
import glob
import json
import math
import os
import statistics
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
AUDIT = os.path.join(ROOT, 'Docs', 'Reports', 'balance-audit')
OUT = AUDIT
DIRS = {}


def tag_dir(tag):
    """Каталог данных тега: «тег=путь» из командной строки или Docs/Reports/balance-audit/<тег>."""
    return DIRS.get(tag, os.path.join(AUDIT, tag))
MODES = ['ChoosingTask', 'MovingToWork', 'Mining', 'MovingToCargo', 'PickingUp', 'Returning',
         'WaitingDepot', 'Unloading', 'Idle', 'GoingHome']

# Ориентиры темпа (минуты) для эталонного игрока «free»: ТЗ §12.6 + предварительная кривая по этапам.
STAGES = [
    ('1', 1, 1, 3.0, 5.0),
    ('2–10', 2, 10, 3.0, 7.0),
    ('11–30', 11, 30, 4.0, 9.0),
    ('31–60', 31, 60, 5.0, 11.0),
    ('61–100', 61, 100, 6.0, 13.0),
    ('101–144', 101, 144, 8.0, 15.0),
]


def stage_of(z):
    for name, a, b, lo, hi in STAGES:
        if a <= z <= b:
            return name, lo, hi
    raise ValueError(z)


def load(tag):
    runs = {}
    for path in sorted(glob.glob(os.path.join(tag_dir(tag), 'campaign-*.json'))):
        with open(path, encoding='utf-8') as f:
            d = json.load(f)
        runs[(d['Scenario'], int(d['Seed']))] = d
    sat = []
    sp = os.path.join(tag_dir(tag), 'saturation.json')
    if os.path.exists(sp):
        with open(sp, encoding='utf-8') as f:
            sat = json.load(f)
    return runs, sat


def minutes(m):
    return (m.get('CompletedSec') or float('nan')) / 60.0


def share(m, *modes):
    tot = m.get('WorkerSecTotal') or 0
    if tot <= 0:
        return 0.0
    return sum(m['WorkerSec'].get(x, 0) for x in modes) / tot


def mine_row(m):
    sectors = m.get('Sectors') or []
    durations = []
    for s in sectors:
        if s.get('OpenSec') is None:
            continue
        end = s.get('GateSec') if s.get('GateSec') is not None else m.get('ClearedSec')
        if end is not None:
            durations.append(end - s['OpenSec'])
    comp = m.get('CompletedSec') or 0
    gate_waits = [(s['GateSec'] - s['RsxClearSec']) for s in sectors if s.get('GateSec') is not None and s.get('RsxClearSec') is not None]
    return {
        'zone': m['Zone'],
        'min': minutes(m),
        'clearedMin': (m.get('ClearedSec') or 0) / 60,
        'firstDelivery': m.get('FirstDeliverySec'),
        'firstPurchase': m.get('FirstPurchaseSec'),
        'firstHire': m.get('FirstHireSec'),
        'purchases': m.get('Purchases'),
        'maxGap': m.get('MaxPurchaseGapSec'),
        'medianGap': m.get('MedianPurchaseGapSec'),
        'maxUnaffordable': m.get('MaxUnaffordableSec'),
        'mining': share(m, 'Mining'),
        'walking': share(m, 'MovingToWork', 'MovingToCargo', 'Returning'),
        'walkCargo': share(m, 'Returning'),
        'pickup': share(m, 'PickingUp'),
        'queue': share(m, 'WaitingDepot'),
        'unloading': share(m, 'Unloading'),
        'idle': share(m, 'Idle', 'GoingHome', 'ChoosingTask'),
        'avgWorkers': m.get('AvgWorkers'),
        'avgBusy': m.get('AvgBusyWorkers'),
        'workersEnd': m.get('WorkersEnd'),
        'workersStart': m.get('WorkersStart'),
        'gateWait': m.get('GateWaitSec'),
        'gateWaitShare': (m.get('GateWaitSec') or 0) / comp if comp else 0,
        'maxSectorShare': max(durations) / comp if durations and comp else 0,
        'longestTarget': m.get('LongestTargetSec'),
        'longestTargetShare': (m.get('LongestTargetSec') or 0) / comp if comp else 0,
        'slowTarget': (m.get('SlowTargets') or [{}])[0],
        'drain': m.get('DrainSec'),
        'creditedBase': m.get('CreditedBase'),
        'incomeBasePerMin': m.get('IncomeBasePerMin'),
        'unspentBase': m.get('UnspentBase'),
        'levels': m.get('Levels'),
        'medianCostIncome': m.get('MedianCostIncomeSec'),
        'maxCostIncome': m.get('MaxCostIncomeSec'),
        'strikeShare': m.get('StrikeDamageShare'),
        'cards': m.get('CardsOwned'),
        'cardLevels': m.get('CardLevels'),
        'targets': m.get('Targets'),
        'gates': m.get('Gates'),
        'cargo': m.get('CargoUnits'),
        'hp': m.get('TotalHp'),
        'start': m.get('Start'),
        'equipped': m.get('Equipped'),
        'equippedLevels': m.get('EquippedLevels'),
        # Заводы (ТЗ 2.2 §9): золото товара отдельно от монет, ручные доставки, автодоставка.
        'goodsSoldBase': m.get('GoodsSoldBase') or 0,
        'goodsShare': (m.get('GoodsSoldBase') or 0) / ((m.get('GoodsSoldBase') or 0) + (m.get('CreditedBase') or 0))
        if ((m.get('GoodsSoldBase') or 0) + (m.get('CreditedBase') or 0)) > 0 else 0,
        'manualDeliveries': m.get('ManualDeliveries') or 0,
        'factories': len(m.get('Factories') or []),
        'factoriesAutoStart': sum(1 for f in (m.get('Factories') or []) if f.get('AutoAtStart')),
        'factoriesAutoEnd': sum(1 for f in (m.get('Factories') or []) if f.get('AutoAtEnd')),
        'factoryCycles': sum(f.get('Cycles') or 0 for f in (m.get('Factories') or [])),
        'goodsLostBase': m.get('GoodsLostBase') or 0,
    }


def rolling_median(values, i, k=5):
    lo, hi = max(0, i - k), min(len(values), i + k + 1)
    neigh = [values[j] for j in range(lo, hi) if j != i and not math.isnan(values[j])]
    return statistics.median(neigh) if neigh else float('nan')


def outliers(rows, label):
    """Кандидаты на разбор: скачки против соседей, доминирующие участки, долгие ожидания, простой котиков."""
    found = []
    mins = [r['min'] for r in rows]
    for i, r in enumerate(rows):
        z = r['zone']
        med = rolling_median(mins, i)
        ratio = r['min'] / med if med and med > 0 else float('nan')
        name, lo, hi = stage_of(z)
        flags = []
        if ratio >= 1.35:
            flags.append('spike x%.2f vs neighbours' % ratio)
        if ratio <= 0.70:
            flags.append('dip x%.2f vs neighbours' % ratio)
        if r['min'] > hi:
            flags.append('above target %.0f-%.0f min' % (lo, hi))
        if r['min'] < lo:
            flags.append('below target %.0f-%.0f min' % (lo, hi))
        if r['longestTargetShare'] >= 0.20:
            flags.append('one target %.0f%% of mine' % (100 * r['longestTargetShare']))
        if r['maxSectorShare'] >= 0.45 and (r['gates'] or 0) >= 2:
            flags.append('one sector %.0f%% of mine' % (100 * r['maxSectorShare']))
        if r['gateWaitShare'] >= 0.20:
            flags.append('gates %.0f%% of mine' % (100 * r['gateWaitShare']))
        if (r['maxUnaffordable'] or 0) >= 120:
            flags.append('no affordable upgrade for %.0f s' % r['maxUnaffordable'])
        if r['idle'] >= 0.30:
            flags.append('cats idle %.0f%%' % (100 * r['idle']))
        if r['queue'] >= 0.15:
            flags.append('depot queue %.0f%%' % (100 * r['queue']))
        if flags:
            found.append({'scenario': label, 'zone': z, 'min': round(r['min'], 2), 'neighbourMedian': round(med, 2) if med == med else None,
                          'flags': flags})
    return found


def stage_summary(rows):
    out = []
    for name, a, b, lo, hi in STAGES:
        sel = [r for r in rows if a <= r['zone'] <= b]
        if not sel:
            continue
        ms = [r['min'] for r in sel]
        out.append({
            'stage': name, 'targetMin': [lo, hi], 'mines': len(sel),
            'totalMin': sum(ms), 'medianMin': statistics.median(ms), 'minMin': min(ms), 'maxMin': max(ms),
            'inTarget': sum(1 for x in ms if lo <= x <= hi), 'below': sum(1 for x in ms if x < lo), 'above': sum(1 for x in ms if x > hi),
            'mining': statistics.mean(r['mining'] for r in sel), 'walking': statistics.mean(r['walking'] for r in sel),
            'depot': statistics.mean(r['queue'] + r['unloading'] for r in sel), 'idle': statistics.mean(r['idle'] for r in sel),
            'queue': statistics.mean(r['queue'] for r in sel), 'unloading': statistics.mean(r['unloading'] for r in sel),
            'pickup': statistics.mean(r['pickup'] for r in sel),
            'medianMaxGap': statistics.median([r['maxGap'] or 0 for r in sel]),
            'medianFirstPurchase': statistics.median([r['firstPurchase'] or 0 for r in sel]),
            'medianWorkersEnd': statistics.median([r['workersEnd'] or 0 for r in sel]),
            'medianAvgBusy': statistics.median([r['avgBusy'] or 0 for r in sel]),
            'gateWaitShare': statistics.mean(r['gateWaitShare'] for r in sel),
            'longestTargetShare': statistics.mean(r['longestTargetShare'] for r in sel),
            'goodsShare': (sum(r['goodsSoldBase'] for r in sel) / sum(r['goodsSoldBase'] + (r['creditedBase'] or 0) for r in sel))
            if sum(r['goodsSoldBase'] + (r['creditedBase'] or 0) for r in sel) > 0 else 0,
            'consecutiveRatioMax': max((sel[i]['min'] / sel[i - 1]['min'] for i in range(1, len(sel))), default=float('nan')),
            'consecutiveRatioMin': min((sel[i]['min'] / sel[i - 1]['min'] for i in range(1, len(sel))), default=float('nan')),
        })
    return out


def summarize(tag):
    runs, sat = load(tag)
    summary = {'tag': tag, 'scenarios': {}, 'luck': {}, 'saturation': sat}
    all_outliers = []
    for (scen, seed), d in sorted(runs.items()):
        rows = [mine_row(m) for m in d['Mines']]
        key = '%s/seed%d' % (scen, seed)
        summary['scenarios'][key] = {
            'success': d['Success'], 'mines': len(rows), 'totalHours': d['TotalSec'] / 3600,
            'stages': stage_summary(rows),
            'rows': rows,
        }
        if seed == 1 or scen == 'free':
            all_outliers += outliers(rows, key)
    # Удача: кампании free по seed.
    free = {seed: d for (scen, seed), d in runs.items() if scen == 'free'}
    if free:
        totals = sorted((d['TotalSec'] / 3600, seed) for seed, d in free.items())
        summary['luck'] = {
            'totalsHours': {str(s): t for t, s in totals},
            'median': statistics.median(t for t, _ in totals),
            'best': totals[0], 'worst': totals[-1],
            'medianSeed': totals[len(totals) // 2][1], 'worstSeed': totals[-1][1],
        }
    with open(os.path.join(OUT, 'summary-%s.json' % tag), 'w', encoding='utf-8') as f:
        json.dump(summary, f, ensure_ascii=False, indent=1)
    with open(os.path.join(OUT, 'outliers-%s.json' % tag), 'w', encoding='utf-8') as f:
        json.dump(all_outliers, f, ensure_ascii=False, indent=1)
    return summary, all_outliers


def fmt(x, nd=1):
    if x is None or (isinstance(x, float) and math.isnan(x)):
        return '—'
    return ('%.' + str(nd) + 'f') % x


def tables_md(summary):
    lines = ['# Таблицы аудита: %s' % summary['tag'], '']
    lines.append('| Сценарий | Кампания, ч | ' + ' | '.join('%s медиана, мин' % s[0] for s in STAGES) + ' |')
    lines.append('|---|---:|' + '---:|' * len(STAGES))
    for key, sc in summary['scenarios'].items():
        med = {s['stage']: s['medianMin'] for s in sc['stages']}
        lines.append('| %s | %s | %s |' % (key, fmt(sc['totalHours'], 2), ' | '.join(fmt(med.get(s[0])) for s in STAGES)))
    lines.append('')
    for key in ('free/seed1', 'idle/seed1', 'active/seed1', 'eager-rr/seed1'):
        sc = summary['scenarios'].get(key)
        if not sc:
            continue
        lines.append('## %s по этапам' % key)
        lines.append('')
        lines.append('| Этап | Цель, мин | Медиана | Мин–макс | В цели/ниже/выше | Добыча | Ходьба | Разгрузка | Очередь | Подбор | Простой | Ворота | Макс. пауза покупки (мед.), с | Котиков к концу (мед.) | Занято в среднем | Доля товара в золоте |')
        lines.append('|---|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
        for s in sc['stages']:
            lines.append('| %s | %g–%g | %s | %s–%s | %d/%d/%d | %s%% | %s%% | %s%% | %s%% | %s%% | %s%% | %s%% | %s | %s | %s | %s%% |' % (
                s['stage'], s['targetMin'][0], s['targetMin'][1], fmt(s['medianMin']), fmt(s['minMin']), fmt(s['maxMin']),
                s['inTarget'], s['below'], s['above'], fmt(100 * s['mining'], 0), fmt(100 * s['walking'], 0), fmt(100 * s['unloading'], 0), fmt(100 * s['queue'], 0),
                fmt(100 * s['pickup'], 0), fmt(100 * s['idle'], 0), fmt(100 * s['gateWaitShare'], 0), fmt(s['medianMaxGap'], 0),
                fmt(s['medianWorkersEnd'], 0), fmt(s['medianAvgBusy'], 1), fmt(100 * s.get('goodsShare', 0), 0)))
        lines.append('')
    if summary.get('luck'):
        l = summary['luck']
        lines.append('## Удача карточек и экипировки (free, 12 seed)')
        lines.append('')
        lines.append('Медиана %.2f ч, лучший seed %d — %.2f ч, худший seed %d — %.2f ч.' % (
            l['median'], l['best'][1], l['best'][0], l['worst'][1], l['worst'][0]))
        lines.append('')
    if summary.get('saturation'):
        lines.append('## Насыщение команды (без покупок, уровни конца шахты free seed 1)')
        lines.append('')
        lines.append('Минуты до завершения при данном числе котиков; в скобках — ускорение от удвоения команды с 4 до 8 и с 8 до 16.')
        lines.append('')
        counts = sorted({r['Workers'] for s in summary['saturation'] for r in s['Rows']})
        lines.append('| Шахта | ' + ' | '.join('%d кот.' % c for c in counts) + ' | ×(4→8) | ×(8→16) |')
        lines.append('|---|' + '---:|' * (len(counts) + 2))
        for s in summary['saturation']:
            by = {r['Workers']: (r.get('CompletedSec') or 0) / 60 for r in s['Rows']}
            def speed(a, b):
                if a in by and b in by and by[b] > 0:
                    return fmt(by[a] / by[b], 2)
                cand = [c for c in by if c >= b]
                return fmt(by[a] / by[min(cand)], 2) + '*' if a in by and cand else '—'
            lines.append('| %d | %s | %s | %s |' % (s['Zone'], ' | '.join(fmt(by[c]) if c in by else '—' for c in counts), speed(4, 8), speed(8, 16)))
        lines.append('')
        lines.append('\\* наибольшая доступная команда шахты (предел «Команды» с карточками).')
        lines.append('')
    return '\n'.join(lines) + '\n'


# ── Графики ──

LIGHT = {'surface': '#fcfcfb', 'text': '#0b0b0b', 'text2': '#52514e', 'grid': '#e7e6e2',
         'before': '#9a9893', 'after': '#2a78d6', 'band': '#cde2fb', 'accent': '#eb6834'}


def charts(before, after=None):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    out = os.path.join(OUT, 'charts')
    os.makedirs(out, exist_ok=True)
    c = LIGHT
    plt.rcParams.update({'font.size': 10, 'axes.edgecolor': c['grid'], 'axes.labelcolor': c['text2'], 'xtick.color': c['text2'],
                         'ytick.color': c['text2'], 'axes.facecolor': c['surface'], 'figure.facecolor': c['surface'],
                         'axes.spines.top': False, 'axes.spines.right': False, 'axes.grid': True, 'grid.color': c['grid'],
                         'grid.linewidth': 0.6})

    def band(ax):
        for name, a, b, lo, hi in STAGES:
            ax.fill_between([a - 0.5, b + 0.5], lo, hi, color=c['band'], alpha=0.55, linewidth=0, zorder=0)

    for key, title in (('free/seed1', 'Эталонный игрок free (seed 1)'), ('idle/seed1', 'Idle-игрок (покупки раз в 5 минут)'),
                       ('active/seed1', 'Активный игрок (удары киркой, реакция 5 с)'), ('eager-rr/seed1', 'Прежний бот C')):
        if key not in before['scenarios']:
            continue
        fig, ax = plt.subplots(figsize=(11, 4.2))
        band(ax)
        rb = before['scenarios'][key]['rows']
        ax.plot([r['zone'] for r in rb], [r['min'] for r in rb], color=c['before'], linewidth=1.6, label=LABELS[0])
        if after and key in after['scenarios']:
            ra = after['scenarios'][key]['rows']
            ax.plot([r['zone'] for r in ra], [r['min'] for r in ra], color=c['after'], linewidth=2, label=LABELS[1])
            ax.annotate('%s: %.1f ч' % (SHORT[1], after['scenarios'][key]['totalHours']), (ra[-1]['zone'], ra[-1]['min']), xytext=(6, 0),
                        textcoords='offset points', color=c['text'], va='center', fontsize=9)
        ax.annotate('%s: %.1f ч' % (SHORT[0], before['scenarios'][key]['totalHours']), (rb[-1]['zone'], rb[-1]['min']), xytext=(6, -12),
                    textcoords='offset points', color=c['text2'], va='center', fontsize=9)
        ax.set_xlim(0, 152)
        ax.set_ylim(bottom=0)
        ax.set_xlabel('Шахта')
        ax.set_ylabel('Минут до завершения')
        ax.set_title(title + ' — время шахты; голубая полоса — ориентир этапа', loc='left', color=c['text'], fontsize=11)
        ax.legend(frameon=False, loc='upper left')
        fig.tight_layout()
        fig.savefig(os.path.join(out, 'campaign-%s.png' % key.split('/')[0]), dpi=130)
        plt.close(fig)

    # Структура времени котиков по этапам (free seed 1): до и после.
    def stack(summary, ax, label):
        st = summary['scenarios']['free/seed1']['stages']
        names = [s['stage'] for s in st]
        parts = [('Добыча', 'mining', '#2a78d6'), ('Ходьба', 'walking', '#eb6834'), ('Подбор', 'pickup', '#1baf7a'),
                 ('Разгрузка', 'unloading', '#eda100'), ('Очередь у склада', 'queue', '#e87ba4'), ('Простой', 'idle', '#008300')]
        bottom = [0] * len(st)
        for title, k, col in parts:
            vals = [100 * s[k] for s in st]
            ax.bar(names, vals, bottom=bottom, color=col, edgecolor=c['surface'], linewidth=2, label=title, width=0.7)
            bottom = [b + v for b, v in zip(bottom, vals)]
        ax.set_ylim(0, 100)
        ax.set_ylabel('% котико-времени')
        ax.set_title(label, loc='left', color=c['text'], fontsize=11)

    n = 2 if after else 1
    fig, axes = plt.subplots(1, n, figsize=(6 * n, 4), squeeze=False)
    stack(before, axes[0][0], '%s (free seed 1)' % LABELS[0].capitalize())
    if after:
        stack(after, axes[0][1], '%s (free seed 1)' % LABELS[1].capitalize())
    axes[0][-1].legend(frameon=False, loc='upper left', bbox_to_anchor=(1.0, 1.0))
    fig.tight_layout()
    fig.savefig(os.path.join(out, 'time-structure.png'), dpi=130)
    plt.close(fig)

    # Насыщение команды.
    def sat_plot(summary, ax, label):
        for i, s in enumerate(summary.get('saturation') or []):
            rows = [r for r in s['Rows'] if r.get('CompletedSec')]
            base = rows[0]['CompletedSec']
            ax.plot([r['Workers'] for r in rows], [base / r['CompletedSec'] for r in rows], linewidth=1.6,
                    color=['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4', '#008300', '#4a3aa7', '#e34948'][i % 8],
                    label='шахта %d' % s['Zone'], marker='o', markersize=4)
        ax.set_xlabel('Котиков')
        ax.set_ylabel('Ускорение относительно 2 котиков')
        ax.set_title(label, loc='left', color=c['text'], fontsize=11)

    if before.get('saturation') or (after and after.get('saturation')):
        fig, axes = plt.subplots(1, n, figsize=(6 * n, 4.2), squeeze=False)
        sat_plot(before, axes[0][0], 'Отдача от котиков — %s' % SHORT[0])
        if after:
            sat_plot(after, axes[0][1], 'Отдача от котиков — %s' % SHORT[1])
        axes[0][-1].legend(frameon=False, fontsize=8, loc='upper left', bbox_to_anchor=(1.0, 1.0))
        fig.tight_layout()
        fig.savefig(os.path.join(out, 'saturation.png'), dpi=130)
        plt.close(fig)

    # Удача seed: суммарное время кампании free.
    if before.get('luck'):
        fig, ax = plt.subplots(figsize=(8, 3.4))
        lb = before['luck']['totalsHours']
        seeds = sorted(lb, key=int)
        ax.scatter([int(s) for s in seeds], [lb[s] for s in seeds], color=c['before'], s=36, label=LABELS[0], zorder=3)
        if after and after.get('luck'):
            la = after['luck']['totalsHours']
            ax.scatter([int(s) for s in seeds if s in la], [la[s] for s in seeds if s in la], color=c['after'], s=36,
                       label=LABELS[1], zorder=3)
        ax.set_xlabel('Seed профиля (карточки и экипировка)')
        ax.set_ylabel('Часов на кампанию')
        ax.set_ylim(bottom=0)
        ax.set_title('Разброс от удачи наград, игрок free', loc='left', color=c['text'], fontsize=11)
        ax.legend(frameon=False)
        fig.tight_layout()
        fig.savefig(os.path.join(out, 'luck.png'), dpi=130)
        plt.close(fig)


LABELS = ['до правок', 'после правок']
SHORT = ['до', 'после']


def main():
    global OUT
    args = sys.argv[1:]
    if '--out' in args:
        i = args.index('--out')
        OUT = os.path.join(ROOT, args[i + 1])
        del args[i:i + 2]
    os.makedirs(OUT, exist_ok=True)
    tags = []
    for a in args or ['before']:
        if '=' in a:
            t, path = a.split('=', 1)
            DIRS[t] = os.path.join(ROOT, path)
            tags.append(t)
        else:
            tags.append(a)
    if tags != ['before', 'after'][:len(tags)]:
        # Свои теги (например b4 и b5): подписи графиков — по тегам.
        LABELS[:] = ['профиль %s' % t for t in tags] + LABELS[len(tags):]
        SHORT[:] = tags + SHORT[len(tags):]
    sums = []
    for t in tags:
        s, o = summarize(t)
        with open(os.path.join(OUT, 'tables-%s.md' % t), 'w', encoding='utf-8') as f:
            f.write(tables_md(s))
        print('%s: %d scenarios, %d outlier rows' % (t, len(s['scenarios']), len(o)))
        sums.append(s)
    charts(sums[0], sums[1] if len(sums) > 1 else None)


if __name__ == '__main__':
    main()
