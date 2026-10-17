// Прогон целой страницы Play Console одним вызовом: agentb run profiles/<страница>.json подставляет
// профиль вместо __PROFILE__ и выполняет функцию на странице. Без скриншотов и UID.
async () => {
  const P = __PROFILE__;
  const norm = s => (s || '').replace(/\s+/g, ' ').trim().toLowerCase();
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  const vis = e => { const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0; };
  const label = e => norm(e.getAttribute('aria-label') || e.innerText || e.textContent);
  const SEL = {
    radio: 'input[type=radio],[role=radio]',
    check: 'input[type=checkbox],[role=checkbox]',
    click: 'button,[role=button],a,[role=tab],[role=menuitem]',
    fill: 'input:not([type=radio]):not([type=checkbox]),textarea',
  };
  // Текст подписи контрола: свой, label[for], обёртка label/mat-radio, ближайший родитель.
  const nameOf = e => {
    const own = label(e);
    if (own) return own;
    const id = e.id && document.querySelector(`label[for="${CSS.escape(e.id)}"]`);
    if (id) return norm(id.innerText);
    const wrap = e.closest('label,material-radio,mat-radio-button,material-checkbox,mat-checkbox,.mdc-form-field');
    return wrap ? norm(wrap.innerText) : '';
  };
  const fillName = e => norm(e.getAttribute('aria-label') || e.placeholder || '') || nameOf(e);
  const scoped = (list, within) => {
    if (!within) return list;
    const w = norm(within);
    const out = [];
    for (const e of list) {
      let a = e.parentElement, best = null;
      while (a && a !== document.body) { if (norm(a.innerText).includes(w)) { best = a; break; } a = a.parentElement; }
      if (best) out.push([norm(best.innerText).length, e]);
    }
    if (!out.length) return [];
    const min = Math.min(...out.map(x => x[0]));
    return out.filter(x => x[0] === min).map(x => x[1]);
  };
  const find = (kind, text, st) => {
    const t = norm(text), nm = kind === 'fill' ? fillName : kind === 'click' ? label : nameOf;
    let all = [...document.querySelectorAll(SEL[kind])].filter(vis);
    all = scoped(all, st.within);
    let hit = all.filter(e => nm(e) === t);
    if (!hit.length) hit = all.filter(e => nm(e).includes(t));
    if (st.nth !== undefined) hit = hit[st.nth] ? [hit[st.nth]] : [];
    if (hit.length !== 1) throw new Error(hit.length ? `неоднозначно (${hit.length}): ${text}; добавь within/nth` : `не найдено: ${text}`);
    return hit[0];
  };
  const isOn = e => e.checked === true || e.getAttribute('aria-checked') === 'true';
  const setValue = (e, v) => {
    const proto = e.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(proto, 'value').set.call(e, v);
    e.dispatchEvent(new Event('input', { bubbles: true }));
    e.dispatchEvent(new Event('change', { bubbles: true }));
    e.dispatchEvent(new Event('blur', { bubbles: true }));
  };

  const log = [];
  let failed = false;
  for (const [i, st] of (P.steps || []).entries()) {
    const kind = ['radio', 'check', 'fill', 'click', 'wait'].find(k => k in st);
    try {
      if (kind === 'wait') {
        const t = norm(st.wait), end = Date.now() + (st.ms || 8000);
        while (!norm(document.body.innerText).includes(t)) { if (Date.now() > end) throw new Error('не дождался: ' + st.wait); await sleep(150); }
      } else if (kind === 'radio') {
        const e = find('radio', st.radio, st);
        if (!isOn(e)) { e.click(); await sleep(120); }
        if (!isOn(e)) throw new Error('не выбрано: ' + st.radio);
      } else if (kind === 'check') {
        const e = find('check', st.check, st), want = st.value !== false;
        if (isOn(e) !== want) { e.click(); await sleep(120); }
        if (isOn(e) !== want) throw new Error('состояние не изменилось: ' + st.check);
      } else if (kind === 'fill') {
        const e = find('fill', st.fill, st);
        setValue(e, String(st.value));
        await sleep(80);
        if (e.value !== String(st.value)) throw new Error('значение не принято: ' + st.fill);
      } else if (kind === 'click') {
        const e = find('click', st.click, st);
        if (e.disabled || e.getAttribute('aria-disabled') === 'true') throw new Error('кнопка неактивна: ' + st.click);
        e.click();
        await sleep(st.ms || 600);
      } else throw new Error('неизвестный шаг');
      log.push({ i, ok: true });
    } catch (err) {
      log.push({ i, ok: false, step: st, error: String(err.message || err) });
      failed = true;
      if (!st.optional) break;
      failed = false;
    }
  }
  // Один итоговый readback: выбранные радио/чекбоксы страницы + ожидаемые тексты.
  const body = norm(document.body.innerText);
  const missing = (P.expect || []).filter(t => !body.includes(norm(t)));
  const forbidden = (P.forbid || []).filter(t => body.includes(norm(t)));
  const on = [...document.querySelectorAll(SEL.radio + ',' + SEL.check)].filter(vis).filter(isOn).map(e => nameOf(e).slice(0, 80));
  return { ok: !failed && !missing.length && !forbidden.length, url: location.pathname, failed: log.filter(x => !x.ok), steps: log.length, missing, forbidden, selected: on };
}
