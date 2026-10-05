using Sample.Contracts;
using Sample.Services.Implementation.Properties;
using System.Threading.Tasks;

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
        // Real code goes here...
        var response = new GetCustomersResponse
        {
            CustomerList =
            [
                new Customer { Name = "Markus Egger", Company = "CODE", Id = "1" },
                new Customer { Name = "Ellen Whitney", Company = "CODE", Id = "2" },
                new Customer { Name = "Mike Yeager", Company = "CODE", Id = "3" },
                new Customer { Name = "Otto Dobretsberger", Company = "CODE", Id = "4" }
            ]
        };

        // var x = response.CustomerList[20];   // Put this line in to simulare an exception and trigger automatic exception handline

        return response;
    }

    public async Task<GetCustomersResponse> GetCustomersAsync(GetCustomersRequest request)
    {
        // Real code goes here...
        var response = new GetCustomersResponse
        {
            CustomerList =
            [
                new Customer { Name = "Markus Egger", Company = "CODE", Id = "1" },
                new Customer { Name = "Ellen Whitney", Company = "CODE", Id = "2" },
                new Customer { Name = "Mike Yeager", Company = "CODE", Id = "3" },
                new Customer { Name = "Otto Dobretsberger", Company = "CODE", Id = "4" }
            ]
        };

        // var x = response.CustomerList[20];   // Put this line in to simulare an exception and trigger automatic exception handline

        return response;
    }

    public DeleteCustomerResponse DeleteCustomer(DeleteCustomerRequest request)
    {
        // Pretending to delete the customer for demonstration purposes
        return new DeleteCustomerResponse
        {
            DeletedCustomerId = request.Id
        };
    }

    [AiTool(Name = "SearchCustomers", Description = "Returns customers based on the provided search string (inactive customers can optionally be included).")]
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

    [AiTool(Description = "Returns a customer based on the provided customer ID.")]
    public GetCustomerResponse GetCustomer(GetCustomerRequest request) => new()
    {
        Customer = new Customer 
        { 
            Id = request.Id, 
            Name = "Markus Egger", 
            Company = "CODE" 
        }
    };

    [AiTool(SubRoute = "Photos"), Description("Returns a photo (the actual bytes) based on the provided customer ID.")]
    public FileResponse GetPhoto(GetPhotoRequest request) => new()
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