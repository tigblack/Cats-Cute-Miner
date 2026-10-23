import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
pairs=collections.defaultdict(collections.Counter)
def walk(o,path):
    if isinstance(o,dict):
        if 'RewardType' in o and 'DetailedType' in o:
            dt=o['DetailedType']
            if isinstance(dt,str) and (dt.startswith('scg') or dt.startswith('Gacha')): dt='<gacha>'
            if isinstance(dt,str) and dt.startswith('ca0'): dt='<card>'
            pairs[o['RewardType']][(dt,path.split('/')[1])]+=1
        for k,v in o.items(): walk(v,path+'/'+k)
    elif isinstance(o,list):
        for v in o: walk(v,path)
walk(d,'')
for k in sorted(pairs): print(k, dict(pairs[k]))
