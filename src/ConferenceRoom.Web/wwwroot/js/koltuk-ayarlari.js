/*
  Etkinlik koltuk ayarları (yönetim paneli: /yonetim/fiyatlandirma/{id})
  ======================================================================
  Etkinliğin bilet kategorilerini (ad, fiyat, renk, protokol) tanımlar, koltuklara kategori atar ve internetten
  satışa açık koltukları seçer. Kroki salt okunurdur; çizim kroki editörüyle ortaktır (kroki-cizim.js).
  Sayfa (Pages/Yonetim/Fiyatlandirma/Koltuklar.cshtml) üç JSON bloğu verir:
    #editorConfig : etkinlik adı, ayar sürümü (revision), kayıt adresi, dil, ekran metinleri (tr.json/en.json "seatEditor" bölümü)
    #planData     : gösterilecek kroki (seansların bağlı olduğu en yeni sürüm)
    #mapData      : kayıtlı ayarlar { schema, categories[], seats{ koltukId: kategoriId }, online[] } (Domain/Events/SeatMap.cs)
  Atama koltuk kimliğine göredir; blok, kat ve sıra aralığı yalnızca toplu seçme kolaylığıdır.

  Dosyanın bölümleri:
    dil ve ayarlar → durum → geri al/yinele → çizim → seçim (fare/dokunma, toplu seçim) → kategoriler → online →
    panel → kaydetme → kısayollar → başlangıç
*/
(() => {
"use strict";

/* ------------------------------------------------------------------
   Dil ve ayarlar. Metinler sayfanın dilinde, sunucudan gelir (anahtarlar "seatEditor." önekisiz).
------------------------------------------------------------------ */
const CONFIG = JSON.parse(document.getElementById("editorConfig").textContent);
const TEXTS = CONFIG.texts;
const locale = CONFIG.culture === "tr" ? "tr-TR" : "en-GB";
function t(key, vars) {
  let s = TEXTS[key] ?? key;
  if (vars) for (const k in vars) s = s.split("{" + k + "}").join(vars[k]);
  return s;
}
const money = new Intl.NumberFormat(locale, { style: "currency", currency: "TRY", maximumFractionDigits: 2 });
const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? "").replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
const uid = () => "c" + Math.random().toString(36).slice(2, 9);
const naturalSort = (a, b) => String(a).localeCompare(String(b), locale, { numeric: true });
const CATEGORY_COLORS = ["#1D5FBF", "#0F766E", "#B4531A", "#6D3FC0", "#B0306A", "#4D7C0F", "#0E7490", "#8A5A12"];

/* ------------------------------------------------------------------
   Durum.
   plan: kroki (değişmez). map: ayarlar (kaydedilen veri). selected: seçili koltuk id'leri.
   seats: kroki koltukları (id → öğe). levelId: gösterilen kat.
------------------------------------------------------------------ */
const plan = JSON.parse($("planData").textContent);
let map = JSON.parse($("mapData").textContent);
const seats = new Map(plan.items.filter((i) => i.type === "seat").map((i) => [i.id, i]));
const blockById = (id) => plan.blocks.find((b) => b.id === id);
const levelById = (id) => plan.levels.find((l) => l.id === id);
let levelId = plan.levels[0].id;
let selected = new Set();
let tool = "select";

const categoryById = (id) => map.categories.find((c) => c.id === id);
const categoryOf = (seatId) => categoryById(map.seats[seatId]);
const onlineSet = () => new Set(map.online);
const levelSeats = () => [...seats.values()].filter((s) => s.levelId === levelId);

/* ---------- Geri al / yinele: her değişiklikten sonra commit() ayarların JSON'unu yığına koyar ---------- */
let undoStack = [], redoStack = [], lastSnap = JSON.stringify(map);
function commit() {
  const snap = JSON.stringify(map);
  if (snap === lastSnap) return;
  undoStack.push(lastSnap); if (undoStack.length > 100) undoStack.shift();
  redoStack = []; lastSnap = snap;
  render();
}
function restore(snap) { map = JSON.parse(snap); lastSnap = snap; render(); }
function undo() { if (!undoStack.length) return; redoStack.push(lastSnap); restore(undoStack.pop()); }
function redo() { if (!redoStack.length) return; undoStack.push(lastSnap); restore(redoStack.pop()); }

/* ------------------------------------------------------------------
   Çizim (Konva). Katmanlar: gridLayer (ızgara), layer (kroki), uiLayer (alan seçimi kutusu).
   Koltuk rengi kategorisinden gelir; kategorisiz koltuk gri, protokol kategorisi kesik kenarlı,
   online koltuğun sağ üst köşesinde yeşil nokta vardır. Seçili koltuk kalın vurgu kenarlıdır.
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

function seatStyle(it) {
  const c = categoryOf(it.id);
  return c
    ? { fill: c.color, text: "#FFFFFF", stroke: c.protocol ? TC.ink : "rgba(0,0,0,.35)", strokeWidth: c.protocol ? 3 : 1.5, dash: c.protocol ? [6, 4] : null }
    : { fill: TC.seat, text: TC.ink, stroke: TC.seatStroke, strokeWidth: 1.5, dash: it.kind === "extra" ? [7, 5] : null };
}

function makeSeat(it, online) {
  const g = new Konva.Group({ x: it.x, y: it.y, rotation: it.rotation || 0 });
  g.setAttr("seatId", it.id);
  KrokiCizim.drawSeat(g, it, seatStyle(it), TC);
  if (online) g.add(new Konva.Circle({ x: SEAT_W / 2 - 9, y: -SEAT_H / 2 + 9, radius: 10, fill: TC.exit, stroke: TC.paper, strokeWidth: 2, listening: false }));
  return g;
}

function render() {
  layer.destroyChildren(); nodes.clear();
  const online = onlineSet();
  const items = plan.items.filter((i) => i.levelId === levelId).sort((a, b) => ORDER[a.type] - ORDER[b.type]);
  for (const it of items) {
    if (it.type === "seat") { const g = makeSeat(it, online.has(it.id)); layer.add(g); nodes.set(it.id, g); }
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
   Seçim: koltuğa tıklama seçer (Shift/Ctrl ile ekler/çıkarır), boş alanda sürükleme alan seçimidir.
   El aracında, boşluk tuşu basılıyken ya da orta tuşla sürükleme kaydırır; iki parmak yakınlaştırır.
------------------------------------------------------------------ */
let spaceDown = false, panning = null, marqueeStart = null, pinch = null;
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
    if (add) selected.has(id) ? selected.delete(id) : selected.add(id);
    else selected = new Set([id]);
    paintSelection(); renderPanel();
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
  if (marqueeStart) {
    const w = layer.getRelativePointerPosition();
    marquee.setAttrs({ x: Math.min(marqueeStart.x, w.x), y: Math.min(marqueeStart.y, w.y), width: Math.abs(w.x - marqueeStart.x), height: Math.abs(w.y - marqueeStart.y) });
    uiLayer.batchDraw(); return;
  }
  showHover(seatIdOf(e.target));
});

function endPointer() {
  pinch = null;
  if (panning) { panning = null; host.classList.remove("panning"); return; }
  if (!marqueeStart) return;
  const box = { x: marquee.x(), y: marquee.y(), width: marquee.width(), height: marquee.height() };
  if (!marqueeStart.add) selected = new Set();
  if (box.width > 2 || box.height > 2) {
    for (const [id, g] of nodes) if (Konva.Util.haveIntersection(box, g.getClientRect({ relativeTo: layer }))) selected.add(id);
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

/* Fare altındaki koltuğun bilgisi: kat · blok · sıra-no · kategori (fiyat) · online */
function showHover(id) {
  const it = id && seats.get(id);
  const el = $("hoverInfo");
  if (!it) { el.hidden = true; return; }
  const b = it.blockId && blockById(it.blockId), c = categoryOf(id);
  const parts = [levelById(it.levelId)?.name, b ? b.name : null, KrokiCizim.seatLabel(it),
    c ? c.name + (c.protocol ? "" : " · " + money.format(c.price)) : t("noCategory"), onlineSet().has(id) ? t("online") : null];
  el.textContent = parts.filter(Boolean).join(" · ");
  el.hidden = false;
}

/* ---------- Toplu seçim: kat, blok, blok içinde sıra aralığı, kategorisiz koltuklar ---------- */
const NO_BLOCK = "-";
const seatsOfBlock = (blockKey) => levelSeats().filter((s) => (s.blockId || NO_BLOCK) === blockKey);
const rowsOf = (blockKey) => [...new Set(seatsOfBlock(blockKey).map((s) => s.row || ""))].sort(naturalSort);
function selectSeats(list, add) {
  if (!add) selected = new Set();
  list.forEach((s) => selected.add(s.id));
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
   Kategoriler. Atama seçili koltuklara yapılır; protokol kategorisine geçen koltuk online listesinden çıkar.
   Kategori silinince koltukları kategorisiz kalır (ve online listesinden çıkar).
------------------------------------------------------------------ */
function addCategory() {
  const n = map.categories.length + 1;
  map.categories.push({ id: uid(), name: t("defaultCategoryName", { n }), price: 0, color: CATEGORY_COLORS[(n - 1) % CATEGORY_COLORS.length], protocol: false });
  commit();
  const inputs = document.querySelectorAll("#catList input[data-field=name]");
  inputs[inputs.length - 1]?.select();
}
function dropOnline(ids) { const drop = new Set(ids); map.online = map.online.filter((id) => !drop.has(id)); }
function assign(categoryId) {
  if (!selected.size) return toast(t("needSelection"));
  const c = categoryById(categoryId);
  selected.forEach((id) => (map.seats[id] = categoryId));
  if (c.protocol) dropOnline(selected);
  commit();
  toast(t("assignedToast", { n: selected.size, name: c.name }));
}
function unassign() {
  if (!selected.size) return toast(t("needSelection"));
  selected.forEach((id) => delete map.seats[id]);
  dropOnline(selected);
  commit();
}
function deleteCategory(categoryId) {
  const c = categoryById(categoryId);
  const ids = Object.keys(map.seats).filter((id) => map.seats[id] === categoryId);
  if (ids.length && !confirm(t("deleteCategoryConfirm", { name: c.name, n: ids.length }))) return;
  ids.forEach((id) => delete map.seats[id]);
  dropOnline(ids);
  map.categories = map.categories.filter((x) => x.id !== categoryId);
  commit();
}
/* Kategori alanı değişince (ad, fiyat, renk, protokol). Geçersiz fiyat eski değere döner. */
function updateCategory(categoryId, field, input) {
  const c = categoryById(categoryId);
  if (field === "name") { const v = input.value.trim().slice(0, 60); if (!v) { input.value = c.name; return; } c.name = v; }
  else if (field === "price") {
    const v = Number(input.value.replace(/\s/g, "").replace(",", "."));
    if (!isFinite(v) || v < 0 || v > 1000000) { input.value = formatPrice(c.price); return; }
    c.price = Math.round(v * 100) / 100;
  }
  else if (field === "color") c.color = input.value.toUpperCase();
  else if (field === "protocol") {
    c.protocol = input.checked;
    if (c.protocol) dropOnline(Object.keys(map.seats).filter((id) => map.seats[id] === categoryId));
  }
  commit();
}
const formatPrice = (v) => v.toLocaleString(locale, { minimumFractionDigits: Number.isInteger(v) ? 0 : 2, maximumFractionDigits: 2, useGrouping: false });

/* ---------- Online: yalnızca kategorisi olan, protokol olmayan koltuklar online olabilir ---------- */
function setOnline(on) {
  if (!selected.size) return toast(t("needSelection"));
  if (!on) { dropOnline(selected); commit(); return; }
  const online = onlineSet();
  let skipped = 0;
  for (const id of selected) {
    const c = categoryOf(id);
    if (!c || c.protocol) { skipped++; continue; }
    online.add(id);
  }
  map.online = [...online];
  commit();
  if (skipped) toast(t("onlineSkipped", { n: skipped }), 4000);
}

/* ------------------------------------------------------------------
   Panel: seçim ve toplu seçim, kategoriler, online, özet. Seçim değişince yeniden üretilir;
   yazı alanlarında odak kaybolmasın diye kategori listesi yalnızca ayarlar değişince yenilenir (render).
------------------------------------------------------------------ */
function renderPanel() {
  // ---- Seçim ----
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
      <button class="btn" data-act="selUnassigned">${esc(t("selectUnassigned"))}</button>
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

  // ---- Kategoriler ----
  const counts = {};
  for (const id in map.seats) counts[map.seats[id]] = (counts[map.seats[id]] || 0) + 1;
  $("catSec").innerHTML = `
    <h3>${esc(t("categories"))}</h3>
    <p class="note">${esc(t("catHint"))}</p>
    <div class="cats" id="catList">${map.categories.length ? map.categories.map((c) => `
      <div class="cat" data-cat="${c.id}">
        <input type="color" data-field="color" value="${esc(c.color)}" aria-label="${esc(t("catColor"))}" title="${esc(t("catColor"))}">
        <input type="text" data-field="name" value="${esc(c.name)}" maxlength="60" aria-label="${esc(t("catName"))}">
        <button class="btn primary" data-act="assign" title="${esc(t("assignTitle"))}">${esc(t("assign"))}</button>
        <label class="fld price"><span>${esc(t("catPrice"))}</span><input type="text" inputmode="decimal" data-field="price" value="${esc(formatPrice(c.price))}" ${c.protocol ? "disabled" : ""}></label>
        <label class="check" title="${esc(t("protocolTitle"))}"><input type="checkbox" data-field="protocol" ${c.protocol ? "checked" : ""}><span>${esc(t("protocol"))}</span></label>
        <span class="cnt">${esc(t("seatCount", { n: counts[c.id] || 0 }))}</span>
        <button class="icon-del" data-act="deleteCat" title="${esc(t("deleteCategory"))}" aria-label="${esc(t("deleteCategory"))}"><svg viewBox="0 0 24 24"><path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13"/></svg></button>
      </div>`).join("") : `<p class="note">${esc(t("noCategories"))}</p>`}
    </div>
    <div class="btns">
      <button class="btn" data-act="addCat"><svg viewBox="0 0 24 24"><path d="M12 5v14M5 12h14"/></svg>${esc(t("newCategory"))}</button>
      <button class="btn" data-act="unassign" ${selected.size ? "" : "disabled"}>${esc(t("unassign"))}</button>
    </div>`;

  // ---- Online ----
  const online = onlineSet();
  $("onlineSec").innerHTML = `
    <h3>${esc(t("onlineTitle"))}</h3>
    ${CONFIG.onlineEnabled ? `<p class="note">${esc(t("onlineHint"))}</p>` : `<p class="note warn">${esc(t("onlineDisabled"))}</p>`}
    <div class="btns">
      <button class="btn" data-act="onlineOn" ${selected.size ? "" : "disabled"}>${esc(t("onlineOn"))}</button>
      <button class="btn" data-act="onlineOff" ${selected.size ? "" : "disabled"}>${esc(t("onlineOff"))}</button>
      <button class="btn" data-act="selOnline">${esc(t("selectOnline"))}</button>
    </div>`;

  // ---- Özet (tüm katlar) ----
  let assigned = 0, protocol = 0, potential = 0;
  for (const id of seats.keys()) {
    const c = categoryOf(id); if (!c) continue;
    assigned++;
    if (c.protocol) protocol++; else potential += c.price;
  }
  const row = (label, value, cls = "") => `<dt class="${cls}">${esc(label)}</dt><dd class="${cls}">${esc(value)}</dd>`;
  $("statSec").innerHTML = `
    <h3>${esc(t("summary"))}</h3>
    <dl class="stats">
      ${row(t("totalSeats"), seats.size)}
      ${row(t("assignedSeats"), assigned)}
      ${row(t("unassignedSeats"), seats.size - assigned)}
      ${row(t("protocolSeats"), protocol)}
      ${row(t("onlineSeats"), online.size)}
      ${row(t("potential"), money.format(potential), "total")}
    </dl>
    <p class="note">${esc(t("settingsNote"))}</p>
    <div class="legend"><span><i class="dot"></i>${esc(t("online"))}</span><span><i></i>${esc(t("noCategory"))}</span><span><i class="proto"></i>${esc(t("protocol"))}</span></div>`;
}

$("panel").addEventListener("click", (e) => {
  const b = e.target.closest("[data-act]"); if (!b || b.disabled) return;
  const catId = b.closest("[data-cat]")?.dataset.cat;
  const a = b.dataset.act;
  if (a === "selLevel") selectSeats(levelSeats(), false);
  else if (a === "selUnassigned") selectSeats(levelSeats().filter((s) => !categoryOf(s.id)), false);
  else if (a === "selOnline") { const on = onlineSet(); selectSeats(levelSeats().filter((s) => on.has(s.id)), false); }
  else if (a === "clearSel") selectSeats([], false);
  else if (a === "selBlock") selectSeats(seatsOfBlock($("blockSel").value), e.shiftKey);
  else if (a === "selRows") selectSeats(selectRows($("blockSel").value, $("rowFrom").value, $("rowTo").value), e.shiftKey);
  else if (a === "addCat") addCategory();
  else if (a === "assign") assign(catId);
  else if (a === "deleteCat") deleteCategory(catId);
  else if (a === "unassign") unassign();
  else if (a === "onlineOn") setOnline(true);
  else if (a === "onlineOff") setOnline(false);
});
$("panel").addEventListener("change", (e) => {
  if (e.target.id === "blockSel") { $("rowFrom").value = ""; $("rowTo").value = ""; renderPanel(); return; }
  const f = e.target.dataset.field, catId = e.target.closest("[data-cat]")?.dataset.cat;
  if (f && catId) updateCategory(catId, f, e.target);
});
$("panelBtn").onclick = () => $("panel").classList.toggle("open");

let toastTimer;
function toast(msg, ms = 2400) { const el = $("toast"); el.textContent = msg; el.hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => (el.hidden = true), ms); }

/* ------------------------------------------------------------------
   Kaydetme: POST ?handler=Save&baseRevision=N, gövde ayarların JSON'u. Sunucunun cevabı:
     saved / unchanged → revision güncellenir · conflict → bu arada başkası kaydetmiş · invalid → message
   Kaydedilmemiş değişiklik varken sayfadan çıkılmak istenirse tarayıcı uyarır.
------------------------------------------------------------------ */
let revision = CONFIG.revision;
let savedSnap = lastSnap;
let saving = false;
const isDirty = () => lastSnap !== savedSnap;
function updateSaveState(text) {
  const dirty = isDirty();
  $("saveState").textContent = text || (dirty ? t("unsaved") : revision ? t("revisionN", { n: revision }) : t("noRevision"));
  $("saveState").classList.toggle("dirty", dirty);
  $("saveBtn").disabled = saving;
  $("undoBtn").disabled = !undoStack.length;
  $("redoBtn").disabled = !redoStack.length;
}
async function save() {
  if (saving) return;
  saving = true; updateSaveState(t("saving"));
  const body = JSON.stringify(map);
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
$("saveBtn").onclick = save;
window.addEventListener("beforeunload", (e) => { if (isDirty()) { e.preventDefault(); e.returnValue = ""; } });
$("undoBtn").onclick = undo;
$("redoBtn").onclick = redo;
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

/* ---------- Başlangıç ---------- */
resize();
render();
fit();
if (document.fonts && document.fonts.ready) document.fonts.ready.then(reTheme);
// Tarayıcı testleri ve hata ayıklama için: konsolda __koltuk.map ile ayarlara bakılabilir.
window.__koltuk = { get map() { return map; }, selected: () => [...selected], select: (ids) => selectSeats(ids.map((id) => seats.get(id)), false) };
})();
