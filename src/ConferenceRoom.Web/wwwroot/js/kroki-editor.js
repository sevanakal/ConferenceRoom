/*
  Salon kroki editörü (yönetim paneli: /yonetim/salonlar/{id}/kroki)
  ===================================================================
  Konferans salonunun 2B krokisini çizen editör. Çizim Konva.js ile yapılır (wwwroot/lib/konva).
  Sayfa (Pages/Yonetim/Salonlar/Kroki.cshtml) iki JSON bloğu verir:
    #editorConfig : salon adı, güncel sürüm numarası, kayıt adresi, dil, editör metinleri (tr.json/en.json "planEditor" bölümü)
    #planData     : açılacak kroki verisi (güncel sürüm, istenen eski sürüm ya da boş kroki)
  "Kaydet" krokiyi sunucuya gönderir; sunucu denetleyip yeni sürüm olarak saklar (Web/Halls/HallPlanStore.cs).

  Dosyanın bölümleri:
    dil ve ayarlar → sabitler/durum → geometri → veri modeli → geri al/yinele → Konva çizimi → etkileşim →
    araçlar/panel → kaydetme → kısayollar → başlangıç
*/
(() => {
"use strict";

/* ------------------------------------------------------------------
   Dil ve ayarlar. Metinler sayfanın dilinde, sunucudan gelir (anahtarlar "planEditor." önekisiz).
   t("tool.select"), t("saved", { n: 3 }) → "{n}" yerine 3 yazılır.
------------------------------------------------------------------ */
const CONFIG = JSON.parse(document.getElementById("editorConfig").textContent);
const TEXTS = CONFIG.texts;
const lang = CONFIG.culture;
const numberLocale = lang === "tr" ? "tr-TR" : "en-GB";
function t(key, vars) {
  let s = TEXTS[key] ?? key;
  if (vars) for (const k in vars) s = s.split("{" + k + "}").join(vars[k]);
  return s;
}

/* ------------------------------------------------------------------
   Sabitler ve durum. Tüm ölçüler santimetredir (1 birim = 1 cm).
   doc: krokinin tamamı (kaydedilen veri). selected: seçili öğe id'leri.
   opts: araç seçenek çubuğundaki değerler.
------------------------------------------------------------------ */
const SEAT_W = 48, SEAT_H = 44;               // cm
const SNAP = 10;                              // cm
const BLOCK_COLORS = ["#0F766E", "#B4531A", "#6D3FC0", "#1D5FBF", "#B0306A", "#4D7C0F", "#0E7490", "#8A5A12"];
const RESIZABLE = new Set(["stage", "area", "wall", "door", "column"]);

const opts = { gap: 55, rowGap: 92, curve: 0, nextRow: "A", numbering: "seq", dir: "ltr", kind: "normal", snap: true };
let doc, levelId, tool = "select";
let selected = new Set();
let undoStack = [], redoStack = [], lastSnap = "";
let clipboard = null;

const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? "").replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
const uid = () => "i" + Math.random().toString(36).slice(2, 9);
const snapV = (v) => (opts.snap ? Math.round(v / SNAP) * SNAP : Math.round(v));
const css = (name) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

/* ---------- Sıra harfleri: A..Z, sonra AA, AB.. ve koltuk numara dizileri (sıralı/tek/çift) ---------- */
function rowToIndex(s) { s = String(s || "A").toUpperCase().replace(/[^A-Z]/g, "") || "A"; let n = 0; for (const c of s) n = n * 26 + (c.charCodeAt(0) - 64); return n - 1; }
function indexToRow(i) { let s = ""; i += 1; while (i > 0) { const m = (i - 1) % 26; s = String.fromCharCode(65 + m) + s; i = Math.floor((i - 1) / 26); } return s; }
function nextRowLabel(s) { return /^[A-Za-z]+$/.test(s) ? indexToRow(rowToIndex(s) + 1) : s; }

function numberSeq(n, pattern, start = 1) {
  const out = [];
  for (let i = 0; i < n; i++) {
    if (pattern === "odd") out.push((start % 2 ? start : start + 1) + 2 * i);
    else if (pattern === "even") out.push((start % 2 ? start + 1 : start) + 2 * i);
    else out.push(start + i);
  }
  return out;
}

/* ---------- Geometri: düz ya da kavisli bir sıra boyunca koltuk konumları ----------
   p0→p1 sıranın kirişidir. curve (cm) kirişin ortasının ne kadar yay yapacağıdır (sagitta).
   Koltuklar yay boyunca 'gap' aralıkla dizilir; her koltuğun açısı yayın teğetidir, böylece
   koltuklar yayın merkezine (sahneye) bakar. Yerel eksende izleyici −y yönüne bakar. */
function rowSeatPositions(p0, p1, gap, curve) {
  const dx = p1.x - p0.x, dy = p1.y - p0.y;
  const c = Math.hypot(dx, dy);
  const th = Math.atan2(dy, dx);
  const cos = Math.cos(th), sin = Math.sin(th);
  const local = [];
  let s = Math.max(-c / 2, Math.min(c / 2, curve));
  if (c < 1) return [{ x: p0.x, y: p0.y, rot: 0 }];
  if (Math.abs(s) < 1) {
    const n = Math.floor(c / gap + 1e-6) + 1;
    for (let k = 0; k < n; k++) local.push({ x: k * gap, y: 0, r: 0 });
  } else {
    const sa = Math.abs(s), sign = Math.sign(s);
    const R = (c * c / 4 + sa * sa) / (2 * sa);
    const alpha = Math.asin(Math.min(1, (c / 2) / R));
    const L = 2 * R * alpha;
    const n = Math.floor(L / gap + 1e-6) + 1;
    const cy = sa - R;
    for (let k = 0; k < n; k++) {
      const phi = Math.PI / 2 + alpha - (k * gap) / R;
      const x = c / 2 + R * Math.cos(phi), y = cy + R * Math.sin(phi);
      const r = Math.atan2(-Math.cos(phi), Math.sin(phi));
      local.push({ x, y: y * sign, r: r * sign });
    }
  }
  return local.map((p) => ({ x: p0.x + p.x * cos - p.y * sin, y: p0.y + p.x * sin + p.y * cos, rot: ((th + p.r) * 180) / Math.PI }));
}

function makeRow(p0, p1, o) {
  const pts = rowSeatPositions(p0, p1, o.gap, o.curve || 0);
  let nums = numberSeq(pts.length, o.numbering, o.start || 1);
  if (o.dir === "rtl") nums = nums.reverse();
  return pts.map((p, i) => ({ id: uid(), levelId: o.levelId, type: "seat", x: Math.round(p.x), y: Math.round(p.y), rotation: +p.rot.toFixed(2), row: o.row, num: nums[i], kind: o.kind || "normal", blockId: o.blockId || null, groupId: o.groupId || null }));
}

/* ---------- Belge (kroki verisi) ----------
   Veri modeli (ileride veritabanı tablolarına dönüşecek):
     levels[]  : katlar (Parter, Balkon...)            → id, name
     blocks[]  : koltuk blokları, kata bağlı           → id, levelId, name, color
     items[]   : çizimdeki her nesne                   → id, levelId, type, x, y, rotation, ...
       type = seat   → row, num, kind(normal|vip|accessible|extra), blockId, groupId
       type = stage|area|wall|door|column → w, h (+ label, color, kind)
       type = text   → label, size
   x,y nesnenin merkezidir. groupId Photoshop'taki grup gibi birlikte seçilen öğeleri bağlar. */
function blankDoc() {
  const lv = { id: uid(), name: t("defaultLevel") };
  return { schema: 1, unit: "cm", name: CONFIG.hallName, levels: [lv], blocks: [], items: [] };
}

function sampleDoc() {
  const d = { schema: 1, unit: "cm", name: CONFIG.hallName, levels: [], blocks: [], items: [] };
  const P = { id: uid(), name: t("defaultLevel") }, B = { id: uid(), name: t("sample.balcony") };
  d.levels.push(P, B);
  const blk = (levelId, name, color) => { const b = { id: uid(), levelId, name, color }; d.blocks.push(b); return b.id; };
  const add = (it) => d.items.push(Object.assign({ id: uid(), rotation: 0 }, it));

  // Parter: duvarlar, sahne, kapılar
  add({ levelId: P.id, type: "wall", x: 1200, y: 0, w: 2420, h: 20 });
  add({ levelId: P.id, type: "wall", x: 0, y: 1300, w: 20, h: 2600 });
  add({ levelId: P.id, type: "wall", x: 2400, y: 1300, w: 20, h: 2600 });
  add({ levelId: P.id, type: "wall", x: 1200, y: 2600, w: 2420, h: 20 });
  add({ levelId: P.id, type: "stage", x: 1200, y: 230, w: 1500, h: 420, label: t("stageLbl") });
  add({ levelId: P.id, type: "door", x: 520, y: 2590, w: 160, h: 14, kind: "door", label: t("sample.entrance1") });
  add({ levelId: P.id, type: "door", x: 1880, y: 2590, w: 160, h: 14, kind: "door", label: t("sample.entrance2") });
  add({ levelId: P.id, type: "door", x: 10, y: 640, w: 140, h: 14, rotation: 90, kind: "exit", label: t("exitLbl") });
  add({ levelId: P.id, type: "door", x: 2390, y: 640, w: 140, h: 14, rotation: -90, kind: "exit", label: t("exitLbl") });
  add({ levelId: P.id, type: "column", x: 760, y: 2330, w: 50, h: 50 });
  add({ levelId: P.id, type: "column", x: 1640, y: 2330, w: 50, h: 50 });
  add({ levelId: P.id, type: "area", x: 1200, y: 2440, w: 560, h: 220, label: t("sample.control"), color: "#64748B" });

  const L = blk(P.id, t("sample.leftBlock"), BLOCK_COLORS[0]);
  const C = blk(P.id, t("sample.centreBlock"), BLOCK_COLORS[3]);
  const R = blk(P.id, t("sample.rightBlock"), BLOCK_COLORS[1]);
  const gL = uid(), gC = uid(), gR = uid();
  for (let i = 0; i < 12; i++) {
    const y = 640 + i * 92, row = indexToRow(i);
    d.items.push(...makeRow({ x: 150, y: y + 46 }, { x: 610, y }, { gap: 55, curve: 0, numbering: "even", dir: "rtl", row, levelId: P.id, blockId: L, groupId: gL }));
    d.items.push(...makeRow({ x: 760, y }, { x: 1640, y }, { gap: 55, curve: 60 + i * 4, numbering: "seq", dir: "ltr", row, levelId: P.id, blockId: C, groupId: gC }));
    d.items.push(...makeRow({ x: 1790, y }, { x: 2250, y: y + 46 }, { gap: 55, curve: 0, numbering: "odd", dir: "ltr", row, levelId: P.id, blockId: R, groupId: gR }));
  }
  // orta bloğun arkasında engelli yerleri, en arkada ekstra sandalyeler
  d.items.push(...makeRow({ x: 860, y: 1830 }, { x: 1540, y: 1830 }, { gap: 170, numbering: "seq", dir: "ltr", row: t("sample.accessibleRow"), kind: "accessible", levelId: P.id, blockId: C, groupId: uid() }));
  d.items.push(...makeRow({ x: 200, y: 2030 }, { x: 600, y: 2030 }, { gap: 55, numbering: "seq", dir: "ltr", row: "EK", kind: "extra", levelId: P.id, groupId: uid() }));
  d.items.push(...makeRow({ x: 1800, y: 2030 }, { x: 2200, y: 2030 }, { gap: 55, numbering: "seq", dir: "ltr", row: "EK", kind: "extra", levelId: P.id, groupId: uid(), start: 9 }));
  // protokol sırası: orta bloğun A sırası VIP
  d.items.forEach((it) => { if (it.blockId === C && it.row === "A") it.kind = "vip"; });

  // Balkon
  add({ levelId: B.id, type: "area", x: 1200, y: 1050, w: 2300, h: 900, label: t("sample.balcony"), color: "#7C6A55" });
  add({ levelId: B.id, type: "wall", x: 1200, y: 600, w: 2300, h: 14 });
  add({ levelId: B.id, type: "text", x: 1200, y: 420, size: 46, label: t("sample.towardsStage") });
  add({ levelId: B.id, type: "door", x: 1200, y: 1495, w: 160, h: 14, kind: "door", label: t("sample.balconyEntrance") });
  const BM = blk(B.id, t("sample.balconyCentre"), BLOCK_COLORS[2]);
  const BL = blk(B.id, t("sample.leftBox"), BLOCK_COLORS[4]);
  const BR = blk(B.id, t("sample.rightBox"), BLOCK_COLORS[5]);
  const gB = uid();
  for (let i = 0; i < 5; i++) {
    const y = 700 + i * 95;
    d.items.push(...makeRow({ x: 380, y }, { x: 2020, y }, { gap: 55, curve: 110 + i * 6, numbering: "seq", dir: "ltr", row: "B" + indexToRow(i), levelId: B.id, blockId: BM, groupId: gB }));
  }
  const gl = uid(), gr = uid();
  for (let i = 0; i < 2; i++) {
    d.items.push(...makeRow({ x: 160 + i * 80, y: 700 }, { x: 160 + i * 80, y: 920 }, { gap: 55, numbering: "seq", dir: "ltr", row: "L" + (i + 1), kind: "vip", levelId: B.id, blockId: BL, groupId: gl }));
    d.items.push(...makeRow({ x: 2240 - i * 80, y: 920 }, { x: 2240 - i * 80, y: 700 }, { gap: 55, numbering: "seq", dir: "ltr", row: "R" + (i + 1), kind: "vip", levelId: B.id, blockId: BR, groupId: gr }));
  }
  return d;
}

function validDoc(d) {
  return d && Array.isArray(d.levels) && d.levels.length && Array.isArray(d.items) && Array.isArray(d.blocks);
}

/* ---------- Geri al / yinele ----------
   Her değişiklikten sonra commit() çağrılır: önceki durumun JSON kopyası yığına atılır,
   kaydetme durumu güncellenir (markChanged) ve ekran yeniden çizilir. */
function commit() {
  undoStack.push(lastSnap);
  if (undoStack.length > 150) undoStack.shift();
  redoStack = [];
  lastSnap = JSON.stringify(doc);
  markChanged(); render();
}
function resetHistory() { undoStack = []; redoStack = []; lastSnap = JSON.stringify(doc); }
function restore(snap) {
  doc = JSON.parse(snap); lastSnap = snap;
  if (!doc.levels.find((l) => l.id === levelId)) levelId = doc.levels[0].id;
  selected = new Set([...selected].filter((id) => doc.items.some((i) => i.id === id && i.levelId === levelId)));
  markChanged(); render();
}
function undo() { if (!undoStack.length) return; redoStack.push(JSON.stringify(doc)); restore(undoStack.pop()); }
function redo() { if (!redoStack.length) return; undoStack.push(JSON.stringify(doc)); restore(redoStack.pop()); }

const levelItems = () => doc.items.filter((i) => i.levelId === levelId);
const itemById = (id) => doc.items.find((i) => i.id === id);
const blockById = (id) => doc.blocks.find((b) => b.id === id);
const selItems = () => doc.items.filter((i) => selected.has(i.id));

/* ---------- Konva (çizim kütüphanesi) ----------
   Katmanlar: gridLayer (ızgara), layer (kroki öğeleri + seçim kutusu), uiLayer (önizleme, hizalama çizgileri).
   Yakınlaştırma/kaydırma stage üzerinde yapılır; öğe koordinatları her zaman cm cinsinden kalır.
   render() mevcut kattaki tüm düğümleri doc'tan yeniden üretir; asıl durum her zaman doc'tadır. */
const host = $("stage");
const stage = new Konva.Stage({ container: "stage", width: host.clientWidth || 800, height: host.clientHeight || 600 });
const gridLayer = new Konva.Layer({ listening: false });
const layer = new Konva.Layer();
const uiLayer = new Konva.Layer({ listening: false });
stage.add(gridLayer, layer, uiLayer);

const grid = new Konva.Shape({
  sceneFunc(ctx) {
    const s = stage.scaleX(), W = stage.width(), H = stage.height();
    const x0 = -stage.x() / s, y0 = -stage.y() / s, x1 = x0 + W / s, y1 = y0 + H / s;
    const pass = (step, color) => {
      if (step * s < 7) return;
      ctx.beginPath();
      for (let x = Math.floor(x0 / step) * step; x <= x1; x += step) { ctx.moveTo(x, y0); ctx.lineTo(x, y1); }
      for (let y = Math.floor(y0 / step) * step; y <= y1; y += step) { ctx.moveTo(x0, y); ctx.lineTo(x1, y); }
      ctx.setAttr("strokeStyle", color); ctx.setAttr("lineWidth", 1 / s); ctx.stroke();
    };
    pass(50, css("--grid-minor"));
    pass(100, css("--grid-major"));
    pass(500, css("--grid-major"));
  }
});
gridLayer.add(grid);

const tr = new Konva.Transformer({
  rotationSnaps: [0, 45, 90, 135, 180, 225, 270, 315], rotationSnapTolerance: 4, ignoreStroke: true,
  anchorSize: 9, rotateAnchorOffset: 26, flipEnabled: false, keepRatio: false, padding: 4
});
const marquee = new Konva.Rect({ visible: false, listening: false, strokeWidth: 1, dash: [4, 4] });
const preview = new Konva.Group({ listening: false });
const guide = new Konva.Group({ listening: false });
uiLayer.add(preview, guide);

const nodes = new Map();

function themeColors() {
  return {
    ink: css("--ink"), muted: css("--muted"), paper: css("--paper"), accent: css("--accent"), seat: css("--seat"), seatStroke: css("--seat-stroke"),
    stage: css("--stage"), stageText: css("--stage-text"), exit: css("--exit"), font: css("--font-ui") || "sans-serif", mono: css("--font-data") || "monospace"
  };
}
let TC = themeColors();

function makeNode(it) {
  const g = new Konva.Group({ x: it.x, y: it.y, rotation: it.rotation || 0, draggable: tool === "select" });
  g.setAttr("itemId", it.id);
  const sel = selected.has(it.id);
  if (it.type === "seat") {
    const b = it.blockId && blockById(it.blockId);
    const fill = b ? b.color : TC.seat;
    const txt = b ? "#FFFFFF" : TC.ink;
    let stroke = b ? "rgba(0,0,0,.35)" : TC.seatStroke, sw = 1.5, dash = null;
    if (it.kind === "vip") { stroke = "#C9A227"; sw = 5; }
    if (it.kind === "extra") { dash = [7, 5]; sw = 2.5; stroke = b ? stroke : TC.ink; }
    const body = new Konva.Rect({ name: "body", x: -SEAT_W / 2, y: -SEAT_H / 2, width: SEAT_W, height: SEAT_H, cornerRadius: [12, 12, 6, 6], fill: it.kind === "extra" && !b ? TC.paper : fill, stroke, strokeWidth: sw, dash });
    body.setAttr("baseStroke", stroke); body.setAttr("baseSW", sw);
    g.add(body);
    // sırtlık: izleyici yerel −y yönüne baktığı için sırtlık +y tarafındadır
    g.add(new Konva.Rect({ x: -SEAT_W / 2 + 4, y: SEAT_H / 2 - 11, width: SEAT_W - 8, height: 7, cornerRadius: 3, fill: "rgba(0,0,0,.28)", listening: false }));
    if (it.kind === "accessible") {
      g.add(new Konva.Text({ text: "♿", x: -SEAT_W / 2, y: -SEAT_H / 2 + 6, width: SEAT_W, align: "center", fontSize: 22, fill: txt, listening: false }));
    } else {
      g.add(new Konva.Text({ text: String(it.num ?? ""), x: -SEAT_W / 2, y: -SEAT_H / 2 + 9, width: SEAT_W, align: "center", fontSize: 16, fontFamily: TC.mono, fill: it.kind === "extra" && !b ? TC.ink : txt, listening: false }));
    }
    if (sel) { body.stroke(TC.accent); body.strokeWidth(5); }
    return g;
  }
  const w = it.w || 100, h = it.h || 100;
  let body;
  if (it.type === "stage") {
    body = new Konva.Rect({ name: "body", x: -w / 2, y: -h / 2, width: w, height: h, fill: TC.stage, cornerRadius: 8 });
    g.add(body);
    g.add(new Konva.Text({ text: it.label || "", x: -w / 2, y: -20, width: w, align: "center", fontSize: 40, fontStyle: "600", letterSpacing: 8, fontFamily: TC.font, fill: TC.stageText, listening: false }));
  } else if (it.type === "area") {
    const col = it.color || "#64748B";
    body = new Konva.Rect({ name: "body", x: -w / 2, y: -h / 2, width: w, height: h, fill: col + "1F", stroke: col, strokeWidth: 3, dash: [16, 10], cornerRadius: 6 });
    g.add(body);
    g.add(new Konva.Text({ text: it.label || "", x: -w / 2 + 16, y: -h / 2 + 14, fontSize: 30, fontStyle: "600", fontFamily: TC.font, fill: col, listening: false }));
  } else if (it.type === "wall") {
    body = new Konva.Rect({ name: "body", x: -w / 2, y: -h / 2, width: w, height: h, fill: TC.ink });
    g.add(body);
  } else if (it.type === "door") {
    const col = it.kind === "exit" ? TC.exit : TC.ink;
    body = new Konva.Rect({ name: "body", x: -w / 2, y: -h / 2, width: w, height: h, fill: TC.paper, stroke: col, strokeWidth: 3 });
    g.add(body);
    g.add(new Konva.Arc({ x: -w / 2, y: -h / 2, innerRadius: w - 1, outerRadius: w, angle: 90, rotation: 270, fill: col, opacity: .6, listening: false }));
    g.add(new Konva.Line({ points: [-w / 2, -h / 2, -w / 2, -h / 2 - w], stroke: col, strokeWidth: 2, listening: false }));
    g.add(new Konva.Text({ text: it.label || "", x: -w / 2 - 40, y: h / 2 + 8, width: w + 80, align: "center", fontSize: 24, fontStyle: "600", fontFamily: TC.font, fill: col, listening: false }));
  } else if (it.type === "column") {
    body = new Konva.Ellipse({ name: "body", radiusX: w / 2, radiusY: h / 2, fill: TC.muted, stroke: TC.ink, strokeWidth: 2 });
    g.add(body);
  } else if (it.type === "text") {
    body = new Konva.Text({ name: "body", text: it.label || "", fontSize: it.size || 40, fontStyle: "600", fontFamily: TC.font, fill: TC.ink, padding: 4 });
    body.offsetX(body.width() / 2); body.offsetY(body.height() / 2);
    g.add(body);
  }
  if (body) { body.setAttr("baseStroke", body.stroke()); body.setAttr("baseSW", body.strokeWidth()); }
  if (sel && body && it.type !== "text") { body.stroke(TC.accent); body.strokeWidth(Math.max(4, body.strokeWidth())); body.dash(null); }
  return g;
}

const ORDER = { area: 0, stage: 1, wall: 2, column: 3, door: 4, seat: 5, text: 6 };
function render() {
  tr.nodes([]); tr.remove(); marquee.remove();
  layer.destroyChildren(); nodes.clear();
  const items = levelItems().sort((a, b) => ORDER[a.type] - ORDER[b.type]);
  for (const it of items) { const n = makeNode(it); layer.add(n); nodes.set(it.id, n); }
  layer.add(tr, marquee);
  syncTransformer();
  layer.batchDraw(); gridLayer.batchDraw();
  renderTabs(); renderPanel(); renderStats(); updateHistoryBtns();
}

function syncTransformer() {
  const ns = [...selected].map((id) => nodes.get(id)).filter(Boolean);
  const its = selItems();
  const single = its.length === 1 ? its[0] : null;
  tr.setAttrs({
    borderStroke: TC.accent, anchorStroke: TC.accent, anchorFill: TC.paper, borderDash: [4, 3],
    enabledAnchors: single && RESIZABLE.has(single.type) ? (single.type === "column" ? ["top-left", "top-right", "bottom-left", "bottom-right"] : ["top-left", "top-center", "top-right", "middle-right", "middle-left", "bottom-left", "bottom-center", "bottom-right"]) : [],
    keepRatio: !!(single && single.type === "column"),
    rotateEnabled: tool === "select",
    resizeEnabled: tool === "select"
  });
  tr.nodes(tool === "select" ? ns : []);
  tr.getLayer() && tr.moveToTop();
}

function refreshSelection() {
  for (const [id, n] of nodes) {
    const body = n.findOne(".body"); if (!body) continue;
    const it = itemById(id);
    const on = selected.has(id);
    if (it.type === "text") continue;
    body.stroke(on ? TC.accent : body.getAttr("baseStroke"));
    body.strokeWidth(on ? Math.max(5, body.getAttr("baseSW") || 0) : body.getAttr("baseSW"));
    if (it.type === "seat" && it.kind === "extra" && !on) body.dash([7, 5]);
    if (it.type === "area" && !on) body.dash([16, 10]);
    if (on && it.type !== "seat") body.dash(null);
  }
  syncTransformer(); layer.batchDraw(); renderPanel(); renderBlocks();
}

/* ---------- Görünüm: yakınlaştırma, ölçek çubuğu, ekrana sığdırma ---------- */
function resize() {
  stage.size({ width: host.clientWidth, height: host.clientHeight });
  gridLayer.batchDraw();
}
new ResizeObserver(resize).observe(host);

function setZoom(scale, center) {
  scale = Math.max(0.05, Math.min(4, scale));
  const old = stage.scaleX();
  center = center || { x: stage.width() / 2, y: stage.height() / 2 };
  const wp = { x: (center.x - stage.x()) / old, y: (center.y - stage.y()) / old };
  stage.scale({ x: scale, y: scale });
  stage.position({ x: center.x - wp.x * scale, y: center.y - wp.y * scale });
  viewChanged();
}
function viewChanged() {
  const s = stage.scaleX();
  $("zoomVal").textContent = Math.round(s * 100) + "%";
  // ölçek çubuğu: ekranda en az 40 px tutan ilk uzunluğu seç (0,5 / 1 / 2 / 5 / 10 / 20 m)
  let m = 1; for (const c of [0.5, 1, 2, 5, 10, 20]) { if (c * 100 * s >= 40) { m = c; break; } m = c; }
  $("scaleBar").style.width = Math.round(m * 100 * s) + "px";
  $("scaleLbl").textContent = String(m).replace(".", lang === "tr" ? "," : ".") + " m";
  gridLayer.batchDraw(); uiLayer.batchDraw();
}
function fit() {
  const its = levelItems();
  if (!its.length) { stage.scale({ x: .3, y: .3 }); stage.position({ x: 40, y: 40 }); viewChanged(); return; }
  layer.draw();
  const r = layer.getClientRect({ relativeTo: layer, skipTransform: false });
  const box = { x: Infinity, y: Infinity, X: -Infinity, Y: -Infinity };
  for (const n of nodes.values()) {
    const c = n.getClientRect({ relativeTo: layer });
    box.x = Math.min(box.x, c.x); box.y = Math.min(box.y, c.y); box.X = Math.max(box.X, c.x + c.width); box.Y = Math.max(box.Y, c.y + c.height);
  }
  if (!isFinite(box.x)) Object.assign(box, { x: r.x, y: r.y, X: r.x + r.width, Y: r.y + r.height });
  const pad = 40, W = stage.width() - pad * 2, H = stage.height() - pad * 2 - 30;
  const s = Math.max(0.05, Math.min(2, Math.min(W / (box.X - box.x), H / (box.Y - box.y))));
  stage.scale({ x: s, y: s });
  stage.position({ x: pad + (W - (box.X - box.x) * s) / 2 - box.x * s, y: pad + (H - (box.Y - box.y) * s) / 2 - box.y * s });
  viewChanged();
}

/* ---------- Fare / dokunma etkileşimi ----------
   Seç aracında: öğeye tıklama seçim yapar (grup üyeleriyle), boş alanda sürükleme alan seçimidir.
   Çizim araçlarında: basılınca başlangıç noktası alınır, bırakınca finishDraw() öğeyi üretir. */
let spaceDown = false, panning = null, drawing = null, marqueeStart = null, pinch = null;
const worldPos = () => layer.getRelativePointerPosition();
const itemIdOf = (target) => { let n = target; while (n && n !== stage) { const id = n.getAttr && n.getAttr("itemId"); if (id) return id; n = n.getParent(); } return null; };
const isTransformerPart = (target) => { let n = target; while (n) { if (n === tr) return true; n = n.getParent(); } return false; };

function setDraggable(flag) { for (const n of nodes.values()) n.draggable(flag); }

function groupMembers(id) {
  const it = itemById(id);
  if (!it || !it.groupId) return [id];
  return doc.items.filter((i) => i.groupId === it.groupId && i.levelId === levelId).map((i) => i.id);
}

stage.on("mousedown touchstart", (e) => {
  const evt = e.evt;
  if (evt.touches && evt.touches.length === 2) return;
  const p = stage.getPointerPosition();
  if (tool === "hand" || spaceDown || evt.button === 1) {
    panning = { x: p.x, y: p.y, sx: stage.x(), sy: stage.y() };
    host.classList.add("panning");
    evt.preventDefault();
    return;
  }
  if (evt.button === 2) return;
  if (isTransformerPart(e.target)) return;
  const w = worldPos();
  if (tool === "select") {
    const id = itemIdOf(e.target);
    if (id) {
      const ids = evt.altKey ? [id] : groupMembers(id);
      if (evt.shiftKey) {
        const allIn = ids.every((x) => selected.has(x));
        ids.forEach((x) => (allIn ? selected.delete(x) : selected.add(x)));
      } else if (!selected.has(id)) {
        selected = new Set(ids);
      } else if (evt.altKey) {
        selected = new Set(ids);
      }
      refreshSelection();
    } else {
      marqueeStart = { x: w.x, y: w.y, add: evt.shiftKey };
      marquee.setAttrs({ x: w.x, y: w.y, width: 0, height: 0, visible: true, stroke: TC.accent, fill: TC.accent + "14", strokeWidth: 1 / stage.scaleX(), dash: [4 / stage.scaleX(), 4 / stage.scaleX()] });
      marquee.moveToTop();
    }
    return;
  }
  // çizim araçları
  const sp = { x: snapV(w.x), y: snapV(w.y) };
  if (["seat", "door", "column", "text"].includes(tool)) { createAt(sp); return; }
  drawing = { start: sp, end: sp };
  updatePreview();
});

stage.on("mousemove touchmove", (e) => {
  const evt = e.evt;
  if (evt.touches && evt.touches.length === 2) { handlePinch(evt); return; }
  const p = stage.getPointerPosition(); if (!p) return;
  const w = worldPos();
  $("posInfo").textContent = "x " + fmtM(w.x) + " · y " + fmtM(w.y);
  if (panning) { stage.position({ x: panning.sx + p.x - panning.x, y: panning.sy + p.y - panning.y }); viewChanged(); return; }
  if (marqueeStart) {
    marquee.setAttrs({ x: Math.min(marqueeStart.x, w.x), y: Math.min(marqueeStart.y, w.y), width: Math.abs(w.x - marqueeStart.x), height: Math.abs(w.y - marqueeStart.y) });
    layer.batchDraw(); return;
  }
  if (drawing) { drawing.end = { x: snapV(w.x), y: snapV(w.y) }; drawing.shift = evt.shiftKey; updatePreview(); return; }
  const id = itemIdOf(e.target);
  const it = id && itemById(id);
  if (it && it.type === "seat") {
    const b = it.blockId && blockById(it.blockId);
    const lv = doc.levels.find((l) => l.id === it.levelId);
    $("hoverInfo").textContent = t("hoverSeat", { level: lv ? lv.name : "", block: b ? b.name : "—", row: it.row || "—", num: it.num ?? "—" }) + (it.kind !== "normal" ? " · " + t("kind." + it.kind) : "");
  } else if (it) {
    $("hoverInfo").textContent = (t("type." + it.type) || it.type) + (it.label ? " · " + it.label : "");
  } else $("hoverInfo").textContent = "";
  $("hoverInfo").hidden = !$("hoverInfo").textContent;
});

window.addEventListener("mouseup", endPointer);
window.addEventListener("touchend", endPointer);
function endPointer(evt) {
  pinch = null;
  if (panning) { panning = null; host.classList.remove("panning"); return; }
  if (marqueeStart) {
    const box = marquee.getClientRect({ relativeTo: layer });
    const hits = [];
    if (box.width > 2 || box.height > 2) {
      // büyük yapı öğeleri (duvar, sahne, bölge) yalnızca kutunun tamamen içindeyse seçilir; diğerleri değince seçilir
      const inside = (a, r) => r.x >= a.x && r.y >= a.y && r.x + r.width <= a.x + a.width && r.y + r.height <= a.y + a.height;
      for (const [id, n] of nodes) {
        const r = n.getClientRect({ relativeTo: layer }), it = itemById(id);
        if (["wall", "stage", "area"].includes(it.type) ? inside(box, r) : Konva.Util.haveIntersection(box, r)) hits.push(id);
      }
    }
    if (!marqueeStart.add) selected = new Set();
    hits.forEach((id) => selected.add(id));
    marquee.visible(false); marqueeStart = null;
    refreshSelection(); return;
  }
  if (drawing) { const d = drawing; drawing = null; preview.destroyChildren(); uiLayer.batchDraw(); finishDraw(d); }
}

stage.on("wheel", (e) => {
  e.evt.preventDefault();
  const p = stage.getPointerPosition();
  if (e.evt.ctrlKey || e.evt.metaKey || !e.evt.shiftKey && Math.abs(e.evt.deltaX) < 1 && e.evt.deltaMode === 0 && Math.abs(e.evt.deltaY) >= 1) {
    const f = Math.exp(-e.evt.deltaY * (e.evt.ctrlKey ? 0.01 : 0.0015));
    setZoom(stage.scaleX() * f, p);
  } else {
    stage.position({ x: stage.x() - e.evt.deltaX, y: stage.y() - e.evt.deltaY }); viewChanged();
  }
});

function handlePinch(evt) {
  evt.preventDefault();
  const [a, b] = evt.touches; const r = host.getBoundingClientRect();
  const c = { x: (a.clientX + b.clientX) / 2 - r.left, y: (a.clientY + b.clientY) / 2 - r.top };
  const dist = Math.hypot(a.clientX - b.clientX, a.clientY - b.clientY);
  if (!pinch) { pinch = { dist, scale: stage.scaleX() }; return; }
  setZoom(pinch.scale * dist / pinch.dist, c);
}

/* Sürükleme ve döndürme bitince düğümlerdeki yeni konumlar doc'a yazılır.
   Transformer çoklu seçimde diğer düğümleri de taşır; hepsi bitince tek commit yapılır. */
let dragRef = null;
layer.on("dragstart", (e) => {
  const id = e.target.getAttr("itemId"); if (!id) return;
  if (!dragRef) { const it = itemById(id); dragRef = { id, x: it.x, y: it.y }; }
});
layer.on("dragmove", (e) => {
  if (!dragRef || e.target.getAttr("itemId") !== dragRef.id) return;
  drawGuides(e.target);
});
layer.on("dragend", (e) => {
  if (!e.target.getAttr("itemId") || !dragRef) return;
  if (layer.find((n) => n.isDragging && n.isDragging()).length) return; // birlikte taşınan düğümlerin hepsi bitsin
  const ref = dragRef; dragRef = null;
  guide.destroyChildren(); uiLayer.batchDraw();
  const lead = nodes.get(ref.id);
  const snapped = applyGuideSnap(lead) || { x: snapV(lead.x()), y: snapV(lead.y()) };
  const dx = snapped.x - ref.x, dy = snapped.y - ref.y;
  const moved = new Set([ref.id, ...selected]);
  if (dx === 0 && dy === 0) { render(); return; }
  for (const id of moved) { const it = itemById(id); if (it && it.levelId === levelId) { it.x = Math.round(it.x + dx); it.y = Math.round(it.y + dy); } }
  commit();
});

/* Akıllı kılavuzlar: sürüklenen nesnenin kenar/merkezini diğer nesnelerle hizalar ve çizgi gösterir */
let guideSnap = null;
function drawGuides(node) {
  guide.destroyChildren(); guideSnap = null;
  const r = node.getClientRect({ relativeTo: layer });
  const cx = r.x + r.width / 2, cy = r.y + r.height / 2;
  const tol = 8 / stage.scaleX();
  let bx = null, by = null;
  for (const [id, n] of nodes) {
    if (selected.has(id) || n === node) continue;
    const o = n.getClientRect({ relativeTo: layer });
    const ox = [o.x, o.x + o.width / 2, o.x + o.width], oy = [o.y, o.y + o.height / 2, o.y + o.height];
    const mx = [r.x, cx, r.x + r.width], my = [r.y, cy, r.y + r.height];
    for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) {
      const ddx = ox[j] - mx[i]; if (Math.abs(ddx) < tol && (!bx || Math.abs(ddx) < Math.abs(bx.d))) bx = { d: ddx, at: ox[j] };
      const ddy = oy[j] - my[i]; if (Math.abs(ddy) < tol && (!by || Math.abs(ddy) < Math.abs(by.d))) by = { d: ddy, at: oy[j] };
    }
  }
  const s = stage.scaleX(), W = stage.width() / s, H = stage.height() / s, x0 = -stage.x() / s, y0 = -stage.y() / s;
  if (bx) guide.add(new Konva.Line({ points: [bx.at, y0, bx.at, y0 + H], stroke: TC.accent, strokeWidth: 1 / s, dash: [6 / s, 4 / s] }));
  if (by) guide.add(new Konva.Line({ points: [x0, by.at, x0 + W, by.at], stroke: TC.accent, strokeWidth: 1 / s, dash: [6 / s, 4 / s] }));
  guideSnap = { dx: bx ? bx.d : null, dy: by ? by.d : null };
  uiLayer.batchDraw();
}
function applyGuideSnap(lead) {
  if (!guideSnap || (guideSnap.dx === null && guideSnap.dy === null)) return null;
  const g = guideSnap; guideSnap = null;
  return { x: g.dx !== null ? Math.round(lead.x() + g.dx) : snapV(lead.x()), y: g.dy !== null ? Math.round(lead.y() + g.dy) : snapV(lead.y()) };
}

tr.on("transformend", () => {
  for (const n of tr.nodes()) {
    const it = itemById(n.getAttr("itemId")); if (!it) continue;
    it.x = Math.round(n.x()); it.y = Math.round(n.y());
    it.rotation = +(((n.rotation() % 360) + 360) % 360).toFixed(2);
    if (RESIZABLE.has(it.type) && (Math.abs(n.scaleX() - 1) > 1e-3 || Math.abs(n.scaleY() - 1) > 1e-3)) {
      it.w = Math.max(10, Math.round((it.w || 100) * n.scaleX()));
      it.h = Math.max(10, Math.round((it.h || 100) * n.scaleY()));
    }
  }
  commit();
});

/* ---------- Öğe oluşturma: tek tıkla (koltuk, kapı, sütun, yazı) ya da sürükleyerek (sıra, blok, sahne, bölge, duvar) ---------- */
function currentLevelName() { const l = doc.levels.find((x) => x.id === levelId); return l ? l.name : ""; }
function nextKindNumber(kind) {
  let m = 0; for (const i of levelItems()) if (i.type === "seat" && i.kind === kind && !isNaN(+i.num)) m = Math.max(m, +i.num);
  return m + 1;
}
function createAt(p) {
  let it;
  if (tool === "seat") it = { type: "seat", x: p.x, y: p.y, rotation: 0, row: opts.kind === "extra" ? "EK" : "", num: nextKindNumber(opts.kind), kind: opts.kind, blockId: null, groupId: null };
  if (tool === "door") it = { type: "door", x: p.x, y: p.y, rotation: 0, w: 120, h: 14, kind: "door", label: t("doorLbl") };
  if (tool === "column") it = { type: "column", x: p.x, y: p.y, rotation: 0, w: 50, h: 50 };
  if (tool === "text") it = { type: "text", x: p.x, y: p.y, rotation: 0, size: 40, label: t("textLbl") };
  it.id = uid(); it.levelId = levelId;
  doc.items.push(it);
  if (tool !== "seat") { selected = new Set([it.id]); setTool("select", true); }
  commit();
}

function rowOpts(extra) {
  return Object.assign({ gap: Math.max(30, +opts.gap || 55), curve: +opts.curve || 0, numbering: opts.numbering, dir: opts.dir, kind: opts.kind, levelId, row: opts.nextRow }, extra);
}
function rowEnd(d) {
  // Shift basılıysa sıra açısı 15° adımlara kilitlenir
  if (!d.shift) return d.end;
  const dx = d.end.x - d.start.x, dy = d.end.y - d.start.y, L = Math.hypot(dx, dy);
  const a = Math.round(Math.atan2(dy, dx) / (Math.PI / 12)) * (Math.PI / 12);
  return { x: d.start.x + L * Math.cos(a), y: d.start.y + L * Math.sin(a) };
}
function gridRows(d) {
  const x0 = Math.min(d.start.x, d.end.x), x1 = Math.max(d.start.x, d.end.x);
  const y0 = Math.min(d.start.y, d.end.y), y1 = Math.max(d.start.y, d.end.y);
  const rg = Math.max(50, +opts.rowGap || 92);
  const nrows = Math.floor((y1 - y0) / rg + 1e-6) + 1;
  const rows = [];
  let label = opts.nextRow;
  for (let r = 0; r < nrows; r++) { rows.push({ p0: { x: x0, y: y0 + r * rg }, p1: { x: x1, y: y0 + r * rg }, row: label }); label = nextRowLabel(label); }
  return { rows, nextLabel: label };
}

function updatePreview() {
  preview.destroyChildren();
  const d = drawing; if (!d) return;
  const col = TC.accent;
  const seatGhost = (p) => preview.add(new Konva.Rect({ x: p.x, y: p.y, width: SEAT_W, height: SEAT_H, offsetX: SEAT_W / 2, offsetY: SEAT_H / 2, rotation: p.rot || 0, cornerRadius: [12, 12, 6, 6], stroke: col, strokeWidth: 2 / stage.scaleX(), fill: col + "22" }));
  let label = "";
  if (tool === "row") {
    const end = rowEnd(d);
    const pts = rowSeatPositions(d.start, end, Math.max(30, +opts.gap || 55), +opts.curve || 0);
    pts.forEach(seatGhost);
    preview.add(new Konva.Line({ points: [d.start.x, d.start.y, end.x, end.y], stroke: col, strokeWidth: 1 / stage.scaleX(), dash: [6, 4] }));
    label = t("row") + " " + opts.nextRow + " · " + t("seatsCount", { n: pts.length }) + " · " + fmtM(Math.hypot(end.x - d.start.x, end.y - d.start.y));
  } else if (tool === "grid") {
    const g = gridRows(d); let n = 0;
    g.rows.forEach((r) => { const pts = rowSeatPositions(r.p0, r.p1, Math.max(30, +opts.gap || 55), +opts.curve || 0); n += pts.length; pts.forEach(seatGhost); });
    label = g.rows.length + " × " + (n / g.rows.length | 0) + " = " + t("seatsCount", { n });
  } else {
    const x = Math.min(d.start.x, d.end.x), y = Math.min(d.start.y, d.end.y), w = Math.abs(d.end.x - d.start.x), h = Math.abs(d.end.y - d.start.y);
    preview.add(new Konva.Rect({ x, y, width: w, height: h, stroke: col, strokeWidth: 2 / stage.scaleX(), dash: [8, 5], fill: col + "14" }));
    label = fmtM(w) + " × " + fmtM(h);
  }
  const s = stage.scaleX();
  const lbl = new Konva.Label({ x: d.end.x + 12 / s, y: d.end.y + 12 / s, scaleX: 1 / s, scaleY: 1 / s });
  lbl.add(new Konva.Tag({ fill: TC.ink, cornerRadius: 4 }));
  lbl.add(new Konva.Text({ text: label, padding: 6, fontSize: 12, fontFamily: TC.mono, fill: TC.paper }));
  preview.add(lbl);
  uiLayer.batchDraw();
}

function finishDraw(d) {
  const dist = Math.hypot(d.end.x - d.start.x, d.end.y - d.start.y);
  if (tool === "row") {
    const items = makeRow(d.start, rowEnd(d), rowOpts({ groupId: uid() }));
    doc.items.push(...items);
    selected = new Set(items.map((i) => i.id));
    opts.nextRow = nextRowLabel(opts.nextRow);
    commit(); renderOptions(); return;
  }
  if (tool === "grid") {
    if (dist < 20) return;
    const g = gridRows(d); const gid = uid(); const all = [];
    for (const r of g.rows) all.push(...makeRow(r.p0, r.p1, rowOpts({ row: r.row, groupId: gid })));
    doc.items.push(...all);
    selected = new Set(all.map((i) => i.id));
    opts.nextRow = g.nextLabel;
    commit(); renderOptions(); return;
  }
  let w = Math.abs(d.end.x - d.start.x), h = Math.abs(d.end.y - d.start.y);
  const defaults = { stage: [1200, 400], area: [800, 500], wall: [600, 20] };
  if (w < 20 && h < 20) { [w, h] = defaults[tool]; d.end = { x: d.start.x + w, y: d.start.y + h }; }
  if (tool === "wall") { if (w >= h) h = 20; else w = 20; }
  const it = { id: uid(), levelId, type: tool, x: Math.round((d.start.x + d.end.x) / 2), y: Math.round((d.start.y + d.end.y) / 2), w: Math.max(10, Math.round(w)), h: Math.max(10, Math.round(h)), rotation: 0 };
  if (tool === "wall") { it.x = tool === "wall" && w === 20 ? d.start.x : it.x; it.y = h === 20 ? d.start.y : it.y; }
  if (tool === "stage") it.label = t("stageLbl");
  if (tool === "area") { it.label = t("areaLbl"); it.color = "#64748B"; }
  doc.items.push(it);
  selected = new Set([it.id]);
  setTool("select", true);
  commit();
}

/* ---------- Seçim işlemleri: hizala, dağıt, döndür, yansıt, grupla, kopyala, numaralandır ---------- */
function rectOf(id) { return nodes.get(id).getClientRect({ relativeTo: layer }); }
function alignSel(mode) {
  const ids = [...selected].filter((id) => nodes.has(id)); if (ids.length < 2) return;
  const rs = ids.map((id) => [id, rectOf(id)]);
  const minX = Math.min(...rs.map(([, r]) => r.x)), maxX = Math.max(...rs.map(([, r]) => r.x + r.width));
  const minY = Math.min(...rs.map(([, r]) => r.y)), maxY = Math.max(...rs.map(([, r]) => r.y + r.height));
  for (const [id, r] of rs) {
    const it = itemById(id);
    if (mode === "left") it.x += minX - r.x;
    if (mode === "right") it.x += maxX - (r.x + r.width);
    if (mode === "hcenter") it.x += (minX + maxX) / 2 - (r.x + r.width / 2);
    if (mode === "top") it.y += minY - r.y;
    if (mode === "bottom") it.y += maxY - (r.y + r.height);
    if (mode === "vmiddle") it.y += (minY + maxY) / 2 - (r.y + r.height / 2);
    it.x = Math.round(it.x); it.y = Math.round(it.y);
  }
  commit();
}
function distributeSel(axis) {
  const its = selItems().filter((i) => i.levelId === levelId); if (its.length < 3) return;
  const k = axis === "h" ? "x" : "y";
  its.sort((a, b) => a[k] - b[k]);
  const a = its[0][k], b = its[its.length - 1][k], step = (b - a) / (its.length - 1);
  its.forEach((it, i) => (it[k] = Math.round(a + i * step)));
  commit();
}
function selCenter() {
  const ids = [...selected].filter((id) => nodes.has(id));
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
  ids.forEach((id) => { const r = rectOf(id); x0 = Math.min(x0, r.x); y0 = Math.min(y0, r.y); x1 = Math.max(x1, r.x + r.width); y1 = Math.max(y1, r.y + r.height); });
  return { x: (x0 + x1) / 2, y: (y0 + y1) / 2 };
}
function rotateSel(deg) {
  const its = selItems(); if (!its.length) return;
  const c = selCenter(), a = (deg * Math.PI) / 180, cos = Math.cos(a), sin = Math.sin(a);
  for (const it of its) {
    const dx = it.x - c.x, dy = it.y - c.y;
    it.x = Math.round(c.x + dx * cos - dy * sin); it.y = Math.round(c.y + dx * sin + dy * cos);
    it.rotation = +((((it.rotation || 0) + deg) % 360 + 360) % 360).toFixed(2);
  }
  commit();
}
function flipSel() {
  const its = selItems(); if (!its.length) return;
  const c = selCenter();
  for (const it of its) { it.x = Math.round(2 * c.x - it.x); it.rotation = +(((-(it.rotation || 0)) % 360 + 360) % 360).toFixed(2); }
  commit();
}
function groupSel() { const its = selItems(); if (its.length < 2) return; const g = uid(); its.forEach((i) => (i.groupId = g)); commit(); toast(t("group").replace(/ \(.*\)/, "")); }
function ungroupSel() { const its = selItems(); its.forEach((i) => (i.groupId = null)); commit(); }
function deleteSel() { if (!selected.size) return; doc.items = doc.items.filter((i) => !selected.has(i.id)); selected.clear(); commit(); }
function copySel() { const its = selItems(); if (its.length) clipboard = JSON.parse(JSON.stringify(its)); }
function pasteItems(src, offset) {
  if (!src || !src.length) return;
  const gmap = {};
  const fresh = src.map((i) => {
    const n = Object.assign({}, i, { id: uid(), levelId, x: i.x + offset, y: i.y + offset });
    if (i.groupId) n.groupId = gmap[i.groupId] || (gmap[i.groupId] = uid());
    if (n.blockId && blockById(n.blockId)?.levelId !== levelId) n.blockId = null;
    return n;
  });
  doc.items.push(...fresh);
  selected = new Set(fresh.map((i) => i.id));
  commit();
}
function duplicateSel() { const its = selItems(); if (!its.length) return; pasteItems(JSON.parse(JSON.stringify(its)), 60); }
function nudge(dx, dy) { const its = selItems(); if (!its.length) return; its.forEach((i) => { i.x += dx; i.y += dy; }); commit(); }
function selectAll() { selected = new Set(levelItems().map((i) => i.id)); refreshSelection(); }

function renumber(o) {
  const seats = selItems().filter((i) => i.type === "seat"); if (!seats.length) return;
  // referans ekseni: seçili koltukların ortalama bakış yönü; u sıra boyunca, v önden arkaya
  let sx = 0, sy = 0; seats.forEach((s) => { const a = (s.rotation || 0) * Math.PI / 180; sx += Math.cos(a); sy += Math.sin(a); });
  const th = Math.atan2(sy, sx), cos = Math.cos(th), sin = Math.sin(th);
  const proj = (s) => ({ s, u: s.x * cos + s.y * sin, v: -s.x * sin + s.y * cos });
  let rows;
  if (o.reassign) {
    const ps = seats.map(proj).sort((a, b) => a.v - b.v);
    rows = []; const tol = Math.max(30, (+opts.rowGap || 92) * 0.45);
    for (const p of ps) { const last = rows[rows.length - 1]; if (last && Math.abs(p.v - last.v) <= tol) { last.list.push(p); last.v = (last.v * (last.list.length - 1) + p.v) / last.list.length; } else rows.push({ v: p.v, list: [p] }); }
    let label = o.firstRow || "A";
    rows.forEach((r) => { r.list.forEach((p) => (p.s.row = label)); label = nextRowLabel(label); });
    rows = rows.map((r) => r.list);
  } else {
    const m = new Map(); seats.map(proj).forEach((p) => { const k = p.s.row || ""; if (!m.has(k)) m.set(k, []); m.get(k).push(p); });
    rows = [...m.values()];
  }
  for (const list of rows) {
    list.sort((a, b) => a.u - b.u);
    if (o.dir === "rtl") list.reverse();
    const nums = numberSeq(list.length, o.pattern, +o.start || 1);
    list.forEach((p, i) => (p.s.num = nums[i]));
  }
  commit(); toast(t("numberTool") + " ✓");
}

/* ---------- Arayüz: sol araç çubuğu ve seçenek çubuğu ---------- */
const ICONS = {
  select: '<path d="M5 3l14 7-6 2-2 6z"/>',
  hand: '<path d="M8 13V5.5a1.5 1.5 0 0 1 3 0V11m0-6.5a1.5 1.5 0 0 1 3 0V11m0-5.5a1.5 1.5 0 0 1 3 0V13a7 7 0 0 1-7 7h-.5A5.5 5.5 0 0 1 5 16.2L3.6 13a1.5 1.5 0 0 1 2.6-1.5L8 14"/>',
  seat: '<rect x="6" y="5" width="12" height="11" rx="3"/><path d="M7 19h10"/>',
  row: '<rect x="2.5" y="9" width="5" height="6" rx="1.5"/><rect x="9.5" y="9" width="5" height="6" rx="1.5"/><rect x="16.5" y="9" width="5" height="6" rx="1.5"/>',
  grid: '<rect x="3" y="4" width="5" height="5" rx="1.2"/><rect x="10" y="4" width="5" height="5" rx="1.2"/><rect x="17" y="4" width="4" height="5" rx="1.2"/><rect x="3" y="13" width="5" height="5" rx="1.2"/><rect x="10" y="13" width="5" height="5" rx="1.2"/><rect x="17" y="13" width="4" height="5" rx="1.2"/>',
  stage: '<path d="M3 5h18v6a9 4 0 0 1-18 0z"/><path d="M8 19h8"/>',
  area: '<rect x="3.5" y="5" width="17" height="14" rx="2" stroke-dasharray="3 2.5"/>',
  wall: '<path d="M3 7h18v4H3zM3 11h18v4H3zM9 7v4M15 11v4M7 15v4h10v-4"/>',
  door: '<path d="M5 20V4h9v16"/><path d="M14 20a9 9 0 0 0-9-9"/><path d="M3 20h18"/>',
  column: '<circle cx="12" cy="12" r="6"/>',
  text: '<path d="M5 6V4h14v2M12 4v16M9 20h6"/>'
};
const TOOLS = [["select", "V"], ["hand", "H"], "|", ["seat", "S"], ["row", "R"], ["grid", "B"], "|", ["stage", "P"], ["area", "A"], ["wall", "W"], ["door", "D"], ["column", "C"], ["text", "T"]];
function renderTools() {
  $("tools").innerHTML = TOOLS.map((x) => x === "|" ? '<div class="sep"></div>' :
    `<button class="tool" data-tool="${x[0]}" aria-pressed="${tool === x[0]}" title="${esc(t("tool." + x[0]))} (${x[1]})" aria-label="${esc(t("tool." + x[0]))}"><svg viewBox="0 0 24 24">${ICONS[x[0]]}</svg><kbd>${x[1]}</kbd></button>`).join("");
}
$("tools").addEventListener("click", (e) => { const b = e.target.closest("[data-tool]"); if (b) setTool(b.dataset.tool); });

function setTool(name, keepSel) {
  tool = name;
  if (!keepSel && name !== "select") selected.clear();
  host.classList.toggle("pan", name === "hand");
  host.classList.toggle("draw", !["select", "hand"].includes(name));
  setDraggable(name === "select");
  renderTools(); renderOptions(); refreshSelection();
}

function optInput(key, type, label, unit, extra = "") {
  return `<label>${esc(label)} <input id="o_${key}" type="${type}" value="${esc(opts[key])}" ${extra}>${unit ? `<span class="unit">${unit}</span>` : ""}</label>`;
}
function optSelect(key, label, choices) {
  return `<label>${esc(label)} <select id="o_${key}">${choices.map((c) => `<option value="${c}" ${opts[key] === c ? "selected" : ""}>${esc(t(c.startsWith("kind.") ? c : c))}</option>`).join("")}</select></label>`;
}
function renderOptions() {
  const name = `<span class="toolname">${esc(t("tool." + tool))}</span>`;
  const kinds = `<label>${esc(t("kindLabel"))} <select id="o_kind">${["normal", "vip", "accessible", "extra"].map((k) => `<option value="${k}" ${opts.kind === k ? "selected" : ""}>${esc(t("kind." + k))}</option>`).join("")}</select></label>`;
  let h = name;
  if (tool === "select") h += `<span class="hint">${esc(t("hint.select"))}</span>`;
  else if (tool === "hand") h += "";
  else if (tool === "seat") h += kinds + `<span class="hint">${esc(t("hint.seat"))}</span>`;
  else if (tool === "row" || tool === "grid") {
    h += optInput("gap", "number", t("gap"), "cm", 'min="30" step="5" style="width:64px"');
    if (tool === "grid") h += optInput("rowGap", "number", t("rowGap"), "cm", 'min="50" step="5" style="width:64px"');
    h += optInput("curve", "number", t("curve"), "cm", 'step="10" style="width:64px"');
    h += optInput("nextRow", "text", t("nextRow"), "", 'maxlength="4" style="width:48px"');
    h += optSelect("numbering", t("numbering"), ["seq", "odd", "even"]);
    h += optSelect("dir", t("dir"), ["ltr", "rtl"]);
    h += kinds;
    h += `<span class="hint">${esc(t(tool === "row" ? "hint.row" : "hint.grid"))}</span>`;
  } else if (["door", "column", "text"].includes(tool)) h += `<span class="hint">${esc(t("hint.click"))}</span>`;
  else h += `<span class="hint">${esc(t("hint.draw"))}</span>`;
  $("optbar").innerHTML = h;
  $("optbar").querySelectorAll("input,select").forEach((el) => el.addEventListener("change", () => {
    const k = el.id.slice(2); opts[k] = el.type === "number" ? +el.value : el.value;
  }));
}

/* ---------- Arayüz: kat sekmeleri ---------- */
function renderTabs() {
  $("tabs").innerHTML = doc.levels.map((l) => `<button class="tab" role="tab" data-level="${l.id}" aria-selected="${l.id === levelId}">${esc(l.name)}</button>`).join("") +
    `<button class="tab add" id="addLevel" title="${esc(t("newLevel"))}" aria-label="${esc(t("newLevel"))}">+</button>`;
}
$("tabs").addEventListener("click", (e) => {
  if (e.target.id === "addLevel") {
    const l = { id: uid(), name: t("levelN", { n: doc.levels.length + 1 }) };
    doc.levels.push(l); levelId = l.id; selected.clear(); commit(); fit(); return;
  }
  const b = e.target.closest("[data-level]");
  if (b && b.dataset.level !== levelId) { levelId = b.dataset.level; selected.clear(); render(); fit(); }
});

/* ---------- Arayüz: özellik paneli (seçime göre değişir) ---------- */
const A_ICONS = {
  left: '<path d="M4 3v18M8 7h10v3H8zM8 14h6v3H8z"/>', hcenter: '<path d="M12 3v18M6 7h12v3H6zM8 14h8v3H8z"/>', right: '<path d="M20 3v18M6 7h10v3H6zM10 14h6v3h-6z"/>',
  top: '<path d="M3 4h18M7 8v10h3V8zM14 8v6h3V8z"/>', vmiddle: '<path d="M3 12h18M7 6v12h3V6zM14 8v8h3V8z"/>', bottom: '<path d="M3 20h18M7 6v10h3V6zM14 10v6h3v-6z"/>',
  dh: '<path d="M4 4v16M20 4v16M10 8h4v8h-4z"/>', dv: '<path d="M4 4h16M4 20h16M8 10h8v4H8z"/>',
  ccw: '<path d="M4 4v6h6"/><path d="M4.5 10A8 8 0 1 1 6 16"/>', cw: '<path d="M20 4v6h-6"/><path d="M19.5 10A8 8 0 1 0 18 16"/>', flip: '<path d="M12 3v18M8 7l-5 5 5 5zM16 7l5 5-5 5z"/>',
  group: '<rect x="3" y="3" width="18" height="18" rx="2" stroke-dasharray="3 2"/><rect x="7" y="7" width="5" height="5"/><rect x="13" y="12" width="5" height="5"/>',
  ungroup: '<rect x="4" y="4" width="7" height="7"/><rect x="13" y="13" width="7" height="7"/>',
  dup: '<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V5a1 1 0 0 0-1-1H5a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h3"/>',
  del: '<path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3"/>'
};
const ib = (act, icon, title) => `<button class="btn icon" data-act="${act}" title="${esc(title)}" aria-label="${esc(title)}"><svg viewBox="0 0 24 24">${A_ICONS[icon]}</svg></button>`;
const blockOptions = (cur) => `<option value="">${esc(t("none"))}</option>` + doc.blocks.filter((b) => b.levelId === levelId).map((b) => `<option value="${b.id}" ${cur === b.id ? "selected" : ""}>${esc(b.name)}</option>`).join("");
const commonVal = (its, k) => { const v = its[0]?.[k]; return its.every((i) => i[k] === v) ? v : undefined; };

function renderPanel() {
  const sec = $("propSec");
  const its = selItems().filter((i) => i.levelId === levelId);
  let h = `<h3>${esc(t("properties"))}</h3>`;
  if (!its.length) {
    const lv = doc.levels.find((l) => l.id === levelId);
    h += `<div class="fld"><span>${esc(t("level"))}</span><input id="p_level" value="${esc(lv.name)}"></div>
      ${doc.levels.length > 1 ? `<div class="btns"><button class="btn" data-act="delLevel">${esc(t("deleteLevel"))}</button></div>` : ""}
      <div class="legend"><span><i></i>${esc(t("kind.normal"))}</span><span><i class="vip"></i>${esc(t("kind.vip"))}</span><span><i class="ext"></i>${esc(t("kind.extra"))}</span><span>♿ ${esc(t("kind.accessible"))}</span></div>
      <div class="keys"><kbd>Ctrl+S</kbd><span>${esc(t("keys.save"))}</span>
      <kbd>Ctrl+A</kbd><span>${esc(t("keys.selectAll"))}</span>
      <kbd>Ctrl+C / V / D</kbd><span>${esc(t("keys.copyPaste"))}</span>
      <kbd>Ctrl+G</kbd><span>${esc(t("keys.group"))}</span>
      <kbd>← ↑ → ↓</kbd><span>${esc(t("keys.nudge"))}</span>
      <kbd>${esc(t("keys.space"))}</kbd><span>${esc(t("keys.pan"))}</span>
      <kbd>Shift</kbd><span>${esc(t("keys.rowStep"))}</span></div>`;
    sec.innerHTML = h; bindPanel(); return;
  }
  const seats = its.filter((i) => i.type === "seat");
  const single = its.length === 1 ? its[0] : null;
  h += `<div class="selinfo">${its.length === 1 ? esc(t("type." + single.type)) : esc(t("selCount", { n: its.length }))}${seats.length && its.length > 1 ? ` · ${esc(t("seatsCount", { n: seats.length }))}` : ""}</div>`;
  if (single && single.type !== "seat") {
    if (single.type !== "wall" && single.type !== "column") h += `<div class="fld"><span>${esc(t("label"))}</span><input id="p_label" value="${esc(single.label || "")}"></div>`;
    if (RESIZABLE.has(single.type)) h += `<div class="row2"><div class="fld"><span>${esc(t("width"))} (cm)</span><input type="number" id="p_w" value="${single.w}"></div><div class="fld"><span>${esc(t("height"))} (cm)</span><input type="number" id="p_h" value="${single.h}"></div></div>`;
    if (single.type === "text") h += `<div class="fld"><span>${esc(t("fontSize"))}</span><input type="number" id="p_size" value="${single.size || 40}"></div>`;
    if (single.type === "area") h += `<div class="fld"><span>${esc(t("color"))}</span><input type="color" id="p_color" value="${esc(single.color || "#64748B")}"></div>`;
    if (single.type === "door") h += `<div class="fld"><span>${esc(t("doorKind"))}</span><select id="p_dkind"><option value="door" ${single.kind !== "exit" ? "selected" : ""}>${esc(t("door"))}</option><option value="exit" ${single.kind === "exit" ? "selected" : ""}>${esc(t("exit"))}</option></select></div>`;
  }
  if (seats.length) {
    const row = commonVal(seats, "row"), kind = commonVal(seats, "kind"), blk = commonVal(seats, "blockId");
    h += `<div class="row2">
      <div class="fld"><span>${esc(t("row"))}</span><input id="p_row" class="mono" value="${row === undefined ? "" : esc(row)}" placeholder="${row === undefined ? "…" : ""}"></div>
      ${seats.length === 1 ? `<div class="fld"><span>${esc(t("number"))}</span><input id="p_num" class="mono" value="${esc(seats[0].num ?? "")}"></div>` : `<div class="fld"><span>${esc(t("kindLabel"))}</span><select id="p_kind">${kind === undefined ? '<option value="">…</option>' : ""}${["normal", "vip", "accessible", "extra"].map((k) => `<option value="${k}" ${kind === k ? "selected" : ""}>${esc(t("kind." + k))}</option>`).join("")}</select></div>`}
    </div>`;
    if (seats.length === 1) h += `<div class="fld"><span>${esc(t("kindLabel"))}</span><select id="p_kind">${["normal", "vip", "accessible", "extra"].map((k) => `<option value="${k}" ${kind === k ? "selected" : ""}>${esc(t("kind." + k))}</option>`).join("")}</select></div>`;
    h += `<div class="fld"><span>${esc(t("block"))}</span><select id="p_block">${blk === undefined ? '<option value="__">…</option>' : ""}${blockOptions(blk ?? "")}</select></div>`;
  }
  if (single) h += `<div class="fld"><span>${esc(t("rotation"))} (°)</span><input type="number" id="p_rot" step="1" value="${Math.round(single.rotation || 0)}"></div>`;
  if (its.length > 1) {
    h += `<h3>${esc(t("alignTitle"))}</h3><div class="btns">${ib("al-left", "left", t("align.left"))}${ib("al-hcenter", "hcenter", t("align.hcenter"))}${ib("al-right", "right", t("align.right"))}${ib("al-top", "top", t("align.top"))}${ib("al-vmiddle", "vmiddle", t("align.vmiddle"))}${ib("al-bottom", "bottom", t("align.bottom"))}${ib("dist-h", "dh", t("distribute.h"))}${ib("dist-v", "dv", t("distribute.v"))}</div>`;
  }
  h += `<h3>${esc(t("transform"))}</h3><div class="btns">${ib("rot-ccw", "ccw", t("rotate.ccw"))}${ib("rot-cw", "cw", t("rotate.cw"))}${ib("flip", "flip", t("flip"))}${its.length > 1 ? ib("group", "group", t("group")) : ""}${its.some((i) => i.groupId) ? ib("ungroup", "ungroup", t("ungroup")) : ""}${ib("dup", "dup", t("duplicate"))}${ib("del", "del", t("del"))}</div>`;
  if (seats.length > 1) {
    h += `<h3>${esc(t("numberTool"))}</h3>
      <div class="row2"><div class="fld"><span>${esc(t("startNo"))}</span><input type="number" id="n_start" value="1" min="0"></div>
      <div class="fld"><span>${esc(t("numbering"))}</span><select id="n_pattern"><option value="seq">${esc(t("seq"))}</option><option value="odd">${esc(t("odd"))}</option><option value="even">${esc(t("even"))}</option></select></div></div>
      <div class="row2"><div class="fld"><span>${esc(t("dir"))}</span><select id="n_dir"><option value="ltr">${esc(t("ltr"))}</option><option value="rtl">${esc(t("rtl"))}</option></select></div>
      <div class="fld"><span>${esc(t("firstRow"))}</span><input id="n_first" class="mono" value="A" maxlength="4"></div></div>
      <label style="display:flex;gap:6px;align-items:center;color:var(--muted)"><input type="checkbox" id="n_reassign"> ${esc(t("reassignRows"))}</label>
      <div class="btns"><button class="btn primary" data-act="renumber">${esc(t("apply"))}</button></div>`;
  }
  sec.innerHTML = h; bindPanel();
}

function bindPanel() {
  const sec = $("propSec");
  const its = () => selItems().filter((i) => i.levelId === levelId);
  const on = (id, fn) => { const el = $(id); if (el) el.addEventListener("change", () => fn(el.value, el)); };
  on("p_level", (v) => { const l = doc.levels.find((x) => x.id === levelId); l.name = v.trim() || l.name; commit(); });
  on("p_label", (v) => { its()[0].label = v; commit(); });
  on("p_w", (v) => { its()[0].w = Math.max(10, +v || 10); commit(); });
  on("p_h", (v) => { its()[0].h = Math.max(10, +v || 10); commit(); });
  on("p_size", (v) => { its()[0].size = Math.max(8, +v || 40); commit(); });
  on("p_color", (v) => { its()[0].color = v; commit(); });
  on("p_dkind", (v) => { const it = its()[0]; it.kind = v; if (it.label === t("doorLbl") || it.label === t("exitLbl")) it.label = t(v === "exit" ? "exitLbl" : "doorLbl"); commit(); });
  on("p_rot", (v) => { its()[0].rotation = ((+v % 360) + 360) % 360; commit(); });
  on("p_row", (v) => { its().filter((i) => i.type === "seat").forEach((i) => (i.row = v.trim())); commit(); });
  on("p_num", (v) => { const s = its()[0]; s.num = /^\d+$/.test(v) ? +v : v; commit(); });
  on("p_kind", (v) => { if (!v) return; its().filter((i) => i.type === "seat").forEach((i) => (i.kind = v)); commit(); });
  on("p_block", (v) => { if (v === "__") return; its().filter((i) => i.type === "seat").forEach((i) => (i.blockId = v || null)); commit(); });
  sec.querySelectorAll("[data-act]").forEach((b) => b.addEventListener("click", () => act(b.dataset.act)));
}

function act(a) {
  if (a.startsWith("al-")) return alignSel(a.slice(3));
  switch (a) {
    case "dist-h": return distributeSel("h");
    case "dist-v": return distributeSel("v");
    case "rot-ccw": return rotateSel(-90);
    case "rot-cw": return rotateSel(90);
    case "flip": return flipSel();
    case "group": return groupSel();
    case "ungroup": return ungroupSel();
    case "dup": return duplicateSel();
    case "del": return deleteSel();
    case "renumber": return renumber({ start: $("n_start").value, pattern: $("n_pattern").value, dir: $("n_dir").value, reassign: $("n_reassign").checked, firstRow: $("n_first").value.trim().toUpperCase() || "A" });
    case "delLevel": {
      if (levelItems().length) { toast(t("levelNotEmpty")); return; }
      doc.levels = doc.levels.filter((l) => l.id !== levelId);
      doc.blocks = doc.blocks.filter((b) => b.levelId !== levelId);
      levelId = doc.levels[0].id; commit(); fit(); return;
    }
  }
}

/* ---------- Arayüz: blok listesi ve kapasite özeti ---------- */
function renderBlocks() {
  const sec = $("blockSec");
  const bl = doc.blocks.filter((b) => b.levelId === levelId);
  const count = (id) => doc.items.filter((i) => i.type === "seat" && i.blockId === id).length;
  const hasSeatsSel = selItems().some((i) => i.type === "seat");
  sec.innerHTML = `<h3>${esc(t("blocks"))}<button class="btn" data-bact="add" style="text-transform:none;letter-spacing:0;font-weight:500">+ ${esc(t("addBlock"))}</button></h3>` +
    (bl.length ? `<div class="blocks">${bl.map((b) => `<div class="blk" data-bid="${b.id}">
      <label class="sw" style="background:${esc(b.color)};position:relative;cursor:pointer" title="${esc(t("color"))}"><input type="color" value="${esc(b.color)}" data-bact="color" style="position:absolute;inset:0;opacity:0;width:100%;height:100%;cursor:pointer" aria-label="${esc(t("color"))}"></label>
      <input value="${esc(b.name)}" data-bact="name" aria-label="${esc(t("block"))}">
      <span class="cnt">${count(b.id)}</span>
      <button data-bact="select" title="${esc(t("selectBlock"))}" aria-label="${esc(t("selectBlock"))}">◎</button>
      ${hasSeatsSel ? `<button data-bact="assign" title="${esc(t("assignSel"))}" aria-label="${esc(t("assignSel"))}">⇲</button>` : "<span></span>"}
      <button data-bact="remove" title="${esc(t("removeBlock"))}" aria-label="${esc(t("removeBlock"))}">×</button>
    </div>`).join("")}</div>` : `<p class="note">${esc(t("noBlocks"))}</p>`);
}
$("blockSec").addEventListener("click", (e) => {
  const b = e.target.closest("[data-bact]"); if (!b) return;
  const a = b.dataset.bact; const row = b.closest("[data-bid]"); const id = row && row.dataset.bid;
  if (a === "add") {
    const used = new Set(doc.blocks.filter((x) => x.levelId === levelId).map((x) => x.color));
    const color = BLOCK_COLORS.find((c) => !used.has(c)) || BLOCK_COLORS[doc.blocks.length % BLOCK_COLORS.length];
    const nb = { id: uid(), levelId, name: t("newBlockName") + " " + (doc.blocks.filter((x) => x.levelId === levelId).length + 1), color };
    doc.blocks.push(nb);
    selItems().filter((i) => i.type === "seat").forEach((i) => (i.blockId = nb.id));
    commit();
  } else if (a === "select") { selected = new Set(doc.items.filter((i) => i.blockId === id).map((i) => i.id)); setTool("select", true); }
  else if (a === "assign") { selItems().filter((i) => i.type === "seat").forEach((i) => (i.blockId = id)); commit(); }
  else if (a === "remove") { doc.items.forEach((i) => { if (i.blockId === id) i.blockId = null; }); doc.blocks = doc.blocks.filter((x) => x.id !== id); commit(); }
});
$("blockSec").addEventListener("change", (e) => {
  const el = e.target; const row = el.closest("[data-bid]"); if (!row) return;
  const b = blockById(row.dataset.bid);
  if (el.dataset.bact === "name") b.name = el.value.trim() || b.name;
  if (el.dataset.bact === "color") b.color = el.value;
  commit();
});

function stats(items) {
  const s = { fixed: 0, vip: 0, acc: 0, extra: 0 };
  for (const i of items) if (i.type === "seat") { if (i.kind === "extra") s.extra++; else { s.fixed++; if (i.kind === "vip") s.vip++; if (i.kind === "accessible") s.acc++; } }
  s.total = s.fixed + s.extra; return s;
}
function renderStats() {
  renderBlocks();
  const a = stats(levelItems()), b = stats(doc.items);
  const fmt = (n) => n.toLocaleString(numberLocale);
  const rows = [["stat.fixed", "fixed"], ["stat.vip", "vip"], ["stat.acc", "acc"], ["stat.extra", "extra"]];
  $("statSec").innerHTML = `<h3>${esc(t("capacity"))}</h3>
    <dl class="stats"><dt></dt><dd style="color:var(--muted);font-family:var(--font-ui);font-size:11px">${esc(t("thisLevel"))} / ${esc(t("allLevels"))}</dd>
    ${rows.map(([k, f]) => `<dt>${esc(t(k))}</dt><dd>${fmt(a[f])} / ${fmt(b[f])}</dd>`).join("")}
    <dt class="total">${esc(t("stat.total"))}</dt><dd class="total">${fmt(a.total)} / ${fmt(b.total)}</dd></dl>`;
}

function updateHistoryBtns() { $("undoBtn").disabled = !undoStack.length; $("redoBtn").disabled = !redoStack.length; }
function fmtM(cm) { return (cm / 100).toLocaleString(numberLocale, { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + " m"; }
let toastTimer;
function toast(msg, ms = 2200) { const el = $("toast"); el.textContent = msg; el.hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => (el.hidden = true), ms); }

/* ---------- Başlık düğmeleri ve JSON penceresi ---------- */
$("undoBtn").onclick = undo; $("redoBtn").onclick = redo;
$("zoomIn").onclick = () => setZoom(stage.scaleX() * 1.25);
$("zoomOut").onclick = () => setZoom(stage.scaleX() / 1.25);
$("fitBtn").onclick = fit;
$("snapBtn").onclick = () => { opts.snap = !opts.snap; $("snapBtn").setAttribute("aria-pressed", opts.snap); };
$("panelBtn").onclick = () => $("panel").classList.toggle("open");
$("dataBtn").onclick = () => { $("jsonArea").value = JSON.stringify(doc, null, 2); $("modal").hidden = false; $("jsonArea").focus(); };
$("closeModal").onclick = () => ($("modal").hidden = true);
$("modal").addEventListener("click", (e) => { if (e.target.id === "modal") $("modal").hidden = true; });
$("copyJson").onclick = () => {
  const txt = $("jsonArea").value;
  const fallback = () => { $("jsonArea").select(); toast(t("copyFail")); };
  try { navigator.clipboard.writeText(txt).then(() => toast(t("copied")), fallback); } catch (e) { fallback(); }
};
function importText(txt) {
  try { const d = JSON.parse(txt); if (!validDoc(d)) throw 0; doc = d; levelId = doc.levels[0].id; selected.clear(); commit(); fit(); $("modal").hidden = true; toast(t("imported")); }
  catch (e) { toast(t("importFail")); }
}
$("importJson").onclick = () => importText($("jsonArea").value);
$("fileIn").onchange = (e) => { const f = e.target.files[0]; if (!f) return; const r = new FileReader(); r.onload = () => importText(r.result); r.readAsText(f); e.target.value = ""; };
$("loadSample").onclick = () => { doc = sampleDoc(); levelId = doc.levels[0].id; selected.clear(); opts.nextRow = "M"; commit(); fit(); $("modal").hidden = true; };
$("newBlank").onclick = () => { doc = blankDoc(); levelId = doc.levels[0].id; selected.clear(); opts.nextRow = "A"; commit(); fit(); $("modal").hidden = true; };

/* ---------- Kaydetme ----------
   savedSnap: sunucudaki son hâlin JSON'u. lastSnap (geri al/yinele) ondan farklıysa kaydedilmemiş değişiklik vardır.
   Kaydet → POST ?handler=Save&baseVersion=N, gövde kroki JSON'u. Sunucunun cevabı:
     saved / unchanged → version güncellenir · conflict → bu arada başkası yeni sürüm kaydetmiş · invalid → message
   Kaydedilmemiş değişiklik varken sayfadan çıkılmak istenirse tarayıcı uyarır. */
let version = CONFIG.version;       // editörün temel aldığı (sunucudaki güncel) sürüm; hiç yoksa 0
let savedSnap = "";
let saving = false;

const isDirty = () => lastSnap !== savedSnap;
function markChanged() { updateSaveState(); }
function updateSaveState(text) {
  const dirty = isDirty();
  $("saveState").textContent = text || (dirty ? t("unsaved") : version ? t("versionN", { n: version }) : t("noVersion"));
  $("saveState").classList.toggle("dirty", dirty);
  $("saveBtn").disabled = saving;
}
async function saveToServer() {
  if (saving) return;
  saving = true;
  updateSaveState(t("saving"));
  const body = JSON.stringify(doc);
  let message = t("saveFailed");
  try {
    const res = await fetch(`${CONFIG.saveUrl}&baseVersion=${version}`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "RequestVerificationToken": CONFIG.token },
      body
    });
    const r = await res.json().catch(() => ({}));
    if (r.status === "saved" || r.status === "unchanged") {
      version = r.version; savedSnap = body;
      $("oldVersion").hidden = true;
      message = t(r.status, { n: version });
    } else if (r.status === "conflict") {
      message = t("conflict", { n: r.version });
    } else if (r.message) {
      message = r.message;
    }
  } catch (e) { /* bağlantı hatası: varsayılan mesaj */ }
  saving = false;
  updateSaveState();
  toast(message, 6000);
}
$("saveBtn").onclick = saveToServer;
window.addEventListener("beforeunload", (e) => { if (isDirty()) { e.preventDefault(); e.returnValue = ""; } });

/* ---------- Klavye kısayolları ---------- */
const KEYMAP = { v: "select", h: "hand", s: "seat", r: "row", b: "grid", p: "stage", a: "area", w: "wall", d: "door", c: "column", t: "text" };
window.addEventListener("keydown", (e) => {
  const tag = (e.target.tagName || "").toLowerCase();
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") { e.preventDefault(); if (["input", "select"].includes(tag)) e.target.blur(); saveToServer(); return; }
  if (["input", "textarea", "select"].includes(tag)) { if (e.key === "Escape") e.target.blur(); return; }
  if (!$("modal").hidden) { if (e.key === "Escape") $("modal").hidden = true; return; }
  const mod = e.ctrlKey || e.metaKey, k = e.key.toLowerCase();
  if (e.code === "Space") { if (!spaceDown) { spaceDown = true; host.classList.add("pan"); setDraggable(false); } e.preventDefault(); return; }
  if (mod && k === "s") { e.preventDefault(); saveToServer(); return; }
  if (mod && k === "z") { e.preventDefault(); e.shiftKey ? redo() : undo(); return; }
  if (mod && k === "y") { e.preventDefault(); redo(); return; }
  if (mod && k === "a") { e.preventDefault(); if (tool !== "select") setTool("select", true); selectAll(); return; }
  if (mod && k === "c") { copySel(); return; }
  if (mod && k === "v") { e.preventDefault(); pasteItems(clipboard && JSON.parse(JSON.stringify(clipboard)), 60); return; }
  if (mod && k === "d") { e.preventDefault(); duplicateSel(); return; }
  if (mod && k === "g") { e.preventDefault(); e.shiftKey ? ungroupSel() : groupSel(); return; }
  if (mod) return;
  if (e.key === "Delete" || e.key === "Backspace") { e.preventDefault(); deleteSel(); return; }
  if (e.key === "Escape") { if (drawing) { drawing = null; preview.destroyChildren(); uiLayer.batchDraw(); } else { selected.clear(); setTool("select", true); } return; }
  if (e.key.startsWith("Arrow")) {
    e.preventDefault(); const st = e.shiftKey ? 10 : 1;
    nudge(e.key === "ArrowLeft" ? -st : e.key === "ArrowRight" ? st : 0, e.key === "ArrowUp" ? -st : e.key === "ArrowDown" ? st : 0); return;
  }
  if (e.key === "+" || e.key === "=") return setZoom(stage.scaleX() * 1.25);
  if (e.key === "-") return setZoom(stage.scaleX() / 1.25);
  if (e.key === "0") return fit();
  if (KEYMAP[k]) setTool(KEYMAP[k]);
});
window.addEventListener("keyup", (e) => { if (e.code === "Space") { spaceDown = false; host.classList.toggle("pan", tool === "hand"); setDraggable(tool === "select"); } });
host.addEventListener("contextmenu", (e) => e.preventDefault());

/* Tema değişince tuval renklerini CSS değişkenlerinden yeniden oku */
const reTheme = () => { TC = themeColors(); render(); };
try { matchMedia("(prefers-color-scheme: dark)").addEventListener("change", reTheme); } catch (e) {}
new MutationObserver(reTheme).observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });

/* ---------- Başlangıç: sayfanın verdiği krokiyi aç (güncel sürüm, istenen eski sürüm ya da boş kroki) ---------- */
doc = JSON.parse($("planData").textContent);
levelId = doc.levels[0].id;
resetHistory();
savedSnap = CONFIG.openedVersion ? "" : lastSnap;   // eski sürüm açıldıysa kaydedilene kadar "kaydedilmemiş" sayılır
resize();
renderTools(); renderOptions(); render(); viewChanged();
updateSaveState();
fit();
if (document.fonts && document.fonts.ready) document.fonts.ready.then(() => { TC = themeColors(); render(); });
// Tarayıcı testleri ve hata ayıklama için: konsolda __salon.doc ile kroki verisine bakılabilir.
window.__salon = { get doc() { return doc; }, selected: () => [...selected], setTool, stage, act };
})();
