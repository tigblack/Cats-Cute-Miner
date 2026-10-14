"""Synthetic gate regression fixtures; never release receipts."""
import copy
import unittest

from verify_listing import validate


def evidence():
    # Universal compositions intentionally reused across device categories.
    assets = [{'asset_id': 'synthetic-asset', 'width': 1080, 'height': 1920,
               'format': 'PNG', 'bytes': 4096, 'sha256': 'a' * 64}]
    fields = []
    for category in ('phone', 'tablet_7', 'tablet_10'):
        fields.append({'id': category, 'label': category, 'required': False,
                       'visual': True, 'applicable': True, 'kind': 'assets',
                       'device_category': category, 'status': 'verified',
                       'inventory_evidence_ref': 'synthetic-form',
                       'readback_evidence_ref': 'synthetic-readback',
                       'constraints_evidence_ref': 'synthetic-limits',
                       'asset_constraints': {'formats': ['PNG', 'JPEG'], 'max_bytes': 8192,
                                             'min_side': 320, 'max_side': 3840,
                                             'allowed_aspect_ratios': [[9, 16], [16, 9]]},
                       'min_count': 1, 'max_count': 8, 'upload_status': 'complete',
                       'observed_count': 1, 'expected_assets': copy.deepcopy(assets),
                       'observed_assets': copy.deepcopy(assets)})
    fields.append({'id': 'title', 'label': 'Title', 'required': True,
                   'visual': False, 'applicable': True, 'kind': 'value',
                   'status': 'verified', 'expected_value': 'Synthetic game',
                   'observed_value': 'Synthetic game',
                   'inventory_evidence_ref': 'synthetic-form',
                   'readback_evidence_ref': 'synthetic-readback'})
    return {'scope_ref': 'synthetic-listing', 'locale': 'en-US',
            'inventory_complete': True, 'inventory_evidence_ref': 'synthetic-form',
            'pending_upload': False, 'unsaved_changes': False, 'has_errors': False,
            'save': {'at': '2026-10-09T10:00:00Z', 'evidence_ref': 'synthetic-save'},
            'reload': {'at': '2026-10-09T10:01:00Z', 'evidence_ref': 'synthetic-reload'},
            'readback': {'at': '2026-10-09T10:02:00Z', 'evidence_ref': 'synthetic-readback'},
            'device_categories': {k: {'present': True, 'applicable': True, 'evidence_ref': 'synthetic-form'}
                                  for k in ('phone', 'tablet_7', 'tablet_10')},
            'fields': fields}


class ListingGateTests(unittest.TestCase):
    def test_complete_universal_reuse(self):
        self.assertEqual(validate(evidence()), [])

    def test_phone_success_does_not_cover_empty_tablets(self):
        data = evidence()
        for field in data['fields'][1:3]:
            field.update(observed_count=0, observed_assets=[])
        self.assertTrue(validate(data))

    def test_removed_tablet_field_still_blocked(self):
        data = evidence()
        del data['fields'][1]
        self.assertTrue(validate(data))

    def test_pending_unsaved_error_unknown_and_stale_readback(self):
        for flag in ('pending_upload', 'unsaved_changes', 'has_errors'):
            for value in (True, None):
                with self.subTest(flag=flag, value=value):
                    data = evidence()
                    data[flag] = value
                    self.assertTrue(validate(data))
        data = evidence()
        data['save']['at'] = '2026-10-09T11:00:00Z'
        self.assertTrue(validate(data))
        data = evidence()
        data['fields'][0]['upload_status'] = 'processing'
        self.assertTrue(validate(data))

    def test_count_order_dimensions_and_text_must_match(self):
        for key, value in (('asset_id', 'other'), ('width', 0), ('height', 1080)):
            with self.subTest(key=key):
                data = evidence()
                data['fields'][0]['observed_assets'][0][key] = value
                self.assertTrue(validate(data))
        data = evidence()
        data['fields'][-1]['observed_value'] = ''
        self.assertTrue(validate(data))
        data = evidence()
        field = data['fields'][0]
        second = dict(field['expected_assets'][0], asset_id='second')
        field['expected_assets'].append(second)
        field.update(observed_assets=list(reversed(field['expected_assets'])), observed_count=2)
        self.assertTrue(validate(data))

    def test_required_visual_cannot_be_omitted_or_unknown(self):
        for index in (0, 3):
            data = evidence()
            data['fields'][index].update(status='omitted', omission_reason='No access')
            self.assertTrue(validate(data))
        data = evidence()
        data['fields'][1]['applicable'] = None
        self.assertTrue(validate(data))

    def test_absent_category_requires_evidence(self):
        data = evidence()
        data['fields'][1].update(applicable=False, status='not_applicable',
                                  applicability_evidence_ref='synthetic-scope')
        data['device_categories']['tablet_7']['present'] = False
        data['device_categories']['tablet_7']['applicable'] = False
        self.assertEqual(validate(data), [])
        del data['fields'][1]['applicability_evidence_ref']
        self.assertTrue(validate(data))

    def test_present_optional_platform_can_be_outside_requested_scope(self):
        data = evidence()
        data['fields'].append({'id': 'xr', 'label': 'XR screenshots', 'device_category': 'xr',
            'required': False, 'visual': True, 'applicable': False, 'kind': 'assets',
            'status': 'not_applicable', 'inventory_evidence_ref': 'synthetic-form',
            'applicability_evidence_ref': 'synthetic-phone-tablet-release-scope'})
        data['device_categories']['xr'] = {'present': True, 'applicable': False,
            'evidence_ref': 'synthetic-form', 'applicability_evidence_ref': 'synthetic-phone-tablet-release-scope'}
        self.assertEqual(validate(data), [])
        data['fields'][-1]['required'] = True
        self.assertTrue(validate(data))
        data['fields'][-1].update(conditional=True, condition_satisfied=False)
        self.assertEqual(validate(data), [])

    def test_false_declaration_is_valid_but_empty_or_wrong_type_is_not(self):
        data = evidence()
        data['fields'][-1].update(expected_value=False, observed_value=False)
        self.assertEqual(validate(data), [])
        data['fields'][-1]['observed_value'] = 0
        self.assertTrue(validate(data))
        for value in ('   ', {}, [], None):
            with self.subTest(value=value):
                data = evidence()
                data['fields'][-1].update(expected_value=value, observed_value=value)
                self.assertTrue(validate(data))

    def test_missing_native_receipts_and_invalid_inventory(self):
        for key in ('inventory_complete', 'device_categories', 'fields', 'reload'):
            with self.subTest(key=key):
                data = evidence()
                del data[key]
                self.assertTrue(validate(data))
        data = evidence()
        del data['fields'][0]['readback_evidence_ref']
        self.assertTrue(validate(data))
        self.assertTrue(validate([]))

    def test_matching_metadata_must_obey_each_field_constraints(self):
        for patch, error in (
                ({'width': 9, 'height': 16}, 'pixel bounds'),
                ({'width': 2169, 'height': 3856}, 'pixel bounds'),
                ({'width': 1080, 'height': 1919}, 'allowed_aspect_ratios'),
                ({'width': 1920, 'height': 1081}, 'allowed_aspect_ratios'),
                ({'bytes': 8193}, 'max_bytes'),
                ({'format': 'VIDEO'}, 'disallowed asset format'),
                ({'format': 'WEBP'}, 'disallowed asset format')):
            with self.subTest(patch=patch):
                data = evidence()
                field = data['fields'][0]
                for key in ('expected_assets', 'observed_assets'):
                    field[key][0].update(patch)
                self.assertTrue(any(error in item for item in validate(data)))

    def test_bytes_and_dimensions_require_positive_integers_in_both_records(self):
        for key in ('bytes', 'width', 'height'):
            for value in (None, False, True, 0, -1, 4096.0, '4096'):
                with self.subTest(key=key, value=value):
                    data = evidence()
                    for record in ('expected_assets', 'observed_assets'):
                        data['fields'][0][record][0][key] = value
                    self.assertTrue(any('unknown/invalid' in item for item in validate(data)))
            data = evidence()
            for record in ('expected_assets', 'observed_assets'):
                del data['fields'][0][record][0][key]
            self.assertTrue(validate(data))

    def test_recorded_constraints_allow_boundaries_and_exact_integer_ratios(self):
        for width, height in ((320, 320), (3840, 3840), (1920, 1080), (1080, 1920)):
            with self.subTest(width=width, height=height):
                data = evidence()
                field = data['fields'][0]
                field['asset_constraints']['allowed_aspect_ratios'].append([2, 2])
                for record in ('expected_assets', 'observed_assets'):
                    field[record][0].update(width=width, height=height, bytes=8192, format='JPEG')
                self.assertEqual(validate(data), [])
        # Beyond float precision: adjacent widths must still have different ratios.
        data = evidence()
        field = data['fields'][0]
        side = 2 ** 54
        field['asset_constraints'].update(min_side=1, max_side=side + 1,
                                          allowed_aspect_ratios=[[1, 1]])
        for record in ('expected_assets', 'observed_assets'):
            field[record][0].update(width=side + 1, height=side)
        self.assertTrue(any('allowed_aspect_ratios' in item for item in validate(data)))

    def test_exact_icon_dimensions_are_field_specific(self):
        data = evidence()
        icon = copy.deepcopy(data['fields'][0])
        icon.update(id='icon', label='Icon', required=True,
                    asset_constraints={'formats': ['PNG'], 'max_bytes': 4096,
                                       'exact_dimensions': [512, 512]})
        del icon['device_category']
        for record in ('expected_assets', 'observed_assets'):
            icon[record][0].update(width=512, height=512)
        data['fields'].append(icon)
        self.assertEqual(validate(data), [])
        for width, height in ((511, 512), (512, 513), (1024, 1024)):
            with self.subTest(width=width, height=height):
                wrong = copy.deepcopy(data)
                for record in ('expected_assets', 'observed_assets'):
                    wrong['fields'][-1][record][0].update(width=width, height=height)
                self.assertTrue(any('exact_dimensions' in item for item in validate(wrong)))

    def test_missing_or_malformed_constraints_block_without_raising(self):
        valid = evidence()['fields'][0]['asset_constraints']
        malformed = [None, [], 'limits', {}, {'exact_dimensions': [512, 512]}]
        for key in valid:
            missing = copy.deepcopy(valid)
            del missing[key]
            malformed.append(missing)
        for key, values in {
                'formats': (None, 'PNG', [], [None], [[]], ['']),
                'max_bytes': (None, True, 0, -1, 8192.0, '8192'),
                'min_side': (None, False, 0, -1, 320.0, '320', 4000),
                'max_side': (None, True, 0, -1, 3840.0, '3840', 319),
                'allowed_aspect_ratios': (None, [], '9:16', [[9]], [[9, 16, 1]],
                                          [[0, 16]], [[9, True]], [[9, 16.0]], [{}]),
        }.items():
            for value in values:
                malformed.append(dict(valid, **{key: value}))
        exact = {'formats': ['PNG'], 'max_bytes': 4096, 'exact_dimensions': [512, 512]}
        for value in (None, [], [512], [512, 512, 512], [0, 512], [512, True],
                      [512.0, 512], '512x512', {}):
            malformed.append(dict(exact, exact_dimensions=value))
        for key in ('min_side', 'max_side', 'allowed_aspect_ratios'):
            malformed.append(dict(exact, **{key: valid[key]}))
        for constraints in malformed:
            with self.subTest(constraints=constraints):
                data = evidence()
                data['fields'][0]['asset_constraints'] = constraints
                self.assertTrue(any('invalid asset_constraints' in item for item in validate(data)))
        for key in ('asset_constraints', 'constraints_evidence_ref'):
            data = evidence()
            del data['fields'][0][key]
            self.assertTrue(validate(data))

    def test_count_gate_remains_independent_of_metadata_constraints(self):
        for minimum, maximum in ((2, 8), (1, 0), (None, 8), (1, True)):
            with self.subTest(minimum=minimum, maximum=maximum):
                data = evidence()
                data['fields'][0].update(min_count=minimum, max_count=maximum)
                self.assertTrue(any('count limits' in item for item in validate(data)))

    def test_observed_metadata_types_cannot_hide_behind_python_equality(self):
        data = evidence()
        field = data['fields'][0]
        field['expected_assets'][0]['bytes'] = 1
        field['observed_assets'][0]['bytes'] = True
        self.assertTrue(any('invalid asset bytes' in item for item in validate(data)))

    def test_excluded_and_omitted_fields_do_not_require_asset_constraints(self):
        data = evidence()
        field = data['fields'][1]
        field.update(applicable=False, status='not_applicable',
                     applicability_evidence_ref='synthetic-scope')
        for key in ('expected_assets', 'observed_assets', 'asset_constraints',
                    'constraints_evidence_ref'):
            del field[key]
        data['device_categories']['tablet_7'].update(
            applicable=False, applicability_evidence_ref='synthetic-scope')
        data['fields'].append({'id': 'optional-assets', 'label': 'Optional assets',
                              'required': False, 'visual': False, 'applicable': True,
                              'kind': 'assets', 'status': 'omitted',
                              'omission_reason': 'Outside requested work',
                              'inventory_evidence_ref': 'synthetic-form'})
        self.assertEqual(validate(data), [])


if __name__ == '__main__':
    unittest.main()
