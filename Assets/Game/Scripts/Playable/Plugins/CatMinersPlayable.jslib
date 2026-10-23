// Cat Miners playable: переход в магазин (Docs/Playable/PLAYABLE_TZ.md §3.5, research/integration.md §8, reference.md §5).
// C#: Assets/Game/Scripts/Playable/PlayableStore.cs. Только по тапу игрока, без автоперехода.
// Порядок выхода: window.openStore (упаковщик) → install()+gameReady (Mintegral) → mraid.open → dapi.openStoreUrl (ironSource)
// → FbPlayableAd.onCTAClick (Meta) → ExitApi.exit (Google) → window.open(url, '_blank'); в самом жесте браузера, если
// вкладку заблокировали, — location.href. Нажатие на CTA взводит слушатель отпускания (CM_StoreArm, флаг consumed снят), и
// ссылка открывается в самом жесте браузера (consumed); клик C# этого же нажатия (CM_StoreOpen) приходит позже — кадром или
// после возвращения на вкладку (runInBackground выключен) — и пропускается, пока consumed не снимет следующий взвод. Вне
// жеста (CM_StoreOpen без взвода) переход вкладкой не делается: заблокированное окно не уводит игрока со страницы.
// Ни один код не выполняется при загрузке: в сборку попадают только функции, которые вызывает C#. Внешние API — через
// строки в квадратных скобках, чтобы минификатор не переименовал их.
var CatMinersPlayableLib = {
  $CMStore: {
    armed: null,
    consumed: false,
    hooked: false,

    network: function () {
      var w = window;
      if (typeof w['install'] === 'function' && typeof w['gameReady'] === 'function') return 'mintegral';
      if (w['mraid'] && typeof w['mraid']['open'] === 'function') return 'mraid';
      if (w['dapi'] && typeof w['dapi']['openStoreUrl'] === 'function') return 'ironsource';
      if (w['FbPlayableAd'] && typeof w['FbPlayableAd']['onCTAClick'] === 'function') return 'facebook';
      if (w['ExitApi'] && typeof w['ExitApi']['exit'] === 'function') return 'google';
      return 'web';
    },

    // 1 — SDK сети или упаковщик, 2 — новая вкладка, 3 — переход этой вкладкой (только в жесте, gesture), -1 — не вышло.
    open: function (url, gesture) {
      CMStore.armed = null;
      var w = window;
      try {
        if (typeof w['openStore'] === 'function') { w['openStore'](url); return 1; }
        switch (CMStore.network()) {
          case 'mintegral': w['install'](); return 1;
          case 'mraid': w['mraid']['open'](url); return 1;
          case 'ironsource': w['dapi']['openStoreUrl'](); return 1;
          case 'facebook': w['FbPlayableAd']['onCTAClick'](); return 1;
          case 'google': w['ExitApi']['exit'](); return 1;
        }
      } catch (e) {
        console.warn('[CatMiners] store SDK call failed, opening the link instead', e);
      }
      var tab = null;
      try { tab = w.open(url, '_blank'); } catch (e) { tab = null; }
      if (tab) {
        try { tab.opener = null; } catch (e) { }
        return 2;
      }
      if (!gesture) return -1;
      try { w.location.href = url; } catch (e) { return -1; }
      return 3;
    },

    // Отпускание после нажатия на CTA: внутри области — магазин в этом же жесте, вне её — взвод снимается.
    release: function (e) {
      var a = CMStore.armed;
      if (!a) return;
      // Мышь: жест браузера — mousedown, отпускание обрабатывает mouseup; палец и перо — pointerup/touchend.
      if (e.type === 'pointerup' && e.pointerType === 'mouse') return;
      if (Date.now() - a.at > 5000) { CMStore.armed = null; return; }
      var p = e.changedTouches && e.changedTouches.length ? e.changedTouches[0] : e;
      var canvas = (typeof Module !== 'undefined' && Module['canvas']) || document.getElementById('unity-canvas');
      var r = canvas ? canvas.getBoundingClientRect() : null;
      if (!r || r.width <= 0 || r.height <= 0) { CMStore.armed = null; return; }
      var x = (p.clientX - r.left) / r.width, y = 1 - (p.clientY - r.top) / r.height;
      if (x < a.x0 || x > a.x1 || y < a.y0 || y > a.y1) { CMStore.armed = null; return; }
      CMStore.consumed = true;
      CMStore.open(a.url, true);
    },

    hook: function () {
      if (CMStore.hooked) return;
      CMStore.hooked = true;
      var options = { capture: true, passive: true };
      window.addEventListener('pointerup', CMStore.release, options);
      window.addEventListener('touchend', CMStore.release, options);
      window.addEventListener('mouseup', CMStore.release, options);
      var cancel = function () { CMStore.armed = null; };
      window.addEventListener('pointercancel', cancel, options);
      window.addEventListener('touchcancel', cancel, options);
    }
  },

  // Сеть, в которой открыта страница (строка для журнала; память освобождает C#).
  CM_StoreNetwork: function () {
    var name = CMStore.network();
    var size = lengthBytesUTF8(name) + 1;
    var buffer = _malloc(size);
    stringToUTF8(name, buffer, size);
    return buffer;
  },

  // Нажатие на CTA: одноразовый взвод с областью CTA в долях холста (начало — левый нижний угол, как Screen в Unity).
  CM_StoreArm: function (urlPtr, x0, y0, x1, y1) {
    CMStore.hook();
    CMStore.consumed = false;
    CMStore.armed = { url: UTF8ToString(urlPtr), x0: x0, y0: y0, x1: x1, y1: y1, at: Date.now() };
  },

  // Клик по CTA из C#: открыть магазин, если жест этого нажатия ещё не открыл его (0 — уже открыт жестом, пропущено).
  // Без перехода вкладкой: клик может прийти вне жеста, и заблокированное окно не должно уводить со страницы.
  CM_StoreOpen: function (urlPtr) {
    if (CMStore.consumed) return 0;
    return CMStore.open(UTF8ToString(urlPtr), false);
  }
};

autoAddDeps(CatMinersPlayableLib, '$CMStore');
mergeInto(LibraryManager.library, CatMinersPlayableLib);
