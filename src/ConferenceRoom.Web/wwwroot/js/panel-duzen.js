/*
  Panel kart düzeni (yönetim paneli: /yonetim)
  ============================================
  Bölüm kartları sağ üstlerindeki tutamaçtan sürüklenip bırakılarak yeniden sıralanır; klavyede tutamaç seçiliyken
  ok tuşlarıyla taşınır. Bırakınca yeni sıra sunucuya kaydedilir (Pages/Yonetim/Index.cshtml.cs, OnPostOrderAsync);
  sıra kullanıcı başınadır. "Varsayılan sıraya dön" kayıtlı sırayı siler ve sayfayı yeniler.
  Fare, dokunmatik ve kalem için Pointer Events kullanılır (HTML5 sürükle-bırak dokunmatikte çalışmaz).
  Metinler sayfadan gelir (data-saved, data-failed; durum satırının ilk metni ipucudur).

  Dosyanın bölümleri: kaydetme → fareyle/dokunarak sürükleme → klavye → varsayılana dönüş
*/
(() => {
"use strict";

const grid = document.getElementById("sectionGrid");
if (!grid) return;
const status = document.getElementById("orderStatus");
const reset = document.getElementById("resetOrder");
const keys = () => [...grid.children].map((li) => li.dataset.key);

/* ---------- Kaydetme: kart anahtarları sırayla; boş dizi varsayılan sıraya döndürür ---------- */
async function save(order) {
  try {
    const res = await fetch(grid.dataset.saveUrl, {
      method: "POST",
      headers: { "Content-Type": "application/json", RequestVerificationToken: grid.dataset.token },
      body: JSON.stringify(order),
    });
    if (!res.ok) throw new Error(res.status);
    status.textContent = grid.dataset.saved;
    return true;
  } catch {
    status.textContent = grid.dataset.failed;
    return false;
  }
}
async function saveCurrent() { if (await save(keys())) reset.hidden = false; }

/* ---------- Fareyle / dokunarak sürükleme: kart imlecin altındaki kartın önüne ya da arkasına geçer ---------- */
let dragged = null, before = "";
grid.addEventListener("pointerdown", (e) => {
  const handle = e.target.closest(".drag-handle");
  if (!handle || e.button !== 0) return;
  e.preventDefault();
  dragged = handle.closest(".section-card");
  before = keys().join();
  dragged.classList.add("dragging");
  grid.classList.add("sorting");
  handle.setPointerCapture(e.pointerId);
});
grid.addEventListener("pointermove", (e) => {
  if (!dragged) return;
  const over = document.elementFromPoint(e.clientX, e.clientY)?.closest(".section-card");
  if (!over || over === dragged || over.parentElement !== grid) return;
  // Hedef sürüklenen kartın arkasındaysa onun arkasına, önündeyse önüne geçilir (kartlar yer değiştirir gibi kayar).
  const items = [...grid.children];
  grid.insertBefore(dragged, items.indexOf(over) > items.indexOf(dragged) ? over.nextSibling : over);
});
function endDrag() {
  if (!dragged) return;
  dragged.classList.remove("dragging");
  grid.classList.remove("sorting");
  dragged = null;
  if (keys().join() !== before) saveCurrent();
}
grid.addEventListener("pointerup", endDrag);
grid.addEventListener("pointercancel", endDrag);

/* ---------- Klavye: tutamaçta sol/yukarı bir öne, sağ/aşağı bir arkaya ---------- */
grid.addEventListener("keydown", (e) => {
  const handle = e.target.closest(".drag-handle");
  if (!handle) return;
  const card = handle.closest(".section-card");
  const back = e.key === "ArrowLeft" || e.key === "ArrowUp", forward = e.key === "ArrowRight" || e.key === "ArrowDown";
  if (!back && !forward) return;
  e.preventDefault();
  const target = back ? card.previousElementSibling : card.nextElementSibling;
  if (!target) return;
  grid.insertBefore(card, back ? target : target.nextSibling);
  handle.focus();
  saveCurrent();
});

/* ---------- Varsayılan sıraya dönüş ---------- */
reset?.addEventListener("click", async () => { if (await save([])) location.reload(); });
})();
