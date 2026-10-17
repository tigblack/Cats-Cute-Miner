#!/usr/bin/env python3
"""Render the game's local legal REVIEW pages; never publishes or changes runtime URLs."""
from pathlib import Path
from html import escape
from markdown_it import MarkdownIt
import hashlib
import json
import zipfile

ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / 'Docs/Legal/CatsCuteMiner'
SITE = DOCS / 'site'
STYLE = '''
:root{color-scheme:light;font:17px/1.65 system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;color:#22304a;background:#eef1f7}
*{box-sizing:border-box}body{margin:0}a{color:#164fb5;text-underline-offset:3px}a:focus-visible{outline:3px solid #eba927;outline-offset:4px}
header{background:#14213c;color:#fff;padding:30px max(20px,calc((100vw - 850px)/2))}header a{color:#ffdc8b}header strong{display:block;font-size:1.5rem}nav{display:flex;gap:22px;flex-wrap:wrap;margin-top:12px}
main{max-width:850px;margin:28px auto;padding:32px 42px;background:#fff;border:1px solid #d6dce8;border-radius:12px}h1{font-size:2rem;line-height:1.2;margin:0 0 24px;color:#14213c}h2{font-size:1.24rem;margin-top:2rem;color:#14213c}p,ul{margin:0 0 1rem}li{margin:.45rem 0}.notice{padding:14px 18px;background:#fff4d9;border-left:4px solid #dc9f22;margin-bottom:26px;font-size:.94rem}footer{max-width:850px;margin:20px auto 36px;padding:0 24px;color:#52617a;font-size:.92rem}.documents{list-style:none;padding:0}.documents a{display:block;padding:18px 20px;background:#f3f6fc;border:1px solid #d6dce8;border-radius:8px;margin:14px 0;font-weight:650}
@media(max-width:600px){header{padding:24px 18px}main{margin:16px 10px;padding:24px 18px}h1{font-size:1.65rem}nav{gap:16px}}
'''


def page(title, body, nested=False):
    prefix = '../' if nested else ''
    return f'''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex,nofollow"><title>{escape(title)} | Cat Miner: Idle Tycoon</title><style>{STYLE}</style></head>
<body><header><strong>Cat Miner: Idle Tycoon</strong><span>Multicast Games Limited</span><nav aria-label="Legal documents"><a href="{prefix}index.html">Documents</a><a href="{prefix}privacy/index.html">Privacy Policy</a><a href="{prefix}terms/index.html">Terms of Use</a></nav></header>
<main><aside class="notice" aria-label="Review status">Review copy — these game-specific documents have not yet been adopted or published for use in Google Play.</aside>{body}</main>
<footer>Questions from players, parents and guardians: <a href="mailto:support@multicastgames.com">support@multicastgames.com</a></footer></body></html>'''


def main():
    renderer = MarkdownIt('commonmark', {'html': False})
    SITE.mkdir(parents=True, exist_ok=True)
    entries = []
    for source_name, route, title in [
        ('privacy-policy.draft.md', 'privacy', 'Privacy Policy'),
        ('terms-of-use.draft.md', 'terms', 'Terms of Use'),
    ]:
        source = DOCS / source_name
        text = source.read_text(encoding='utf-8')
        output = SITE / route / 'index.html'
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(page(title, renderer.render(text), nested=True), encoding='utf-8')
        entries.append({'source': str(source.relative_to(ROOT)), 'path': str(output.relative_to(ROOT)),
                        'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                        'html_sha256': hashlib.sha256(output.read_bytes()).hexdigest(),
                        'proposed_url': f'https://multicastgames.com/cats-cute-miner/{route}/',
                        'public_url_verified': False})
    home = '''<h1>Documents for review</h1><p>Separate documents for Cat Miner: Idle Tycoon, including protected play for children and players of unknown age.</p><ul class="documents"><li><a href="privacy/index.html">Read the Privacy Policy</a></li><li><a href="terms/index.html">Read the Terms of Use</a></li></ul><p>The game operator must confirm the actual retention/deletion procedure and the final enabled-provider inventory before adopting these pages. Public hosting and the Android links are still pending.</p>'''
    (SITE / 'index.html').write_text(page('Legal documents for review', home), encoding='utf-8')
    manifest = {'status': 'local_review_pages_rendered_not_adopted_not_published', 'package': 'com.multicastgames.catscuteminer',
                'audience': 'mixed_including_children_under13; all_under16_and_unknown_protected',
                'files': entries, 'runtime_urls_changed': False, 'website_contains_tracking_or_forms': False,
                'pending': ['Actual studio child-data retention/deletion procedure', 'Actual enabled-provider/network inventory',
                            'Operator adoption and public hosting at confirmed URLs', 'Final Android/native/device comparison']}
    (DOCS / 'publication-plan.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    archive = DOCS / 'cats-cute-miner-legal-review.zip'
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as bundle:
        for path in sorted(SITE.rglob('*.html')):
            bundle.write(path, path.relative_to(DOCS))
        for name in ['privacy-policy.draft.md', 'terms-of-use.draft.md', 'publication-plan.json']:
            bundle.write(DOCS / name, name)
    print(json.dumps({'archive': str(archive.relative_to(ROOT)), 'sha256': hashlib.sha256(archive.read_bytes()).hexdigest(),
                      'review_pages': [entry['path'] for entry in entries], 'published': False}, indent=2))


if __name__ == '__main__':
    main()
