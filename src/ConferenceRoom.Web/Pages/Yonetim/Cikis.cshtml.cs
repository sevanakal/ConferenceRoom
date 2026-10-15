using ConferenceRoom.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConferenceRoom.Web.Pages.Yonetim;

/// <summary>Yönetim panelinden çıkış. Yalnızca POST ile (başka bir siteden bağlantıyla çıkış yaptırılamasın).</summary>
public class CikisModel : PageModel
{
    public IActionResult OnGet() => LocalRedirect("/yonetim");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(AuthSchemes.Staff);
        return LocalRedirect("/yonetim/giris");
    }
}
