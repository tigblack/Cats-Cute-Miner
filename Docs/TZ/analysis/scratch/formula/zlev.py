import json,collections,statistics
J='/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'
d=json.load(open(J))
Z={z['Id']:z for z in d['Zones']}
for i in [1,2,3,4,5,6,8,10,15,20,30,50,80,100,144]:
    z=Z['zone%d'%i]; g=z['Grid'].split(',')
    rl=[int(t.split(':')[2]) for t in g if t.startswith('r:rockbasic') or t.startswith('r:rockonboardingmine')]
    sl=[(t.split(':')[1],int(t.split(':')[2]),int(t.split(':')[5])) for t in g if t.startswith('s:')]
    pl=[int(t.split(':')[2]) for t in g if t.startswith('p:')]
    el=[int(t.split(':')[2]) for t in g if t.startswith('e:')]
    print(i,'rocks',len(rl),'lvl',min(rl),statistics.median(rl),max(rl),'shafts',sl,'cp',pl,'exit',el)
