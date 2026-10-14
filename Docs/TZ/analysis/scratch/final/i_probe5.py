import json
J=json.load(open('/home/claude-sargsyan-tinote/.t3/userdata/attachments/1cbf2b9f-4fe2-4acb-8a57-74cb166e193f-63492c51-4458-4d4a-9df2-8438921bb39a-json.json'))
O={o['Id']:o for o in J['Obstructions']}; M={m['Id']:m for m in J['MineShafts']}
for zn in (0,1):
    z=J['Zones'][zn]; W=z['WidthCells']; D=z['DepthCells']; t=z['Grid'].split(',')
    g=[['.']*W for _ in range(D)]
    for i,tok in enumerate(t):
        c,r=i%W,i//W; p=tok.split(':')
        if p[0]=='r': g[r][c]='R'
        elif p[0]=='x':
            o=O[p[1]]
            for dx in range(o['WidthCells']):
                for dz in range(o['DepthCells']): g[r-dz][c+dx]='#'
        elif p[0]=='s':
            m=M[p[1]]
            for dx in range(m['WidthCells']):
                for dz in range(m['DepthCells']): g[r-dz][c+dx]='S'
        elif p[0] in 'epc': g[r][c]=p[0].upper()
    print(z['Id'])
    for r in range(D): print(r,''.join(g[r]), [tok for tok in t[r*W:(r+1)*W] if tok!='.'])
