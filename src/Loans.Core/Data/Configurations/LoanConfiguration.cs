using Loans.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Loans.Core.Data.Configurations;

internal sealed class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        builder.ToTable("Loans");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.LoanNumber).IsRequired().HasMaxLength(20);
        builder.Property(l => l.Product).HasMaxLength(20);
        builder.Property(l => l.Status).HasMaxLength(20);

        builder.HasIndex(l => l.LoanNumber).IsUnique();

        builder.HasOne<Borrower>()
            .WithMany()
            .HasForeignKey(l => l.BorrowerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Application>()
            .WithMany()
            .HasForeignKey(l => l.SourceApplicationId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(l => l.Payments)
            .WithOne()
            .HasForeignKey(p => p.LoanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(l => l.Payments)
            .HasField("_payments")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
