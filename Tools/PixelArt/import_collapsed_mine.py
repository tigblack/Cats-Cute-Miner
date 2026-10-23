#!/usr/bin/env python3
"""Import the approved generated sprite sheet into the game's existing pixel palette.
Only extraction, chroma key, nearest-neighbour resampling and palette conversion; no drawn replacement artwork.
Source: DostupAI generation d99e1b9b-2cca-41a3-b4ee-c1c37d8e3f20, 21 Oct 2026.
"""
from pathlib import Path
from PIL import Image, ImageChops
from pxl import PALETTE

HERE=Path(__file__).resolve().parent
SOURCE=HERE/'source/collapsed_mine_sheet.png'
OUT=HERE/'assets/collapsed_mine'
NARROW={2,4,5,6,7,10,11,15}

def keyed(image):
    image=image.convert('RGBA')
    pixels=list(image.getdata())
    image.putdata([(r,g,b,0 if r>g+25 and b>g+25 and min(r,b)>max(r,b)*.6 else a) for r,g,b,a in pixels])
    return image

def game_palette(image):
    colors=sorted(set(PALETTE.values()))
    palette=Image.new('P',(1,1))
    entries=[v for rgb in colors for v in rgb]
    entries+=(768-len(entries))*[0]
    palette.putpalette(entries)
    alpha=image.getchannel('A').point(lambda a:255 if a>=128 else 0)
    result=image.convert('RGB').quantize(palette=palette,dither=Image.Dither.NONE).convert('RGBA')
    result.putalpha(alpha)
    return result

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    source=Image.open(SOURCE)
    assert source.size==(3312,2480), 'Review source sheet layout before importing a replacement'
    # A fixed crop and identical transform retain the portal position across all six HP states.
    for stage in range(6):
        col=stage%2;row=stage//2
        sprite=keyed(source.crop((col*1656,row*568,(col+1)*1656,(row+1)*568)))
        sprite=sprite.resize((416,143),Image.Resampling.NEAREST)
        canvas=Image.new('RGBA',(416,152));canvas.paste(sprite,(0,6))
        game_palette(canvas).save(OUT/f'entrance_{stage}.png')
    for i in range(16):
        col=i%8;row=i//8
        sprite=keyed(source.crop((col*414,1704+row*388,(col+1)*414,1704+(row+1)*388)))
        sprite=sprite.crop(sprite.getbbox())
        width,height=(32,44) if i in NARROW else (64,44)
        scale=min((width-4)/sprite.width,(height-3)/sprite.height)
        sprite=sprite.resize((max(1,round(sprite.width*scale)),max(1,round(sprite.height*scale))),Image.Resampling.NEAREST)
        canvas=Image.new('RGBA',(width,height));canvas.paste(sprite,((width-sprite.width)//2,height-sprite.height))
        game_palette(canvas).save(OUT/f'prop_{i}.png')
    print('Imported 6 aligned entrance states and 16 stationary props')

if __name__=='__main__':main()
