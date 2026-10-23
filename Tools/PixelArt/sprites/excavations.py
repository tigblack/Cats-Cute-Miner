"""Collapsed mine entrance and abandoned equipment, imported into the existing pixel-art atlas.
The stone arch stays in place while rubble disappears at the same saved HP thresholds.
"""
from pathlib import Path
from PIL import Image
from pxl import Canvas

ASSETS=Path(__file__).resolve().parents[1]/'assets/collapsed_mine'

def load(name):
    image=Image.open(ASSETS/name).convert('RGBA')
    cv=Canvas(*image.size);cv.img=image;cv.px=image.load()
    return cv

def build(reg):
    for stage in range(6):
        cv=load(f'entrance_{stage}.png')
        reg.add(f'final_collapsed_{stage}',cv,pivot=(208,8),note='Collapsed mine entrance, shared HP, stage '+str(stage))
        # Keep old presentation names available for saved snapshots and any older view clients.
        for old in ('crystal','amber','dinosaur','ancient'):
            reg.add(f'final_{old}_{stage}',cv,pivot=(208,8),note='Legacy alias of collapsed mine entrance')
    for i in range(16):
        cv=load(f'prop_{i}.png')
        reg.add(f'prop_salvage_{i}',cv,pivot=(cv.w/2,0),note='Stationary abandoned mining equipment, visual only')
