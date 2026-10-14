from fprof import *
print('thresholds spawningcart first 6:',thresholds('spawningcart')[:6]); print('amethyst first 6:',thresholds('amethyst')[:6])
print('\n-- Hire: cost x=n (0-based) / x=n+1 ; level=1+floor(0.4+0.125n) vs ceil(0.4+0.125(n+1))')
for n in range(14):
    print(n+1, 'cost(x=n)=%g'%ft1(0,2,4,n), 'cost(x=n+1)=%g'%ft1(0,2,4,n+1), 'lvl A=%d'%(1+math.floor(ft0(0.4,0.125,0,n))), 'lvl B=%d'%(1+math.floor(ft0(0.4,0.125,0,n+1))), 'raw=%.3f'%ft0(0.4,0.125,0,n))
print('\n-- Damage L1..8: x=L / x=L-1; dynamite 750+300*2.1^x')
for L in range(1,9): print(L, '%.3f'%ft1(0,1,2.1,L), '%.3f'%ft1(0,1,2.1,L-1), 'dyn %.0f / %.0f'%(ft1(750,300,2.1,L),ft1(750,300,2.1,L-1)))
print('\n-- seconds to break rock level (HP rockbasic) by ONE cat, attack 1/s, ceil hits')
for lvl in range(1,7):
    h=hp('rockbasic',lvl)
    print('rockL%d HP=%g'%(lvl,h), ' '.join('catL%d: %d/%d s'%(c, math.ceil(h/ft1(0,1,2.1,c)), math.ceil(h/ft1(0,1,2.1,c-1))) for c in range(1,5)))
for t,l in [('exit',1),('exit',2),('exit',3),('amethyst',1),('citrine',2),('checkpoint',5),('exit',5)]:
    h=hp(t,l); print(t,l,'HP',h,' '.join('catL%d: %d/%d s'%(c, math.ceil(h/ft1(0,1,2.1,c)), math.ceil(h/ft1(0,1,2.1,c-1))) for c in range(1,6)))
print('\n-- Forge: level, output/cycle(3L*thr), gold/s, upgrade cost x=L-1 / x=L, payback s')
for L in list(range(1,13))+[19,20,21,29,30,31,39,40,41]:
    m=thr_mult('spawningcart',L); out=3*L*m; rate=out/2
    c1=ft2a(72,0,1.43,L-1); c2=ft2a(72,0,1.43,L)
    m2=thr_mult('spawningcart',L+1); dr=(3*(L+1)*m2)/2-rate
    print(L, 'out=%g'%out, 'rate=%.2f/s'%rate, 'up(x=L-1)=%.1f'%c1, 'up(x=L)=%.1f'%c2, 'payback=%.0fs'%(c1/dr))
print('cumulative forge cost L1->L10 (x=L-1): %.0f'%sum(ft2a(72,0,1.43,k) for k in range(0,9)))
print('\n-- Amethyst: level, out/cycle, rate, upgrade x=L-1')
for L in [1,2,3,5,9,10,11,20,21]:
    m=thr_mult('amethyst',L); print(L, 400*L*m, '%.1f/s'%(400*L*m/8), '%.0f'%ft2a(512,0,1.46,L-1))
print('\n-- Rock CoreCurrencySeconds by level: 3a(x=L) 3a(x=L-1) 3b(x=L) 3b(x=L-1) 3c(x=L)')
for L in [1,2,3,4,5,6,8,10,15,20,30,40,61,71]:
    a=(10,635,0.2,1.15,15)
    print(L, ft3a(*a,L), ft3a(*a,L-1), ft3b(*a,L), ft3b(*a,L-1), ft3c(*a,L))
print('\n-- Delivery Core seconds (Mult 300/600/900/1500/7500), x=rank: 3a / 3b')
for r in [4,5,6,7,10,20,50,100,144]:
    print(r, [ft3a(0,M,0.1,2,60,r) for M in (300,600,900,1500,7500)], [ft3b(0,M,0.1,2,60,r) for M in (300,600,900,1500,7500)])
print('\n-- Delivery Soft elixir id6/id7/id8, x=rank: 3a / 3b')
for r in [4,5,6,7,10,20,50,100,144]:
    print(r, [ft3a(10,900,0.05,1.1,5,r),ft3a(2.5,600,0.01,1.091,5,r),ft3a(0.7,200,0.01,1.1,1,r)], [ft3b(10,900,0.05,1.1,5,r),ft3b(2.5,600,0.01,1.091,5,r),ft3b(0.7,200,0.01,1.1,1,r)])
print('\n-- BattlePass legacy (-4.5,48,0.105,1.15,5) x=rank: 3a/3b', [(r,ft3a(-4.5,48,0.105,1.15,5,r),ft3b(-4.5,48,0.105,1.15,5,r)) for r in (1,2,5,7,10,30,144)])
print('\n-- Delivery delay FT2 (22,1,1.02) x=claims: a / b / c(x>=1)')
for x in [0,1,2,5,10,20,30,50]:
    print(x, '%.1f %.1f %s'%(ft2a(22,1,1.02,x), ft2b(22,1,1.02,x), '%.1f'%ft2c(22,1,1.02,x) ))
print('\n-- GeneratorObjectives elixir per threshold FT0: x=threshold index k / x=level at threshold, x zone mult 1')
for gid in ['spawningcart','amethyst','citrine']:
    g=GO[gid]; th=thresholds(gid)[:4]
    print(gid, [round(ft0(g['SoftCurrencyRewardBase'],g['SoftCurrencyRewardMultiplier'],0,k+1),3) for k in range(4)], [round(ft0(g['SoftCurrencyRewardBase'],g['SoftCurrencyRewardMultiplier'],0,lv),3) for lv,_ in th])
print('\n-- Rock elixir FT0 (rockbasic 4+0.3L, soft small 16+2.5L, big 45+7L) x zone1 mult 0.1 / zone11 mult 1')
for L in [1,2,3,4,10]: print(L, ft0(4,0.3,0,L), ft0(16,2.5,0,L), ft0(45,7,0,L))
print('\n-- Cards FT1 x=L / x=L-1: manager 1*3^x, uncommon 1*4^x, ca028 0.5*10^x, ca035/ca037 1*2^x')
for L in range(1,6): print(L, ft1(0,1,3,L), ft1(0,1,3,L-1), ft1(0,1,4,L), ft1(0,0.5,10,L), ft1(0,0.5,10,L-1), ft1(0,1,2,L), ft1(0,1,2,L-1))
