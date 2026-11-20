using Loans.Core.Common;

namespace Loans.Core.Domain;

public sealed class Borrower
{
    private Borrower()
    {
        LegalName = null!;
        Industry = null!;
        State = null!;
    }

    public int Id { get; private set; }
    public string LegalName { get; private set; }
    public EntityType EntityType { get; private set; }
    public string Industry { get; private set; }
    public int YearsInBusiness { get; private set; }
    public string State { get; private set; }
    public string? LinkedUserId { get; private set; }

    public static Result<Borrower> Create(
        string legalName,
        EntityType entityType,
        string industry,
        int yearsInBusiness,
        string state,
        string? linkedUserId = null)
    {
        if (string.IsNullOrWhiteSpace(legalName))
        {
            return Result<Borrower>.Failure(new Error("borrower.invalid_legal_name", "Legal name is required."));
        }

        if (string.IsNullOrWhiteSpace(industry))
        {
            return Result<Borrower>.Failure(new Error("borrower.invalid_industry", "Industry is required."));
        }

        if (yearsInBusiness < 0)
        {
            return Result<Borrower>.Failure(new Error("borrower.invalid_years_in_business", "Years in business cannot be negative."));
        }

        if (state is not { Length: 2 } || !state.All(char.IsAsciiLetterUpper))
        {
            return Result<Borrower>.Failure(new Error("borrower.invalid_state", "State must be two uppercase letters."));
        }

        return Result<Borrower>.Success(new Borrower
        {
            LegalName = legalName.Trim(),
            EntityType = entityType,
            Industry = industry.Trim(),
            YearsInBusiness = yearsInBusiness,
            State = state,
            LinkedUserId = string.IsNullOrWhiteSpace(linkedUserId) ? null : linkedUserId,
        });
    }
}
