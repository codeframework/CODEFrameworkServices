// Using the service garden as a local IoC container ("dependency injection")
ServiceGardenLocal.InjectSingletonDependency<IUserProvider, FakeUserProvider>();

// Hosting service implementations in-process
ServiceGardenLocal.AddServiceHost(typeof(CustomerService));
ServiceGardenLocal.AddServiceHost(typeof(UserService));



// From here on, this code is identical to the Sample.Client.Console project

var originalColor = Console.ForegroundColor;

Console.WriteLine("CODE Framework Service Example Test Client.\r");
Console.WriteLine("Press key to call ICustomerService.GetCustomers().\r");
Console.ReadLine();

ServiceClient.Call<ICustomerService>(c =>
{
    try
    {
        Console.WriteLine("Calling service....");

        var response = c.GetCustomers(new GetCustomersRequest());
        if (response.Success)
        {
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine("Customers Retrieved:\r");
            foreach (var customer in response.CustomerList)
                Console.WriteLine($"Customer: {customer.Name} - Company: {customer.Company}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine($"Service call returned Success = false. Failure Information: {response.FailureInformation}\r");
        }
    }
    catch (Exception e)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(e);
        throw;
    }
});

Console.ForegroundColor = originalColor;
Console.WriteLine();
Console.WriteLine("Press key to call ICustomerService.SearchTest().\r");
Console.ReadLine();

ServiceClient.Call<ICustomerService>(c =>
{
    try
    {
        Console.WriteLine("Calling service....");
        var response = c.SearchTest(new SearchTestRequest { SearchString = "Example Search String", IncludeInactive = true });
        if (response.Success)
        {
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine("Search Test Result:");
            Console.WriteLine($"Search string used: {response.SearchStringUsed}");
            Console.WriteLine($"Inactive Included: {response.InactivesAreIncluded}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine($"Service call returned Success = false. Failure Information: {response.FailureInformation}\r");
        }
    }
    catch (Exception e)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(e);
        throw;
    }
});

Console.ForegroundColor = originalColor;
Console.WriteLine();
Console.WriteLine("Press key to call ICustomerService.DateTest().\r");
Console.ReadLine();

ServiceClient.Call<ICustomerService>(c =>
{
    try
    {
        Console.WriteLine("Calling service....");
        var response = c.DateTest(new DateTestRequest { FirstDate = DateTime.Now, SecondDate = DateTime.Now.AddYears(1) });
        if (response.Success)
        {
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine("Search Test Result:");
            Console.WriteLine($"First date returned: {response.FirstDateReturned}");
            Console.WriteLine($"Second date returned: {response.SecondDateReturned}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine($"Service call returned Success = false. Failure Information: {response.FailureInformation}\r");
        }
    }
    catch (Exception e)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(e);
        throw;
    }
});

Console.ForegroundColor = originalColor;
Console.WriteLine();
Console.WriteLine("Press key to call ICustomerService.GetPhoto().\r");
Console.ReadLine();

ServiceClient.Call<ICustomerService>(s =>
{
    Console.WriteLine("Calling service....");
    var response = s.GetPhoto(new GetPhotoRequest { CustomerId = "1" });
    Console.ForegroundColor = ConsoleColor.DarkGreen;
    Console.WriteLine($"File Name: {response.FileName}");
    Console.WriteLine($"Content Type: {response.ContentType}");
    Console.WriteLine($"Content Length: {response.FileBytes.Length}");
});

Console.ForegroundColor = originalColor;
Console.WriteLine();
Console.WriteLine("Done.");