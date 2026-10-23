"""Render the local Axon preparation pack from isolated Unity capture sources.

This command only writes local media. It has no network, account or campaign API.
Run: Tools/.venv/bin/python Tools/AdVideo/axon_prepare.py
"""
from pathlib import Path
import json
import shutil
import hashlib

import compose
from compose import Ad, Seg, Ov, mark, taps, render, zoom_to
from edl import cap, legal, first_core, pops

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / 'release/advertising/applovin'


def build_ad():
    unlocked = mark('f10_chains', 'unlocked')
    deliver = taps('f10_deliver')[0][0]
    multi = taps('f30_deliver')[0][0]
    sell = taps('f30_market')[-1][0]
    auto = first_core('f3_auto', 'GoodsDispatched', after=0.8, default=1.8)
    grown = first_core('f140_factories', 'GoodsDispatched', default=6.0)
    segments = [
        Seg(0, 3.75, 'f10_chains', max(0, unlocked - 3.75 + 0.05)),
        Seg(3.75, 7.5, 'f10_chains', unlocked + 0.05),
        Seg(7.5, 12.1875, 'f10_deliver', max(0, deliver - 1.7)),
        Seg(12.1875, 15, 'f30_deliver', max(0, multi - 0.6)),
        Seg(15, 18.75, 'f30_market', max(0, sell - 1.9)),
        Seg(18.75, 22.5, 'f3_auto', max(0, auto - 1.2),
            view=zoom_to(1, (540, 960), 1.4, (600, 820))),
        Seg(22.5, 26.25, 'f140_factories', max(0, grown - 1.5),
            view=zoom_to(1, (540, 960), 1.25, (550, 700))),
    ]
    overlays = [
        cap(0, 3.75, 'Break the *chains*!', cy=1140),
        cap(3.75, 7.5, 'Unlock a *cat factory*!'),
        cap(7.5, 9.84375, 'Tap *Deliver*!', cy=1140),
        cap(9.84375, 12.1875, 'Pipes carry the *goods*'),
        cap(12.1875, 15, 'More factories, more *goods*!', cy=1140),
        cap(15, 18.75, 'Sell goods for *gold*!'),
        cap(18.75, 22.5, 'Automate your *factory*!', style='green', cy=1140),
        cap(22.5, 26.25, 'Build a factory *empire*!', cy=1140),
        Ov('end', 26.25, 30, {'tagline': 'Run your own *cat factory*!',
                             'legal': 'In-game purchases (incl. random items)', 'taps': ()}),
        cap(26.25, 30, 'Cat Miner: Idle Tycoon', style='dark', cy=180, k=4, exit=False),
    ]
    sounds = [(0, 'whoosh', 0.35), (3.73, 'impact', 0.5),
              (12, 'whoosh', 0.35), (17.1, 'cash', 0.5),
              (22.3, 'whoosh', 0.35), *pops(overlays)]
    return Ad('CatMiner_Axon_Factories_30s_en', 't1_progress_30',
              segments, overlays, sounds, length=30)


def main():
    compose.OUT = DEST / 'assets'
    compose.STILLS = DEST / 'review'
    compose.OUT.mkdir(parents=True, exist_ok=True)
    compose.STILLS.mkdir(parents=True, exist_ok=True)
    ad = build_ad()
    source_paths = sorted({ROOT / 'build/adcapture' / (s.src + '.mp4') for s in ad.segs})
    sources = [{'path': str(p.relative_to(ROOT)),
                'sha256': hashlib.sha256(p.read_bytes()).hexdigest()} for p in source_paths]
    output = render(ad, stills=[1, 8.5, 16.5, 24, 28.5])
    end = compose.STILLS / (ad.name + '_28.50.png')
    shutil.copyfile(end, compose.OUT / 'endcard-1080x1920.png')
    shutil.copyfile(ROOT / 'Docs/Store/GooglePlay/assets/en-US/icon-512.png',
                    compose.OUT / 'icon-512.png')
    record = {
        'name': ad.name, 'language': 'en', 'duration_seconds': 30,
        'dimensions': [1080, 1920], 'fps': 30, 'video': str(output.relative_to(ROOT)),
        'runtime': 'Actual Unity Outpost gameplay from isolated AdCaptureTests, prior factory captures reused',
        'current_release_comparison': 'Factory loop preserved. No card-count, shop, ad-reward or offline-double footage included. Final Android comparison pending.',
        'sources': sources,
        'audio': 'Game SFX reconstructed from frame-matched capture cue logs; original synthesized t1_progress_30 music, existing project pipeline',
        'staging': 'QA mine progress and automation staged in isolated capture profiles; no production purchase/reward receipts',
        'story': {
            'situation': 'A cat crew finds a chained factory in a mine.',
            'desire': 'The player wants the factory to produce useful goods.',
            'conflict': 'Chains block production and manual dispatch needs attention.',
            'change': 'The crew unlocks the factory; the player dispatches goods and automates production.',
            'result': 'More factories deliver goods that the player can sell for in-game gold.',
        },
        'shot_plan': [{'from': s.start, 'to': s.end, 'source': s.src,
                       'source_from': s.src_t, 'speed': s.speed} for s in ad.segs],
        'advertising_in_game': False, 'purchases_disclosed': True,
        'campaign_uploaded': False, 'campaign_activated': False,
        'review': 'Render complete; encoded-stream and playback review still required',
    }
    (DEST / 'creative-source.json').write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n')
    print('Local Axon creative assets rendered; no campaign calls were made.')


if __name__ == '__main__':
    main()
