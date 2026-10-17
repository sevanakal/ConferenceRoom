/*
  Personel satış ekranı (satış bölümü: /satis/seans/{seansId})
  =============================================================
  Krokiden koltuk seçilir, alıcının adı (zorunlu), telefonu ve açıklaması girilir; toplam 0'dan büyükse ödemenin alınıp
  alınmadığı seçilir, sonra "Sat" (rezervasyonlu etkinlikte "Rezervasyon yap"). Kroki, seçim, yakınlaştırma ve kat sekmeleri
  koltuk seçicinin ortak çatısındadır (koltuk-secici.js, yalnızca seçtiren kip); burada yalnızca bu ekrana özgü olanlar var.
  Kurallar sunucuda yeniden denetlenir (Web/Sales/SalesDesk.cs); fiyat sunucuda kategoriden hesaplanır.

  Veri (#seatData): { map: etkinliğin koltuk ayarları (Domain/Events/SeatMap.cs), sold: [satılmış koltuk id],
                      allowed: [personelin satabileceği koltuk id] ya da null (tümü) }
  Ekran metinleri: tr.json/en.json "seatEditor" (ortak) ve "sales" bölümleri.

  Dosyanın bölümleri: yardımcılar → koltuk durumu ve görünümü → panel → satış → başlangıç
*/
(() => {
"use strict";

const hooks = { seatStyle, selectable, hoverParts, renderPanel, stateText, onAction };
const ed = KoltukSecici.create(hooks);
const { t, esc, locale, config } = ed;
const $ = (id) => document.getElementById(id);

/* ---------- Yardımcılar ---------- */
const money = new Intl.NumberFormat(locale, { style: "currency", currency: "TRY", maximumFractionDigits: 2 });
const isSale = config.kind === "Sale";
const categoryById = (id) => ed.data.map.categories.find((c) => c.id === id);
const categoryOf = (seatId) => categoryById(ed.data.map.seats[seatId]);
const soldSet = () => new Set(ed.data.sold);
const allowedSet = () => (ed.data.allowed ? new Set(ed.data.allowed) : null);
/* Fiyatı 0 olan kategori "Rezervasyon" diye görünür (koltuk ayarlarındaki kural). */
const priceText = (price) => (price ? money.format(price) : t("reservation"));

/* ------------------------------------------------------------------
   Koltuk durumu: sold (satılmış/rezerve), closed (kategorisi yok ya da protokol), scope (personelin kapsamı dışında),
   free (satılabilir). Satılabilir koltuk kategorisinin renginde, satılmış koltuk koyu, diğerleri soluk ve kesik kenarlıdır.
------------------------------------------------------------------ */
function stateOf(id) {
  if (soldSet().has(id)) return "sold";
  const c = categoryOf(id);
  if (!c || c.protocol) return "closed";
  const allowed = allowedSet();
  if (allowed && !allowed.has(id)) return "scope";
  return "free";
}
function seatStyle(it, TC) {
  const state = stateOf(it.id);
  if (state === "free") { const c = categoryOf(it.id); return { fill: c.color, text: "#FFFFFF", stroke: "rgba(0,0,0,.35)", strokeWidth: 1.5 }; }
  if (state === "sold") return { fill: TC.ink, text: TC.paper, stroke: TC.ink, strokeWidth: 1.5 };
  return { fill: TC.seat, text: TC.muted, stroke: TC.seatStroke, strokeWidth: 1.5, dash: [6, 4] };
}
function selectable(it) { return config.isOpen && stateOf(it.id) === "free"; }
function hoverParts(it) {
  const state = stateOf(it.id), c = categoryOf(it.id);
  if (state === "free") return [c.name, priceText(c.price)];
  return [t(state === "sold" ? (isSale ? "stateSold" : "stateReserved") : state === "scope" ? "stateScope" : "stateClosed")];
}
function stateText() {
  const total = ed.seats.size, sold = ed.data.sold.length;
  return t("occupancy", { sold, total });
}

/* ------------------------------------------------------------------
   Panel: renk açıklaması (kategoriler, boş koltuk sayıları) ve satış formu. Panel her seçimde yeniden yazıldığı için
   formdaki değerler "form" nesnesinde tutulur.
------------------------------------------------------------------ */
const form = { buyerName: "", buyerPhone: "", note: "", paid: null };
let busy = false;

function selectedSeats() { return [...ed.selected].map((id) => ed.seats.get(id)).filter(Boolean); }
function totalOf(list) { return list.reduce((sum, s) => sum + categoryOf(s.id).price, 0); }

function renderPanel() {
  renderLegend();
  renderSell();
}

function renderLegend() {
  const states = new Map([...ed.seats.keys()].map((id) => [id, stateOf(id)]));
  const freeIn = (c) => [...states].filter(([id, st]) => st === "free" && ed.data.map.seats[id] === c.id).length;
  const rows = ed.data.map.categories.filter((c) => !c.protocol).map((c) => `
    <li><i style="background:${esc(c.color)}"></i><span>${esc(c.name)}</span><b>${esc(priceText(c.price))}</b><em>${esc(t("freeCount", { n: freeIn(c) }))}</em></li>`).join("");
  $("legendSec").innerHTML = `
    <h3>${esc(t("legend"))}</h3>
    ${rows ? `<ul class="cat-legend sales-legend">${rows}</ul>` : `<p class="note">${esc(t("noCategories"))}</p>`}
    <ul class="cat-legend sales-legend">
      <li><i class="sold"></i><span>${esc(t(isSale ? "stateSold" : "stateReserved"))}</span></li>
      <li><i class="off"></i><span>${esc(t(ed.data.allowed ? "stateClosedOrScope" : "stateClosed"))}</span></li>
    </ul>`;
}

function renderSell() {
  if (!config.isOpen) { $("sellSec").innerHTML = `<h3>${esc(t(isSale ? "sellTitle" : "reserveTitle"))}</h3><p class="note">${esc(t("closed"))}</p>`; return; }
  const list = selectedSeats();
  const total = list.length ? totalOf(list) : 0;
  const needsPayment = total > 0;
  // Kategoriye göre özet: "2 × 1. Kategori  1.000 ₺"
  const groups = new Map();
  list.forEach((s) => { const c = categoryOf(s.id); const g = groups.get(c.id) ?? { c, seats: [] }; g.seats.push(s); groups.set(c.id, g); });
  const summary = [...groups.values()].map(({ c, seats }) => `
    <li><i style="background:${esc(c.color)}"></i><span>${seats.length} × ${esc(c.name)}</span><b>${esc(priceText(c.price * seats.length))}</b></li>`).join("");
  const labels = list.map((s) => KrokiCizim.seatLabel(s)).sort((a, b) => a.localeCompare(b, locale, { numeric: true })).join(", ");
  const L = config.limits;
  $("sellSec").innerHTML = `
    <h3>${esc(t(isSale ? "sellTitle" : "reserveTitle"))}</h3>
    ${list.length ? `
      <p class="sel-seats">${esc(labels)}</p>
      <ul class="cat-legend">${summary}</ul>
      <p class="total"><span>${esc(t("total"))}</span><b>${esc(priceText(total))}</b></p>` : `<p class="note">${esc(t("pickSeats"))}</p>`}
    <form id="sellForm" class="sell-form" novalidate>
      <label class="fld"><span>${esc(t("buyerName"))} *</span><input name="buyerName" maxlength="${L.name}" autocomplete="off" required value="${esc(form.buyerName)}"></label>
      <label class="fld"><span>${esc(t("buyerPhone"))}</span><input name="buyerPhone" type="tel" maxlength="${L.phone}" autocomplete="off" value="${esc(form.buyerPhone)}"></label>
      <label class="fld"><span>${esc(t("note"))}</span><textarea name="note" rows="2" maxlength="${L.note}">${esc(form.note)}</textarea></label>
      ${needsPayment ? `
      <fieldset class="pay">
        <legend>${esc(t("payment"))} *</legend>
        <label><input type="radio" name="paid" value="true" ${form.paid === true ? "checked" : ""}> ${esc(t("paid"))}</label>
        <label><input type="radio" name="paid" value="false" ${form.paid === false ? "checked" : ""}> ${esc(t("unpaid"))}</label>
      </fieldset>` : ""}
      <button type="submit" class="btn primary sell-btn" ${list.length && !busy ? "" : "disabled"}>${esc(busy ? t("working") : t(isSale ? "sell" : "reserve"))}</button>
    </form>`;
}

/* Formdaki her değişiklik "form" nesnesine yazılır; panel yeniden çizilince değerler korunur. */
document.addEventListener("input", (e) => {
  const f = e.target.closest?.("#sellForm"); if (!f) return;
  const { name, value } = e.target;
  if (name === "paid") form.paid = value === "true";
  else if (name in form) form[name] = value;
});
document.addEventListener("submit", (e) => {
  if (e.target.id !== "sellForm") return;
  e.preventDefault();
  sell();
});
function onAction() { /* panelde bu ekrana ait data-act düğmesi yok */ }

/* ------------------------------------------------------------------
   Satış: sunucuya gönderilir; cevapta seansın güncel satılmış koltukları gelir. Başarılıysa seçim ve form temizlenir,
   başkası aynı koltuğu az önce sattıysa o koltuklar seçimden düşer.
------------------------------------------------------------------ */
async function sell() {
  const list = selectedSeats();
  if (busy || !list.length) return;
  if (!form.buyerName.trim()) { ed.toast(t("errorBuyerName")); document.querySelector("#sellForm [name=buyerName]")?.focus(); return; }
  if (totalOf(list) > 0 && form.paid === null) { ed.toast(t("errorPayment")); return; }
  busy = true; renderSell();
  let message = t("failed");
  try {
    const res = await fetch(config.sellUrl, {
      method: "POST",
      headers: { "Content-Type": "application/json", "RequestVerificationToken": config.token },
      body: JSON.stringify({ seats: list.map((s) => s.id), buyerName: form.buyerName, buyerPhone: form.buyerPhone, note: form.note, paid: form.paid }),
    });
    const r = await res.json().catch(() => ({}));
    if (r.message) message = r.message;
    if (Array.isArray(r.sold)) ed.data.sold = r.sold;
    if (r.status === "sold") Object.assign(form, { buyerName: "", buyerPhone: "", note: "", paid: null });
    if (r.status === "sold" || r.status === "taken") {
      const sold = soldSet();
      ed.selected.forEach((id) => { if (sold.has(id)) ed.selected.delete(id); });
    }
  } catch (e) { /* bağlantı hatası: varsayılan mesaj */ }
  busy = false;
  ed.render();
  ed.toast(message, 6000);
}

/* ---------- Başlangıç ---------- */
ed.init();
})();
