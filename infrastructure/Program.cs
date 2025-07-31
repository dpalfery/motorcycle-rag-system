using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.KeyVault;
using Pulumi.AzureNative.KeyVault.Inputs;
using Pulumi.AzureNative.CognitiveServices;
using Pulumi.AzureNative.CognitiveServices.Inputs;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using Pulumi.AzureNative.OperationalInsights;
using Pulumi.AzureNative.OperationalInsights.Inputs;
using Pulumi.AzureNative.Authorization;
using Pulumi.AzureNative.Web;
using Pulumi.AzureNative.Web.Inputs;

return await Pulumi.Deployment.RunAsync<MyStack>();

public class MyStack : Stack
{
    public MyStack()
    {
        var cfg = new Pulumi.Config();
        var currentClientConfig = Output.Create(GetClientConfig.InvokeAsync());

        // General
        var location = cfg.Get("location") ?? "eastus";

        // Naming convention: <org>-<workload>-<env>-<loc>-<resType>[<instance>]
        var org = "mcr";           // motorcycle
        var workload = "rag";      // rag system
        var env = "dev";           // development
        var loc = "eus";           // east us
        var namePrefix = $"{org}-{workload}-{env}-{loc}";

        // 1. Resource Group
        var resourceGroup = new ResourceGroup($"{namePrefix}-rg", new ResourceGroupArgs
        {
            Location = location,
        });

        // 2. Storage Account for AI services (no dashes, 24 char limit)
        var storageAccount = new StorageAccount($"{org}{workload}{env}st01", new Pulumi.AzureNative.Storage.StorageAccountArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Location = location,
            Sku = new Pulumi.AzureNative.Storage.Inputs.SkuArgs
            {
                Name = Pulumi.AzureNative.Storage.SkuName.Standard_LRS
            },
            Kind = Kind.StorageV2,
            AllowBlobPublicAccess = false,
            MinimumTlsVersion = MinimumTlsVersion.TLS1_2
        });

        // 3. Key Vault for storing service keys
        var keyVault = new Vault($"{namePrefix}-kv", new VaultArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Location = location,
            Properties = new VaultPropertiesArgs
            {
                TenantId = currentClientConfig.Apply(config => config.TenantId),
                Sku = new Pulumi.AzureNative.KeyVault.Inputs.SkuArgs
                {
                    Family = SkuFamily.A,
                    Name = Pulumi.AzureNative.KeyVault.SkuName.Standard
                },
                EnabledForDeployment = true,
                EnabledForTemplateDeployment = true,
                EnabledForDiskEncryption = true,
                AccessPolicies = new[]
                {
                    new AccessPolicyEntryArgs
                    {
                        TenantId = currentClientConfig.Apply(config => config.TenantId),
                        ObjectId = currentClientConfig.Apply(config => config.ObjectId),
                        Permissions = new PermissionsArgs
                        {
                            Keys = new InputList<Union<string, KeyPermissions>>
                            {
                                "get", "list", "create", "delete", "update", "decrypt", "encrypt"
                            },
                            Secrets = new InputList<Union<string, SecretPermissions>>
                            {
                                "get", "list", "set", "delete"
                            },
                            Certificates = new InputList<Union<string, CertificatePermissions>>
                            {
                                "get", "list", "create", "delete", "update"
                            }
                        }
                    }
                }
            }
        });

        // 4. Log Analytics Workspace
        var logAnalytics = new Workspace($"{namePrefix}-log", new WorkspaceArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Location = location,
            Sku = new WorkspaceSkuArgs
            {
                Name = WorkspaceSkuNameEnum.PerGB2018
            }
        });

        // 5. Azure AI Services (includes OpenAI, Document Intelligence, etc.)
        var aiServices = new Account($"{namePrefix}-cog01", new AccountArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Location = location,
            Kind = "AIServices",
            Sku = new Pulumi.AzureNative.CognitiveServices.Inputs.SkuArgs
            {
                Name = "S0"
            },
            Properties = new AccountPropertiesArgs
            {
                CustomSubDomainName = $"{org}-{workload}-{env}-cog01",
                PublicNetworkAccess = Pulumi.AzureNative.CognitiveServices.PublicNetworkAccess.Enabled
            }
        });

        // 6. App Service Plan
        var appServicePlan = new AppServicePlan($"{namePrefix}-asp", new AppServicePlanArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Location = location,
            Sku = new SkuDescriptionArgs
            {
                Name = "B1", // Basic tier - can be scaled up as needed
                Tier = "Basic"
            },
            Kind = "app"
        });

        // 7. App Service (Web App)
        var webApp = new WebApp($"{namePrefix}-app", new WebAppArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Location = location,
            ServerFarmId = appServicePlan.Id,
            SiteConfig = new SiteConfigArgs
            {
                NetFrameworkVersion = "v8.0", // .NET 8
                AppSettings = new[]
                {
                    new NameValuePairArgs { Name = "AZURE_AI_SERVICES_ENDPOINT", Value = aiServices.Properties.Apply(p => p.Endpoint) },
                    new NameValuePairArgs { Name = "KEY_VAULT_URI", Value = Output.Format($"https://{keyVault.Name}.vault.azure.net") }
                },
                AlwaysOn = true,
                Use32BitWorkerProcess = false
            },
            Identity = new ManagedServiceIdentityArgs
            {
                Type = Pulumi.AzureNative.Web.ManagedServiceIdentityType.SystemAssigned
            },
            HttpsOnly = true
        });

        // Get service keys and store in Key Vault
        var aiServicesKeys = Output.Tuple(resourceGroup.Name, aiServices.Name).Apply(t =>
            ListAccountKeys.InvokeAsync(new ListAccountKeysArgs
            {
                ResourceGroupName = t.Item1,
                AccountName = t.Item2
            }));

        // Store AI Services key in Key Vault
        var aiServicesKeySecret = new Secret("ai-services-key", new Pulumi.AzureNative.KeyVault.SecretArgs
        {
            ResourceGroupName = resourceGroup.Name,
            VaultName = keyVault.Name,
            Properties = new SecretPropertiesArgs
            {
                Value = aiServicesKeys.Apply(keys => keys.Key1)
            }
        });

        // Outputs
        this.AiServicesEndpoint = aiServices.Properties.Apply(p => p.Endpoint ?? "");
        this.KeyVaultUri = Output.Format($"https://{keyVault.Name}.vault.azure.net");
        this.StorageAccountName = storageAccount.Name;
        this.LogAnalyticsWorkspaceName = logAnalytics.Name;
        this.WebAppUrl = Output.Format($"https://{webApp.DefaultHostName}");
        this.WebAppName = webApp.Name;
    }

    [Output("aiServicesEndpoint")]
    public Output<string> AiServicesEndpoint { get; set; }

    [Output("keyVaultUri")]
    public Output<string> KeyVaultUri { get; set; }

    [Output("storageAccountName")]
    public Output<string> StorageAccountName { get; set; }

    [Output("logAnalyticsWorkspaceName")]
    public Output<string> LogAnalyticsWorkspaceName { get; set; }

    [Output("webAppUrl")]
    public Output<string> WebAppUrl { get; set; }

    [Output("webAppName")]
    public Output<string> WebAppName { get; set; }
}