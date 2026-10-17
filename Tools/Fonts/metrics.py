#!/usr/bin/env python3
"""Метрики шрифтов: em, ascent/descent/lineGap (hhea, OS/2), высота заглавных и строчных."""
import sys
from fontTools.ttLib import TTFont
for path in sys.argv[1:]:
    f = TTFont(path)
    upm = f['head'].unitsPerEm
    hh = f['hhea']; os2 = f['OS/2']
    print(path.split('/')[-1], 'upm', upm, 'hhea', hh.ascent, hh.descent, hh.lineGap, 'typo', os2.sTypoAscender, os2.sTypoDescender, os2.sTypoLineGap,
          'win', os2.usWinAscent, os2.usWinDescent, 'cap', getattr(os2, 'sCapHeight', None), 'x', getattr(os2, 'sxHeight', None),
          'line/em %.3f' % ((hh.ascent - hh.descent + hh.lineGap) / upm), 'cap/em %.3f' % ((getattr(os2, 'sCapHeight', 0) or 0) / upm),
          'x/em %.3f' % ((getattr(os2, 'sxHeight', 0) or 0) / upm), 'fvar' if 'fvar' in f else '')
