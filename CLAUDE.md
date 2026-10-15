# ConferenceRoom: proje hafızası

Bu dosya, projede çalışan Claude oturumlarının (ve geliştiricilerin) projeyi baştan öğrenmeden
devam edebilmesi için tutulur. Claude oturumlar arasında hafıza tutmaz; kalıcı bilgi buradadır.
Yeni bir karar alındığında ya da bir kural değiştiğinde bu dosya aynı PR içinde güncellenir.

## Proje

Konferans salonu yönetim sistemi. Açık kaynak (MIT), ücretsiz; yalnızca bağış kabul edilecek.
Proje sahibi ve mimar: Sevan (GitHub: `sevanakal`). Kodun tamamını Claude yazar, Sevan inceler ve birleştirir.
Arayüz dili Türkçe; kullanıcı ile yazışma Türkçe.

## Alınmış kararlar

| Konu | Karar |
| --- | --- |
| Altyapı | .NET 10 LTS, ASP.NET Core (Razor Pages + Web API). Blazor kullanılmıyor: ön yüz düz HTML/CSS kalsın, Sevan tasarımı doğrudan uygulayabilsin. |
| Veritabanı | MySQL, EF Core ile (Pomelo sağlayıcısı). |
| Mobil | Aynı Web API'yi kullanacak. Önce PWA, gerekirse .NET MAUI. |
| Barındırma | Linux + Docker. Sevan'ın bilgisayarı Windows; Docker Desktop ile aynı şekilde çalışır. Visual Studio ile açılabilir olmalı. |
| Kurum | Başta tek kurum, ama her tabloda baştan kurum alanı (`OrganizationId`) bulunur; ileride çok kuruma geçilebilir. |
| Roller | Sabit kodlanmaz. Rol ve yetkiler veritabanında; yeni rol kod değiştirmeden eklenebilmeli. Başlangıç rolleri: Yönetici, Salon sorumlusu, Rezervasyon yapan kullanıcı. |
| Rezervasyon | Rezervasyonun birimi **etkinlik**tir; her etkinlik bir salonda yapılır. Onaylı mı anında mı kuralı etkinlik türünde varsayılan olarak tanımlanır, tek tek etkinlikte değiştirilebilir. |
| Dil | Ayrı `ConferenceRoom.Localization` kütüphanesi. Tüm buton, metin ve uyarılar `tr.json`, `en.json` gibi dosyalardan gelir; arayüzde sabit metin yok. Mobil aynı dosyaları API'den alır. |
| Lisans | MIT (depoda `LICENSE`). |
| Tasarım | Grafik tasarım Sevan'da. Kod tarafı renkleri ve yazı tiplerini CSS değişkenlerinden okur ki tasarım kolayca değiştirilsin. |
| Salon krokisi | Projenin en önemli parçası. Photoshop tarzı 2B editör: koltuk sıraları, kavisli sıralar, bloklar, katlar (balkon), hizalama, gruplama, numaralama, ekstra sandalye. Kroki her değiştiğinde yeni sürüm olarak saklanır; etkinlik o anki sürüme bağlanır, böylece sonradan eklenen sandalye geçmiş etkinlikleri bozmaz. |

## Depo yapısı

```
salon-kroki-editoru/   Kroki editörünün bağımsız prototipi (tek HTML dosyası). Sevan'ın onayladığı hâli.
                       Ana projeye taşınana kadar referans olarak burada durur. Bkz. salon-kroki-editoru/README.md
src/                   (sıradaki adım) ASP.NET Core çözümü
```

## Çalışma kuralları

- Her iş ayrı bir dalda yapılır ve PR olarak açılır; `main`'e doğrudan push edilmez. Sevan inceleyip birleştirir.
- Kod yorumları Türkçe. Her dosyanın başında ne işe yaradığı, her bölümün başında ne yaptığı yazılır.
  Amaç: projeye aylar sonra dönen biri (Claude dahil) dosyayı baştan okumadan yolunu bulabilsin.
- Karmaşıklığı büyütmeden ilerle: bir dosya çok büyürse bölünür, tekrar eden kod ortak yere alınır,
  kullanılmayan kod silinir. "Sonra temizleriz" denmez.
- Kullanıcıya görünen her metin dil dosyasından gelir (prototipte `I18N` nesnesi).
- Ölçüler kroki verisinde santimetre olarak tutulur.
