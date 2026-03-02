using System.Linq;
using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.KeyVault;
using Pulumi.AzureNative.KeyVault.Inputs;
using Pulumi.AzureNative.CognitiveServices;
using Pulumi.AzureNative.CognitiveServices.Inputs;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using Pulumi.AzureNative.ApplicationInsights;
using Pulumi.AzureNative.OperationalInsights;
using Pulumi.AzureNative.OperationalInsights.Inputs;
using Pulumi.AzureNative.Authorization;
using Pulumi.AzureNative.App;
using Pulumi.AzureNative.App.Inputs;
using Pulumi.AzureNative.ContainerRegistry;
using Pulumi.AzureNative.ContainerRegistry.Inputs;
using Pulumi.AzureNative.Search;
using Pulumi.AzureNative.Sql;
using Pulumi.AzureNative.AppConfiguration;
using AppConfigInputs = Pulumi.AzureNative.AppConfiguration.Inputs;
using MotorcycleRAG.Infrastructure;

// Add this explicit using for App.Inputs to resolve ambiguity:
using ManagedServiceIdentityArgs = Pulumi.AzureNative.App.Inputs.ManagedServiceIdentityArgs;

return await Pulumi.Deployment.RunAsync<MyStack>().ConfigureAwait(false);

namespace MotorcycleRAG.Infrastructure {
#pragma warning disable CA1506 // Avoid excessive class coupling
#pragma warning disable S1200 // Pulumi stacks naturally have many dependencies; splitting would require major refactor
#pragma warning disable S3059 // Pulumi requires public class for deployment
#pragma warning disable CA1515 // Pulumi requires public class for deployment
    public sealed class MyStack : Stack {
#pragma warning disable S138 // Functions should not have too many lines of code
        public MyStack() {
            var cfg = new Pulumi.Config();
            var currentClientConfig = Output.Create(GetClientConfig.InvokeAsync());

            var azureAdTenantId = cfg.RequireSecret("azureAdTenantId");
            var azureAdClientId = cfg.RequireSecret("azureAdClientId");
            var adminClientId = cfg.RequireSecret("adminClientId");
            var deepinfraApiKey = cfg.RequireSecret("deepinfraApiKey");
            var bffClientSecret = cfg.RequireSecret("bffClientSecret");

            // General
            var location = cfg.Get("location") ?? "centralus";

            // Naming convention: <org>-<workload>-<env>-<loc>-<resType>[<instance>]
            const string org = "mcr";           // motorcycle
            const string workload = "rag";      // rag system
            const string env = "dev";           // development
            const string loc = "cus";           // central us
            const string namePrefix = $"{org}-{workload}-{env}-{loc}";

            // 1. Resource Group
            var resourceGroup = new ResourceGroup($"{namePrefix}-rg", new ResourceGroupArgs {
                Location = location,
            });

            // 2. Storage Account for AI services
            // Fix ambiguous reference for 'Kind' and 'MinimumTlsVersion' by fully qualifying with Pulumi.AzureNative.Storage
            var storageAccount = new StorageAccount($"{org}{workload}{env}st01", new Pulumi.AzureNative.Storage.StorageAccountArgs {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                Sku = new Pulumi.AzureNative.Storage.Inputs.SkuArgs {
                    Name = Pulumi.AzureNative.Storage.SkuName.Standard_LRS
                },
                Kind = Pulumi.AzureNative.Storage.Kind.StorageV2,
                AllowBlobPublicAccess = false,
                MinimumTlsVersion = Pulumi.AzureNative.Storage.MinimumTlsVersion.TLS1_2
            });

            // 3. Key Vault
            var keyVault = new Vault($"{namePrefix}-kv", new VaultArgs {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                VaultName = $"{org}{workload}{env}kv",
                Properties = new VaultPropertiesArgs {
                    TenantId = currentClientConfig.Apply(config => config.TenantId),
                    Sku = new Pulumi.AzureNative.KeyVault.Inputs.SkuArgs {
                        Family = SkuFamily.A,
                        Name = Pulumi.AzureNative.KeyVault.SkuName.Standard
                    },
                    EnabledForDeployment = true,
                    EnabledForTemplateDeployment = true,
                    EnabledForDiskEncryption = true,
                    EnableRbacAuthorization = true, // Use RBAC for modern access control
                }
            });

            // 3.5. App Configuration Store (centralized config management)
            var appConfig = new ConfigurationStore($"{namePrefix}-appconfig", new ConfigurationStoreArgs {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                ConfigStoreName = $"{org}{workload}{env}appconfig",
                Sku = new AppConfigInputs.SkuArgs { Name = "free" },
                DisableLocalAuth = false, // Local auth required for Pulumi ARM provider to manage key-values
                SoftDeleteRetentionInDays = 0,
                EnablePurgeProtection = false,
            });

            // 4. Log Analytics Workspace
            var logAnalytics = new Workspace($"{namePrefix}-log", new WorkspaceArgs {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                Sku = new WorkspaceSkuArgs {
                    Name = WorkspaceSkuNameEnum.PerGB2018
                }
            });

            // 5. Application Insights
            var appInsights = new Component($"{namePrefix}-appi", new ComponentArgs {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                ApplicationType = ApplicationType.Web,
                Kind = "web",
                WorkspaceResourceId = logAnalytics.Id
            });

            // 6. Azure AI Services
            var aiServices = new Account($"{namePrefix}-cog01", new AccountArgs {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                Kind = "AIServices",
                Sku = new Pulumi.AzureNative.CognitiveServices.Inputs.SkuArgs {
                    Name = "S0"
                },
                Properties = new AccountPropertiesArgs {
                    CustomSubDomainName = $"{org}-{workload}-{env}-cog01",
                    PublicNetworkAccess = Pulumi.AzureNative.CognitiveServices.PublicNetworkAccess.Enabled
                }
            });

            // 8. Azure Container Registry
            var registry = new Registry($"{org}{workload}{env}acr", new RegistryArgs {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                Sku = new Pulumi.AzureNative.ContainerRegistry.Inputs.SkuArgs {
                    Name = Pulumi.AzureNative.ContainerRegistry.SkuName.Basic
                },
                AdminUserEnabled = true
            });

            // 9. Managed Environment (ACA Environment)
            var managedEnvironment = new ManagedEnvironment($"{namePrefix}-env", new ManagedEnvironmentArgs {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                AppLogsConfiguration = new AppLogsConfigurationArgs {
                    Destination = "log-analytics",
                    LogAnalyticsConfiguration = new LogAnalyticsConfigurationArgs {
                        CustomerId = logAnalytics.CustomerId,
                        SharedKey = GetSharedKeys.Invoke(new GetSharedKeysInvokeArgs {
                            ResourceGroupName = resourceGroup.Name,
                            WorkspaceName = logAnalytics.Name
                        }).Apply(keys => keys.PrimarySharedKey ?? "")
                    }
                }
            });

            // Get Registry Credentials
            var registryCredentials = ListRegistryCredentials.Invoke(new ListRegistryCredentialsInvokeArgs {
                ResourceGroupName = resourceGroup.Name,
                RegistryName = registry.Name
            });

            // Define generic settings for ACA
            var commonEnvs = new[]
            {
                new EnvironmentVarArgs { Name = "AppConfig__Endpoint", Value = appConfig.Endpoint }
            };

            // 10. API Container App
            var apiApp = new ContainerApp($"{namePrefix}-api", new ContainerAppArgs {
                ResourceGroupName = resourceGroup.Name,
                ManagedEnvironmentId = managedEnvironment.Id,
                Configuration = new ConfigurationArgs {
                    Ingress = new IngressArgs {
                        External = true,
                        TargetPort = 8080 // Standard .NET 8/10 port
                    },
                    Registries = new[]
                    {
                    new RegistryCredentialsArgs
                    {
                        Server = registry.LoginServer,
                        Username = registryCredentials.Apply(c => c.Username ?? ""),
                        PasswordSecretRef = "acr-password"
                    }
                },
                    Secrets = new[]
                    {
                    new Pulumi.AzureNative.App.Inputs.SecretArgs { Name = "acr-password", Value = registryCredentials.Apply(c => c.Passwords[0].Value ?? "") }
                }
                },
                Template = new TemplateArgs {
                    Containers = new[]
                    {
                    new ContainerArgs
                    {
                        Name = "api",
                        Image = "mcr.microsoft.com/k8se/quickstart:latest",
                        Resources = new ContainerResourcesArgs
                        {
                            Cpu = 0.25,
                            Memory = "0.5Gi"
                        },
                        Env = commonEnvs,
                        Probes = new[]
                        {
                            new ContainerAppProbeArgs
                            {
                                HttpGet = new ContainerAppProbeHttpGetArgs { Path = "/health", Port = 8080 },
                                Type = Pulumi.AzureNative.App.Type.Liveness,
                                TimeoutSeconds = 30,
                                InitialDelaySeconds = 15,
                                PeriodSeconds = 60,
                                FailureThreshold = 3
                            }
                        }
                    }
                },
                Scale = new ScaleArgs
                {
                    MinReplicas = 0,
                    MaxReplicas = 10
                }
            },
            Identity = new ManagedServiceIdentityArgs {
                Type = Pulumi.AzureNative.App.ManagedServiceIdentityType.SystemAssigned
            }
        });

        // 11. UI Container App (BFF)
        var uiApp = new ContainerApp($"{namePrefix}-ui", new ContainerAppArgs {
            ResourceGroupName = resourceGroup.Name,
            ManagedEnvironmentId = managedEnvironment.Id,
            Configuration = new ConfigurationArgs {
                Ingress = new IngressArgs {
                    External = true,
                    TargetPort = 8080
                },
                Registries = new[]
                {
                    new RegistryCredentialsArgs
                    {
                        Server = registry.LoginServer,
                        Username = registryCredentials.Apply(c => c.Username ?? ""),
                        PasswordSecretRef = "acr-password"
                    }
                },
                Secrets = new[]
                {
                    new Pulumi.AzureNative.App.Inputs.SecretArgs { Name = "acr-password", Value = registryCredentials.Apply(c => c.Passwords[0].Value ?? "") },
                    new Pulumi.AzureNative.App.Inputs.SecretArgs { Name = "bff-client-secret", Value = bffClientSecret }
                }
            },
            Template = new TemplateArgs {
                Containers = new[]
                {
                    new ContainerArgs
                    {
                        Name = "ui",
                        Image = Output.Format($"{registry.LoginServer}/motorcycle-rag-ui:latest"),
                        Resources = new ContainerResourcesArgs
                        {
                            Cpu = 0.25,
                            Memory = "0.5Gi"
                        },
                        Env = commonEnvs.Concat(new[]
                        {
                            new EnvironmentVarArgs { Name = "API_URL", Value = apiApp.Configuration.Apply(c => $"https://{c!.Ingress!.Fqdn}") },
                            new EnvironmentVarArgs { Name = "AzureAd__ClientId", Value = "6b1ec527-ccdd-4d84-9b12-98e889cab6ca" },
                            new EnvironmentVarArgs { Name = "AzureAd__ClientSecret", SecretRef = "bff-client-secret" },
                            new EnvironmentVarArgs { Name = "AzureAd__TenantId", Value = "0f8f8a52-f135-43af-af88-e0b54ca9ff91" },
                            new EnvironmentVarArgs { Name = "ASPNETCORE_HTTP_PORTS", Value = "8080" },
                            new EnvironmentVarArgs { Name = "ReverseProxy__Clusters__api-cluster__Destinations__destination1__Address", Value = apiApp.Configuration.Apply(c => $"https://{c!.Ingress!.Fqdn}") },
                            new EnvironmentVarArgs { Name = "AllowedHosts", Value = "*" },
                        }).ToArray(),
                        Probes = new[]
                        {
                            new ContainerAppProbeArgs
                            {
                                HttpGet = new ContainerAppProbeHttpGetArgs { Path = "/health", Port = 8080 },
                                Type = Pulumi.AzureNative.App.Type.Liveness,
                                TimeoutSeconds = 30,
                                InitialDelaySeconds = 15,
                                PeriodSeconds = 60,
                                FailureThreshold = 3
                            }
                        }
                    }
                },
                Scale = new ScaleArgs
                {
                    MinReplicas = 1,
                    MaxReplicas = 10
                }
            },
            Identity = new ManagedServiceIdentityArgs {
                Type = Pulumi.AzureNative.App.ManagedServiceIdentityType.SystemAssigned
            }
        });

            // 12. Azure AI Search
            var searchService = new Pulumi.AzureNative.Search.Service($"{namePrefix}-search", new Pulumi.AzureNative.Search.ServiceArgs {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                Sku = new Pulumi.AzureNative.Search.Inputs.SkuArgs { Name = "free" },
                HostingMode = Pulumi.AzureNative.Search.HostingMode.Default
            });

            // 13. Azure SQL Server
            var sqlServer = new Pulumi.AzureNative.Sql.Server($"{org}{workload}{env}sql01", new Pulumi.AzureNative.Sql.ServerArgs {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                ServerName = $"{org}{workload}{env}sql01",
                AdministratorLogin = cfg.Require("sqlAdminLogin"),
                AdministratorLoginPassword = cfg.RequireSecret("sqlAdminPassword"),
                Version = "12.0"
            });

            // Allow Azure services (Container Apps) to connect to SQL Server
            _ = new Pulumi.AzureNative.Sql.FirewallRule("sql-allow-azure-services", new Pulumi.AzureNative.Sql.FirewallRuleArgs {
                ResourceGroupName = resourceGroup.Name,
                ServerName = sqlServer.Name,
                FirewallRuleName = "AllowAzureServices",
                StartIpAddress = "0.0.0.0",
                EndIpAddress = "0.0.0.0"
            });

            // 14. Azure SQL Database
            var sqlDatabase = new Pulumi.AzureNative.Sql.Database($"{org}{workload}{env}sqldb01", new Pulumi.AzureNative.Sql.DatabaseArgs {
                ResourceGroupName = resourceGroup.Name,
                ServerName = sqlServer.Name,
                DatabaseName = $"{org}{workload}{env}sqldb01",
                Location = location,
                Sku = new Pulumi.AzureNative.Sql.Inputs.SkuArgs { Name = "Basic" }
            });

            // 15. Azure OpenAI
            var openAIAccount = new Pulumi.AzureNative.CognitiveServices.Account($"{org}{workload}{env}oai01", new Pulumi.AzureNative.CognitiveServices.AccountArgs {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                AccountName = $"{org}{workload}{env}oai01",
                Kind = "OpenAI",
                Sku = new Pulumi.AzureNative.CognitiveServices.Inputs.SkuArgs { Name = "S0" },
                Properties = new Pulumi.AzureNative.CognitiveServices.Inputs.AccountPropertiesArgs {
                    CustomSubDomainName = $"{org}-{workload}-{env}-oai01",
                    PublicNetworkAccess = Pulumi.AzureNative.CognitiveServices.PublicNetworkAccess.Enabled
                }
            });

            // 16. OpenAI Deployments
            var gpt4oDeployment = new Pulumi.AzureNative.CognitiveServices.Deployment("gpt-4o", new Pulumi.AzureNative.CognitiveServices.DeploymentArgs {
                ResourceGroupName = resourceGroup.Name,
                AccountName = openAIAccount.Name,
                DeploymentName = "gpt-4o",
                Sku = new Pulumi.AzureNative.CognitiveServices.Inputs.SkuArgs {
                    Name = "GlobalStandard",
                    Capacity = 1
                },
                Properties = new Pulumi.AzureNative.CognitiveServices.Inputs.DeploymentPropertiesArgs {
                    Model = new Pulumi.AzureNative.CognitiveServices.Inputs.DeploymentModelArgs {
                        Format = "OpenAI",
                        Name = "gpt-4o",
                        Version = "2024-05-13"
                    }
                }
            });

            _ = new Pulumi.AzureNative.CognitiveServices.Deployment("text-embedding-3-large", new Pulumi.AzureNative.CognitiveServices.DeploymentArgs {
                ResourceGroupName = resourceGroup.Name,
                AccountName = openAIAccount.Name,
                DeploymentName = "text-embedding-3-large",
                Sku = new Pulumi.AzureNative.CognitiveServices.Inputs.SkuArgs {
                    Name = "GlobalStandard",
                    Capacity = 1
                },
                Properties = new Pulumi.AzureNative.CognitiveServices.Inputs.DeploymentPropertiesArgs {
                    Model = new Pulumi.AzureNative.CognitiveServices.Inputs.DeploymentModelArgs {
                        Format = "OpenAI",
                        Name = "text-embedding-3-large",
                        Version = "1"
                    }
                }
            }, new Pulumi.CustomResourceOptions { DependsOn = { gpt4oDeployment } });

            // 17. Document Intelligence
            var docIntel = new Pulumi.AzureNative.CognitiveServices.Account($"{namePrefix}-docintel", new Pulumi.AzureNative.CognitiveServices.AccountArgs {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                AccountName = $"{namePrefix}-docintel",
                Kind = "FormRecognizer",
                Sku = new Pulumi.AzureNative.CognitiveServices.Inputs.SkuArgs { Name = "F0" },
                Properties = new Pulumi.AzureNative.CognitiveServices.Inputs.AccountPropertiesArgs {
                    CustomSubDomainName = $"{org}-{workload}-{env}-docintel",
                    PublicNetworkAccess = Pulumi.AzureNative.CognitiveServices.PublicNetworkAccess.Enabled
                }
            });

            // Key Vault Secrets for App Config KV references
            var kvSecretTenantId = new Secret("kv-secret-tenant-id", new Pulumi.AzureNative.KeyVault.SecretArgs {
                ResourceGroupName = resourceGroup.Name,
                VaultName = keyVault.Name,
                SecretName = "MCR-API-AZURE-AD-TENANT-ID",
                Properties = new SecretPropertiesArgs { Value = azureAdTenantId }
            });

            var kvSecretClientId = new Secret("kv-secret-client-id", new Pulumi.AzureNative.KeyVault.SecretArgs {
                ResourceGroupName = resourceGroup.Name,
                VaultName = keyVault.Name,
                SecretName = "MCR-API-AZURE-AD-CLIENT-ID",
                Properties = new SecretPropertiesArgs { Value = azureAdClientId }
            });

            var kvSecretAdminClientId = new Secret("kv-secret-admin-client-id", new Pulumi.AzureNative.KeyVault.SecretArgs {
                ResourceGroupName = resourceGroup.Name,
                VaultName = keyVault.Name,
                SecretName = "MCR-ADMIN-CLIENT-ID",
                Properties = new SecretPropertiesArgs { Value = adminClientId }
            });

            var kvSecretAppInsightsConnStr = new Secret("kv-secret-appinsights-connstr", new Pulumi.AzureNative.KeyVault.SecretArgs {
                ResourceGroupName = resourceGroup.Name,
                VaultName = keyVault.Name,
                SecretName = "ConnectionStrings--ApplicationInsights",
                Properties = new SecretPropertiesArgs { Value = appInsights.ConnectionString }
            });

            // Build SQL connection string from existing resources
#pragma warning disable S103 // Lines should not be too long
            var sqlConnectionString = Output.Format($"Server=tcp:{sqlServer.Name}.database.windows.net,1433;Database={sqlDatabase.Name};User ID={cfg.Require("sqlAdminLogin")};Password={cfg.RequireSecret("sqlAdminPassword")};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;");
#pragma warning restore S103

            var kvSecretSqlConnStr = new Secret("kv-secret-sql-connstr", new Pulumi.AzureNative.KeyVault.SecretArgs {
                ResourceGroupName = resourceGroup.Name,
                VaultName = keyVault.Name,
                SecretName = "Sql--ConnectionString",
                Properties = new SecretPropertiesArgs { Value = sqlConnectionString }
            });

            var kvSecretDeepinfraKey = new Secret("kv-secret-deepinfra-key", new Pulumi.AzureNative.KeyVault.SecretArgs {
                ResourceGroupName = resourceGroup.Name,
                VaultName = keyVault.Name,
                SecretName = "DEEPINFRA-API-KEY",
                Properties = new SecretPropertiesArgs { Value = deepinfraApiKey }
            });

            var kvSecretBffClientSecret = new Secret("kv-secret-bff-client-secret", new Pulumi.AzureNative.KeyVault.SecretArgs {
                ResourceGroupName = resourceGroup.Name,
                VaultName = keyVault.Name,
                SecretName = "MCR-WEB-BFF-CLIENT-SECRET",
                Properties = new SecretPropertiesArgs { Value = bffClientSecret }
            });

            // App Config: Plain configuration values
            _ = new KeyValue("appconfig-kv-openai-endpoint", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAI:OpenAIEndpoint",
                Value = openAIAccount.Properties.Apply(p => p.Endpoint ?? "")
            });

            _ = new KeyValue("appconfig-kv-search-endpoint", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAI:SearchServiceEndpoint",
                Value = searchService.Name.Apply(name => $"https://{name}.search.windows.net")
            });

            _ = new KeyValue("appconfig-kv-docintel-endpoint", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAI:DocumentIntelligenceEndpoint",
                Value = docIntel.Properties.Apply(p => p.Endpoint ?? "")
            });

            _ = new KeyValue("appconfig-kv-ai-services-endpoint", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AZURE_AI_SERVICES_ENDPOINT",
                Value = aiServices.Properties.Apply(p => p.Endpoint ?? "")
            });

            _ = new KeyValue("appconfig-kv-foundry-endpoint", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAI:FoundryEndpoint",
                Value = aiServices.Properties.Apply(p => p.Endpoint ?? "")
            });

            _ = new KeyValue("appconfig-kv-foundry-chat-endpoint", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "MCR_API_FOUNDRY_ENDPOINT",
                Value = aiServices.Properties.Apply(p => p.Endpoint ?? "")
            });

            _ = new KeyValue("appconfig-kv-foundry-chat-model", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "MCR_API_FOUNDRY_CHAT_MODEL",
                Value = "gpt-4o-mini"
            });

            _ = new KeyValue("appconfig-kv-telemetry-enabled", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "ApplicationInsights:EnableTelemetry",
                Value = "true"
            });

            _ = new KeyValue("appconfig-kv-sentinel", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "Settings:Sentinel",
                Value = "1"
            });

            // Key Vault references (content type tells App Config to resolve from KV)
            const string kvRefContentType = "application/vnd.microsoft.appconfig.keyvaultref+json;charset=utf-8";

            _ = new KeyValue("appconfig-kvref-tenant-id", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "MCR_API_AZURE_AD_TENANT_ID",
                ContentType = kvRefContentType,
                Value = kvSecretTenantId.Properties.Apply(p => $"{{\"uri\":\"{p.SecretUri}\"}}")
            });

            _ = new KeyValue("appconfig-kvref-client-id", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "MCR_API_AZURE_AD_CLIENT_ID",
                ContentType = kvRefContentType,
                Value = kvSecretClientId.Properties.Apply(p => $"{{\"uri\":\"{p.SecretUri}\"}}")
            });

            _ = new KeyValue("appconfig-kvref-azure-ad-tenant-id", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAd:TenantId",
                ContentType = kvRefContentType,
                Value = kvSecretTenantId.Properties.Apply(p => $"{{\"uri\":\"{p.SecretUri}\"}}")
            });

            _ = new KeyValue("appconfig-kvref-azure-ad-client-id", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAd:ClientId",
                ContentType = kvRefContentType,
                Value = kvSecretClientId.Properties.Apply(p => $"{{\"uri\":\"{p.SecretUri}\"}}")
            });

            _ = new KeyValue("appconfig-kvref-admin-client-id", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "MCR_ADMIN_CLIENT_ID",
                ContentType = kvRefContentType,
                Value = kvSecretAdminClientId.Properties.Apply(p => $"{{\"uri\":\"{p.SecretUri}\"}}")
            });

            _ = new KeyValue("appconfig-kvref-appinsights-connstr", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "ConnectionStrings:ApplicationInsights",
                ContentType = kvRefContentType,
                Value = kvSecretAppInsightsConnStr.Properties.Apply(p => $"{{\"uri\":\"{p.SecretUri}\"}}")
            });

            _ = new KeyValue("appconfig-kvref-sql-connstr", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "Sql:ConnectionString",
                ContentType = kvRefContentType,
                Value = kvSecretSqlConnStr.Properties.Apply(p => $"{{\"uri\":\"{p.SecretUri}\"}}")
            });

            _ = new KeyValue("appconfig-kvref-deepinfra-key", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "DEEPINFRA_API_KEY",
                ContentType = kvRefContentType,
                Value = kvSecretDeepinfraKey.Properties.Apply(p => $"{{\"uri\":\"{p.SecretUri}\"}}")
            });

            // RBAC: Key Vault Secrets User for both apps
            _ = new RoleAssignment($"{namePrefix}-api-kv-role", new RoleAssignmentArgs {
                PrincipalId = apiApp.Identity.Apply(i => i!.PrincipalId),
                RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/4633458b-17de-408a-b874-0445c86b69e6", // Key Vault Secrets User
                Scope = keyVault.Id,
                PrincipalType = Pulumi.AzureNative.Authorization.PrincipalType.ServicePrincipal
            });
            _ = new RoleAssignment($"{namePrefix}-ui-kv-role", new RoleAssignmentArgs {
                PrincipalId = uiApp.Identity.Apply(i => i!.PrincipalId),
                RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/4633458b-17de-408a-b874-0445c86b69e6", // Key Vault Secrets User
                Scope = keyVault.Id,
                PrincipalType = Pulumi.AzureNative.Authorization.PrincipalType.ServicePrincipal
            });

            // RBAC: App Configuration Data Reader for both apps
            _ = new RoleAssignment($"{namePrefix}-api-appconfig-role", new RoleAssignmentArgs {
                PrincipalId = apiApp.Identity.Apply(i => i!.PrincipalId),
                RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/516239f1-63e1-4d78-a4de-a74fb236a071",
                Scope = appConfig.Id,
                PrincipalType = Pulumi.AzureNative.Authorization.PrincipalType.ServicePrincipal
            });
            _ = new RoleAssignment($"{namePrefix}-ui-appconfig-role", new RoleAssignmentArgs {
                PrincipalId = uiApp.Identity.Apply(i => i!.PrincipalId),
                RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/516239f1-63e1-4d78-a4de-a74fb236a071",
                Scope = appConfig.Id,
                PrincipalType = Pulumi.AzureNative.Authorization.PrincipalType.ServicePrincipal
            });

            // AllowedHosts for HostHeaderValidationMiddleware (depends on Container App FQDNs)
            _ = new KeyValue("appconfig-kv-allowed-hosts", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AllowedHosts",
                Value = Output.Format($"localhost;127.0.0.1;::1;{apiApp.Configuration.Apply(c => c!.Ingress!.Fqdn)};{uiApp.Configuration.Apply(c => c!.Ingress!.Fqdn)}")
            });

            // Outputs
            this.AiServicesEndpoint = aiServices.Properties.Apply(p => p.Endpoint ?? "");
            this.KeyVaultUri = Output.Format($"https://{keyVault.Name}.vault.azure.net");
            this.StorageAccountName = storageAccount.Name;
            this.LogAnalyticsWorkspaceName = logAnalytics.Name;
            this.ApcUrl = uiApp.Configuration.Apply(c => $"https://{c!.Ingress!.Fqdn}");
            this.ApiUrl = apiApp.Configuration.Apply(c => $"https://{c!.Ingress!.Fqdn}");
            this.AcrLoginServer = registry.LoginServer;
            this.SearchEndpoint = searchService.Name.Apply(name => $"https://{name}.search.windows.net");
            this.SqlServerName = sqlServer.Name;
            this.SqlDatabaseName = sqlDatabase.Name;
            this.OpenAIEndpoint = openAIAccount.Properties.Apply(p => p.Endpoint);
            this.DocumentIntelligenceEndpoint = docIntel.Properties.Apply(p => p.Endpoint);
            this.AppInsightsConnectionString = appInsights.ConnectionString;
            this.ResourceGroupName = resourceGroup.Name;
            this.ApiAppName = apiApp.Name;
            this.UiAppName = uiApp.Name;
            this.AppConfigEndpoint = appConfig.Endpoint;
        }
#pragma warning restore S138 // Functions should not have too many lines of code
#pragma warning restore S1200 // Pulumi stacks naturally have many dependencies; splitting would require major refactor
#pragma warning restore S3059 // Pulumi requires public class for deployment
#pragma warning restore CA1515 // Pulumi requires public class for deployment

        [Output("aiServicesEndpoint")]
        public Output<string> AiServicesEndpoint { get; set; }

        [Output("keyVaultUri")]
        public Output<string> KeyVaultUri { get; set; }

        [Output("storageAccountName")]
        public Output<string> StorageAccountName { get; set; }

        [Output("logAnalyticsWorkspaceName")]
        public Output<string> LogAnalyticsWorkspaceName { get; set; }

        [Output("uiAppUrl")]
        public Output<string> ApcUrl { get; set; }

        [Output("apiAppUrl")]
        public Output<string> ApiUrl { get; set; }

        [Output("acrLoginServer")]
        public Output<string> AcrLoginServer { get; set; }
        [Output("searchEndpoint")]
        public Output<string> SearchEndpoint { get; set; }

        [Output("sqlServerName")]
        public Output<string> SqlServerName { get; set; }

        [Output("sqlDatabaseName")]
        public Output<string> SqlDatabaseName { get; set; }

        [Output("openAIEndpoint")]
        public Output<string> OpenAIEndpoint { get; set; }

        [Output("documentIntelligenceEndpoint")]
        public Output<string> DocumentIntelligenceEndpoint { get; set; }

        [Output("resourceGroupName")]
        public Output<string> ResourceGroupName { get; set; }

        [Output("apiAppName")]
        public Output<string> ApiAppName { get; set; }

        [Output("uiAppName")]
        public Output<string> UiAppName { get; set; }

        [Output("appInsightsConnectionString")]
        public Output<string> AppInsightsConnectionString { get; set; }

        [Output("appConfigEndpoint")]
        public Output<string> AppConfigEndpoint { get; set; }
    }
#pragma warning restore CA1506 // Avoid excessive class coupling
}
