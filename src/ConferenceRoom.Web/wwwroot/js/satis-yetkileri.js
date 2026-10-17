/*
  Satış yetkileri: personele koltuk verme (yönetim paneli: /yonetim/satis-yetkileri/{salon|etkinlik}/{id})
  ==================================================================================================
  Koltuk kısıtlaması olan personelin hangi koltukları satabileceğini krokiden seçtirir. Kroki, seçim, kaydetme ve
  kısayollar koltuk seçicinin ortak çatısındadır (koltuk-secici.js); burada yalnızca bu ekrana özgü olanlar var.
  Düzenlenen veri (#seatData): { schema, staff{ personelId: [koltukId...] } } (Domain/Sales/SeatScopeData.cs)
  #editorConfig.staff: satış yetkisi olan personel [{ id, name, restricted }]; restricted=false olan tam yetkilidir,
  tüm koltukları satar ve burada koltuk verilmez.
  #editorConfig.categoryEvents: koltuk ayarı yapılmış etkinlikler [{ id, title, map }] (map: Domain/Events/SeatMap.cs).
  Ekran metinleri: tr.json/en.json "seatScope" bölümü (ortaklar "seatEditor").

  Krokide: koltuk, panelde seçilen etkinliğin kategori rengindedir (fiyatlandırma ekranındaki gibi; yalnızca görünüm,
  kaydedilmez). Koltuğu satabilen personel koltuğun üstünde, personelin rengindeki küçük etiketle baş harfleriyle
  gösterilir; panelde seçili personelinki tam renkli, diğerleri soluktur. Etiketi olmayan koltuğu yalnızca tam yetkili
  personel satar. Böylece kategori rengi ile personel rengi karışmaz.

  Dosyanın bölümleri: yardımcılar → kategoriler → koltuk görünümü → koltuk verme/alma → panel → başlangıç
*/
(() => {
"use strict";

const hooks = { seatStyle, seatBadge, hoverParts, renderPanel, onAction, onChange };
const ed = KoltukSecici.create(hooks);
const { t, esc, locale } = ed;
const $ = (id) => document.getElementById(id);

/* ---------- Yardımcılar ---------- */
const staff = ed.config.staff;
const restricted = staff.filter((s) => s.restricted);
const fullAccess = staff.filter((s) => !s.restricted);
const nameOf = new Map(staff.map((s) => [String(s.id), s.name]));
/* Personelin rengi: css/koltuk-ayarlari.css başındaki --staff-1..8 değişkenleri, listedeki sırasına göre dönerek. */
const STAFF_COLORS = 8;
const colorIndex = new Map(restricted.map((s, i) => [String(s.id), (i % STAFF_COLORS) + 1]));
const colorVar = (id) => `var(--staff-${colorIndex.get(id)})`;
let colors = null, colorsFor = null;
// Kroki için değerleri okunur; tema değişince (yeni TC) yeniden okunur.
function staffColor(id, TC) {
  if (colorsFor !== TC) { colors = new Map(); colorsFor = TC; }
  if (!colors.has(id)) colors.set(id, KrokiCizim.css(`--staff-${colorIndex.get(id)}`));
  return colors.get(id);
}
/* Baş harfler: ilk ve son kelimenin ilk harfi (Ayşe Yılmaz → AY); tek kelimede ilk iki harf. */
function initials(name) {
  const words = name.trim().split(/\s+/).filter(Boolean);
  const text = words.length > 1 ? words[0][0] + words[words.length - 1][0] : (words[0] ?? "?").slice(0, 2);
  return text.toLocaleUpperCase(locale);
}
const initialsOf = new Map(staff.map((s) => [String(s.id), initials(s.name)]));
let activeId = restricted.length ? String(restricted[0].id) : null;   // panelde seçili personel (veri değil, kaydedilmez)

const seatsOf = (id) => ed.data.staff[id] ?? [];
/* Koltuk → onu satabilen kısıtlı personel. Yalnızca ekranda listelenen personel sayılır. */
function holders() {
  const map = new Map();
  for (const s of restricted) for (const seat of seatsOf(String(s.id))) {
    if (!map.has(seat)) map.set(seat, []);
    map.get(seat).push(String(s.id));
  }
  return map;
}
/* Önbellek: veri nesnesi değişince (geri al/yinele yeni nesne üretir) ya da change() sonrası yeniden hesaplanır. */
let holderCache = null, cacheData = null;
function holdersOf(seatId) {
  if (!holderCache || cacheData !== ed.data) { holderCache = holders(); cacheData = ed.data; }
  return holderCache.get(seatId) ?? [];
}

/* ------------------------------------------------------------------
   Kategoriler: koltuk renkleri seçilen etkinliğin koltuk ayarından gelir (ilki varsayılan). Seçim yalnızca görünüm içindir.
------------------------------------------------------------------ */
const categoryEvents = ed.config.categoryEvents;
let categoryEventId = categoryEvents.length ? categoryEvents[0].id : null;
let categoryMap = null;
function useCategoryEvent(id) {
  categoryEventId = id;
  const map = categoryEvents.find((e) => e.id === id)?.map;
  categoryMap = map ? { byId: new Map(map.categories.map((c) => [c.id, c])), seats: map.seats } : null;
}
useCategoryEvent(categoryEventId);
const categoryOf = (seatId) => categoryMap?.byId.get(categoryMap.seats[seatId]);
/* Kategorinin fiyatı: fiyatı 0 ise "Rezervasyon", protokolde "Satışa kapalı". */
const money = new Intl.NumberFormat(locale, { style: "currency", currency: "TRY", maximumFractionDigits: 2 });
const priceText = (c) => (c.protocol ? t("notForSale") : c.price ? money.format(c.price) : t("reservation"));

/* ------------------------------------------------------------------
   Koltuk görünümü: renk kategoriden (kategorisiz gri, protokol kesik kenarlı), üstte satabilen personelin etiketi.
   Fare altında kategori ve satabilenlerin adları.
------------------------------------------------------------------ */
function seatStyle(it, TC) {
  const c = categoryOf(it.id);
  return c
    ? { fill: c.color, text: "#FFFFFF", stroke: c.protocol ? TC.ink : "rgba(0,0,0,.35)", strokeWidth: c.protocol ? 3 : 1.5, dash: c.protocol ? [6, 4] : null }
    : { fill: TC.seat, text: TC.ink, stroke: TC.seatStroke, strokeWidth: 1.5, dash: it.kind === "extra" ? [7, 5] : null };
}
/* Etiket: seçili personel koltuğu satabiliyorsa onun, yoksa ilk satabilenin baş harfleri; birden çok kişi varsa "+n". */
function seatBadge(it, TC) {
  const h = holdersOf(it.id);
  if (!h.length) return null;
  const own = activeId && h.includes(activeId), id = own ? activeId : h[0];
  return { text: initialsOf.get(id) + (h.length > 1 ? `+${h.length - 1}` : ""), color: staffColor(id, TC), faded: !own };
}
function hoverParts(it) {
  const h = holdersOf(it.id), c = categoryOf(it.id);
  return [c ? `${c.name} · ${priceText(c)}` : null, h.length ? t("soldBy", { names: h.map((id) => nameOf.get(id)).join(", ") }) : t("onlyFullAccess")];
}

/* ------------------------------------------------------------------
   Koltuk verme / alma: seçili koltuklar bir personele verilir ya da ondan alınır. Boş kalan personel veriden çıkar.
------------------------------------------------------------------ */
function change(staffId, give) {
  const selected = ed.selected;
  if (!selected.size) return ed.toast(t("needSelection"));
  const set = new Set(seatsOf(staffId));
  selected.forEach((id) => (give ? set.add(id) : set.delete(id)));
  if (set.size) ed.data.staff[staffId] = [...set];
  else delete ed.data.staff[staffId];
  activeId = staffId;
  holderCache = null;
  ed.commit();
  ed.toast(t(give ? "givenToast" : "takenToast", { n: selected.size, name: nameOf.get(staffId) }));
}
function activate(staffId) { activeId = staffId; ed.render(); }

/* ------------------------------------------------------------------
   Panel: personel listesi (kısıtlı olanlar düzenlenir, tam yetkililer bilgi olarak), özet.
------------------------------------------------------------------ */
function renderPanel() {
  const hasSel = ed.selected.size > 0;

  // ---- Personel ----
  $("staffSec").innerHTML = `
    <h3>${esc(t("staffTitle"))}</h3>
    <p class="note">${esc(t("staffHint"))}</p>
    ${restricted.length ? `<div class="staff-list">${restricted.map((s) => {
      const id = String(s.id), on = id === activeId;
      return `
      <div class="staff${on ? " on" : ""}" data-staff="${id}">
        <button class="staff-name" data-act="activate" aria-pressed="${on}"><i class="swatch" style="background:${colorVar(id)}">${esc(initialsOf.get(id))}</i><b>${esc(s.name)}</b><span>${esc(t("seatCount", { n: seatsOf(id).length }))}</span></button>
        <button class="btn primary" data-act="give" ${hasSel ? "" : "disabled"} title="${esc(t("giveTitle"))}">${esc(t("give"))}</button>
        <button class="btn" data-act="take" ${hasSel ? "" : "disabled"} title="${esc(t("takeTitle"))}">${esc(t("take"))}</button>
      </div>`;
    }).join("")}</div>` : `<p class="note warn">${esc(t("noRestricted"))}</p>`}
    ${fullAccess.length ? `<p class="note"><b>${esc(t("fullAccess"))}:</b> ${esc(fullAccess.map((s) => s.name).join(", "))}</p>` : ""}
    <p class="note">${esc(t("restrictHint"))}</p>`;

  // ---- Kategoriler (görünüm) ----
  const shown = categoryEvents.find((e) => e.id === categoryEventId);
  $("catSec").innerHTML = `
    <h3>${esc(t("categoriesTitle"))}</h3>
    ${categoryEvents.length > 1 ? `<label class="fld"><span>${esc(t("categoryEvent"))}</span><select data-field="categoryEvent">${categoryEvents.map((e) =>
      `<option value="${e.id}" ${e.id === categoryEventId ? "selected" : ""}>${esc(e.title)}</option>`).join("")}</select></label>` : ""}
    ${shown && shown.map.categories.length
      ? `<ul class="cat-legend">${shown.map.categories.map((c) => `<li><i class="${c.protocol ? "proto" : ""}" style="background:${esc(c.color)}"></i><span>${esc(c.name)}</span><b>${esc(priceText(c))}</b></li>`).join("")}<li><i></i><span>${esc(t("noCategory"))}</span></li></ul>
         ${categoryEvents.length > 1 ? `<p class="note">${esc(t("categoryPreviewNote"))}</p>` : ""}`
      : `<p class="note">${esc(t(ed.config.isEvent ? "noCategoriesEvent" : "noCategoriesHall"))}</p>`}`;

  // ---- Özet (tüm katlar) ----
  let given = 0;
  for (const id of ed.seats.keys()) if (holdersOf(id).length) given++;
  const row = (label, value) => `<dt>${esc(label)}</dt><dd>${esc(value)}</dd>`;
  $("statSec").innerHTML = `
    <h3>${esc(t("summary"))}</h3>
    <dl class="stats">
      ${row(t("totalSeats"), ed.seats.size)}
      ${row(t("givenSeats"), given)}
      ${row(t("notGivenSeats"), ed.seats.size - given)}
    </dl>`;
}

function onChange(e) {
  if (e.target.dataset.field === "categoryEvent") { useCategoryEvent(Number(e.target.value)); ed.render(); }
}
function onAction(a, b) {
  const staffId = b.closest("[data-staff]")?.dataset.staff;
  if (a === "activate") activate(staffId);
  else if (a === "give") change(staffId, true);
  else if (a === "take") change(staffId, false);
}

/* ---------- Toplu seçim düğmeleri: panelde seçili personelin koltukları, kimseye verilmemiş koltuklar ---------- */
hooks.quickSelect = [
  { act: "own", label: t("selectOwn"), seats: () => ed.levelSeats().filter((s) => activeId && holdersOf(s.id).includes(activeId)) },
  { act: "free", label: t("selectNotGiven"), seats: () => ed.levelSeats().filter((s) => !holdersOf(s.id).length) },
];

/* ---------- Başlangıç ---------- */
ed.init();
// Tarayıcı testleri ve hata ayıklama için.
window.__kapsam = { get data() { return ed.data; }, select: (ids) => ed.selectSeats(ids.map((id) => ed.seats.get(id)), false) };
})();
