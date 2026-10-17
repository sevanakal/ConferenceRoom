using ConferenceRoom.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConferenceRoom.Web.Pages.Satis;

/// <summary>Satış personeli çıkışı (/satis/cikis). Yalnızca POST ile; yalnızca satış çerezini siler.</summary>
public class CikisModel : PageModel
{
    public IActionResult OnGet() => LocalRedirect("/satis");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(AuthSchemes.Sales);
        return LocalRedirect("/giris");
    }
}
