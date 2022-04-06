using Sample.Contracts;
using Sample.Services.Implementation.Properties;

namespace Sample.Services.Implementation;

public class CustomerService : ICustomerService, IServiceEvents
{
    public PingResponse Ping(PingRequest request) => this.GetPopulatedPingResponse();

    public DateTestResponse DateTest(DateTestRequest request) => new DateTestResponse
    {
        FirstDateReturned = request.FirstDate,
        SecondDateReturned = request.SecondDate
    };

    public GetCustomersResponse GetCustomers(GetCustomersRequest request)
    {
        var response = new GetCustomersResponse();

        // Real code goes here...

        response.CustomerList = new List<Customer> 
        {
            new Customer { Name = "Markus Egger", Company = "CODE" },
            new Customer { Name = "Ellen Whitney", Company = "CODE" },
            new Customer { Name = "Mike Yeager", Company = "CODE" },
            new Customer { Name = "Otto Dobretsberger", Company = "CODE" }
        };

        // var x = response.CustomerList[20];   // Put this line in to simulare an exception and trigger automatic exception handline

        return response;
    }

    public SearchTestResponse SearchTest(SearchTestRequest request)
    {
        var response = new SearchTestResponse
        {
            SearchStringUsed = request.SearchString,
            InactivesAreIncluded = request.IncludeInactive
        };

        for (var x = 1; x <= 10; x++)
            response.Customers.Add(new Customer
            {
                Name = $"{request.SearchString} {x}",
                Company = "EPS/CODE",
                Id = x.ToString()
            });

        return response;
    }

    public GetCustomerResponse GetCustomer(GetCustomerRequest request) => new GetCustomerResponse
    {
        Customer = new Customer 
        { 
            Id = request.Id, 
            Name = "Markus Egger", 
            Company = "CODE" 
        }
    };

    public FileResponse GetPhoto(GetPhotoRequest request) => new FileResponse
    {
        ContentType = "image/png",
        FileName = "ExampleImage.png",
        FileBytes = Resources.RocketMan
    };

    public void OnInProcessHostLaunched()
    {
        // Could add some special code here for in-process hosting specific things that need to happen
    }

    public GetStatusResponse GetStatus(GetStatusRequest request) => new GetStatusResponse { Status = request.Status };

    public FileResponse UploadCustomerFile(UploadCustomerFileRequest request) => new FileResponse { ContentType = request.ContentType, FileBytes = request.FileBytes, FileName = request.FileName };
}