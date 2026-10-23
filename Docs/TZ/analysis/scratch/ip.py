import json, sys, collections, re
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
raw=open(J,encoding='utf-8').read()
d=json.loads(raw)
S=d['StoreItems']
print(len(S)); print(json.dumps(S[0])[:600])
ids=[s['Id'] for s in S]
print(collections.Counter(re.sub(r'[._][^.]*$','',i) for i in ids).most_common(8))
print(ids[:8])
for pat in ['redcell','goldandgoblins','goblin','appquantum','Goblin','theme.','token']:
    print(pat, len(re.findall(pat,raw)))
print(sorted(set(re.findall(r'"([a-zA-Z_]*[Gg]oblin[a-zA-Z_]*)"',raw)))[:30])
print(sorted(set(re.findall(r'"(com\.[a-z0-9_.]+)',raw)))[:5])
print('AssetIds sample', sorted(set(s['AssetId'] for s in S))[:20])
print('NameTokens sample', sorted(set(s['NameToken'] for s in S))[:10])
bpr=d['BattlePassRegions'][0]
print({k:(v if not isinstance(v,str) or len(v)<60 else v[:60]) for k,v in bpr.items()})
