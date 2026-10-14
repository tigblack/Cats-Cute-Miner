#!/usr/bin/env python3
"""Reproducible collection contact sheets, separate from the atlas.
Run: python3 Tools/PixelArt/review_cards.py
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
from pxl import Registry, C
from sprites import card_art, cards, ui

OUT=Path(__file__).resolve().parent/'out'
FONT='/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'


def composite(reg,i):
    rarity,_=card_art.CATALOG[i]
    bg=cards.portrait_bg(card_art.RARITIES[rarity-1]).img.copy()
    bg.alpha_composite(reg.entries[f'card_art_ca{i:03}'].canvas.img,(8,2))
    return bg


def main():
    OUT.mkdir(exist_ok=True)
    reg=Registry();card_art.build(reg)
    font=ImageFont.truetype(FONT,12);title=ImageFont.truetype(FONT,19)
    cols=7;cw,ch=216,195;rows=(61+cols-1)//cols
    im=Image.new('RGB',(cols*cw+24,rows*ch+65),C('navy0')[:3]);d=ImageDraw.Draw(im)
    d.text((16,12),'Cat Miners · 61 уникальная карта · арт ×3 / 48×44',font=title,fill=C('cream3'))
    for k,i in enumerate(sorted(card_art.CATALOG)):
        x=12+(k%cols)*cw;y=53+(k//cols)*ch
        bg=composite(reg,i).resize((192,144),Image.Resampling.NEAREST)
        im.paste(bg,(x+12,y),bg)
        rarity,name=card_art.CATALOG[i]
        d.text((x+12,y+147),f'ca{i:03} · {card_art.RARITIES[rarity-1]}',font=font,fill=C('gold4'))
        # Wrap Russian names without losing words or squeezing text.
        words=name.split();lines=['']
        for word in words:
            candidate=(lines[-1]+' '+word).strip()
            if d.textlength(candidate,font=font)>cw-24:lines.append(word)
            else:lines[-1]=candidate
        for j,line in enumerate(lines):d.text((x+12,y+163+j*14),line,font=font,fill=C('cream3'))
    im.save(OUT/'cards_overview.png')
    state=Image.new('RGB',(770,298),C('navy0')[:3]);d=ImageDraw.Draw(state)
    d.text((16,12),'ca003 · Знаток цитрина · проверка читаемости',font=title,fill=C('cream3'))
    normal=composite(reg,3);locked=normal.copy()
    # Simulate the game's art-area shade only: 30% black, 48×44 inset.
    locked.alpha_composite(Image.new('RGBA',(48,44),(0,0,0,round(255*.30))),(8,2))
    for img,x,size,label in ((normal,16,(256,192),'Обычная · ×4'),(locked,294,(256,192),'Закрытая · чёрный 30% · ×4'),(normal,620,(96,72),'Телефон · ×1.5')):
        sample=img.resize(size,Image.Resampling.NEAREST)
        state.paste(sample,(x,58),sample)
        d.text((x,261),label,font=font,fill=C('cream3'))
    state.save(OUT/'cards_states.png')
    # Rarity 6 frame proof, at native and stretched 9-slice reference sizes.
    frames=Image.new('RGB',(6*110,170),C('navy0')[:3])
    for j,r in enumerate(card_art.RARITIES):
        art=ui.card_frame(r).scaled(4);frames.paste(art,(j*110+7,5),art)
        ImageDraw.Draw(frames).text((j*110+7,141),r,font=font,fill=C('cream3'))
    frames.save(OUT/'cards_rarities.png')
    print('[cards] 61 illustrations → out/cards_overview.png; states → out/cards_states.png')


if __name__=='__main__':main()
