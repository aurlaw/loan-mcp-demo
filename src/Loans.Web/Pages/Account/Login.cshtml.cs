using System.ComponentModel.DataAnnotations;
using Loans.Core.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Loans.Web.Pages.Account;

[AllowAnonymous]
public class LoginModel(SignInManager<ApplicationUser> signInManager, ILogger<LoginModel> logger) : PageModel
{
    public const string InvalidCredentialsMessage = "Invalid email or password.";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(SafeReturnUrl());
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(Input.Email, Input.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            logger.LogInformation("User {Email} signed in", Input.Email);
            return LocalRedirect(SafeReturnUrl());
        }

        if (result.IsLockedOut)
        {
            logger.LogWarning("User {Email} is locked out", Input.Email);
        }

        // Generic message for every failure (unknown user, wrong password, locked out).
        ErrorMessage = InvalidCredentialsMessage;
        return Page();
    }

    private string SafeReturnUrl() =>
        !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }
}
