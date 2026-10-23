# Candidate formula profiles (working hypotheses, NOT original algorithms)
import json, math
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
def ft0(B,M,G,x): return B+M*x+G*x*x
def ft1(B,M,G,x): return B+M*G**x
def ft2a(B,M,G,x): return B*G**x+M*x          # exp + linear
def ft2b(B,M,G,x): return (B+M*x)*G**x        # (lin)*exp
def ft2c(B,M,G,x): return B*(x**M)*G**x       # poly*exp
def rnd(v,R):
    if not R: return v
    R=abs(R); return round(v/R)*R
def ft3a(B,M,G,P,R,x): return rnd(B+M*(G*x)**P,R)
def ft3b(B,M,G,P,R,x): return rnd(B+M*(1+G*x)**P,R)
def ft3c(B,M,G,P,R,x): return rnd(B+M*G*x**P,R)
GO={g['GeneratorId']:g for g in d['GeneratorObjectives']}
def thresholds(gid):
    g=GO[gid]; lv=1; out=[]
    for c,m in zip(g['ObjectiveCount'],g['CoreCurrencyMultiplier']):
        lv+=c; out.append((lv,m))
    return out
def thr_mult(gid,level):
    p=1
    for lv,m in thresholds(gid):
        if level>=lv: p*=m
        else: break
    return p
H=d['MiningTargetHealths']
def hp(t,l): return H[t][l-1]
