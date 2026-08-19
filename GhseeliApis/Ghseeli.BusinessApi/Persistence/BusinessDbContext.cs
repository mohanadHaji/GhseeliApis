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
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<ServiceOffering> ServiceOfferings => Set<ServiceOffering>();
    public DbSet<AddonGroup> AddonGroups => Set<AddonGroup>();
    public DbSet<AddonChoice> AddonChoices => Set<AddonChoice>();
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
            entity.Property(company => company.NameHe).HasMaxLength(200);
            entity.Property(company => company.Phone).HasMaxLength(30);
            entity.Property(company => company.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
        });

        builder.Entity<Branch>(entity =>
        {
            entity.Property(branch => branch.NameAr).HasMaxLength(200).IsRequired();
            entity.Property(branch => branch.NameHe).HasMaxLength(200);
            entity.Property(branch => branch.AddressAr).HasMaxLength(300).IsRequired();
            entity.Property(branch => branch.AddressHe).HasMaxLength(300);
            entity.HasOne(branch => branch.Company)
                .WithMany(company => company.Branches)
                .HasForeignKey(branch => branch.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(branch => new { branch.CompanyId, branch.IsActive });
        });

        builder.Entity<ServiceCategory>(entity =>
        {
            entity.Property(category => category.NameAr)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(category => category.NameHe)
                .HasMaxLength(200);
            entity.Property(category => category.DescriptionAr)
                .HasMaxLength(1000);
            entity.Property(category => category.DescriptionHe)
                .HasMaxLength(1000);
            entity.Property(category => category.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.HasOne(category => category.Company)
                .WithMany(company => company.Categories)
                .HasForeignKey(category => category.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(category => new { category.CompanyId, category.DisplayOrder });
            entity.HasIndex(category => new { category.CompanyId, category.IsActive });
        });

        builder.Entity<ServiceOffering>(entity =>
        {
            entity.Property(offering => offering.NameAr)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(offering => offering.NameHe)
                .HasMaxLength(200);
            entity.Property(offering => offering.DescriptionAr)
                .HasMaxLength(1000);
            entity.Property(offering => offering.DescriptionHe)
                .HasMaxLength(1000);
            entity.Property(offering => offering.ImageUrl)
                .HasMaxLength(500);
            entity.Property(offering => offering.ReferenceCode)
                .HasMaxLength(100);
            entity.Property(offering => offering.BasePrice)
                .HasPrecision(18, 2);
            entity.Property(offering => offering.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.HasOne(offering => offering.Category)
                .WithMany(category => category.Offerings)
                .HasForeignKey(offering => offering.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(offering => offering.Branch)
                .WithMany(branch => branch.ServiceOfferings)
                .HasForeignKey(offering => offering.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(offering => new { offering.CategoryId, offering.DisplayOrder });
            entity.HasIndex(offering => new { offering.CategoryId, offering.IsActive });
            entity.HasIndex(offering => new { offering.BranchId, offering.IsActive });
        });

        builder.Entity<AddonGroup>(entity =>
        {
            entity.Property(group => group.NameAr)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(group => group.NameHe)
                .HasMaxLength(200);
            entity.Property(group => group.DescriptionAr)
                .HasMaxLength(1000);
            entity.Property(group => group.DescriptionHe)
                .HasMaxLength(1000);
            entity.Property(group => group.SelectionType)
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(group => group.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.HasOne(group => group.ServiceOffering)
                .WithMany(offering => offering.AddonGroups)
                .HasForeignKey(group => group.ServiceOfferingId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(group => new { group.ServiceOfferingId, group.DisplayOrder });
            entity.HasIndex(group => new { group.ServiceOfferingId, group.IsActive });
        });

        builder.Entity<AddonChoice>(entity =>
        {
            entity.Property(choice => choice.NameAr)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(choice => choice.NameHe)
                .HasMaxLength(200);
            entity.Property(choice => choice.DescriptionAr)
                .HasMaxLength(1000);
            entity.Property(choice => choice.DescriptionHe)
                .HasMaxLength(1000);
            entity.Property(choice => choice.PriceAdjustment)
                .HasPrecision(18, 2);
            entity.Property(choice => choice.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");
            entity.HasOne(choice => choice.AddonGroup)
                .WithMany(group => group.Choices)
                .HasForeignKey(choice => choice.AddonGroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(choice => new { choice.AddonGroupId, choice.DisplayOrder });
            entity.HasIndex(choice => new { choice.AddonGroupId, choice.IsActive });
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
