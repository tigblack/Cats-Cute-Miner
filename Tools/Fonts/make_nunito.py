#!/usr/bin/env python3
"""Статические начертания Nunito для UI (Docs/Rework/MINE_DRESSING.md, «Шрифт»): SemiBold 600 и ExtraBold 800 из вариативного
Nunito[wght].ttf (SIL OFL 1.1, без зарезервированных имён). Изменения: метрики строки (hhea/typo: ascent 970,
descent −250, lineGap 0 → 1,22 em, как у прежнего Pixeloid Sans — раскладка HUD рассчитана на эту высоту строки) и добавленная
стрелка → (U+2192), которой нет в Nunito.
Запуск: Tools/.venv/bin/python -I Tools/Fonts/make_nunito.py <Nunito[wght].ttf> <папка вывода>
"""
import sys
from fontTools.ttLib import TTFont
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.varLib import instancer

ASCENT, DESCENT = 970, -250


def add_arrow(font, stroke):
    """Стрелка → (U+2192), которой нет в Nunito: ствол и наконечник толщиной штриха начертания (строки «было → стало»)."""
    if 0x2192 in font.getBestCmap():
        return
    pen = TTGlyphPen(None)
    mid, half = 320, stroke // 2
    # Ствол.
    pen.moveTo((70, mid - half)); pen.lineTo((70, mid + half)); pen.lineTo((560, mid + half)); pen.lineTo((560, mid - half)); pen.closePath()
    # Наконечник.
    pen.moveTo((430, mid + 260)); pen.lineTo((760, mid)); pen.lineTo((430, mid - 260)); pen.lineTo((430 + stroke // 3, mid)); pen.closePath()
    glyph = pen.glyph()
    font['glyf']['arrowright'] = glyph
    order = font.getGlyphOrder()
    if 'arrowright' not in order:
        font.setGlyphOrder(order + ['arrowright'])
    font['hmtx']['arrowright'] = (830, 70)
    for table in font['cmap'].tables:
        if table.isUnicode():
            table.cmap[0x2192] = 'arrowright'
    font['maxp'].numGlyphs = len(font.getGlyphOrder())

def make(src, weight, style, out):
    f = TTFont(src)
    inst = instancer.instantiateVariableFont(f, {"wght": weight}, updateFontNames=True)
    hh, os2 = inst['hhea'], inst['OS/2']
    hh.ascent, hh.descent, hh.lineGap = ASCENT, DESCENT, 0
    os2.sTypoAscender, os2.sTypoDescender, os2.sTypoLineGap = ASCENT, DESCENT, 0
    os2.fsSelection |= 1 << 7  # USE_TYPO_METRICS
    add_arrow(inst, 96 if weight < 700 else 128)
    name = inst['name']
    for rec in list(name.names):
        if rec.nameID == 5:
            name.setName(rec.toUnicode() + "; Cat Miners line metrics", 5, rec.platformID, rec.platEncID, rec.langID)
    inst.save(out)
    print(out, style, weight)

if __name__ == '__main__':
    src, outdir = sys.argv[1], sys.argv[2]
    make(src, 600, 'SemiBold', outdir + '/Nunito-SemiBold.ttf')
    make(src, 800, 'ExtraBold', outdir + '/Nunito-ExtraBold.ttf')
