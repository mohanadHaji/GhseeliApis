namespace GhseeliApis.Models.Enums;

/// <summary>
/// Represents the payment method used for a transaction
/// </summary>
public enum PaymentMethod
{
    /// <summary>
    /// Payment via credit/debit card
    /// </summary>
    Card = 0,
    
    /// <summary>
    /// Payment from user's wallet balance
    /// </summary>
    Wallet = 1,
    
    /// <summary>
    /// Cash payment when service provider arrives
    /// </summary>
    CashOnArrival = 2,
    
    /// <summary>
    /// Payment via third-party provider (PayPal, etc.)
    /// </summary>
    ThirdParty = 3
}
