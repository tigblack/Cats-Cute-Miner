"""Макет группы dressing в контексте поля ×3: python3 Tools/PixelArt/out/dressing_context.py → out/dressing_context.png.
Пол и стены из env, для сравнения rock_plain_0, block_single_5 и beam_post. Раскладка — клетки (столбец, строка),
pivot «ноги»: низ холста = низ footprint."""
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)

import pxl  # noqa: E402
from sprites import dressing, env, rocks  # noqa: E402

COLS, ROWS = 11, 12
ROCK_CELLS = {(c, 0) for c in range(COLS)} | {(c, 1) for c in range(COLS)} | {(6, 6), (7, 6), (8, 6)}


def main():
    reg = pxl.Registry()
    for m in (env, rocks, dressing):
        m.build(reg)
    W, H = COLS * 32, ROWS * 32
    cv = pxl.Canvas(W, H)
    for r in range(ROWS):
        for c in range(COLS):
            if c == 0:
                t = env.wall_edge_l(r % 2)
            elif c == COLS - 1:
                t = env.wall_edge_r(r % 2)
            elif (c, r) in ROCK_CELLS:
                t = env.wall_top((c + r) % 4)
            else:
                t = env.floor_tile((c * 3 + r * 5) % 4)
            cv.blit(t, c * 32, r * 32)
    # декали пола — под всем
    items = [
        ('decal_paint_0', 3, 2, 1), ('decal_paws_0', 5, 3, 1), ('decal_stain_0', 8, 4, 1), ('decal_pebbles_1', 2, 8, 1),
        ('decal_board_0', 4, 10, 1), ('decal_stain_1', 9, 9, 1), ('decal_paint_1', 6, 8, 1), ('decal_pebbles_0', 9, 3, 1),
        ('debris_planks_rest', 3, 7, 1), ('debris_yarn_rest', 7, 10, 1), ('debris_scrap_rest', 9, 7, 1),
    ]
    objs = [
        # стена: декор и заколоченный штрек на скальных клетках над полом
        ('wall_brace_0', 2, 1, 1), ('prop_sealedshaft_0', 4, 1, 1), ('wall_sign_0', 6, 1, 1), ('wall_rack_0', 8, 1, 1),
        ('prop_sealedshaft_1', 7, 6, 1), ('wall_sign_0', 6, 6, 1), ('wall_brace_0', 8, 6, 1),
        # у стены: крепь, рядом рыхлая кучка досок; лебёдка, бункер
        ('prop_support_0', 1, 2, 1), ('debris_planks_0', 2, 2, 1), ('prop_hopper_0', 5, 2, 2), ('prop_winch_0', 9, 2, 1),
        ('beam_post', 3, 3, 1),
        # коридор с завалами
        ('prop_minecart_1', 1, 4, 2), ('debris_crates_1', 4, 4, 1), ('debris_rubble_0', 5, 4, 1), ('debris_cardboard_1', 6, 4, 1),
        ('rock_plain_0', 7, 4, 1), ('prop_reel_0', 9, 4, 1),
        ('prop_support_1', 1, 5, 1), ('debris_planks_2', 2, 5, 1), ('debris_scrap_2', 3, 5, 2), ('debris_yarn_1', 5, 5, 1),
        ('prop_tower_0', 9, 5, 1),
        ('debris_rubble_2', 1, 7, 2), ('debris_sacks_0', 4, 7, 1), ('debris_catfurn_1', 5, 7, 1), ('prop_statue_0', 6, 7, 1),
        ('prop_drill_0', 7, 7, 2), ('prop_support_2', 9, 6, 1),
        ('debris_crates_2', 1, 8, 2), ('debris_scrap_0', 3, 8, 1), ('debris_crates_0_d1', 4, 8, 1), ('block_single_5', 5, 8, 1),
        ('prop_reel_1', 8, 8, 1), ('debris_yarn_2', 9, 8, 1),
        ('prop_minecart_0', 1, 9, 2), ('debris_cardboard_0', 3, 9, 1), ('debris_catfurn_2', 4, 9, 1),
        ('debris_planks_1_d2', 5, 9, 1), ('prop_hopper_1', 6, 9, 2), ('prop_statue_1', 8, 9, 1), ('rock_plain_0_d2', 9, 9, 1),
        ('prop_minecart_2', 1, 11, 2), ('debris_sacks_2', 3, 11, 1), ('debris_rubble_1_d1', 4, 11, 1), ('prop_tower_1', 5, 11, 1),
        ('debris_catfurn_0', 6, 11, 1), ('debris_sacks_1', 7, 11, 1), ('debris_scrap_1', 8, 11, 1), ('debris_cardboard_2', 9, 11, 1),
    ]
    for group in (items, sorted(objs, key=lambda o: o[2])):
        for name, c, r, w in group:
            e = reg.entries[name]
            x = c * 32 + w * 16 - int(e.pivot[0])
            y = (r + 1) * 32 - e.canvas.h + int(e.pivot[1])
            cv.blit(e.canvas, x, y)
    out = os.path.join(HERE, 'out', 'dressing_context.png')
    cv.scaled(3).save(out)
    print(out)


if __name__ == '__main__':
    main()
