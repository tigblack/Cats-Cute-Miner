#!/usr/bin/env python3
"""Author only Mine 1: a compact two-lane rescue mine with a short right-hand branch.
Does not regenerate, open or retune the rest of the campaign.
"""
import json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
RES=ROOT/'Assets/Game/Resources/Outpost'
LAYOUT='mia-two-lane-mine-2'

def content(version='wide-branching-mines-3'):
    width,depth,center=13,11,6
    raw=['.']*(width*depth)
    raw[center]='e:exit:1:scg_m001_exit'
    raw[(depth-1)*width+center-1]='c:spawningcart:1'
    placements=[]
    for row in range(3,9):
        for col in (center-1,center):
            index=row*width+col
            raw[index]='r:rockonboardingmine1:1'
            placements.append({'TargetId':index,'Column':col,'Row':row,'Branch':0})
    override={'Name':LAYOUT,'Reason':'Owner revision: compact two-lane Mine 1, 12 rocks, two debris blocks on a short right branch, and the rescue lock (15 total). Prior runs keep their saved layout.','ReplacementWidth':width,'ReplacementDepth':depth,'ReplacementGrid':','.join(raw),'AddTokens':[]}
    floor=[]
    for row in range(depth):
        cells=['#']*width;cells[center]='.'
        if row>=3:cells[center-1]='.' # Two parallel columns of breakable blocks.
        if row==1 or 5<=row<=7:cells[center+1]='.' # Cage, and a short branch turning two cells upward.
        floor.append(''.join(cells))
    snapshot={'Version':version,'ZoneId':'zone1','Layout':LAYOUT,'Width':width,'Depth':depth,'BranchCrowdingPenalty':0,'FloorRows':floor,'SectorRows':[s.replace('.','0') for s in floor],'ExitColumn':center,'ExitRow':0,'Placements':placements,'Gates':[]}
    return override,snapshot

def main():
    overrides=json.loads((RES/'level_overrides.json').read_text())
    topology=json.loads((RES/'mine_topology.json').read_text())
    previous=ROOT/'Docs/Reports/mia-corridor/previous-mine1-layout.json'
    previous.parent.mkdir(parents=True,exist_ok=True)
    if not previous.exists():
        previous.write_text(json.dumps({'Override':overrides['Zones']['zone1'],'Topology':topology['Zones']['zone1']},indent=2)+'\n')
    overrides['Version']='overrides-3'
    overrides['Zones']['zone1'],topology['Zones']['zone1']=content(topology['Version'])
    (RES/'level_overrides.json').write_text(json.dumps(overrides,indent=2,ensure_ascii=False)+'\n')
    (RES/'mine_topology.json').write_text(json.dumps(topology,separators=(',',':'),ensure_ascii=False)+'\n')
    print('Mine 1 only: 13x11, two lanes + right branch, 12 rocks + 2 debris + 1 rescue lock; other 143 topology entries unchanged')

if __name__=='__main__':main()
