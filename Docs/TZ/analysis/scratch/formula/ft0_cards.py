import json
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
maxlvl={c['Rarity']:len(c['Duplicates'])+1 for c in d['CardUpgradeCosts']}
print('maxlvl',maxlvl)
H={
 'quad x=L      B+M*L+G*L^2':      lambda B,M,G,L: B+M*L+G*L*L,
 'quad x=L-1':                     lambda B,M,G,L: B+M*(L-1)+G*(L-1)**2,
 'arith B+M*L+G*L(L-1)/2':         lambda B,M,G,L: B+M*L+G*L*(L-1)/2,
 'arith2 B+M*L+G*L(L+1)/2':        lambda B,M,G,L: B+M*L+G*L*(L+1)/2,
 'B+(M+G*L)*L same as quad':       None,
 'B+M*L*(1+G)^L':                  lambda B,M,G,L: B+M*L*(1+G)**L,
}
def rnd(v):
    # distance to nearest 'round' number (multiples of 0.05 for <2, of 1 for >=2)
    if abs(v)<2: q=0.05
    else: q=1
    return abs(v-round(v/q)*q)/max(abs(v),1e-9)
for name,f in H.items():
    if f is None: continue
    tot=0; out=[]
    for c in d['Cards']:
        if c['ModifierFormulaType']!=0: continue
        B,M,G=c['ModifierBase'],c['ModifierMultiplier'],c['ModifierGrowth']
        if G==0 and B==0: continue
        Lm=maxlvl[c['Rarity']]
        v1=f(B,M,G,1); vm=f(B,M,G,Lm)
        tot+=rnd(vm); out.append(f"{c['Id']}(r{c['Rarity']},smt{c['StatModifierType']}) L1={v1:.4g} Lmax{Lm}={vm:.4g}")
    print('##',name,'roundness err sum=%.4f'%tot); print('   '+'\n   '.join(out))
