// Data safety, шаг 4 «Data usage and handling»: заполняет диалоги всех типов за один вызов.
// Запуск: agentb run scenarios/data_safety_dialogs.js --url <страница шага 4> --set-file PLAN=<plan.json>.
// Подставь PLAN: [[строка типа в таблице, секция, [цели]], ...]. Метки целей — как в диалоге Console.
// Каждый тип: Collected+Shared, не эфемерно, обязательно, цели в обеих группах (collected и shared), Save.
async () => {
  const sl=ms=>new Promise(r=>setTimeout(r,ms));
  const vis=e=>{const r=e.getBoundingClientRect();return r.width>0&&r.height>0;};
  const dlg=()=>[...document.querySelectorAll('[role=dialog],material-dialog,mat-dialog-container')].filter(vis).pop();
  const on=e=>e.checked===true||e.getAttribute('aria-checked')==='true';
  const cname=e=>(e.getAttribute('aria-label')||e.closest('label,material-radio,mat-radio-button,material-checkbox,mat-checkbox')?.innerText||'').split('\n')[0].trim();
  const F='App functionality',A='Analytics',V='Advertising or marketing',S='Fraud prevention, security, and compliance';
  const PLAN=__PLAN__; // например [['Device or other IDs','Device or other IDs',[F,A,V,S]],['Crash logs','App info and performance',[A]]]
  async function fillDialog(purposes){
    const d=dlg(); const click=async e=>{ if(!on(e)){e.click(); await sl(300);} };
    for(const n of ['Collected','Shared']) await click([...d.querySelectorAll('input[type=checkbox]')].find(e=>cname(e)===n));
    await sl(500);
    const R=[...d.querySelectorAll('input[type=radio]')];
    await click(R.find(e=>/^No, this collected/.test(cname(e)))); await click(R.find(e=>/^Data collection is required/.test(cname(e))));
    const pc=[...d.querySelectorAll('input[type=checkbox]')].filter(e=>!['Collected','Shared'].includes(cname(e)));
    if(pc.length!==14) throw new Error('purpose boxes='+pc.length);
    for(const e of pc) if(purposes.includes(cname(e))) await click(e);
    const n=[...d.querySelectorAll('input[type=checkbox]')].filter(on).length;
    if(n!==2+2*purposes.length) throw new Error('checked='+n);
    [...d.querySelectorAll('button')].filter(vis).find(b=>b.innerText.trim()==='Save').click();
    await sl(1500);
    if(dlg()) throw new Error('dialog still open');
  }
  const log=[];
  for(const [type,sec,purposes] of PLAN){
    try{
      const findBtn=()=>{ const bs=[...document.querySelectorAll('button,[role=button]')].filter(vis).filter(b=>/^(Start|Edit)/.test(b.innerText.trim()));
        for(const b of bs){ let a=b.parentElement; for(let i=0;i<4&&a;i++,a=a.parentElement){ const t=a.innerText||''; if(bs.filter(x=>a.contains(x)).length===1&&t.split('\n').some(l=>l.trim()===type)) return b; } } return null; };
      let b=findBtn();
      if(!b){ const hdr=[...document.querySelectorAll('*')].find(e=>e.children.length<=4&&(e.innerText||'').trim().split('\n')[0]===sec&&e.innerText.length<200&&vis(e)); const sb=hdr&&[...hdr.querySelectorAll('button,[role=button]')].find(x=>/^Show/.test(x.innerText.trim())); if(sb){sb.click(); await sl(800);} b=findBtn(); }
      if(!b) throw new Error('no row button');
      b.click(); await sl(1000);
      if(!dlg()) throw new Error('dialog not opened');
      await fillDialog(purposes); log.push(type+': saved');
    }catch(e){ log.push(type+': FAIL '+e.message); break; }
  }
  return {log, progress:[...document.body.innerText.matchAll(/\d+ of \d+ completed/g)].map(m=>m[0])};
}
