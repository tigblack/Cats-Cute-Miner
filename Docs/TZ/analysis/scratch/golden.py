import math
def ft0(b,m,g,x): return b+m*x+g*(x-1)
def ft1(b,m,g,x): return b+m*g**(x-1)
def ft2(b,m,g,x): return b*g**(x-1)
def rnd(v,r):
    if r==0: return v
    r=abs(r); return r*math.floor(v/r+0.5)
def ft3(b,m,g,p,r,x): return rnd(b+m*(g*x)**p, r)
print('dmg', [round(ft1(0,1,2.1,L),4) for L in range(1,6)])
print('hire cost', [ft1(0,2,4,k) for k in range(1,9)])
print('hire lvl', [(n, max(1, math.floor(0.4+0.125*n+0.5))) for n in (1,8,9,16,17)])
print('cart upg', [math.ceil(ft2(72,0,1.43,L)) for L in range(1,6)])
print('ame upg', [math.ceil(ft2(512,0,1.46,L)) for L in range(1,4)])
print('rock sec x=lvl', [ft3(10,635,0.2,1.15,15,L) for L in range(1,6)])
print('deliv core600 rank', [(R,ft3(0,600,0.1,2,60,R)) for R in (4,5,10,20)])
print('deliv soft id6 rank', [(R,ft3(10,900,0.05,1.1,5,R)) for R in (5,10)])
print('spell', [ft1(750,300,2.1,x) for x in (1,2,3)])
print('rock soft small lvl2 zone1', ft0(16,2.5,0,2)*0.1, 'rockbasic lvl3 zone6?', ft0(4,0.3,0,3))
