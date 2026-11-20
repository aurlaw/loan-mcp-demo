using Microsoft.AspNetCore.Identity;

namespace Loans.Core.Data;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
}
