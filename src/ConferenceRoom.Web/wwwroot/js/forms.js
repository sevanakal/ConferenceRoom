// Form yardımcıları (giriş/kayıt ve yönetim formları ortak).
// Şifre alanındaki göz düğmesi: şifreyi göster/gizle. Düğme metinleri data-show / data-hide alanlarından gelir.
document.querySelectorAll("[data-toggle-password]").forEach(function (btn) {
    btn.addEventListener("click", function () {
        var input = document.getElementById(btn.getAttribute("data-toggle-password"));
        var show = input.type === "password";
        input.type = show ? "text" : "password";
        btn.setAttribute("aria-pressed", show ? "true" : "false");
        btn.setAttribute("aria-label", show ? btn.dataset.hide : btn.dataset.show);
        btn.title = btn.getAttribute("aria-label");
    });
});

// Onay isteyen formlar (ör. rol silme): <form data-confirm="Emin misiniz?">
document.querySelectorAll("form[data-confirm]").forEach(function (form) {
    form.addEventListener("submit", function (e) {
        if (!window.confirm(form.dataset.confirm)) e.preventDefault();
    });
});
