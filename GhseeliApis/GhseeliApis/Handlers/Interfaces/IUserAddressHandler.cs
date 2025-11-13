using GhseeliApis.Models;

namespace GhseeliApis.Handlers.Interfaces;

public interface IUserAddressHandler
{
    Task<List<UserAddress>> GetByUserIdAsync(Guid userId);
    Task<UserAddress?> GetByIdAsync(Guid id);
    Task<UserAddress?> GetPrimaryAddressAsync(Guid userId);
    Task<UserAddress> CreateAsync(UserAddress address, Guid userId);
    Task<UserAddress?> UpdateAsync(Guid id, UserAddress address, Guid userId);
    Task<bool> DeleteAsync(Guid id, Guid userId);
    Task<bool> SetAsPrimaryAsync(Guid id, Guid userId);
}
