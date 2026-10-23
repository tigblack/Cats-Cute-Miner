import json,sys,collections
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
res=collections.defaultdict(list)
def walk(o,path):
    if isinstance(o,dict):
        for k,v in o.items():
            if k.endswith('FormulaType') and not isinstance(v,list):
                pre=k[:-len('FormulaType')]
                vals=tuple(o.get(pre+s) for s in ['Base','Multiplier','Growth','Power','Round'])
                res[v].append((path.split('/')[1],o.get('Id',o.get('GeneratorId','')),pre,vals))
        for k,v in o.items(): walk(v,path+'/'+k)
    elif isinstance(o,list):
        for v in o: walk(v,path)
walk(d,'')
for ft in sorted(res):
    print('FT',ft,len(res[ft]))
    seen=set()
    for sec,i,pre,vals in res[ft]:
        key=(sec,pre)
        if ft==0 and vals[2] not in (0,None): print('   FT0 growth!=0',sec,i,pre,vals)
        if key in seen: continue
        seen.add(key); print('  ',sec,i,pre,vals)
