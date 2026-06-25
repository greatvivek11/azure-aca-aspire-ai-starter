namespace AcaAspireAiTemplate.Backend.Features.Customers;

public interface ICustomerRepository
{
    Task<List<CustomerRecord>> GetCustomersAsync();
    Task<int> CreateCustomerAsync(CreateCustomerRequest request);
    Task<bool> UpdateCustomerAsync(int id, UpdateCustomerRequest request);
    Task<bool> DeleteCustomerAsync(int id);
}
