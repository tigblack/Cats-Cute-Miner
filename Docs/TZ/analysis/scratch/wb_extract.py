import sys,re,html
t=sys.stdin.read()
# try to isolate article body
m=re.search(r'(<article.*?</article>)',t,re.S) or re.search(r'(class="[^"]*faq[^"]*body.*)',t,re.S)
body=m.group(1) if m else t
body=re.sub(r'<script.*?</script>|<style.*?</style>','',body,flags=re.S)
txt=html.unescape(re.sub(r'<[^>]+>',' ',body))
txt=re.sub(r'[ \t]+',' ',txt); txt=re.sub(r'\n\s*\n+','\n',txt)
print(txt[:5000])
