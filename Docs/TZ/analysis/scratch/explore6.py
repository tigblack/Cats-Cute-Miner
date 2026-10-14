import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
for r in d['Rocks']:
    cs=[r[k] for k in ['CoreCurrencySecondsFormulaType','CoreCurrencySecondsBase','CoreCurrencySecondsMultiplier','CoreCurrencySecondsGrowth','CoreCurrencySecondsPower','CoreCurrencySecondsRound']]
    sc=[r[k] for k in ['SoftCurrencyBase','SoftCurrencyMultiplier','SoftCurrencyGrowth','SoftCurrencyFormulaType']]
    lb=[r[k] for k in ['LeaderboardCurrencyBase','LeaderboardCurrencyMultiplier','LeaderboardCurrencyGrowth','LeaderboardCurrencyFormulaType','LeaderboardCurrencyVariance']]
    has=set(x['DetailedType'] for x in r['RewardCycle'])
    print(r['Id'][:32].ljust(32),'CS',cs,'SC',sc,'LB',lb,'has',has)
