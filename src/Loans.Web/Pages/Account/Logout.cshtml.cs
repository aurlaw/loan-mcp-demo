using Loans.Core.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Loans.Web.Pages.Account;

/// <summary>POST-only sign-out (antiforgery is validated automatically for Razor Pages POSTs).</summary>
public class LogoutModel(SignInManager<ApplicationUser> signInManager) : PageModel
{
    public IActionResult OnGet() => StatusCode(StatusCodes.Status405MethodNotAllowed);

    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();
        return RedirectToPage("/Account/Login");
    }
}
