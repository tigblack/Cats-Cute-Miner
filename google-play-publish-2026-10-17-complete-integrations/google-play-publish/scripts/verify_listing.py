#!/usr/bin/env python3
"""Check supplied listing evidence only; never discover fields or write status."""
import argparse
import json
import re
from datetime import datetime
from pathlib import Path


def validate(data):
    errors = []

    def need(condition, message):
        if not condition:
            errors.append(message)

    def ref(value):
        return isinstance(value, str) and bool(value.strip())

    def positive_int(value):
        return type(value) is int and value > 0

    def dimensions(value):
        return (isinstance(value, list) and len(value) == 2
                and all(positive_int(side) for side in value))

    def asset_constraints(value):
        if not isinstance(value, dict):
            return False
        formats = value.get('formats')
        if not (isinstance(formats, list) and formats
                and all(ref(fmt) for fmt in formats)
                and positive_int(value.get('max_bytes'))):
            return False
        if 'exact_dimensions' in value:
            return (dimensions(value['exact_dimensions'])
                    and not any(key in value for key in
                                ('min_side', 'max_side', 'allowed_aspect_ratios')))
        ratios = value.get('allowed_aspect_ratios')
        return (positive_int(value.get('min_side'))
                and positive_int(value.get('max_side'))
                and value['min_side'] <= value['max_side']
                and isinstance(ratios, list) and bool(ratios)
                and all(dimensions(ratio) for ratio in ratios))

    def timestamp(value):
        try:
            result = datetime.fromisoformat(value.replace('Z', '+00:00'))
            return result if result.tzinfo else None
        except (AttributeError, TypeError, ValueError):
            return None

    if not isinstance(data, dict):
        return ['evidence must be an object']
    for key in ('scope_ref', 'locale', 'inventory_evidence_ref'):
        need(ref(data.get(key)), f'missing {key}')
    need(data.get('inventory_complete') is True, 'inventory incomplete/unknown')
    for key in ('pending_upload', 'unsaved_changes', 'has_errors'):
        need(data.get(key) is False, f'{key} must be explicitly false')
    times = []
    for step in ('save', 'reload', 'readback'):
        record = data.get(step)
        record = record if isinstance(record, dict) else {}
        need(ref(record.get('evidence_ref')), f'missing native {step} evidence')
        times.append(timestamp(record.get('at')))
    need(all(times) and times[0] < times[1] <= times[2],
         'need timezone-aware save < reload <= readback timestamps')
    fields = data.get('fields')
    if not isinstance(fields, list) or not fields:
        return errors + ['fields inventory missing/empty']
    ids = set()
    active_categories = set()
    inventoried_categories = set()
    for index, field in enumerate(fields):
        if not isinstance(field, dict):
            errors.append(f'field {index}: must be an object')
            continue
        name = field.get('id')
        tag = f'field {name or index}'
        need(ref(name) and name not in ids, f'{tag}: missing/duplicate ID')
        if isinstance(name, str):
            ids.add(name)
        need(ref(field.get('label')), f'{tag}: missing observed label')
        need(ref(field.get('inventory_evidence_ref')), f'{tag}: missing inventory source')
        for key in ('required', 'visual'):
            need(type(field.get(key)) is bool, f'{tag}: unknown {key}')
        applicable = field.get('applicable')
        need(type(applicable) is bool, f'{tag}: unknown applicability')
        category = field.get('device_category')
        if category is not None:
            need(ref(category) and field.get('visual') is True
                 and field.get('kind') == 'assets', f'{tag}: invalid device category')
            if isinstance(category, str):
                inventoried_categories.add(category)
        if applicable is False:
            need(field.get('status') == 'not_applicable'
                 and ref(field.get('applicability_evidence_ref')),
                 f'{tag}: exclusion needs native condition evidence')
            if field.get('required') is True:
                need(field.get('conditional') is True and field.get('condition_satisfied') is False,
                     f'{tag}: required field cannot be excluded without an inactive condition')
            continue
        if applicable is not True:
            continue
        if category is not None:
            if isinstance(category, str):
                active_categories.add(category)
        if field.get('status') == 'omitted':
            need(field.get('required') is False and field.get('visual') is False
                 and ref(field.get('omission_reason')), f'{tag}: cannot omit required/visual field')
            continue
        need(field.get('status') == 'verified', f'{tag}: unverified/pending/error')
        need(ref(field.get('readback_evidence_ref')), f'{tag}: missing native field readback')
        kind = field.get('kind')
        if kind == 'value':
            value = field.get('expected_value')
            need(value is not None and value != [] and value != {}
                 and (not isinstance(value, str) or bool(value.strip())),
                 f'{tag}: empty expected value')
            need('observed_value' in field and type(field['observed_value']) is type(value)
                 and field['observed_value'] == value,
                 f'{tag}: persisted value mismatch/unknown')
        elif kind == 'assets':
            assets = field.get('expected_assets')
            observed = field.get('observed_assets')
            constraints = field.get('asset_constraints')
            valid_constraints = asset_constraints(constraints)
            need(valid_constraints, f'{tag}: missing/invalid asset_constraints')
            if not isinstance(assets, list) or not assets:
                errors.append(f'{tag}: empty expected assets')
                continue
            count = field.get('observed_count')
            need(type(count) is int and count == len(assets), f'{tag}: persisted count mismatch')
            minimum, maximum = field.get('min_count'), field.get('max_count')
            need(type(minimum) is int and type(maximum) is int
                 and 0 < minimum <= len(assets) <= maximum, f'{tag}: missing/violated count limits')
            need(ref(field.get('constraints_evidence_ref')), f'{tag}: missing asset constraints source')
            need(observed == assets, f'{tag}: asset identity/order/dimensions mismatch')
            need(field.get('upload_status') == 'complete', f'{tag}: upload/processing not complete')
            need(isinstance(observed, list) and bool(observed), f'{tag}: empty/invalid observed assets')
            # Validate both records: equal metadata alone does not prove compliance.
            records = assets + (observed if isinstance(observed, list) else [])
            for asset in records:
                if not isinstance(asset, dict):
                    errors.append(f'{tag}: invalid asset')
                    continue
                need(ref(asset.get('asset_id')), f'{tag}: missing native asset ID/mapping')
                valid_dimensions = all(positive_int(asset.get(k)) for k in ('width', 'height'))
                need(valid_dimensions,
                     f'{tag}: unknown/invalid pixel dimensions')
                valid_bytes = positive_int(asset.get('bytes'))
                need(valid_bytes, f'{tag}: unknown/invalid asset bytes')
                if valid_constraints:
                    need(asset.get('format') in constraints['formats'], f'{tag}: disallowed asset format')
                    if valid_bytes:
                        need(asset['bytes'] <= constraints['max_bytes'], f'{tag}: asset exceeds max_bytes')
                    if valid_dimensions:
                        width, height = asset['width'], asset['height']
                        if 'exact_dimensions' in constraints:
                            need([width, height] == constraints['exact_dimensions'],
                                 f'{tag}: asset violates exact_dimensions')
                        else:
                            need(all(constraints['min_side'] <= side <= constraints['max_side']
                                     for side in (width, height)), f'{tag}: asset violates pixel bounds')
                            need(any(width * ratio_h == height * ratio_w
                                     for ratio_w, ratio_h in constraints['allowed_aspect_ratios']),
                                 f'{tag}: asset violates allowed_aspect_ratios')
                need(isinstance(asset.get('sha256'), str)
                     and re.fullmatch(r'[0-9a-fA-F]{64}', asset['sha256']) is not None,
                     f'{tag}: missing file hash')
        else:
            errors.append(f'{tag}: unsupported/unknown kind')
    categories = data.get('device_categories')
    categories = categories if isinstance(categories, dict) else {}
    for category in set(categories) | {'phone', 'tablet_7', 'tablet_10'} | active_categories:
        entry = categories.get(category)
        entry = entry if isinstance(entry, dict) else {}
        present = entry.get('present')
        applicable = entry.get('applicable')
        need(type(present) is bool and ref(entry.get('evidence_ref')),
             f'{category}: missing native presence/absence check')
        need(type(applicable) is bool, f'{category}: unknown category applicability')
        need((applicable is True) == (category in active_categories),
             f'{category}: applicability disagrees with asset fields')
        need(not applicable or present is True, f'{category}: applicable field absent from form')
        need(present is not True or category in inventoried_categories,
             f'{category}: present category omitted from field inventory')
        if present is True and applicable is False:
            need(ref(entry.get('applicability_evidence_ref')),
                 f'{category}: optional category exclusion needs scope/condition evidence')
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('evidence', type=Path)
    args = parser.parse_args()
    try:
        errors = validate(json.loads(args.evidence.read_text(encoding='utf-8')))
    except (OSError, ValueError, TypeError) as exc:
        errors = [f'invalid evidence: {exc}']
    print(json.dumps({'gate': 'blocked' if errors else 'passed', 'errors': errors}, ensure_ascii=False))
    return 1 if errors else 0


if __name__ == '__main__':
    raise SystemExit(main())
