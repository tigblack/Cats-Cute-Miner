#!/usr/bin/env python3
"""Render review pages, or adopted public pages with --release; never deploy or edit runtime URLs."""
import argparse
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
    home = '''<h1>Documents for review</h1><p>Separate documents for Cat Miner: Idle Tycoon, for players aged 13 and older, with protected play for under-16 and unknown ages.</p><p>The first release contains no advertisements for any player. MAX and advertising providers are deferred to a second release. Six optional consumable purchases and optional eligible-player analytics remain in scope.</p><ul class="documents"><li><a href="privacy/index.html">Read the Privacy Policy</a></li><li><a href="terms/index.html">Read the Terms of Use</a></li></ul><p>The studio has not confirmed the actual support, parental-request, access, retention or deletion process. Adoption, public hosting, Android links and final native and device verification remain pending.</p>'''
    (SITE / 'index.html').write_text(page('Legal documents for review', home), encoding='utf-8')
    manifest = {'status': 'local_review_pages_rendered_not_adopted_not_published', 'package': 'com.multicastgames.catscuteminer',
                'audience': '13_and_older; all_under16_and_unknown_protected',
                'files': entries, 'runtime_urls_changed': False, 'website_contains_tracking_or_forms': False,
                'pending': ['Actual studio support/parental-request/access/retention/deletion process (explicitly not confirmed)',
                            'Final no-ads native inventory: no MAX/advertising provider; verify optional analytics and essential IAP services',
                            'Operator adoption and public hosting at confirmed game-specific URLs (actual URLs empty)',
                            'Final Android/native/Billing/ABI/16KB/purchase/device and analytics comparison'],
                'first_release_scope': 'no_ads_for_any_player_MAX_and_advertising_providers_deferred_second_release',
                'ads_enabled': False, 'enabled_ad_formats': [], 'active_advertising_providers': [],
                'optional_analytics_providers': ['AppMetrica', 'Adjust', 'Meta App Events'], 'consumable_iap_count': 6,
                'operator_process_confirmed': False, 'adopted': False, 'published': False,
                'published_urls': {'privacy': None, 'terms': None},
                'advertising_cmp_and_mediation': 'deferred_second_release_not_first_release_blocker',
                'home': {'path': str((SITE / 'index.html').relative_to(ROOT)),
                         'html_sha256': hashlib.sha256((SITE / 'index.html').read_bytes()).hexdigest()}}
    (DOCS / 'publication-plan.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    archive = DOCS / 'cats-cute-miner-legal-review.zip'
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as bundle:
        for path in sorted(SITE.rglob('*.html')):
            bundle.write(path, path.relative_to(DOCS))
        for name in ['privacy-policy.draft.md', 'terms-of-use.draft.md', 'publication-plan.json']:
            bundle.write(DOCS / name, name)
    print(json.dumps({'archive': str(archive.relative_to(ROOT)), 'sha256': hashlib.sha256(archive.read_bytes()).hexdigest(),
                      'review_pages': [entry['path'] for entry in entries], 'published': False}, indent=2))


def release_main():
    adoption_path = DOCS / 'adoption-current.json'
    if not adoption_path.is_file():
        adoption_path = DOCS / 'adoption-20261019.json'
    adoption = json.loads(adoption_path.read_text(encoding='utf-8'))
    if not adoption.get('adoption_authorized') or not adoption.get('operator_process_confirmed'):
        raise SystemExit('Public pages require adoption authorization and the actual support process.')
    if adoption.get('package') != 'com.multicastgames.catscuteminer' or adoption.get('ads_enabled') is not False:
        raise SystemExit('Unexpected game or advertising scope.')
    public_site = DOCS / 'public-site'
    public_site.mkdir(parents=True, exist_ok=True)
    renderer = MarkdownIt('commonmark', {'html': False})

    def public_page(title, body, nested=True):
        prefix = '../' if nested else ''
        links = [('privacy', 'Privacy Policy'), ('terms', 'Terms of Use'), ('delete-data', 'Data requests')]
        navigation = ''.join(f'<a href="{prefix}{route}/">{label}</a>' for route, label in links)
        return f'''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="referrer" content="no-referrer">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src 'self'; base-uri 'none'; form-action 'none'">
<title>{escape(title)} | Cat Miners: Idle Tycoon</title><style>{STYLE}</style></head>
<body><header><strong>Cat Miners: Idle Tycoon</strong><span>Multicast Games Limited</span><nav aria-label="Legal documents"><a href="{prefix}index.html">Documents</a>{navigation}</nav></header>
<main>{body}</main><footer>Multicast Games Limited · <a href="mailto:support@multicastgames.com">support@multicastgames.com</a><br>Android: com.multicastgames.catscuteminer</footer></body></html>'''

    entries = []
    for name, route, title in [
        ('privacy-policy.md', 'privacy', 'Privacy Policy'),
        ('terms-of-use.md', 'terms', 'Terms of Use'),
        ('delete-data.md', 'delete-data', 'Data Access and Deletion'),
    ]:
        source = DOCS / name
        source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
        if adoption.get('adopted_source_sha256', {}).get(name) != source_hash:
            raise SystemExit(f'Adoption receipt does not match {name}.')
        text = source.read_text(encoding='utf-8')
        if any(marker in text for marker in ['Review copy', 'Review limitation', 'not yet adopted', 'not confirmed']):
            raise SystemExit(f'Unresolved review wording in {name}.')
        output = public_site / route / 'index.html'
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(public_page(title, renderer.render(text)), encoding='utf-8')
        entries.append({'source': str(source.relative_to(ROOT)), 'path': str(output.relative_to(ROOT)),
                        'route': route + '/', 'source_sha256': source_hash,
                        'html_sha256': hashlib.sha256(output.read_bytes()).hexdigest()})
    home = '''<h1>Legal documents</h1><p>Cat Miners: Idle Tycoon is an Android game by Multicast Games Limited for players aged 13 and older.</p><p>The game has no advertisements. Optional purchases are available through Google Play. From version 0.1.5, basic AppMetrica gameplay measurement starts automatically and can be disabled in settings. Adjust and Meta remain additional optional services for eligible players aged 16 or older.</p><ul class="documents"><li><a href="privacy/">Privacy Policy</a></li><li><a href="terms/">Terms of Use</a></li><li><a href="delete-data/">Request access, correction or deletion of data</a></li></ul><p>Players, parents and guardians can contact <a href="mailto:support@multicastgames.com">support@multicastgames.com</a> for privacy and purchase support.</p>'''
    (public_site / 'index.html').write_text(public_page('Legal documents', home, nested=False), encoding='utf-8')
    manifest = {'package': adoption['package'], 'status': 'adopted_public_pages_ready_to_publish',
                'adoption_ref': str(adoption_path.relative_to(ROOT)), 'adopted': True,
                'operator_process_confirmed': True, 'published': False,
                'public_folder': str(public_site.relative_to(ROOT)), 'files': entries,
                'home_sha256': hashlib.sha256((public_site / 'index.html').read_bytes()).hexdigest(),
                'published_urls': {'privacy': None, 'terms': None, 'data_requests': None},
                'runtime_urls_changed': False, 'website_contains_tracking_or_forms': False}
    (DOCS / 'publication-release.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'public_folder': manifest['public_folder'], 'documents': len(entries),
                      'adopted': True, 'operator_process_confirmed': True, 'published': False}, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release', action='store_true', help='Render final adopted documents without review banners.')
    args = parser.parse_args()
    release_main() if args.release else main()
