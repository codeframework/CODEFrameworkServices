using System;
using CODE.Framework.Fundamentals.Configuration;
using CODE.Framework.Services.Client;
using Sample.Contracts;

ConfigurationSettings.Sources["Memory"].Settings["RestServiceUrl:ICustomerService"] = "http://localhost:5008/api/customers";
ConfigurationSettings.Sources["Memory"].Settings["RestServiceUrl:IUserService"] = "http://localhost:5008/api/users";

var originalColor = Console.ForegroundColor;

Console.WriteLine($"CODE Framework Service Example Test Client.{Environment.NewLine}");
Console.WriteLine($"Press key to call ICustomerService.GetCustomers().{Environment.NewLine}");
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
            Console.WriteLine($"Customers Retrieved:{Environment.NewLine}");
            foreach (var customer in response.CustomerList)
                Console.WriteLine($"Customer: {customer.Name} - Company: {customer.Company}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine($"Service call returned Success = false. Failure Information: {response.FailureInformation}{Environment.NewLine}");
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
Console.WriteLine($"Press key to call ICustomerService.GetCustomer().{Environment.NewLine}");
Console.ReadLine();

ServiceClient.Call<ICustomerService>(c =>
{
    try
    {
        Console.WriteLine("Calling service....");

        var response = c.GetCustomer(new GetCustomerRequest());
        if (response.Success)
        {
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine($"Customer Retrieved: {response.Customer.Name}{Environment.NewLine}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine($"Service call returned Success = false. Failure Information: {response.FailureInformation}{Environment.NewLine}");
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
Console.WriteLine($"Press key to call ICustomerService.DeleteCustomer().{Environment.NewLine}");
Console.ReadLine();

ServiceClient.Call<ICustomerService>(c =>
{
    try
    {
        Console.WriteLine("Calling service....");

        var response = c.DeleteCustomer(new DeleteCustomerRequest { Id = "Egger" });
        if (response.Success)
        {
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine($"Customer Deleted: {response.DeletedCustomerId}{Environment.NewLine}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine($"Service call returned Success = false. Failure Information: {response.FailureInformation}{Environment.NewLine}");
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
Console.WriteLine($"Press key to call ICustomerService.SearchTest().{Environment.NewLine}");
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
            Console.WriteLine($"Service call returned Success = false. Failure Information: {response.FailureInformation}{Environment.NewLine}");
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
Console.WriteLine($"Press key to call ICustomerService.DateTest().{Environment.NewLine}");
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
            Console.WriteLine($"Service call returned Success = false. Failure Information: {response.FailureInformation}{Environment.NewLine}");
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
Console.WriteLine($"Press key to call ICustomerService.GetPhoto().{Environment.NewLine}");
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