# Звуки: авторы и лицензии

Все сторонние исходники звуков — **CC0 1.0 Universal** (передача в общественное достояние,
<https://creativecommons.org/publicdomain/zero/1.0/>): можно использовать в коммерческом проекте, изменять и
распространять без разрешения и без обязательного указания авторства. Авторы указаны из благодарности.
Готовые файлы — производные (обрезка, фильтры, сведение слоёв, см. [`SOUNDS.md`](SOUNDS.md) и [`build_sfx.py`](build_sfx.py)).

| Источник | Автор | Лицензия | Ссылка | Что взято |
|---|---|---|---|---|
| Impact Sounds 1.0 | Kenney (kenney.nl) | CC0 (License.txt в архиве) | <https://kenney.nl/assets/impact-sounds> | `impactMining_000/001/002/003/004`, `impactMetal_light_001/004`, `impactWood_light_000/002` → hit, hit_crystal, hit_wood, hit_metal, weapon_hammer, weapon_crusher, weapon_saw, weapon_dualpick, break, coin, payout, denied, land |
| RPG Audio | Kenney (kenney.nl) | CC0 (License.txt в архиве) | <https://kenney.nl/assets/rpg-audio> | `cloth2`, `bookClose` → open, close, cannon, land, hit_wood |
| Music Jingles | Kenney (kenney.nl) | CC0 (License.txt в архиве) | <https://kenney.nl/assets/music-jingles> | `jingles_PIZZI08/10/16`, `jingles_STEEL04/10` → upgrade, unlock, chest, exit, gate |
| Meow cat #6, #7, #11 (`s1894`, `s1895`, `s1899`) | Joseph Sardin, BigSoundBank.com | CC0 («CC0 (public domain): Free and royalty-free») | <https://bigsoundbank.com/meow-cat-6-s1894.html>, <https://bigsoundbank.com/meow-cat-7-s1895.html>, <https://bigsoundbank.com/meow-cat-11-s1899.html>; условия: <https://bigsoundbank.com/licenses.html> | meow_0..2 |
| Meow | IgnasD, OpenGameArt.org | CC0 | <https://opengameart.org/content/meow> | meow_3 |
| Various Sound Effects | Spring Spring (Julie Damsgaard), <https://spring-enterprises.neocities.org> | CC0 | <https://opengameart.org/content/various-sound-effects-0> | `small_amber`, `big_amber`, `bup`, `dull_explosion` → chime, merge, click, select, dynamite, cannon |
| 80 CC0 RPG SFX | rubberduck, OpenGameArt.org | CC0 | <https://opengameart.org/content/80-cc0-rpg-sfx> | `item_coins_02/03`, `stones_04` → payout, break |
| 75 CC0 breaking / falling / hit sfx | rubberduck, OpenGameArt.org | CC0 | <https://opengameart.org/content/75-cc0-breaking-falling-hit-sfx> | `bfh1_rock_falling_02/07` → break, dynamite |

**Оригинальное, без сторонних материалов** — синтез и аранжировка кодом в проекте, права у проекта:
- музыка `music_warm` «Тёплая штольня», `music_crystal` «Кристальная глубина» — `Tools/Audio/music.py`;
- атмосфера `amb_bed`, `amb_drip_*`, `amb_creak_*`, `amb_pebbles_*`, `amb_machine_*` — `Tools/Audio/ambience.py`;
- стеклянный «тинь» в `hit_crystal_*` — `Docs/Audio/build_sfx.py` (`r_hit_crystal`);
- звуки оружия: «тело» молота, хруст дробилки, моторы и визг пилы, бура и бензопилы, «пиу» лазера — `Docs/Audio/build_sfx.py` (`r_weapon_*`).

Строка для титров (необязательно): «Звуки: Kenney (kenney.nl), Joseph Sardin (BigSoundBank.com), Spring Spring,
rubberduck, IgnasD (OpenGameArt.org) — CC0».

Прямые ссылки на архивы (скачаны 6 октября 2026):
- `https://kenney.nl/media/pages/assets/impact-sounds/87b4ddecda-1677589768/kenney_impact-sounds.zip`
- `https://kenney.nl/media/pages/assets/rpg-audio/8e99002d76-1677590336/kenney_rpg-audio.zip`
- `https://kenney.nl/media/pages/assets/music-jingles/f37e530b9e-1677590399/kenney_music-jingles.zip`
- `https://bigsoundbank.com/UPLOAD/flac/<номер>.flac` (1894, 1895, 1899)
- `https://opengameart.org/sites/default/files/Meow.ogg`
- `https://opengameart.org/sites/default/files/<имя>.wav` (small_amber, big_amber, bup, dull_explosion)
- `https://opengameart.org/sites/default/files/80-CC0-RPG-SFX_0.zip`
- `https://opengameart.org/sites/default/files/sfx_breaking_and_falling.zip`
