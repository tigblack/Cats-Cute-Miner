import re, sys, json, collections
P=sys.argv[1]
raw=open(P,'rb').read()
txt=raw.decode('utf-8')
print('BOM', raw[:3]==b'\xef\xbb\xbf', 'CRLF', b'\r\n' in raw, 'non-ascii bytes', sum(1 for b in raw if b>127))
# number literal tokens outside strings: crude tokenizer
num_re=re.compile(r'-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?')
i=0;n=len(txt);nums=[];instr=False
out=[]
# simple scan
res=[]
j=0
while j<n:
    c=txt[j]
    if c=='"':
        k=j+1
        while txt[k]!='"':
            if txt[k]=='\\': k+=2
            else: k+=1
        j=k+1;continue
    if c=='-' or c.isdigit():
        m=num_re.match(txt,j)
        res.append(m.group(0)); j=m.end(); continue
    j+=1
print('numeric literals', len(res))
ints=[r for r in res if not re.search('[.eE]',r)]
flts=[r for r in res if re.search('[.eE]',r)]
exps=[r for r in res if re.search('[eE]',r)]
print('ints',len(ints),'floats',len(flts),'with exponent',len(exps))
big=[r for r in ints if abs(int(r))>2**31-1]
print('ints > int32:',len(big), big[:10])
big64=[r for r in ints if abs(int(r))>2**63-1]
print('ints > int64:',len(big64), big64[:5])
print('ints > 2^53:', sum(1 for r in ints if abs(int(r))>2**53))
mx=max(res,key=lambda r: abs(float(r)))
print('max abs literal', mx)
print('top exps', sorted(set(exps), key=lambda r:-abs(float(r)))[:15])
# digits precision
def sig(r):
    s=r.lstrip('-').split('e')[0].split('E')[0].replace('.','').lstrip('0')
    return len(s)
lp=[r for r in res if sig(r)>15]
print('literals with >15 significant digits', len(lp), lp[:10])
dec_overflow=[r for r in res if abs(float(r))>7.9e28]
print('literals > decimal max', len(dec_overflow), dec_overflow[:5])
# strings that look like numbers / NaN / Infinity
print('NaN/Infinity tokens', len(re.findall(r'(?<!")\b(NaN|Infinity)\b', txt)))
