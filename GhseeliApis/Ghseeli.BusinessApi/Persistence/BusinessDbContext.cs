using Ghseeli.BusinessApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Ghseeli.BusinessApi.Persistence;

public class BusinessDbContext : IdentityDbContext<BusinessUser, IdentityRole<Guid>, Guid>
{
    public BusinessDbContext(DbContextOptions<BusinessDbContext> options)
        : base(options)
    {
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<BusinessUserAssignment> BusinessUserAssignments =>
        Set<BusinessUserAssignment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<BusinessUser>(entity =>
        {
            entity.Property(user => user.FullName)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(user => user.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()")
                .IsRequired();
        });

        builder.Entity<Company>(entity =>
        {
            entity.Property(company => company.NameAr).HasMaxLength(200).IsRequired();
            entity.Property(company => company.NameHe).HasMaxLength(200).IsRequired();
            entity.Property(company => company.Phone).HasMaxLength(30);
            entity.Property(company => company.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
        });

        builder.Entity<Branch>(entity =>
        {
            entity.Property(branch => branch.NameAr).HasMaxLength(200).IsRequired();
            entity.Property(branch => branch.NameHe).HasMaxLength(200).IsRequired();
            entity.Property(branch => branch.AddressAr).HasMaxLength(300).IsRequired();
            entity.Property(branch => branch.AddressHe).HasMaxLength(300).IsRequired();
            entity.HasOne(branch => branch.Company)
                .WithMany(company => company.Branches)
                .HasForeignKey(branch => branch.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(branch => new { branch.CompanyId, branch.IsActive });
        });

        builder.Entity<BusinessUserAssignment>(entity =>
        {
            entity.HasOne(assignment => assignment.User)
                .WithMany(user => user.Assignments)
                .HasForeignKey(assignment => assignment.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(assignment => assignment.Company)
                .WithMany(company => company.Assignments)
                .HasForeignKey(assignment => assignment.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(assignment => assignment.Branch)
                .WithMany(branch => branch.Assignments)
                .HasForeignKey(assignment => assignment.BranchId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(assignment => new
            {
                assignment.UserId,
                assignment.CompanyId,
                assignment.BranchId
            }).IsUnique();
        });
    }
}
