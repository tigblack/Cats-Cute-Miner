from common import *
import collections
print('rock dims', collections.Counter((r['WidthCells'],r['DepthCells']) for r in d['Rocks']))
print('shaft dims', collections.Counter((r['WidthCells'],r['DepthCells']) for r in d['MineShafts']))
print('obst dims', collections.Counter((r['WidthCells'],r['DepthCells']) for r in d['Obstructions']))
shaftcols=collections.Counter(); shaft_adj=collections.Counter(); shaft_side=collections.Counter()
exit_row1=collections.Counter(); exit_free_eventual=collections.Counter()
gate_below=collections.Counter(); gate_below_eventual=collections.Counter()
shaft_adj_sector_gate=0
multi_target_cells=collections.Counter()
for z in ZL:
    W,D,g,occ,objs=parse(z)
    kind={o['i']:o['k'] for o in objs}
    gates=sorted(o['r'] for o in objs if o['k']=='p')
    def walkable_eventually(q):
        if not(0<=q[0]<W and 0<=q[1]<D): return False
        if q not in occ: return True
        return kind[occ[q]]=='r'
    for o in objs:
        if o['k']=='s':
            shaftcols[min(x for x,y in o['cells'])]+=1
            cs=set(o['cells'])
            adj=set()
            for x,y in cs:
                for q in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
                    if q not in cs and walkable_eventually(q): adj.add(q)
            shaft_adj[len(adj)]+=1
            # which sides
            minx=min(x for x,y in cs); maxx=max(x for x,y in cs); miny=min(y for x,y in cs); maxy=max(y for x,y in cs)
            sides=tuple(sorted(set(('L' if q[0]<minx else 'R' if q[0]>maxx else 'T' if q[1]<miny else 'B') for q in adj)))
            shaft_side[sides]+=1
        if o['k']=='e':
            row=tuple(kind[occ[(x,1)]] if (x,1) in occ else '.' for x in range(2,5))
            exit_row1[row]+=1
            exit_free_eventual[sum(walkable_eventually((x,1)) for x in range(2,5))]+=1
        if o['k']=='p':
            r=o['r']
            row=tuple(kind[occ[(x,r+1)]] if (x,r+1) in occ else '.' for x in range(1,6))
            gate_below[''.join(row)]+=1
            gate_below_eventual[sum(walkable_eventually((x,r+1)) for x in range(1,6))]+=1
    # cells adjacent to >=2 distinct minable targets (r,s,p,e)
    for y in range(D):
        for x in range(W):
            if (x,y) in occ: continue
            t=set()
            for q in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
                if q in occ and kind[occ[q]] in 'rspe': t.add(occ[q])
            multi_target_cells[len(t)]+=1
print('shaft min col',shaftcols)
print('shaft #eventually-walkable adjacent cells',sorted(shaft_adj.items()))
print('shaft sides',shaft_side.most_common(10))
print('exit row1 cols2-4',exit_row1.most_common(10))
print('exit eventual work cells',exit_free_eventual)
print('gate below-row patterns',gate_below.most_common(10))
print('gate below eventual',gate_below_eventual)
print('initially free cells by #adjacent targets',multi_target_cells)
