const shots = {
  data: {
    count: '01 / 04', title: '選擇資料表與 Record',
    text: '左側選 Table，中間搜尋、排序並選擇 Record，右側 Inspector 查看欄位。PrimaryKey 與 SecondaryKey 會保持唯讀。',
    points: ['可釘選常用 Table', '搜尋條件可依欄位縮小結果', 'Inspector 顯示 Record 全部可檢查欄位'],
    image: 'assets/remote-zh-tw.png', alt: 'Remote Editor 資料表與 Inspector', caption: '連線後的 Data 畫面'
  },
  override: {
    count: '02 / 04', title: '調整執行中的數值',
    text: '在 Inspector 編輯欄位並 Apply，遊戲端立刻收到 Runtime Override。示範把範例角色的 HP 從 1200 調成 1400。',
    points: ['支援欄位編輯與批次 Set / Add / Multiply', '可用 Undo / Redo 回退操作', '修改不會直接改寫原始資料庫'],
    image: 'assets/remote-override.png', alt: 'Remote Editor 顯示 HP 覆寫後的資料', caption: 'HP 1200 → 1400 的 Runtime Override'
  },
  changes: {
    count: '03 / 04', title: '確認變更內容',
    text: '切換到 Changes，核對每一筆調整的原值與新值。這裡可以在交接或保存 Patch 前先確認結果。',
    points: ['逐筆查看變更差異', '複製或貼上 TSV 與試算表交換資料', '確認後再保存或匯出 Patch'],
    image: 'assets/remote-changes.png', alt: 'Changes 畫面顯示 HP 從 1200 變成 1400', caption: 'Changes 頁籤：HP 1200 → 1400'
  },
  patches: {
    count: '04 / 04', title: '把調整保存為 Patch',
    text: '在 Patches 頁籤建立命名 Patch，預覽內容後可保存、比較、合併、套用或匯出 JSON。畫面中的示範 Patch 在截圖後已刪除。',
    points: ['Patch 保存在桌面工具端', 'JSON 可交接給其他測試者', '匯入與套用前先確認 Record 型別版本'],
    image: 'assets/remote-patches.png', alt: 'Patches 頁籤顯示示範 Patch 預覽', caption: 'Patches 頁籤：預覽命名 Patch'
  }
};

const tabs = [...document.querySelectorAll('.showcase-tabs [role="tab"]')];
if (document.getElementById("shot-image")) {
const shotImage = document.getElementById('shot-image');
const shotFigure = shotImage.closest('figure');
function selectShot(key, focus = false) {
  const shot = shots[key];
  if (!shot) return;
  tabs.forEach(tab => {
    const selected = tab.dataset.shot === key;
    tab.setAttribute('aria-selected', String(selected));
    tab.tabIndex = selected ? 0 : -1;
    if (selected && focus) tab.focus();
  });
  document.getElementById('shot-count').textContent = shot.count;
  document.getElementById('shot-title').textContent = shot.title;
  document.getElementById('shot-text').textContent = shot.text;
  const list = document.getElementById('shot-points');
  list.replaceChildren(...shot.points.map(point => {
    const li = document.createElement('li');
    li.textContent = point;
    return li;
  }));
  shotImage.src = shot.image;
  shotImage.alt = shot.alt;
  shotFigure.dataset.caption = shot.caption;
  shotFigure.setAttribute("aria-label", `放大截圖：${shot.caption}`);
  document.getElementById('shot-caption').firstChild.textContent = shot.caption + ' ';
}
tabs.forEach((tab, index) => {
  tab.addEventListener('click', () => selectShot(tab.dataset.shot));
  tab.addEventListener('keydown', event => {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : (index + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
    selectShot(tabs[next].dataset.shot, true);
  });
});
selectShot('data');

}

const lightbox = document.getElementById('lightbox');
const lightboxImage = document.getElementById('lightbox-image');
const lightboxCaption = document.getElementById('lightbox-caption');
const closeButton = lightbox.querySelector('.lightbox-close');
let priorFocus;
function closeLightbox() {
  lightbox.hidden = true;
  document.body.classList.remove('lightbox-open');
  lightboxImage.removeAttribute('src');
  priorFocus?.focus();
}
function openLightbox(figure) {
  const source = figure.querySelector('img');
  priorFocus = document.activeElement;
  lightboxImage.src = source.src;
  lightboxImage.alt = source.alt;
  lightboxCaption.textContent = figure.dataset.caption || source.alt;
  lightbox.hidden = false;
  document.body.classList.add('lightbox-open');
  closeButton.focus();
}
document.querySelectorAll('.screenshot').forEach(figure => {
  figure.tabIndex = 0;
  figure.setAttribute('role', 'button');
  figure.setAttribute('aria-label', `放大截圖：${figure.dataset.caption || figure.querySelector('img').alt}`);
  figure.addEventListener('click', () => openLightbox(figure));
  figure.addEventListener('keydown', event => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      openLightbox(figure);
    }
  });
});
closeButton.addEventListener('click', closeLightbox);
lightbox.addEventListener('click', event => { if (event.target === lightbox) closeLightbox(); });
document.addEventListener('keydown', event => {
  if (lightbox.hidden) return;
  if (event.key === 'Escape') closeLightbox();
  if (event.key === 'Tab') {
    event.preventDefault();
    closeButton.focus();
  }
});
