"""Package the local Axon campaign preparation without calling any network API."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PACK = ROOT / 'release/advertising/applovin'


def main():
    draft = json.loads((PACK / 'campaign-draft.json').read_text())
    assert draft['kind'] == 'local_planning_draft_not_provider_api_payload'
    assert draft['launch_authorized'] is False and draft['active'] is False
    assert draft['externally_created'] is False and draft['campaign_id'] is None
    assert draft['spend_started'] is False
    assert draft['targeting']['country_codes'] is None
    assert draft['budget']['daily_amount'] is None
    assert 'BR' in draft['targeting']['excluded_country_codes']
    assert draft['schedule']['start_at'] is None
    assert draft['schedule']['automatic_activation'] is False
    assert draft['game_ads']['max_enabled'] is False
    checks = json.loads((PACK / 'artifact-checks.json').read_text())
    assert checks['full_stream_decode']['status'] == 'passed'
    for item in checks['assets']:
        asset = PACK / item['path']
        assert asset.stat().st_size == item['bytes']
        assert hashlib.sha256(asset.read_bytes()).hexdigest() == item['sha256']

    files = [PACK / x for x in (
        'README.md', 'index.html', 'campaign-draft.json', 'tracking-plan.json',
        'account-readback.json', 'readiness.json', 'artifact-checks.json',
        'creative-source.json', 'studio-assets.json',
    )]
    files += [PACK / draft['creative'][k] for k in ('video', 'endcard', 'icon')]
    files += sorted((PACK / 'review').glob('CatMiner_Axon_*.png'))
    files += sorted((PACK / 'review').glob('preview-*.png'))
    for path in files:
        assert path.is_file() and path.resolve().is_relative_to(PACK.resolve())
        assert '.local.' not in path.name and not path.name.startswith('.env')
        if path.suffix in ('.json', '.md', '.html'):
            data = path.read_text()
            assert not any(x in data for x in ('slmcp_', '-----BEGIN PRIVATE KEY', '-----BEGIN RSA PRIVATE KEY'))
    manifest = {
        'generated_at': datetime.now(timezone.utc).isoformat(),
        'kind': 'non_serving_local_preparation_package',
        'campaign_created': False, 'campaign_active': False,
        'countries': None, 'daily_budget_usd': None,
        'files': [{'path': str(p.relative_to(PACK)), 'bytes': p.stat().st_size,
                   'sha256': hashlib.sha256(p.read_bytes()).hexdigest()} for p in files],
        'excluded': ['private configs', 'browser sessions', 'keys and credentials',
                     'raw captures', 'source audio', 'unrelated media'],
    }
    (PACK / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    files.append(PACK / 'manifest.json')
    archive = PACK / 'CatMiner_AppLovin_Preparation.zip'
    with zipfile.ZipFile(archive, 'w') as z:
        for path in files:
            compression = zipfile.ZIP_STORED if path.suffix == '.mp4' else zipfile.ZIP_DEFLATED
            z.write(path, arcname=str(path.relative_to(PACK)), compress_type=compression)
    with zipfile.ZipFile(archive) as z:
        assert z.testzip() is None
        assert all(not p.startswith('/') and '..' not in Path(p).parts for p in z.namelist())
    result = {'archive': str(archive.relative_to(ROOT)), 'bytes': archive.stat().st_size,
              'sha256': hashlib.sha256(archive.read_bytes()).hexdigest(), 'files': len(files)}
    (PACK / 'archive-checks.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result))


if __name__ == '__main__':
    main()
