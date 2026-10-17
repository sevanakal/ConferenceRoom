/*
  Satış bildirimleri (yönetim panelinin her sayfası, _AdminLayout.cshtml)
  =======================================================================
  "Satış bildirimlerini al" yetkisi olan kullanıcının panelinde çalışır. Sunucuyla canlı bağlantı kurar (SignalR,
  Sales/SalesNotifier.cs içindeki NotificationHub); bir satış ya da rezervasyon olunca sunucu "notify" ile bildirimi yollar:
    - üst çubuktaki zilin okunmamış sayısı artar,
    - sağ altta birkaç saniye görünen kısa bir kutu çıkar,
    - bildirim listesi sayfası açıksa liste başına eklenir,
    - kullanıcı izin verdiyse tarayıcı bildirimi de gösterilir (sekme arka plandayken işe yarar).
  Metinler ve ayarlar sayfadaki #notifyConfig JSON'undan gelir. Liste satırının yapısı _NotificationItem.cshtml ile aynıdır.

  Dosyanın bölümleri: ayarlar ve biçimleme → bildirimi çizme → canlı bağlantı → tarayıcı bildirimi izni
*/
(() => {
"use strict";

// ---- Ayarlar ve biçimleme ----
const configEl = document.getElementById("notifyConfig");
if (!configEl || !window.signalR) return;
const config = JSON.parse(configEl.textContent);
const T = config.texts;
const fmt = (text, ...args) => text.replace(/\{(\d+)\}/g, (_, i) => args[Number(i)] ?? "");
const zone = { timeZone: "Europe/Istanbul" };
const utc = (value) => new Date(/[zZ]|[+-]\d\d:\d\d$/.test(value) ? value : value + "Z");
const time = (value) => utc(value).toLocaleString(config.locale, { ...zone, dateStyle: "short", timeStyle: "short" });
const money = (value) => fmt(T.money, Number(value).toLocaleString(config.locale, { maximumFractionDigits: 2 }));
const isSale = (item) => item.kind === 0 || item.kind === "Sale";

/** Bildirimin başlığı ve ayrıntı satırı (sunucudaki _NotificationItem.cshtml ile aynı metinler). */
function describe(item) {
  return {
    headline: fmt(isSale(item) ? T.sold : T.reserved, item.seatCount),
    detail: fmt(T.detail, item.eventTitle, time(item.sessionStartsAtUtc), item.buyerName, item.sellerName ?? T.online),
    url: config.canView ? `/yonetim/satislar/seans/${item.sessionId}#islem-${item.orderId}` : config.listUrl,
  };
}

// ---- Bildirimi çizme: zil sayısı, köşe kutusu, liste ----
const count = document.getElementById("notifyCount");
const bell = document.getElementById("notifyBell");
const toasts = document.getElementById("notifyToasts");
const list = document.getElementById("notifyList");

function bump() {
  const now = (parseInt(count.textContent, 10) || 0) + 1;
  count.textContent = now > 99 ? "99+" : String(now);
  count.hidden = false;
  bell.setAttribute("aria-label", fmt(T.bell, now));
}

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function showToast(item, d) {
  const box = el("a", "notify-toast");
  box.href = d.url;
  box.append(el("b", "", d.headline + (item.total > 0 ? ` · ${money(item.total)}` : "")), el("span", "", d.detail));
  toasts.append(box);
  setTimeout(() => box.remove(), 8000);
}

function addToList(item, d) {
  const li = el("li", "notify-item");
  li.dataset.unread = "true";
  const head = el("div", "notify-item__head");
  if (config.canView) {
    const link = el("a", "row-title", d.headline);
    link.href = d.url;
    head.append(link);
  } else {
    head.append(el("b", "", d.headline));
  }
  if (item.total > 0) head.append(el("span", "muted", money(item.total)));
  const stamp = el("time", "notify-item__time", time(item.createdAtUtc));
  stamp.dateTime = utc(item.createdAtUtc).toISOString();
  head.append(stamp);
  li.append(head, el("div", "row-sub", d.detail));
  list.prepend(li);
  document.getElementById("notifyEmpty")?.remove();
}

function showBrowser(d) {
  if (!("Notification" in window) || Notification.permission !== "granted" || !document.hidden) return;
  const n = new Notification(d.headline, { body: d.detail, tag: "konferans-satis" });
  n.onclick = () => { window.focus(); location.href = d.url; };
}

// ---- Canlı bağlantı ----
const conn = new signalR.HubConnectionBuilder().withUrl(config.hubUrl).withAutomaticReconnect().build();
conn.on("notify", (item) => {
  const d = describe(item);
  bump();
  if (list) addToList(item, d);
  else showToast(item, d);
  showBrowser(d);
});
conn.start().catch(() => { /* canlı bağlantı yok: bildirimler sayfa yenilenince görünür */ });

// ---- Tarayıcı bildirimi izni (yalnızca bildirim listesi sayfasındaki düğmeyle istenir) ----
const ask = document.getElementById("browserNotify");
if (ask && "Notification" in window) {
  const refresh = () => {
    ask.hidden = Notification.permission === "granted";
    ask.disabled = Notification.permission === "denied";
    if (Notification.permission === "denied") { ask.hidden = false; ask.textContent = T.denied; }
  };
  refresh();
  ask.addEventListener("click", async () => {
    await Notification.requestPermission();
    refresh();
    if (Notification.permission === "granted") ask.insertAdjacentElement("afterend", el("span", "muted", T.enabled));
  });
}
})();
