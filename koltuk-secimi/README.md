# Koltuk Seçimi ve Bilet (prototip)

İzleyicinin salon krokisinden koltuk seçip bilet aldığı ekran. Tek dosyadır: `index.html`.
Sunucu ya da kurulum gerekmez; dosyayı tarayıcıda açmak yeterli (internet bağlantısı Konva.js
kütüphanesi ve yazı tipleri için gerekir).

Kroki verisi [salon kroki editörünün](../salon-kroki-editoru/) ürettiği yapıdır. Aynı tarayıcıda editörde
kayıtlı bir kroki varsa o açılır, yoksa editördeki örnek salon yüklenir. Editörün "{ }" penceresinden
kopyalanan JSON, "Kroki yükle" düğmesiyle de açılabilir.

Fiyatlar, kategoriler ve satılmış koltuklar şimdilik örnek veridir; gerçek ödeme alınmaz, e-posta gönderilmez.

## Neler yapılabiliyor

- **Seçim:** koltuğa dokunarak seçme/bırakma (en fazla 10). Üzerine gelince blok, sıra, numara, kategori ve fiyat görünür.
- **Kategoriler:** orta blok 1. kategori, parterdeki diğer bloklar 2. kategori, balkon ve ekstra sandalyeler 3. kategori.
  VIP/protokol koltukları satışa kapalıdır, satılmış koltuklar gri görünür.
- **En iyi yer:** kişi sayısı seçilip "Bul"a basılınca aynı sırada yan yana boş koltuklardan sahneye en iyi bakanı seçilir
  (sahnenin ortasından yaklaşık 6,5 m geride bir nokta ideal sayılır; balkon ve ekstra sandalye puan kaybeder).
- **Katlar:** Parter, Balkon gibi katlar sekme olarak görünür, her sekmede boş koltuk sayısı yazar.
- **Satın alma:** 1) koltuklar ve bilet türü (tam / öğrenci %60), 2) ad, e-posta, telefon ve KVKK onayı, 3) rezervasyon kodu.
  Seçilen koltuklar 10 dakika ayrılır, süre dolunca bırakılır.
- **Telefon:** 860 px'ten dar ekranda kroki tam ekran olur, sepet alttan açılır.
- **Dil ve tema:** TR/EN düğmesi, sistem ayarına göre koyu tema. Metinlerin tamamı `I18N` sözlüğünden gelir.

## Ana projeye taşınırken

- `I18N` nesnesi → `ConferenceRoom.Localization` içindeki `tr.json` / `en.json` (`booking.*` bölümü).
- `buildEvent()` içindeki örnek kategori, fiyat ve koltuk durumları → etkinliğin bağlı olduğu kroki sürümü ve Web API.
- Koltuk ayırma ve 10 dakikalık süre sunucuda tutulmalı; aynı koltuğu iki kişinin alamaması veritabanında garanti edilmeli.
- Ödeme adımına ödeme sağlayıcısı eklenir; ücretsiz ya da bağışlı etkinlikte bu adım atlanır.
- Konva.js CDN yerine projeye dahil edilir.
