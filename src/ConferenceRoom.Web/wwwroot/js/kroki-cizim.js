/*
  Kroki çizimi: kroki editörü (kroki-editor.js) ve koltuk ayarları ekranı (koltuk-ayarlari.js) ortak kullanır.
  Konva.js'ten sonra, kullanan betikten önce yüklenir; window.KrokiCizim nesnesini tanımlar.
  Tüm ölçüler santimetredir; x,y öğenin merkezidir. Renkler sayfanın CSS değişkenlerinden okunur (css/kroki-editor.css başı).

  Bölümler: sabitler → tema renkleri → koltuk → yapı öğeleri (sahne, duvar, kapı...) → ızgara → görünüm (yakınlaştırma, sığdırma)
*/
window.KrokiCizim = (() => {
"use strict";

/* ---------- Sabitler ---------- */
const SEAT_W = 52, SEAT_H = 48;               // cm; koltuk üstünde "A-12" gibi sıra-numara etiketi sığacak kadar
// Çizim sırası: alttan üste (bölge en altta, yazı en üstte)
const ORDER = { area: 0, stage: 1, wall: 2, column: 3, door: 4, seat: 5, text: 6 };

/* ---------- Tema renkleri (koyu/açık temaya göre CSS değişkenlerinden) ---------- */
const css = (name) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
function themeColors() {
  return {
    ink: css("--ink"), muted: css("--muted"), paper: css("--paper"), accent: css("--accent"), seat: css("--seat"), seatStroke: css("--seat-stroke"),
    stage: css("--stage"), stageText: css("--stage-text"), exit: css("--exit"), font: css("--font-ui") || "sans-serif", mono: css("--font-data") || "monospace"
  };
}

/* ---------- Koltuk ----------
   Etiket "Sıra-No" (A-12); sıra adı yoksa yalnızca numara.
   style: { fill, text, stroke, strokeWidth, dash } — rengi çağıran belirler (editörde blok rengi, koltuk ayarlarında kategori rengi).
   Dönen gövde (body) seçim çerçevesi için kullanılır; ilk kenar rengi baseStroke/baseSW özniteliklerinde saklanır. */
function seatLabel(it) { return it.row ? it.row + "-" + (it.num ?? "") : String(it.num ?? ""); }

function drawSeat(g, it, style, TC) {
  const body = new Konva.Rect({ name: "body", x: -SEAT_W / 2, y: -SEAT_H / 2, width: SEAT_W, height: SEAT_H, cornerRadius: [12, 12, 6, 6], fill: style.fill, stroke: style.stroke, strokeWidth: style.strokeWidth, dash: style.dash || null });
  body.setAttr("baseStroke", style.stroke); body.setAttr("baseSW", style.strokeWidth);
  g.add(body);
  // sırtlık: izleyici yerel −y yönüne baktığı için sırtlık +y tarafındadır
  g.add(new Konva.Rect({ x: -SEAT_W / 2 + 4, y: SEAT_H / 2 - 11, width: SEAT_W - 8, height: 7, cornerRadius: 3, fill: "rgba(0,0,0,.28)", listening: false }));
  if (it.kind === "accessible") {
    g.add(new Konva.Text({ text: "♿", x: -SEAT_W / 2, y: -SEAT_H / 2 + 6, width: SEAT_W, align: "center", fontSize: 22, fill: style.text, listening: false }));
  } else {
    // eşit genişlikli yazı tipi; uzun etiket küçültülerek sığdırılır
    const label = seatLabel(it);
    const fontSize = Math.min(15, (SEAT_W - 6) / (0.6 * Math.max(1, label.length)));
    g.add(new Konva.Text({ text: label, x: -SEAT_W / 2, y: -SEAT_H / 2, width: SEAT_W, height: SEAT_H - 12, align: "center", verticalAlign: "middle", fontSize, fontFamily: TC.mono, fill: style.text, listening: false }));
  }
  return body;
}

/* ---------- Yapı öğeleri: sahne, bölge, duvar, kapı, sütun, yazı ---------- */
function drawFixture(g, it, TC) {
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
  return body;
}

/* ---------- Izgara: 50 cm, 1 m ve 5 m çizgileri; çok sıklaşan çizgiler çizilmez ---------- */
function makeGrid(stage) {
  return new Konva.Shape({
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
}

/* ---------- Görünüm ----------
   zoomAt: ekrandaki bir noktayı sabit tutarak ölçeği değiştirir (%5 – %400).
   fitBox: verilen dünya kutusunu (cm) tuvale ortalayarak sığdırır; bottom: alttaki bilgi çubuğu için bırakılan boşluk.
   wheel: fare tekerleği/dokunmatik yüzey: Ctrl ya da dikey kaydırma yakınlaştırır, yatay/Shift kaydırır. */
function zoomAt(stage, scale, center) {
  scale = Math.max(0.05, Math.min(4, scale));
  const old = stage.scaleX();
  center = center || { x: stage.width() / 2, y: stage.height() / 2 };
  const wp = { x: (center.x - stage.x()) / old, y: (center.y - stage.y()) / old };
  stage.scale({ x: scale, y: scale });
  stage.position({ x: center.x - wp.x * scale, y: center.y - wp.y * scale });
}

function fitBox(stage, box, bottom = 30) {
  const pad = 40, W = stage.width() - pad * 2, H = stage.height() - pad * 2 - bottom;
  const s = Math.max(0.05, Math.min(2, Math.min(W / (box.X - box.x), H / (box.Y - box.y))));
  stage.scale({ x: s, y: s });
  stage.position({ x: pad + (W - (box.X - box.x) * s) / 2 - box.x * s, y: pad + (H - (box.Y - box.y) * s) / 2 - box.y * s });
}

/* Düğümlerin (Konva.Node) dünya koordinatlarındaki ortak kutusu; düğüm yoksa null. */
function boxOf(nodes, layer) {
  const box = { x: Infinity, y: Infinity, X: -Infinity, Y: -Infinity };
  for (const n of nodes) {
    const c = n.getClientRect({ relativeTo: layer });
    box.x = Math.min(box.x, c.x); box.y = Math.min(box.y, c.y); box.X = Math.max(box.X, c.x + c.width); box.Y = Math.max(box.Y, c.y + c.height);
  }
  return isFinite(box.x) ? box : null;
}

function wheel(stage, evt, changed) {
  evt.preventDefault();
  const p = stage.getPointerPosition();
  if (evt.ctrlKey || evt.metaKey || !evt.shiftKey && Math.abs(evt.deltaX) < 1 && evt.deltaMode === 0 && Math.abs(evt.deltaY) >= 1) {
    const f = Math.exp(-evt.deltaY * (evt.ctrlKey ? 0.01 : 0.0015));
    zoomAt(stage, stage.scaleX() * f, p);
  } else {
    stage.position({ x: stage.x() - evt.deltaX, y: stage.y() - evt.deltaY });
  }
  changed();
}

return { SEAT_W, SEAT_H, ORDER, css, themeColors, seatLabel, drawSeat, drawFixture, makeGrid, zoomAt, fitBox, boxOf, wheel };
})();
