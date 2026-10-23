"""Advertising references must not be mistaken for installed SDK classes."""
import importlib.util
from pathlib import Path
import struct
import unittest

spec = importlib.util.spec_from_file_location('inspect_aab', Path(__file__).with_name('inspect-aab.py'))
inspector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(inspector)


def fixture(defined):
    names = [b'Lgame/Main;', b'Lcom/applovin/sdk/AppLovinSdk;']
    strings_offset, types_offset, classes_offset = 112, 120, 128
    data = bytearray(classes_offset + 32 * len(defined))
    data[:8] = b'dex\n035\0'
    struct.pack_into('<4I', data, 56, 2, strings_offset, 2, types_offset)
    struct.pack_into('<2I', data, 96, len(defined), classes_offset)
    for index, name in enumerate(names):
        struct.pack_into('<I', data, strings_offset + index * 4, len(data))
        struct.pack_into('<I', data, types_offset + index * 4, index)
        data.extend(bytes([len(name)]) + name + b'\0')
    for index, type_index in enumerate(defined):
        struct.pack_into('<I', data, classes_offset + index * 32, type_index)
    return bytes(data)


class DexClassesTests(unittest.TestCase):
    def test_optional_reference_is_not_an_installed_class(self):
        self.assertEqual(inspector.dex_classes(fixture([0])), {b'Lgame/Main;'})

    def test_installed_advertising_class_is_detected(self):
        self.assertIn(b'Lcom/applovin/sdk/AppLovinSdk;', inspector.dex_classes(fixture([0, 1])))

    def test_invalid_dex_is_rejected(self):
        with self.assertRaises(ValueError):
            inspector.dex_classes(b'not a dex')


if __name__ == '__main__':
    unittest.main()
