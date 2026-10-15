# Salon Kroki Editörü (prototip)

Konferans salonunun 2B krokisini çizmek için tarayıcıda çalışan editör. Tek dosyadır: `index.html`.
Sunucu ya da kurulum gerekmez; dosyayı çift tıklayıp tarayıcıda açmak yeterli (internet bağlantısı
Konva.js kütüphanesi ve yazı tipleri için gerekir).

Açılışta 674 kişilik örnek bir salon yüklenir. Yapılan değişiklikler tarayıcıya kaydedilir;
"{ }" düğmesinden kroki verisi JSON olarak kopyalanabilir, içe aktarılabilir ya da boş krokiyle başlanabilir.

## Neler yapılabiliyor

- **Çizim:** tek koltuk (S), koltuk sırası (R), koltuk bloğu (B), sahne (P), bölge (A), duvar (W), kapı/acil çıkış (D), sütun (C), yazı (T).
- **Kavisli sıra:** seçenek çubuğundaki "Kavis" değeri sırayı sahneye dönük bir yaya çevirir.
- **Seçim:** bir koltuğa tıklamak bloğun tamamını seçer; Alt+tık tek koltuk, Shift ekle/çıkar, boş alanda sürükleme alan seçimi.
- **Düzenleme:** taşıma, döndürme, boyutlandırma, hizalama, eşit dağıtma, yansıtma, gruplama (Ctrl+G), çoğaltma (Ctrl+D), geri al/yinele.
- **Akıllı kılavuzlar:** sürüklerken diğer nesnelerin kenar ve merkezleriyle hizalama çizgileri.
- **Numaralama:** sıralı, tek ya da çift numara; soldan sağa ya da sağdan sola; sıra harflerini yeniden verme.
- **Koltuk türleri:** normal, VIP/protokol, engelli yeri, ekstra sandalye. Kapasite özeti bunları ayrı sayar.
- **Katlar:** Parter, Balkon gibi katlar sekme olarak eklenir; bloklar kata bağlıdır.
- **Dil:** TR/EN düğmesi. Metinlerin tamamı `I18N` sözlüğünden gelir.

## Veri modeli

Ana projede veritabanı tablolarına dönüşecek yapı (tüm ölçüler cm, `x,y` nesnenin merkezi):

```json
{
  "schema": 1, "unit": "cm", "name": "Ana Salon",
  "levels": [{ "id": "…", "name": "Parter" }],
  "blocks": [{ "id": "…", "levelId": "…", "name": "Orta Blok", "color": "#1D5FBF" }],
  "items": [
    { "id": "…", "levelId": "…", "type": "seat", "x": 760, "y": 640, "rotation": 15.5,
      "row": "A", "num": 1, "kind": "vip", "blockId": "…", "groupId": "…" },
    { "id": "…", "levelId": "…", "type": "stage", "x": 1200, "y": 230, "w": 1500, "h": 420, "rotation": 0, "label": "SAHNE" }
  ]
}
```

## Ana projeye taşınırken

- `I18N` nesnesi → `ConferenceRoom.Localization` içindeki `tr.json` / `en.json`.
- `localStorage` kaydı → Web API çağrısı; her kayıt yeni bir kroki sürümü oluşturur.
- Konva.js CDN yerine projeye dahil edilir (çevrimdışı çalışabilmesi için).
