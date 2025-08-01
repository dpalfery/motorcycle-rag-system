using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Web;
using Pulumi.AzureNative.Web.Inputs;

return await Pulumi.Deployment.RunAsync<MyStack>();

public class MyStack : Stack
{
    public MyStack()
    {
        var cfg = new Pulumi.Config();

        // General
        var location = cfg.Get("location") ?? "eastus";

        // 1. Resource Group
        var resourceGroup = new ResourceGroup("motorcyclerag-rg", new ResourceGroupArgs
        {
            Location = location,
        });

        // 2. App Service Plan
        var appServicePlan = new AppServicePlan("motorcyclerag-plan", new AppServicePlanArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Location = location,
            Sku = new SkuDescriptionArgs
            {
                Name = "B1",
                Tier = "Basic",
            },
            Kind = "app",
            Reserved = false, // Windows App Service
        });

        // 3. Web App for .NET
        var app = new WebApp("motorcyclerag-api", new WebAppArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Location = location,
            ServerFarmId = appServicePlan.Id,
            SiteConfig = new SiteConfigArgs
            {
                NetFrameworkVersion = "v9.0", // .NET 8
                AppSettings =
                {
                    new NameValuePairArgs { Name = "ASPNETCORE_ENVIRONMENT", Value = "Production" },
                    new NameValuePairArgs { Name = "WEBSITE_RUN_FROM_PACKAGE", Value = "1" },
                    // Azure service configuration will be added via Key Vault integration
                },
                AlwaysOn = true,
                FtpsState = FtpsState.Disabled,
            },
        });

        this.Endpoint = app.DefaultHostName.Apply(hostname => $"https://{hostname}");
    }

    [Output("endpoint")]
    public Output<string> Endpoint { get; set; }
}