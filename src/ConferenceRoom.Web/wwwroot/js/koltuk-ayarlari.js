/*
  Etkinlik koltuk ayarları (yönetim paneli: /yonetim/fiyatlandirma/{id})
  ======================================================================
  Etkinliğin bilet kategorilerini (ad, fiyat, renk, protokol) tanımlar, koltuklara kategori atar ve internetten
  satışa açık koltukları seçer. Kroki, seçim, kaydetme ve kısayollar koltuk seçicinin ortak çatısındadır (koltuk-secici.js);
  burada yalnızca bu ekrana özgü olanlar var.
  Düzenlenen veri (#seatData): { schema, categories[], seats{ koltukId: kategoriId }, online[] } (Domain/Events/SeatMap.cs)
  Kategori: { id, name, price, color, protocol }; fiyatı 0 olan kategori ekranlarda "Rezervasyon" diye görünür.
  showOnlinePrice (etkinlik geneli): online satışta/rezervasyonda bilet fiyatı gösterilsin mi; yoksa true sayılır.
  Ekran metinleri: tr.json/en.json "seatEditor" bölümü. Atama koltuk kimliğine göredir.

  Dosyanın bölümleri: yardımcılar → koltuk görünümü → kategoriler → online → panel → başlangıç
*/
(() => {
"use strict";

const hooks = { seatStyle, seatDot, hoverParts, renderPanel, onAction, onChange };
const ed = KoltukSecici.create(hooks);
const { t, esc, locale } = ed;
const $ = (id) => document.getElementById(id);
// Seçim bölümüne eklenen düğme: bu kattaki kategorisiz koltuklar.
hooks.quickSelect = [{ act: "unassigned", label: t("selectUnassigned"), seats: () => ed.levelSeats().filter((s) => !categoryOf(s.id)) }];

/* ---------- Yardımcılar ---------- */
const money = new Intl.NumberFormat(locale, { style: "currency", currency: "TRY", maximumFractionDigits: 2 });
const uid = () => "c" + Math.random().toString(36).slice(2, 9);
const CATEGORY_COLORS = ["#1D5FBF", "#0F766E", "#B4531A", "#6D3FC0", "#B0306A", "#4D7C0F", "#0E7490", "#8A5A12"];
const categoryById = (id) => ed.data.categories.find((c) => c.id === id);
const categoryOf = (seatId) => categoryById(ed.data.seats[seatId]);
const onlineSet = () => new Set(ed.data.online);
/* Fare altında kategorinin fiyatı: fiyatı 0 ise "Rezervasyon" yazar. */
const priceText = (c) => (c.price ? money.format(c.price) : t("reservation"));
const formatPrice = (v) => v.toLocaleString(locale, { minimumFractionDigits: Number.isInteger(v) ? 0 : 2, maximumFractionDigits: 2, useGrouping: false });

/* ------------------------------------------------------------------
   Koltuk görünümü: renk kategoriden gelir; kategorisiz koltuk gri, protokol kategorisi kesik kenarlı,
   online koltuğun köşesinde yeşil nokta vardır.
------------------------------------------------------------------ */
function seatStyle(it, TC) {
  const c = categoryOf(it.id);
  return c
    ? { fill: c.color, text: "#FFFFFF", stroke: c.protocol ? TC.ink : "rgba(0,0,0,.35)", strokeWidth: c.protocol ? 3 : 1.5, dash: c.protocol ? [6, 4] : null }
    : { fill: TC.seat, text: TC.ink, stroke: TC.seatStroke, strokeWidth: 1.5, dash: it.kind === "extra" ? [7, 5] : null };
}
function seatDot(it) { return ed.data.online.includes(it.id); }
function hoverParts(it) {
  const c = categoryOf(it.id);
  return [c ? c.name + (c.protocol ? "" : " · " + priceText(c)) : t("noCategory"), onlineSet().has(it.id) ? t("online") : null];
}

/* ------------------------------------------------------------------
   Kategoriler. Atama seçili koltuklara yapılır; protokol kategorisine geçen koltuk online listesinden çıkar.
   Kategori silinince koltukları kategorisiz kalır (ve online listesinden çıkar).
------------------------------------------------------------------ */
function addCategory() {
  const n = ed.data.categories.length + 1;
  ed.data.categories.push({ id: uid(), name: t("defaultCategoryName", { n }), price: 0, color: CATEGORY_COLORS[(n - 1) % CATEGORY_COLORS.length], protocol: false });
  ed.commit();
  const inputs = document.querySelectorAll("#catList input[data-field=name]");
  inputs[inputs.length - 1]?.select();
}
function dropOnline(ids) { const drop = new Set(ids); ed.data.online = ed.data.online.filter((id) => !drop.has(id)); }
function assign(categoryId) {
  const selected = ed.selected;
  if (!selected.size) return ed.toast(t("needSelection"));
  const c = categoryById(categoryId);
  selected.forEach((id) => (ed.data.seats[id] = categoryId));
  if (c.protocol) dropOnline(selected);
  ed.commit();
  ed.toast(t("assignedToast", { n: selected.size, name: c.name }));
}
function unassign() {
  const selected = ed.selected;
  if (!selected.size) return ed.toast(t("needSelection"));
  selected.forEach((id) => delete ed.data.seats[id]);
  dropOnline(selected);
  ed.commit();
}
function deleteCategory(categoryId) {
  const map = ed.data, c = categoryById(categoryId);
  const ids = Object.keys(map.seats).filter((id) => map.seats[id] === categoryId);
  if (ids.length && !confirm(t("deleteCategoryConfirm", { name: c.name, n: ids.length }))) return;
  ids.forEach((id) => delete map.seats[id]);
  dropOnline(ids);
  map.categories = map.categories.filter((x) => x.id !== categoryId);
  ed.commit();
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
    if (c.protocol) dropOnline(Object.keys(ed.data.seats).filter((id) => ed.data.seats[id] === categoryId));
  }
  ed.commit();
}

/* ---------- Online: yalnızca kategorisi olan, protokol olmayan koltuklar online olabilir ---------- */
function setOnline(on) {
  const selected = ed.selected;
  if (!selected.size) return ed.toast(t("needSelection"));
  if (!on) { dropOnline(selected); ed.commit(); return; }
  const online = onlineSet();
  let skipped = 0;
  for (const id of selected) {
    const c = categoryOf(id);
    if (!c || c.protocol) { skipped++; continue; }
    online.add(id);
  }
  ed.data.online = [...online];
  ed.commit();
  if (skipped) ed.toast(t("onlineSkipped", { n: skipped }), 4000);
}

/* ------------------------------------------------------------------
   Panel: kategoriler, online, özet (seçim bölümü ortak çatıda). Seçim değişince yeniden üretilir.
------------------------------------------------------------------ */
function renderPanel() {
  const map = ed.data, hasSel = ed.selected.size > 0;

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
      <button class="btn" data-act="unassign" ${hasSel ? "" : "disabled"}>${esc(t("unassign"))}</button>
    </div>`;

  // ---- Online ----
  const online = onlineSet();
  $("onlineSec").innerHTML = `
    <h3>${esc(t("onlineTitle"))}</h3>
    ${ed.config.onlineEnabled ? `<p class="note">${esc(t("onlineHint"))}</p>` : `<p class="note warn">${esc(t("onlineDisabled"))}</p>`}
    <label class="check online-price" title="${esc(t("showOnlinePriceHint"))}"><input type="checkbox" data-field="showOnlinePrice" ${map.showOnlinePrice !== false ? "checked" : ""}><span>${esc(t("showOnlinePrice"))}</span></label>
    <p class="note">${esc(t("showOnlinePriceHint"))}</p>
    <div class="btns">
      <button class="btn" data-act="onlineOn" ${hasSel ? "" : "disabled"}>${esc(t("onlineOn"))}</button>
      <button class="btn" data-act="onlineOff" ${hasSel ? "" : "disabled"}>${esc(t("onlineOff"))}</button>
      <button class="btn" data-act="selOnline">${esc(t("selectOnline"))}</button>
    </div>`;

  // ---- Özet (tüm katlar) ----
  let assigned = 0, protocol = 0, potential = 0;
  for (const id of ed.seats.keys()) {
    const c = categoryOf(id); if (!c) continue;
    assigned++;
    if (c.protocol) protocol++; else potential += c.price;
  }
  const row = (label, value, cls = "") => `<dt class="${cls}">${esc(label)}</dt><dd class="${cls}">${esc(value)}</dd>`;
  $("statSec").innerHTML = `
    <h3>${esc(t("summary"))}</h3>
    <dl class="stats">
      ${row(t("totalSeats"), ed.seats.size)}
      ${row(t("assignedSeats"), assigned)}
      ${row(t("unassignedSeats"), ed.seats.size - assigned)}
      ${row(t("protocolSeats"), protocol)}
      ${row(t("onlineSeats"), online.size)}
      ${row(t("potential"), money.format(potential), "total")}
    </dl>
    <p class="note">${esc(t("settingsNote"))}</p>
    <div class="legend"><span><i class="dot"></i>${esc(t("online"))}</span><span><i></i>${esc(t("noCategory"))}</span><span><i class="proto"></i>${esc(t("protocol"))}</span></div>`;
}

function onAction(a, b) {
  const catId = b.closest("[data-cat]")?.dataset.cat;
  if (a === "selOnline") { const on = onlineSet(); ed.selectSeats(ed.levelSeats().filter((s) => on.has(s.id)), false); }
  else if (a === "addCat") addCategory();
  else if (a === "assign") assign(catId);
  else if (a === "deleteCat") deleteCategory(catId);
  else if (a === "unassign") unassign();
  else if (a === "onlineOn") setOnline(true);
  else if (a === "onlineOff") setOnline(false);
}
function onChange(e) {
  const f = e.target.dataset.field, catId = e.target.closest("[data-cat]")?.dataset.cat;
  if (f === "showOnlinePrice") { ed.data.showOnlinePrice = e.target.checked; ed.commit(); }
  else if (f && catId) updateCategory(catId, f, e.target);
}

/* ---------- Başlangıç ---------- */
ed.init();
// Tarayıcı testleri ve hata ayıklama için: konsolda __koltuk.map ile ayarlara bakılabilir.
window.__koltuk = { get map() { return ed.data; }, selected: () => [...ed.selected], select: (ids) => ed.selectSeats(ids.map((id) => ed.seats.get(id)), false) };
})();
