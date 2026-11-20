using Loans.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Loans.Core.Data.Configurations;

internal sealed class BorrowerConfiguration : IEntityTypeConfiguration<Borrower>
{
    public void Configure(EntityTypeBuilder<Borrower> builder)
    {
        builder.ToTable("Borrowers");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.LegalName).IsRequired().HasMaxLength(200);
        builder.Property(b => b.EntityType).HasMaxLength(20);
        builder.Property(b => b.Industry).IsRequired().HasMaxLength(100);
        builder.Property(b => b.State).IsRequired().HasMaxLength(2).IsFixedLength();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(b => b.LinkedUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => b.LegalName);
        builder.HasIndex(b => b.LinkedUserId);
    }
}
