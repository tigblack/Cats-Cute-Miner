import json,math,collections as C
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
def R(v, r):
    if r == 0: return v
    r = abs(r); q = v / r
    return math.copysign(math.floor(abs(q) + 0.5), q) * r
F3=lambda q,x: R(q['QuantityBase']+q['QuantityMultiplier']*(q['QuantityGrowth']*x)**q['QuantityPower'], q['QuantityRound'])
for q in d['Deliveries']:
    print(q['Id'],q['RewardModel'],'B/M/G/P/R/FT',q['QuantityBase'],q['QuantityMultiplier'],q['QuantityGrowth'],q['QuantityPower'],q['QuantityRound'],q['QuantityFormulaType'],'W',q['Weight'],'MD',q['MaxDupes'],'RU',q['RankUnlock'],'CPU',q['CheckPointUnlock'],'Ad',q['IsAd'],'Q@4,10', F3(q,4),F3(q,10))
# ad share
for rank in (4,5,6,7,9,10,20):
  for cp in (0,1):
    pool=[q for q in d['Deliveries'] if q['RankUnlock']<=rank and q['CheckPointUnlock']<=cp]
    tot=sum(q['Weight'] for q in pool); ad=sum(q['Weight'] for q in pool if q['IsAd'])
    print('rank',rank,'cp',cp,'tot',tot,'ad',ad,'nonad share',round((tot-ad)/tot,3) if tot else None)
