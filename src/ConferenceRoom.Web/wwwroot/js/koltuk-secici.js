/*
  Koltuk seçici (yönetim paneli): krokiyi salt okunur gösterip koltuk seçtiren ekranların ortak çatısı
  ==================================================================================================
  Kullanan ekranlar: etkinlik koltuk ayarları (koltuk-ayarlari.js), personel satış yetkileri (satis-yetkileri.js) ve
  personel satış ekranı (satis-ekrani.js). Sayfada Kaydet düğmesi yoksa (şablonda PickOnly) ekran yalnızca seçtirir:
  geri al/yinele, kaydetme ve "kaydedilmemiş değişiklik" uyarısı devre dışıdır, işi ekranın paneli yapar.
  Çizim kroki editörüyle ortaktır (kroki-cizim.js); sayfa iskeleti Pages/Shared/_SeatScreenLayout.cshtml.
  Sayfa üç JSON bloğu verir:
    #editorConfig : ayar sürümü (revision), kayıt adresi, dil, ekran metinleri
    #planData     : gösterilecek kroki
    #seatData     : ekranın düzenlediği veri (kaydedilen JSON; yapısı ekrana göre değişir)

  Burada olanlar: veri, geri al/yinele, çizim, seçim (tıklama, alan seçimi, toplu seçim), yakınlaştırma, kat sekmeleri,
  kaydetme, kısayollar, bildirim. Ekrana özgü olanlar (koltuğun rengi, panel bölümleri, düğmelerin işi) ekranın
  verdiği kancalardan (hooks) gelir:
    seatStyle(it)        → koltuğun dolgu/kenar/yazı rengi (KrokiCizim.drawSeat biçiminde)
    selectable(it)       → koltuk seçilebilir mi (yoksa hepsi; ör. satış ekranında satılmış koltuk seçilmez)
    stateText()          → yalnızca seçtiren ekranda başlığın yanındaki durum yazısı
    seatDot(it)          → koltuğun köşesine yeşil nokta konsun mu
    seatBadge(it, TC)    → koltuğun üstündeki küçük etiket { text, color, faded } ya da null (ör. satış yetkilisinin baş harfleri)
    hoverParts(it)       → fare altındaki koltuk için ek bilgiler (dizi)
    quickSelect          → seçim bölümüne eklenecek düğmeler: [{ act, label, seats: () => koltuklar }]
    renderPanel()        → ekranın kendi panel bölümlerini yazar (seçim değiştikçe çağrılır)
    onAction(act, b, e)  → panelde ekrana ait bir düğmeye basıldı
    onChange(e)          → panelde ekrana ait bir alan değişti

  Kullanım:  const ed = KoltukSecici.create(hooks);  ...  ed.init();
  Dosyanın bölümleri: dil ve ayarlar → durum → geri al/yinele → çizim → görünüm → seçim → toplu seçim → panel →
  kaydetme → kısayollar → dış arayüz
*/
(() => {
"use strict";

function create(hooks) {

/* ------------------------------------------------------------------
   Dil ve ayarlar. Metinler sayfanın dilinde, sunucudan gelir (anahtarlar bölüm önekisiz).
------------------------------------------------------------------ */
const $ = (id) => document.getElementById(id);
const CONFIG = JSON.parse($("editorConfig").textContent);
const TEXTS = CONFIG.texts;
const locale = CONFIG.culture === "tr" ? "tr-TR" : "en-GB";
function t(key, vars) {
  let s = TEXTS[key] ?? key;
  if (vars) for (const k in vars) s = s.split("{" + k + "}").join(vars[k]);
  return s;
}
const esc = (s) => String(s ?? "").replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
const naturalSort = (a, b) => String(a).localeCompare(String(b), locale, { numeric: true });

/* ------------------------------------------------------------------
   Durum.
   plan: kroki (değişmez). data: ekranın düzenlediği veri. selected: seçili koltuk id'leri.
   seats: kroki koltukları (id → öğe). levelId: gösterilen kat.
------------------------------------------------------------------ */
const plan = JSON.parse($("planData").textContent);
let data = JSON.parse($("seatData").textContent);
const seats = new Map(plan.items.filter((i) => i.type === "seat").map((i) => [i.id, i]));
const blockById = (id) => plan.blocks.find((b) => b.id === id);
const levelById = (id) => plan.levels.find((l) => l.id === id);
let levelId = plan.levels[0].id;
let selected = new Set();
let tool = "select";
const levelSeats = () => [...seats.values()].filter((s) => s.levelId === levelId);
const pickOnly = !$("saveBtn");
const canSelect = (id) => !hooks.selectable || hooks.selectable(seats.get(id));

/* ---------- Geri al / yinele: her değişiklikten sonra commit() verinin JSON'unu yığına koyar ---------- */
let undoStack = [], redoStack = [], lastSnap = JSON.stringify(data);
function commit() {
  const snap = JSON.stringify(data);
  if (snap === lastSnap) return;
  undoStack.push(lastSnap); if (undoStack.length > 100) undoStack.shift();
  redoStack = []; lastSnap = snap;
  render();
}
function restore(snap) { data = JSON.parse(snap); lastSnap = snap; render(); }
function undo() { if (pickOnly || !undoStack.length) return; redoStack.push(lastSnap); restore(undoStack.pop()); }
function redo() { if (pickOnly || !redoStack.length) return; undoStack.push(lastSnap); restore(redoStack.pop()); }

/* ------------------------------------------------------------------
   Çizim (Konva). Katmanlar: gridLayer (ızgara), layer (kroki), uiLayer (alan seçimi kutusu).
   Koltuğun rengi ekrandan gelir (seatStyle); seatDot doğruysa sağ üst köşede yeşil nokta olur,
   seatBadge bir etiket verirse koltuğun önünde (sahne tarafında) renkli küçük bir etiket çizilir.
   Seçili koltuk kalın vurgu kenarlıdır.
------------------------------------------------------------------ */
const host = $("stage");
const stage = new Konva.Stage({ container: "stage", width: host.clientWidth || 800, height: host.clientHeight || 600 });
const gridLayer = new Konva.Layer({ listening: false });
const layer = new Konva.Layer();
const uiLayer = new Konva.Layer({ listening: false });
stage.add(gridLayer, layer, uiLayer);
gridLayer.add(KrokiCizim.makeGrid(stage));
const marquee = new Konva.Rect({ visible: false, strokeWidth: 1 });
uiLayer.add(marquee);
const nodes = new Map();      // koltuk id → Konva.Group
let TC = KrokiCizim.themeColors();
const { SEAT_W, SEAT_H, ORDER } = KrokiCizim;

function makeSeat(it) {
  const g = new Konva.Group({ x: it.x, y: it.y, rotation: it.rotation || 0 });
  g.setAttr("seatId", it.id);
  KrokiCizim.drawSeat(g, it, hooks.seatStyle(it, TC), TC);
  if (hooks.seatDot?.(it)) g.add(new Konva.Circle({ x: SEAT_W / 2 - 9, y: -SEAT_H / 2 + 9, radius: 10, fill: TC.exit, stroke: TC.paper, strokeWidth: 2, listening: false }));
  const badge = hooks.seatBadge?.(it, TC);
  if (badge) drawBadge(g, badge);
  return g;
}

/* Koltuk etiketi: koltuğun üst kenarına oturan, alt ucu koltuğu gösteren küçük renkli kutu (ipucu balonu gibi). */
function drawBadge(g, badge) {
  const fontSize = 15, h = 22, w = Math.max(30, badge.text.length * fontSize * 0.62 + 12);
  const top = -SEAT_H / 2 - h - 4;
  const b = new Konva.Group({ listening: false, opacity: badge.faded ? 0.6 : 1 });
  b.add(new Konva.Rect({ x: -w / 2, y: top, width: w, height: h, cornerRadius: 6, fill: badge.color, stroke: TC.paper, strokeWidth: 2 }));
  b.add(new Konva.Line({ points: [-5, top + h - 1, 0, top + h + 5, 5, top + h - 1], closed: true, fill: badge.color }));
  b.add(new Konva.Text({ text: badge.text, x: -w / 2, y: top, width: w, height: h, align: "center", verticalAlign: "middle", fontSize, fontStyle: "700", fontFamily: TC.font, fill: "#FFFFFF" }));
  g.add(b);
}

function render() {
  layer.destroyChildren(); nodes.clear();
  const items = plan.items.filter((i) => i.levelId === levelId).sort((a, b) => ORDER[a.type] - ORDER[b.type]);
  for (const it of items) {
    if (it.type === "seat") { const g = makeSeat(it); layer.add(g); nodes.set(it.id, g); }
    else { const g = new Konva.Group({ x: it.x, y: it.y, rotation: it.rotation || 0, listening: false }); KrokiCizim.drawFixture(g, it, TC); layer.add(g); }
  }
  paintSelection();
  renderTabs(); renderPanel(); updateSaveState();
}

/* Seçimi yalnızca kenar renkleriyle günceller (tüm krokiyi yeniden çizmeden). */
function paintSelection() {
  for (const [id, g] of nodes) {
    const body = g.findOne(".body");
    const on = selected.has(id);
    body.stroke(on ? TC.accent : body.getAttr("baseStroke"));
    body.strokeWidth(on ? 6 : body.getAttr("baseSW"));
  }
  layer.batchDraw();
}

/* ---------- Görünüm: yakınlaştırma, ekrana sığdırma, kat sekmeleri ---------- */
function resize() { stage.size({ width: host.clientWidth, height: host.clientHeight }); gridLayer.batchDraw(); }
new ResizeObserver(resize).observe(host);
function viewChanged() { $("zoomVal").textContent = Math.round(stage.scaleX() * 100) + "%"; gridLayer.batchDraw(); uiLayer.batchDraw(); }
function setZoom(scale, center) { KrokiCizim.zoomAt(stage, scale, center); viewChanged(); }
function fit() {
  const box = KrokiCizim.boxOf(layer.getChildren(), layer);
  if (box) KrokiCizim.fitBox(stage, box); else { stage.scale({ x: .3, y: .3 }); stage.position({ x: 40, y: 40 }); }
  viewChanged();
}
function renderTabs() {
  $("tabs").hidden = plan.levels.length < 2;
  $("tabs").innerHTML = plan.levels.map((l) => `<button class="tab" role="tab" data-level="${l.id}" aria-selected="${l.id === levelId}">${esc(l.name)}</button>`).join("");
}
$("tabs").addEventListener("click", (e) => {
  const b = e.target.closest("[data-level]"); if (!b || b.dataset.level === levelId) return;
  levelId = b.dataset.level; render(); fit();
});

/* ------------------------------------------------------------------
   Seçim: koltuğa tıklama seçer (Shift/Ctrl ile ekler/çıkarır). Koltuktan başlayıp fare basılıyken (ya da parmakla)
   sürüklemek, üzerinden geçilen koltukları da seçer; Shift/Ctrl ile seçili bir koltuktan başlanırsa geçilenleri bırakır.
   Boş alanda sürükleme alan seçimidir.
   El aracında, boşluk tuşu basılıyken ya da orta tuşla sürükleme kaydırır; iki parmak yakınlaştırır.
------------------------------------------------------------------ */
let spaceDown = false, panning = null, marqueeStart = null, pinch = null, paint = null;
const seatIdOf = (target) => { let n = target; while (n && n !== stage) { const id = n.getAttr && n.getAttr("seatId"); if (id) return id; n = n.getParent(); } return null; };

function setTool(name) {
  tool = name;
  $("toolSelect").setAttribute("aria-pressed", String(name === "select"));
  $("toolHand").setAttribute("aria-pressed", String(name === "hand"));
  host.classList.toggle("pan", name === "hand");
}
$("toolSelect").onclick = () => setTool("select");
$("toolHand").onclick = () => setTool("hand");

stage.on("mousedown touchstart", (e) => {
  const evt = e.evt;
  if (evt.touches && evt.touches.length === 2) return;
  const p = stage.getPointerPosition();
  if (tool === "hand" || spaceDown || evt.button === 1) {
    panning = { x: p.x, y: p.y, sx: stage.x(), sy: stage.y() };
    host.classList.add("panning"); evt.preventDefault(); return;
  }
  if (evt.button === 2) return;
  const id = seatIdOf(e.target);
  const add = evt.shiftKey || evt.ctrlKey || evt.metaKey;
  if (id) {
    if (!canSelect(id)) return;
    const remove = add && selected.has(id);
    if (!add) selected = new Set();
    remove ? selected.delete(id) : selected.add(id);
    paint = { remove, last: p };
    paintSelection();
    return;
  }
  const w = layer.getRelativePointerPosition();
  marqueeStart = { x: w.x, y: w.y, add };
  const s = stage.scaleX();
  marquee.setAttrs({ x: w.x, y: w.y, width: 0, height: 0, visible: true, stroke: TC.accent, fill: TC.accent + "14", strokeWidth: 1 / s, dash: [4 / s, 4 / s] });
});

stage.on("mousemove touchmove", (e) => {
  const evt = e.evt;
  if (evt.touches && evt.touches.length === 2) { handlePinch(evt); return; }
  const p = stage.getPointerPosition(); if (!p) return;
  if (panning) { stage.position({ x: panning.sx + p.x - panning.x, y: panning.sy + p.y - panning.y }); viewChanged(); return; }
  if (paint) { paintAlong(paint.last, p); paint.last = p; return; }
  if (marqueeStart) {
    const w = layer.getRelativePointerPosition();
    marquee.setAttrs({ x: Math.min(marqueeStart.x, w.x), y: Math.min(marqueeStart.y, w.y), width: Math.abs(w.x - marqueeStart.x), height: Math.abs(w.y - marqueeStart.y) });
    uiLayer.batchDraw(); return;
  }
  showHover(seatIdOf(e.target));
});

/* Sürüklerken son konumdan bu konuma kadar çizgi üzerindeki koltukları seçer (ya da bırakır). Fare hızlı gidince olaylar
   seyrek gelir; aradaki noktalara da bakılır ki küçük ölçekte koltuk atlanmasın. */
function paintAlong(from, to) {
  const steps = Math.max(1, Math.ceil(Math.hypot(to.x - from.x, to.y - from.y) / 3));
  let changed = false;
  for (let i = 1; i <= steps; i++) {
    const id = seatIdOf(stage.getIntersection({ x: from.x + (to.x - from.x) * i / steps, y: from.y + (to.y - from.y) * i / steps }));
    if (!id || !canSelect(id) || selected.has(id) !== paint.remove) continue;
    paint.remove ? selected.delete(id) : selected.add(id);
    changed = true;
  }
  if (changed) paintSelection();
}

function endPointer() {
  pinch = null;
  if (panning) { panning = null; host.classList.remove("panning"); return; }
  if (paint) { paint = null; renderPanel(); return; }
  if (!marqueeStart) return;
  const box = { x: marquee.x(), y: marquee.y(), width: marquee.width(), height: marquee.height() };
  if (!marqueeStart.add) selected = new Set();
  if (box.width > 2 || box.height > 2) {
    for (const [id, g] of nodes) if (canSelect(id) && Konva.Util.haveIntersection(box, g.getClientRect({ relativeTo: layer }))) selected.add(id);
  }
  marquee.visible(false); marqueeStart = null; uiLayer.batchDraw();
  paintSelection(); renderPanel();
}
window.addEventListener("mouseup", endPointer);
window.addEventListener("touchend", endPointer);
stage.on("wheel", (e) => KrokiCizim.wheel(stage, e.evt, viewChanged));
function handlePinch(evt) {
  evt.preventDefault();
  const [a, b] = evt.touches; const r = host.getBoundingClientRect();
  const c = { x: (a.clientX + b.clientX) / 2 - r.left, y: (a.clientY + b.clientY) / 2 - r.top };
  const dist = Math.hypot(a.clientX - b.clientX, a.clientY - b.clientY);
  if (!pinch) { pinch = { dist, scale: stage.scaleX() }; return; }
  setZoom(pinch.scale * dist / pinch.dist, c);
}

/* Fare altındaki koltuğun bilgisi: kat · blok · sıra-no · ekranın eklediği bilgiler */
function showHover(id) {
  const it = id && seats.get(id);
  const el = $("hoverInfo");
  if (!it) { el.hidden = true; return; }
  const b = it.blockId && blockById(it.blockId);
  const parts = [levelById(it.levelId)?.name, b ? b.name : null, KrokiCizim.seatLabel(it), ...(hooks.hoverParts?.(it) ?? [])];
  el.textContent = parts.filter(Boolean).join(" · ");
  el.hidden = false;
}

/* ---------- Toplu seçim: kat, blok, blok içinde sıra aralığı ve ekranın eklediği düğmeler ---------- */
const NO_BLOCK = "-";
const seatsOfBlock = (blockKey) => levelSeats().filter((s) => (s.blockId || NO_BLOCK) === blockKey);
const rowsOf = (blockKey) => [...new Set(seatsOfBlock(blockKey).map((s) => s.row || ""))].sort(naturalSort);
function selectSeats(list, add) {
  if (!add) selected = new Set();
  list.forEach((s) => { if (canSelect(s.id)) selected.add(s.id); });
  paintSelection(); renderPanel();
}
function selectRows(blockKey, from, to) {
  const rows = rowsOf(blockKey);
  let i = rows.indexOf(from), j = rows.indexOf(to);
  if (i > j) [i, j] = [j, i];
  const wanted = new Set(rows.slice(i, j + 1));
  return seatsOfBlock(blockKey).filter((s) => wanted.has(s.row || ""));
}

/* ------------------------------------------------------------------
   Panel: ilk bölüm (#selSec) seçim ve toplu seçimdir; sonrasını ekran yazar (hooks.renderPanel).
   Panel düğmeleri data-act taşır; ortak olmayanlar ekrana iletilir (hooks.onAction).
------------------------------------------------------------------ */
function renderPanel() {
  const lvSeats = levelSeats();
  const blockKeys = [...new Set(lvSeats.map((s) => s.blockId || NO_BLOCK))];
  const blockName = (k) => (k === NO_BLOCK ? t("noBlock") : blockById(k)?.name ?? "");
  const curBlock = $("blockSel")?.value;
  const blockKey = blockKeys.includes(curBlock) ? curBlock : blockKeys[0];
  const rows = blockKey ? rowsOf(blockKey) : [];
  const keep = (id) => $(id)?.value;
  const opt = (v, label, cur) => `<option value="${esc(v)}" ${v === cur ? "selected" : ""}>${esc(label)}</option>`;
  const fromRow = rows.includes(keep("rowFrom")) ? keep("rowFrom") : rows[0];
  const toRow = rows.includes(keep("rowTo")) ? keep("rowTo") : rows[rows.length - 1];
  $("selSec").innerHTML = `
    <h3>${esc(t("selection"))}</h3>
    <p class="selinfo">${esc(selected.size ? t("selCount", { n: selected.size }) : t("selNone"))}</p>
    <p class="note">${esc(t("selHint"))}</p>
    <div class="btns">
      <button class="btn" data-act="selLevel">${esc(t("selectLevel"))}</button>
      ${(hooks.quickSelect ?? []).map((q) => `<button class="btn" data-act="quick:${q.act}">${esc(q.label)}</button>`).join("")}
      <button class="btn" data-act="clearSel" ${selected.size ? "" : "disabled"}>${esc(t("clearSel"))}</button>
    </div>
    ${blockKeys.length ? `
    <label class="fld"><span>${esc(t("block"))}</span><select id="blockSel">${blockKeys.map((k) => opt(k, blockName(k), blockKey)).join("")}</select></label>
    <div class="row2">
      <label class="fld"><span>${esc(t("rowFrom"))}</span><select id="rowFrom">${rows.map((r) => opt(r, r || "—", fromRow)).join("")}</select></label>
      <label class="fld"><span>${esc(t("rowTo"))}</span><select id="rowTo">${rows.map((r) => opt(r, r || "—", toRow)).join("")}</select></label>
    </div>
    <div class="btns">
      <button class="btn" data-act="selBlock">${esc(t("selectBlock"))}</button>
      <button class="btn" data-act="selRows">${esc(t("selectRows"))}</button>
    </div>` : ""}`;
  hooks.renderPanel();
}

$("panel").addEventListener("click", (e) => {
  const b = e.target.closest("[data-act]"); if (!b || b.disabled) return;
  const a = b.dataset.act;
  if (a === "selLevel") selectSeats(levelSeats(), false);
  else if (a === "clearSel") selectSeats([], false);
  else if (a === "selBlock") selectSeats(seatsOfBlock($("blockSel").value), e.shiftKey);
  else if (a === "selRows") selectSeats(selectRows($("blockSel").value, $("rowFrom").value, $("rowTo").value), e.shiftKey);
  else if (a.startsWith("quick:")) selectSeats(hooks.quickSelect.find((q) => "quick:" + q.act === a).seats(), false);
  else hooks.onAction?.(a, b, e);
});
$("panel").addEventListener("change", (e) => {
  if (e.target.id === "blockSel") { $("rowFrom").value = ""; $("rowTo").value = ""; renderPanel(); return; }
  hooks.onChange?.(e);
});
$("panelBtn").onclick = () => $("panel").classList.toggle("open");

let toastTimer;
function toast(msg, ms = 2400) { const el = $("toast"); el.textContent = msg; el.hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => (el.hidden = true), ms); }

/* ------------------------------------------------------------------
   Kaydetme: POST {saveUrl}&baseRevision=N, gövde verinin JSON'u. Sunucunun cevabı:
     saved / unchanged → revision güncellenir · conflict → bu arada başkası kaydetmiş · invalid → message
   Kaydedilmemiş değişiklik varken sayfadan çıkılmak istenirse tarayıcı uyarır.
------------------------------------------------------------------ */
let revision = CONFIG.revision;
let savedSnap = lastSnap;
let saving = false;
const isDirty = () => lastSnap !== savedSnap;
function updateSaveState(text) {
  if (pickOnly) { $("saveState").textContent = hooks.stateText?.() ?? ""; return; }
  const dirty = isDirty();
  $("saveState").textContent = text || (dirty ? t("unsaved") : revision ? t("revisionN", { n: revision }) : t("noRevision"));
  $("saveState").classList.toggle("dirty", dirty);
  $("saveBtn").disabled = saving;
  $("undoBtn").disabled = !undoStack.length;
  $("redoBtn").disabled = !redoStack.length;
}
async function save() {
  if (saving || pickOnly) return;
  saving = true; updateSaveState(t("saving"));
  const body = JSON.stringify(data);
  let message = t("saveFailed");
  try {
    const res = await fetch(`${CONFIG.saveUrl}&baseRevision=${revision}`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "RequestVerificationToken": CONFIG.token },
      body
    });
    const r = await res.json().catch(() => ({}));
    if (r.status === "saved" || r.status === "unchanged") { revision = r.revision; savedSnap = body; message = t(r.status); }
    else if (r.status === "conflict") message = t("conflict");
    else if (r.message) message = r.message;
  } catch (e) { /* bağlantı hatası: varsayılan mesaj */ }
  saving = false; updateSaveState(); toast(message, 6000);
}
if (!pickOnly) {
  $("saveBtn").onclick = save;
  window.addEventListener("beforeunload", (e) => { if (isDirty()) { e.preventDefault(); e.returnValue = ""; } });
  $("undoBtn").onclick = undo;
  $("redoBtn").onclick = redo;
}
$("zoomIn").onclick = () => setZoom(stage.scaleX() * 1.25);
$("zoomOut").onclick = () => setZoom(stage.scaleX() / 1.25);
$("fitBtn").onclick = fit;

/* ---------- Klavye kısayolları: V seç, H el, boşluk kaydır, 0 sığdır, Ctrl+A tümü, Ctrl+Z/Y geri al/yinele, Ctrl+S kaydet ---------- */
window.addEventListener("keydown", (e) => {
  const tag = (e.target.tagName || "").toLowerCase();
  const mod = e.ctrlKey || e.metaKey, k = e.key.toLowerCase();
  if (mod && k === "s") { e.preventDefault(); if (["input", "select"].includes(tag)) e.target.blur(); save(); return; }
  if (["input", "textarea", "select"].includes(tag)) { if (e.key === "Escape") e.target.blur(); return; }
  if (e.code === "Space") { if (!spaceDown) { spaceDown = true; host.classList.add("pan"); } e.preventDefault(); return; }
  if (mod && k === "z") { e.preventDefault(); e.shiftKey ? redo() : undo(); return; }
  if (mod && k === "y") { e.preventDefault(); redo(); return; }
  if (mod && k === "a") { e.preventDefault(); selectSeats(levelSeats(), false); return; }
  if (mod) return;
  if (e.key === "Escape") return selectSeats([], false);
  if (e.key === "+" || e.key === "=") return setZoom(stage.scaleX() * 1.25);
  if (e.key === "-") return setZoom(stage.scaleX() / 1.25);
  if (e.key === "0") return fit();
  if (k === "v") setTool("select");
  if (k === "h") setTool("hand");
});
window.addEventListener("keyup", (e) => { if (e.code === "Space") { spaceDown = false; host.classList.toggle("pan", tool === "hand"); } });
host.addEventListener("contextmenu", (e) => e.preventDefault());

/* Tema değişince tuval renklerini CSS değişkenlerinden yeniden oku */
const reTheme = () => { TC = KrokiCizim.themeColors(); render(); };
try { matchMedia("(prefers-color-scheme: dark)").addEventListener("change", reTheme); } catch (e) {}

/* ------------------------------------------------------------------
   Dış arayüz: ekran betiği veriyi okur/değiştirir, değişiklikten sonra commit() çağırır.
   init() kancalar hazır olduktan sonra ilk çizimi yapar.
------------------------------------------------------------------ */
return {
  config: CONFIG, t, esc, locale, plan, seats,
  get data() { return data; },
  set data(value) { data = value; },
  get selected() { return selected; },
  levelSeats, selectSeats, commit, render, toast,
  init() {
    resize(); render(); fit();
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(reTheme);
  },
};
}

window.KoltukSecici = { create };
})();
