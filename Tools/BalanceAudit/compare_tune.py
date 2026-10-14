#!/usr/bin/env python3
"""Сводка вариантов перебора: медианы минут по этапам, доли времени, покупки. Запуск: python3 Tools/BalanceAudit/compare_tune.py [имя...]"""
import glob, json, os, statistics, sys
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TUNE = os.path.join(ROOT, 'Docs', 'Reports', 'balance-audit', 'tune')
STAGES = [(1, 1), (2, 10), (11, 30), (31, 60), (61, 100), (101, 144)]
TARGET = ['3-5', '3-7', '4-9', '5-11', '6-13', '8-15']

def sh(m, *ks):
    t = m['WorkerSecTotal']
    return sum(m['WorkerSec'].get(k, 0) for k in ks) / t if t else 0

names = sys.argv[1:] or sorted(os.listdir(TUNE))
for scen in ('free', 'idle', 'active'):
    print('==', scen, ' stage medians (min); targets', ' '.join(TARGET))
    for n in names:
        p = os.path.join(TUNE, n, 'campaign-%s-seed1.json' % scen)
        if not os.path.exists(p):
            continue
        d = json.load(open(p))
        ms = d['Mines']
        meds = []
        for a, b in STAGES:
            sel = [m['CompletedSec'] / 60 for m in ms if a <= m['Zone'] <= b]
            meds.append(statistics.median(sel))
        late = [m for m in ms if m['Zone'] >= 101]
        extra = ''
        if scen == 'free':
            extra = ' late: queue %.0f%% unl %.0f%% idle %.0f%% walk %.0f%% mine %.0f%% unspent %.2f workers %.0f lv%s' % (
                100 * statistics.mean(sh(m, 'WaitingDepot') for m in late),
                100 * statistics.mean(sh(m, 'Unloading') for m in late),
                100 * statistics.mean(sh(m, 'Idle', 'GoingHome') for m in late),
                100 * statistics.mean(sh(m, 'MovingToWork', 'MovingToCargo', 'Returning') for m in late),
                100 * statistics.mean(sh(m, 'Mining') for m in late),
                statistics.mean(m['UnspentBase'] / max(1, m['CreditedBase']) for m in late),
                statistics.median(m['WorkersEnd'] for m in late),
                [round(statistics.median(m['Levels'][i] for m in late)) for i in range(6)])
        if scen == 'free' and ms[0].get('PurchaseLog') is not None:
            def lastfrac(m):
                ts = [p['Sec'] for p in m['PurchaseLog'] if p['Branch'] != 'Value']
                return max(ts) / m['CompletedSec'] if ts else 0
            mid = [m for m in ms if 31 <= m['Zone'] <= 100]
            extra += ' lastUseful mid %.0f%% late %.0f%%' % (100 * statistics.median(lastfrac(m) for m in mid), 100 * statistics.median(lastfrac(m) for m in late))
        print('  %-22s %6.2f h | %s |%s' % (n, d['TotalSec'] / 3600, ' '.join('%5.1f' % x for x in meds), extra))
