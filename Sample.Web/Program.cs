var builder = WebApplication.CreateBuilder(args);

builder.Services.AddServiceHandler(config =>
{
    config.Services.Clear();

    // This is an example of configuring hosted services in code. This can also be done in appsettings.json
    // The two services hosted in this API example deliberately set a variety of options for demonstration purposes.
    config.Services.AddRange(new List<ServiceHandlerConfigurationInstance>
    {
        new ServiceHandlerConfigurationInstance
        {
            ServiceType = typeof(UserService), // Using an explicit Type, which also implies the assembly the type is in
            RouteBasePath = "/api/users",
            JsonFormatMode = JsonFormatModes.CamelCase, // camel-case formats JSON like firstName
            OnAuthorize = context =>
            {
                // fake a user context 
                context.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("Permission", "CanViewPage"),
                    new Claim(ClaimTypes.Role, "Administrator"),
                    new Claim(ClaimTypes.NameIdentifier, "Markus E. User")
                }, "Basic"));

                return Task.FromResult(true);
            }
        },
        new ServiceHandlerConfigurationInstance
        {
            ServiceTypeName = "Sample.Services.Implementation.CustomerService", // dynamically loaded type by specifying the name in a tring
            AssemblyName = "Sample.Services.Implementation", // framework needs to load assembly
            RouteBasePath = "/api/customers",
            JsonFormatMode = JsonFormatModes.ProperCase, // proper-case formats JSON like FirstName
            OnAuthorize = context =>
            {
                // fake a user context 
                context.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("Permission", "CanViewPage"),
                    new Claim(ClaimTypes.Role, "Administrators"),
                    new Claim(ClaimTypes.NameIdentifier, "Rick S. Cust")
                }, "Basic"));

                return Task.FromResult(true);
            }
        }
    });

    // These are the defaults anyway, but they could manually be set to something different
    //config.Cors.UseCorsPolicy = true;
    //config.Cors.AllowedOrigins = "*";
});

// Add services to the dependency injection container to support our injection example.
builder.Services.AddScoped<IUserProvider, FakeUserProvider>();

// Ready to let ASP.NET build the app
var app = builder.Build();

// Enabled the CODE Framework service hosting environment
app.UseServiceHandler(); 

// Add CODE Framework OpenAPI support
app.UseOpenApiHandler(info: new OpenApiInfo
{
    Title = "CODE Framework Service/API Example",
    Description = "This service/api example is used to test, demonstrate, and document some of the CODE Framework service/api features.",
    TermsOfService = "http://codeframework.io",
    License = "MIT",
    Contact = "info@codemag.com"
});

// Using Swashbuckle to show Swagger UI based on the OpenAPI features provided by CODE Framework
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi.json", "Service Description");
});

app.Run();