// Hosting service implementations in-process
ServiceGardenLocal.AddServiceHost(typeof(CustomerService));
ServiceGardenLocal.AddServiceHost(typeof(UserService));

// Still calling the service in its standard syntax, as if it was remote (and could in fact be re-configured to be remote)
ServiceClient.Call<ICustomerService>(s =>
{
    var response = s.GetCustomers(new GetCustomersRequest());
    Console.WriteLine($"Success: {response.Success}");
});

ServiceClient.Call<IUserService>(s =>
{
    var response = s.AuthenticateUser(new AuthenticateUserRequest { UserName = "abc", Password = "12345" });
    Console.WriteLine($"Authenticated as: {response.Firstname} {response.Lastname}");
});