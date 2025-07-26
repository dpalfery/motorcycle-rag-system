using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Resources.Inputs;
using Pulumi.AzureNative.ContainerRegistry;
using Pulumi.AzureNative.ContainerRegistry.Inputs;
using Pulumi.AzureNative.ContainerApps;
using Pulumi.AzureNative.ContainerApps.Inputs;
using Pulumi.Docker;

return await Deployment.RunAsync<MyStack>();

public class MyStack : Stack
{
    public MyStack()
    {
        var cfg = new Config();

        // General
        var location = cfg.Get("location") ?? "eastus";

        // 1. Resource Group
        var resourceGroup = new ResourceGroup("motorcyclerag-rg", new ResourceGroupArgs
        {
            Location = location,
        });

        // 2. Container Registry (ACR)
        var registry = new Registry("motorcycleragacr", new RegistryArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Location = location,
            Sku = new SkuArgs { Name = "Basic" },
            AdminUserEnabled = true,
        });

        var creds = Output.Tuple(resourceGroup.Name, registry.Name).Apply(t =>
            ListRegistryCredentials.InvokeAsync(new ListRegistryCredentialsArgs
            {
                ResourceGroupName = t.Item1,
                RegistryName = t.Item2,
            }));

        var acrUsername = creds.Apply(c => c.Username);
        var acrPassword = creds.Apply(c => c.Passwords[0].Value);

        // 3. Build & push image using Pulumi.Docker
        var imageName = "motorcyclerag-api";
        var image = new Image("api-image", new ImageArgs
        {
            ImageName = Output.Format($"{registry.LoginServer}/{imageName}:v{System.DateTime.UtcNow:yyyyMMddHHmmss}"),
            Build = new DockerBuild { Context = "../" }, // root contains Dockerfile
            Registry = new ImageRegistry
            {
                Server = registry.LoginServer,
                Username = acrUsername,
                Password = acrPassword,
            },
        });

        // 4. Container App Environment
        var environment = new ManagedEnvironment("motorcyclerag-env", new ManagedEnvironmentArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Location = location,
            AppLogsConfiguration = new AppLogsConfigurationArgs
            {
                Destination = "log-analytics",
                LogAnalyticsConfiguration = new LogAnalyticsConfigurationArgs
                {
                    CustomerId = cfg.RequireSecret("logAnalyticsCustomerId"),
                    SharedKey = cfg.RequireSecret("logAnalyticsSharedKey"),
                },
            },
        });

        // 5. Container App
        var app = new ContainerApp("motorcyclerag-api", new ContainerAppArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Location = location,
            ManagedEnvironmentId = environment.Id,
            Configuration = new ConfigurationArgs
            {
                Ingress = new IngressArgs
                {
                    External = true,
                    TargetPort = 80,
                    Transport = "auto",
                },
                Registries =
                {
                    new RegistryCredentialsArgs
                    {
                        Server = registry.LoginServer,
                        Username = acrUsername,
                        PasswordSecretRef = "acr-pwd",
                    },
                },
                Secrets =
                {
                    new SecretArgs { Name = "acr-pwd", Value = acrPassword },
                    new SecretArgs { Name = "azure-openai-key", Value = cfg.RequireSecret("azureOpenAIKey") },
                    new SecretArgs { Name = "azure-search-key", Value = cfg.RequireSecret("azureSearchKey") },
                    new SecretArgs { Name = "docint-key", Value = cfg.RequireSecret("documentIntelligenceKey") },
                    new SecretArgs { Name = "appinsights-conn", Value = cfg.RequireSecret("appInsightsConnectionString") },
                },
            },
            Template = new TemplateArgs
            {
                Containers =
                {
                    new ContainerArgs
                    {
                        Name = "api",
                        Image = image.ImageName,
                        Env =
                        {
                            new EnvironmentVarArgs { Name = "AZURE_OPENAI_ENDPOINT", Value = cfg.Require("azureOpenAIEndpoint") },
                            new EnvironmentVarArgs { Name = "AZURE_OPENAI_KEY", SecretRef = "azure-openai-key" },
                            new EnvironmentVarArgs { Name = "AZURE_SEARCH_ENDPOINT", Value = cfg.Require("azureSearchEndpoint") },
                            new EnvironmentVarArgs { Name = "AZURE_SEARCH_KEY", SecretRef = "azure-search-key" },
                            new EnvironmentVarArgs { Name = "DOCUMENT_INTELLIGENCE_ENDPOINT", Value = cfg.Require("documentIntelligenceEndpoint") },
                            new EnvironmentVarArgs { Name = "DOCUMENT_INTELLIGENCE_KEY", SecretRef = "docint-key" },
                            new EnvironmentVarArgs { Name = "APPLICATIONINSIGHTS_CONNECTION_STRING", SecretRef = "appinsights-conn" },
                        },
                    },
                },
                Dapr = new DaprArgs { Enabled = false },
            },
        });

        this.Endpoint = app.LatestRevisionFqdn.Apply(fqdn => $"https://{fqdn}");
    }

    [Output("endpoint")]
    public Output<string> Endpoint { get; set; }
}