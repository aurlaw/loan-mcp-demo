using Loans.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Loans.Core.Data.Configurations;

internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    private static readonly ValueComparer<List<PolicyException>> ExceptionsComparer = new(
        (a, b) => a!.SequenceEqual(b!),
        list => list.Aggregate(0, (hash, e) => HashCode.Combine(hash, e)),
        list => list.ToList());

    public void Configure(EntityTypeBuilder<Application> builder)
    {
        builder.ToTable("Applications");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.AssignedOfficerUserId).IsRequired();
        builder.Property(a => a.Product).HasMaxLength(20);
        builder.Property(a => a.Purpose).IsRequired().HasMaxLength(500);
        builder.Property(a => a.Status).HasMaxLength(20);

        builder.HasOne<Borrower>()
            .WithMany()
            .HasForeignKey(a => a.BorrowerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.AssignedOfficerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.Status);

        builder.OwnsOne(a => a.Financials, f =>
        {
            f.Property(x => x.AnnualRevenueCents).HasColumnName("AnnualRevenueCents");
            f.Property(x => x.NetOperatingIncomeCents).HasColumnName("NetOperatingIncomeCents");
            f.Property(x => x.ExistingAnnualDebtServiceCents).HasColumnName("ExistingAnnualDebtServiceCents");
        });
        builder.Navigation(a => a.Financials).IsRequired();

        builder.OwnsOne(a => a.Collateral, c =>
        {
            c.Property(x => x.Type).HasColumnName("CollateralType").HasMaxLength(20);
            c.Property(x => x.AppraisedValueCents).HasColumnName("CollateralAppraisedValueCents");
        });
        builder.Navigation(a => a.Collateral).IsRequired();

        // Owned (not a separate aggregate) so ApplyUnderwriting can replace the latest result in place.
        builder.OwnsOne(a => a.UnderwritingResult, uw =>
        {
            uw.ToTable("UnderwritingResults");
            uw.WithOwner().HasForeignKey(r => r.ApplicationId);
            uw.HasKey(r => r.ApplicationId);

            uw.Property(r => r.Recommendation).HasMaxLength(30);
            uw.Property(r => r.RunByUserId).IsRequired();

            uw.Ignore(r => r.Exceptions);
            uw.Property<List<PolicyException>>("_exceptions")
                .HasColumnName("Exceptions")
                .HasConversion(
                    list => string.Join(',', list),
                    value => value.Length == 0
                        ? new List<PolicyException>()
                        : value.Split(',', StringSplitOptions.None).Select(Enum.Parse<PolicyException>).ToList(),
                    ExceptionsComparer)
                .IsRequired();

            uw.HasIndex(r => r.RunAt);
        });

        builder.OwnsOne(a => a.Decision, d =>
        {
            d.ToTable("Decisions");
            d.WithOwner().HasForeignKey(x => x.ApplicationId);
            d.HasKey(x => x.ApplicationId);

            d.Property(x => x.Outcome).HasMaxLength(30);
            d.Property(x => x.Conditions).HasMaxLength(2000);
            d.Property(x => x.DecidedByUserId).IsRequired();
        });
    }
}
