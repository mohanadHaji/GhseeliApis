using System.ComponentModel.DataAnnotations;

namespace GhseeliApis.DTOs.User;

/// <summary>
/// Request to create a new user (Admin only)
/// </summary>
public class CreateUserRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Phone]
    [MaxLength(30)]
    public string? Phone { get; set; }

    [Required]
    [MinLength(8)]
    [MaxLength(100)]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Role to assign to the user (User, Company, Admin)
    /// Defaults to "User" if not specified
    /// </summary>
    [MaxLength(50)]
    public string? Role { get; set; }
}

/// <summary>
/// Request to update an existing user (Admin only)
/// </summary>
public class UpdateUserRequest
{
    [EmailAddress]
    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(150)]
    public string? FullName { get; set; }

    [Phone]
    [MaxLength(30)]
    public string? Phone { get; set; }

    /// <summary>
    /// Whether the user account is active
    /// </summary>
    public bool? IsActive { get; set; }

    /// <summary>
    /// Role to assign to the user (User, Company, Admin)
    /// Leave null to keep current role
    /// </summary>
    [MaxLength(50)]
    public string? Role { get; set; }
}

/// <summary>
/// User response DTO
/// </summary>
public class UserResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }

    /// <summary>
    /// New email pending verification, if any
    /// </summary>
    public string? PendingEmail { get; set; }

    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// If set, account is scheduled for permanent deletion on this date
    /// </summary>
    public DateTime? DeleteScheduledFor { get; set; }

    public List<string> Roles { get; set; } = new();
    
    /// <summary>
    /// Counts for related entities
    /// </summary>
    public int VehicleCount { get; set; }
    public int AddressCount { get; set; }
    public int BookingCount { get; set; }
    public decimal? WalletBalance { get; set; }
}

/// <summary>
/// Simplified user response for lists
/// </summary>
public class UserListResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> Roles { get; set; } = new();
}
