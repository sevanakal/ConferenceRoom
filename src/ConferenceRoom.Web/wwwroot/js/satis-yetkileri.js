/*
  Satış yetkileri: personele koltuk verme (yönetim paneli: /yonetim/satis-yetkileri/{salon|etkinlik}/{id})
  ==================================================================================================
  Koltuk kısıtlaması olan personelin hangi koltukları satabileceğini krokiden seçtirir. Kroki, seçim, kaydetme ve
  kısayollar koltuk seçicinin ortak çatısındadır (koltuk-secici.js); burada yalnızca bu ekrana özgü olanlar var.
  Düzenlenen veri (#seatData): { schema, staff{ personelId: [koltukId...] } } (Domain/Sales/SeatScopeData.cs)
  #editorConfig.staff: satış yetkisi olan personel [{ id, name, restricted }]; restricted=false olan tam yetkilidir,
  tüm koltukları satar ve burada koltuk verilmez.
  Ekran metinleri: tr.json/en.json "seatScope" bölümü (ortaklar "seatEditor").

  Krokide: panelde seçili personelin koltukları dolu renkte, başka bir kısıtlı personele verilmiş koltuklar açık renkte,
  kimseye verilmemiş koltuklar gridir (onları yalnızca tam yetkili personel satar).

  Dosyanın bölümleri: yardımcılar → koltuk görünümü → koltuk verme/alma → panel → başlangıç
*/
(() => {
"use strict";

const hooks = { seatStyle, hoverParts, renderPanel, onAction };
const ed = KoltukSecici.create(hooks);
const { t, esc } = ed;
const $ = (id) => document.getElementById(id);

/* ---------- Yardımcılar ---------- */
const staff = ed.config.staff;
const restricted = staff.filter((s) => s.restricted);
const fullAccess = staff.filter((s) => !s.restricted);
const nameOf = new Map(staff.map((s) => [String(s.id), s.name]));
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
   Koltuk görünümü ve fare altı bilgisi (koltuğu satabilenler).
------------------------------------------------------------------ */
// Renkler css/koltuk-ayarlari.css başındaki --scope-* değişkenlerinden; tema değişince (yeni TC) yeniden okunur.
let colors = null, colorsFor = null;
function scopeColors(TC) {
  if (colorsFor !== TC) {
    const css = KrokiCizim.css;
    colors = { own: css("--scope-own"), ownText: css("--scope-own-text"), other: css("--scope-other"), otherText: css("--scope-other-text") };
    colorsFor = TC;
  }
  return colors;
}
function seatStyle(it, TC) {
  const h = holdersOf(it.id), c = scopeColors(TC);
  if (activeId && h.includes(activeId)) return { fill: c.own, text: c.ownText, stroke: "rgba(0,0,0,.35)", strokeWidth: 1.5, dash: null };
  if (h.length) return { fill: c.other, text: c.otherText, stroke: c.own, strokeWidth: 1.5, dash: null };
  return { fill: TC.seat, text: TC.ink, stroke: TC.seatStroke, strokeWidth: 1.5, dash: it.kind === "extra" ? [7, 5] : null };
}
function hoverParts(it) {
  const h = holdersOf(it.id);
  return [h.length ? t("soldBy", { names: h.map((id) => nameOf.get(id)).join(", ") }) : t("onlyFullAccess")];
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
        <button class="staff-name" data-act="activate" aria-pressed="${on}">${esc(s.name)}<span>${esc(t("seatCount", { n: seatsOf(id).length }))}</span></button>
        <button class="btn primary" data-act="give" ${hasSel ? "" : "disabled"} title="${esc(t("giveTitle"))}">${esc(t("give"))}</button>
        <button class="btn" data-act="take" ${hasSel ? "" : "disabled"} title="${esc(t("takeTitle"))}">${esc(t("take"))}</button>
      </div>`;
    }).join("")}</div>` : `<p class="note warn">${esc(t("noRestricted"))}</p>`}
    ${fullAccess.length ? `<p class="note"><b>${esc(t("fullAccess"))}:</b> ${esc(fullAccess.map((s) => s.name).join(", "))}</p>` : ""}
    <p class="note">${esc(t("restrictHint"))}</p>`;

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
    </dl>
    <div class="legend"><span><i class="own"></i>${esc(t("legendOwn"))}</span><span><i class="other"></i>${esc(t("legendOther"))}</span><span><i></i>${esc(t("legendNone"))}</span></div>`;
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
