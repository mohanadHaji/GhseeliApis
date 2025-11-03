using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Models;

/// <summary>
/// Represents a user's wallet for storing balance
/// Has a 1:1 relationship with User
/// </summary>
public class Wallet
{
    /// <summary>
    /// Primary key is the UserId (1:1 relationship)
    /// </summary>
    [Key]
    public Guid UserId { get; set; }
    
    [Precision(18, 2)]
    public decimal Balance { get; set; } = 0m;

    // Navigation properties
    public User User { get; set; } = null!;
    public ICollection<WalletTransaction> Transactions { get; set; } = new List<WalletTransaction>();
}
