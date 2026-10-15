using ConferenceRoom.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConferenceRoom.Web.Pages.Hesap;

/// <summary>İzleyici çıkışı. Yalnızca POST ile; yönetim çerezine dokunmaz.</summary>
public class CikisModel : PageModel
{
    public IActionResult OnGet() => LocalRedirect("/");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(AuthSchemes.Customer);
        return LocalRedirect("/");
    }
}
