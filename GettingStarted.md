# Getting Started

**DISCLAIMER(s): This project is still in alpha mode so breaking changes are still expected build to build.**

- This walkthrough assumes .NET Core 3.1 but `m8s` may be used with earlier .NET Core versions (and on non .NET Core projects).


## Pre-requisites

1. Install .NET Core from http://dot.net version 3.1.
1. Install the `m8s` command line using the following command:
   ```
   dotnet tool install m8s --version 0.1.203-alpha.gaceac50605 -g --add-source https://f.feedz.io/davidfowl/micronetes/nuget/index.json
   ```
   *The list of versions can be found [here](https://f.feedz.io/davidfowl/micronetes/nuget/v3/packages/m8s/index.json)*
1. Verify the installation was complete by running: 
   ```
   m8s --help
   ```

## Make a new application

1. Make a new folder called `microservice` and navigate to it:
   ```
   mkdir microservice
   cd microservice
   ```
1. Create a front end project:
   ```
   dotnet new razor -n frontend 
   ```
1. Run it with the `m8s` command line:
   ```
   m8s run frontend --port 8001
   ```
   We chose port 8001 for the dashboard. After running this command, it should be possible to navigate to http://localhost:8001 to see the dashboard running.
   
   The dashboard should show the `api` application running. You should be able to view the application logs and you should be able to hit navigate to the application in your browser.
1. Create a back end API that the front end will call.
   ```
   dotnet new api -n backend
   ```
1. Change the ports to `5002` and `5003` on the `backend` project in `Properties/launchSettings.json`.
   ```JSON
   {
    ...
        "profiles": {
            ...
            "backend": {
                "commandName": "Project",
                "launchBrowser": true,
                "launchUrl": "weatherforecast",
                "applicationUrl": "https://localhost:5002;http://localhost:5003",
                "environmentVariables": {
                    "ASPNETCORE_ENVIRONMENT": "Development"
                }
            }
        }
   }
   ```
   This avoids the port conflict between the frontend and the backend projects (this will be resolved automatically in the future. See [#28](https://github.com/davidfowl/Micronetes/issues/28))
1. Create a solution file and add both projects
   ```
   dotnet new sln
   dotnet sln add frontend
   dotnet sln add backend
   ```
   You should have a solution called `microservice.sln` that references the `frontend` and `backend` projects.
1. Stop the existing `m8s` command line using `Ctrl + C`. Run the `m8s` command line in the folder with the solution.
   ```
   m8s run --port 8001
   ```

   The dashboard should show both the `frontend` and `backend` services.

## Service Discovery and Communication

1. Now that we have 2 applications running, lets make them communicate. By default, **Micronetes** enables service discovery by injecting environment variables with a specific naming convention. 

    Add this method to the `frontend` project at the bottom of the `Startup.cs` class:
    ```C#
    private Uri GetUri(IConfiguration configuration, string name)
    {
        return new Uri($"http://{configuration[$"service:{name}:host"]}:{configuration[$"service:{name}:port"]}");
    }
    ```
    This method resolved the URL using the **Micronetes** naming convention for services. For more information on this, see the [README](README.md#service-descriptions)
1. Add a file `WeatherClient.cs` to the `frontend` project with the following contents:
   ```C#
   public class WeatherClient
   {
       private readonly HttpClient client;

       public WeatherClient(HttpClient client)
       {
           this.client = client;
       }

       public async Task<string> GetWeatherAsync()
       {
           return await this.client.GetStringAsync("/weatherforecast");
       }
   }
   ```
1. Now register this client in `Startup.cs` class in `ConfigureServices` of the `frontend` project:
   ```C#
   public void ConfigureServices(IServiceCollection services)
   {
       services.AddRazorPages();

       services.AddHttpClient<WeatherClient>(client =>
       {
            client.BaseAddress = GetUri(Configuration, "backend");
       });
   }
   ```
   This will wire up the `WeatherClient` to use the correct URL for the `backend` service.
1. Add a `Message` property to the `Index` page model under `Pages\Index.cshtml.cs` in the `frontend` project.
   ```C#
   public string Message { get; set; }
   ```

   Change the `OnGet` method to take the `WeatherClient` to call the `backend` service and store the result in the `Message` property:
   ```C#
   public async Task OnGet([FromServices]WeatherClient client)
   {
       Message = await client.GetWeatherAsync();
   }
   ``` 
1. Change the `Index.cshtml` razor view to render the `Message` property in the razor page:
   ```html
   <div class="text-center">
       <h1 class="display-4">Welcome</h1>
       <p>Learn about <a href="https://docs.microsoft.com/aspnet/core">building Web apps with ASP.NET Core</a>.</p>
   </div>

   Weather Forecast:

   @Model.Message
   ```
1. Run the project and the `frontend` service should be able to successfully call the `backend` service!

## Dependencies

We just showed how **Micronetes** makes it easier to communicate between 2 applications running locally but what happens if we want to use redis to store weather information?

### Docker

**Micronetes** can use `docker` to run images that run as part of your application. See the instructions for [installing docker](https://docs.docker.com/install/) on your operating system.

1. To create a **Micronetes** manifest from the solution file.
   ```
   m8s new microservice.sln
   ```
   This will create a manifest called `m8s.yaml` with the following contents:
   ```yaml
   - name: backend
   project: backend\backend.csproj
   - name: frontend
   project: frontend\frontend.csproj
   ```
   This will be the source of truth for `m8s` execution from now on. To see a full schema of file, see the reference in the [README](README.md).
1. Change the `WeatherForecastController.Get` method in the `backend` project to cache the weather information in redis using an `IDistributedCache`.
   ```C#
   [HttpGet]
   public async Task<string> Get([FromServices]IDistributedCache cache)
   {
       var weather = await cache.GetStringAsync("weather");

       if (weather == null)
       {
           var rng = new Random();
           var forecasts = Enumerable.Range(1, 5).Select(index => new WeatherForecast
           {
               Date = DateTime.Now.AddDays(index),
               TemperatureC = rng.Next(-20, 55),
               Summary = Summaries[rng.Next(Summaries.Length)]
           })
           .ToArray();

           weather = JsonSerializer.Serialize(forecasts);

           await cache.SetStringAsync("weather", weather, new DistributedCacheEntryOptions
           {
               AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(15)
           });
       }
       return weather;
   }
   ```
1. Add a package reference to `Microsoft.Extensions.Caching.StackExchangeRedis`:
   ```
   dotnet package add Microsoft.Extensions.Caching.StackExchangeRedis
   ```
1. Modify `Startup.ConfigureServices` in the `backend` project to add the redis `IDistributedCache` implementation.
   ```C#
   public void ConfigureServices(IServiceCollection services)
   {
       services.AddControllers();

       services.AddStackExchangeRedisCache(o =>
       {
           o.Configuration = $"{Configuration["service:redis:host"]}:{Configuration["service:redis:port"]}";
       });
   }
   ```
   The above configures redis to use the host and port for the `redis` service injected by the **Micronetes** host.
1. Modify `m8s.yaml` to include redis as a dependency.
   ```yaml
   - name: backend
     project: backend\backend.csproj
   - name: frontend
     project: frontend\frontend.csproj
   - name: redis
     dockerImage: redis
     bindings:
       - port: 6379
   - name: redis-cli
     dockerImage: redis
     args: "redis-cli -h host.docker.internal MONITOR"
   ```

   We've added 2 services to the `m8s.yaml` file. The redis service itself and a redis-cli service that we will use to watch the data being sent to and retrieved from redis.
1. Run the `m8s` command line in the solution root
   ```
   m8s run --port 8001
   ```

   This should run the applications (including redis in the docker containers) and you should be able to view the logs for each of the services running.
