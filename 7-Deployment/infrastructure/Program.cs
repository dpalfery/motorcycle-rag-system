using System.Collections.Generic;
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
            var bffClientId = cfg.Require("bffClientId");
            var bffCiamInstance = cfg.Require("bffCiamInstance"); // e.g. https://palfery.ciamlogin.com/
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
            var aiServicesAccountName = $"{namePrefix}-cog01";
            var aiServicesSubdomain = $"{org}-{workload}-{env}-cog01";
            var aiServicesEndpoint = Output.Format($"https://{aiServicesSubdomain}.cognitiveservices.azure.com/");
            var aiServices = new Pulumi.AzureNative.Resources.Resource(aiServicesAccountName, new Pulumi.AzureNative.Resources.ResourceArgs {
                ResourceGroupName = resourceGroup.Name,
                ResourceProviderNamespace = "Microsoft.CognitiveServices",
                ResourceType = "accounts",
                ResourceName = aiServicesAccountName,
                ParentResourcePath = "",
                ApiVersion = "2025-06-01",
                Location = location,
                Kind = "AIServices",
                Identity = new Pulumi.AzureNative.Resources.Inputs.IdentityArgs {
                    Type = Pulumi.AzureNative.Resources.ResourceIdentityType.SystemAssigned
                },
                Sku = new Pulumi.AzureNative.Resources.Inputs.SkuArgs {
                    Name = "S0"
                },
                Properties = new Dictionary<string, object?> {
                    ["allowProjectManagement"] = true,
                    ["customSubDomainName"] = aiServicesSubdomain,
                    ["publicNetworkAccess"] = "Enabled"
                }
            });

            const string foundryProjectName = "motorcycle-rag";
            var foundryProject = new Project($"{namePrefix}-foundry-project", new ProjectArgs {
                ResourceGroupName = resourceGroup.Name,
                AccountName = aiServicesAccountName,
                Location = location,
                ProjectName = foundryProjectName,
                Identity = new Pulumi.AzureNative.CognitiveServices.Inputs.IdentityArgs {
                    Type = Pulumi.AzureNative.CognitiveServices.ResourceIdentityType.SystemAssigned
                },
                Properties = new ProjectPropertiesArgs {
                    DisplayName = "Motorcycle RAG",
                    Description = "Azure AI Foundry project for the Motorcycle RAG system."
                }
            });

            var foundryProjectEndpoint = Output.Tuple(aiServices.Name, foundryProject.Properties).Apply(values =>
            {
                var accountName = values.Item1;
                var projectProperties = values.Item2;
                if (projectProperties.Endpoints is not null && projectProperties.Endpoints.Count > 0)
                {
                    return projectProperties.Endpoints.Values.First();
                }

                return $"https://{accountName}.services.ai.azure.com/api/projects/{foundryProjectName}";
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
            // ConnectionStrings__ApplicationInsights is injected directly (not via App Config / Key Vault)
            // so that the bootstrap TelemetryClient can capture App Config load failures and other
            // pre-DI startup exceptions. The App Insights connection string is not a credential for
            // reading data — it only allows sending telemetry — so env-var injection is acceptable.
            var commonEnvs = new[]
            {
                new EnvironmentVarArgs { Name = "AppConfig__Endpoint", Value = appConfig.Endpoint },
                new EnvironmentVarArgs { Name = "ConnectionStrings__ApplicationInsights", Value = appInsights.ConnectionString }
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
                    new Pulumi.AzureNative.App.Inputs.SecretArgs { Name = "acr-password", Value = registryCredentials.Apply(c => c.Passwords[0].Value ?? "") }
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
                            new EnvironmentVarArgs { Name = "ASPNETCORE_HTTP_PORTS", Value = "8080" },
                            new EnvironmentVarArgs { Name = "ReverseProxy__Clusters__api-cluster__Destinations__destination1__Address", Value = apiApp.Configuration.Apply(c => $"https://{c!.Ingress!.Fqdn}") },
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

            // 15. Document Intelligence
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
            // NOTE: tenantId, clientId, and adminClientId are not secrets — they are stored as plain
            // labelled App Config values (api/bff labels) rather than Key Vault references.

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
                Value = aiServicesEndpoint
            });

            _ = new KeyValue("appconfig-kv-foundry-endpoint", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAI:FoundryEndpoint",
                Value = foundryProjectEndpoint
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

            // Labelled App Config entries: API
            _ = new KeyValue("appconfig-kv-api-tenant-id", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAd:TenantId$api",
                Value = azureAdTenantId
            });

            _ = new KeyValue("appconfig-kv-api-client-id", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAd:ClientId$api",
                Value = azureAdClientId
            });

            _ = new KeyValue("appconfig-kv-api-admin-client-id", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAd:AdminClientId$api",
                Value = adminClientId
            });

            // CIAM External ID issuer for API JWT validation.
            // Pattern: https://{tenantId}.ciamlogin.com/{tenantId}/v2.0 (from CIAM discovery document).
            // Required so the API accepts tokens issued by CIAM (External ID) in addition to workforce Entra ID.
            _ = new KeyValue("appconfig-kv-api-external-id-issuer", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "Authentication:Issuers:ExternalId",
                Value = azureAdTenantId.Apply(tid =>
                    $"https://{tid}.ciamlogin.com/{tid}/v2.0")
            });

            // Labelled App Config entries: BFF
            _ = new KeyValue("appconfig-kv-bff-ciam-instance", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAd:Instance$bff",
                Value = bffCiamInstance
            });

            _ = new KeyValue("appconfig-kv-bff-tenant-id", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAd:TenantId$bff",
                Value = azureAdTenantId
            });

            _ = new KeyValue("appconfig-kv-bff-client-id", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAd:ClientId$bff",
                Value = bffClientId
            });

            _ = new KeyValue("appconfig-kv-bff-client-secret", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAd:ClientSecret$bff",
                ContentType = kvRefContentType,
                Value = kvSecretBffClientSecret.Properties.Apply(p => $"{{\"uri\":\"{p.SecretUri}\"}}") 
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

            // Foundry agent IDs are written by the deploy pipeline into Key Vault after provisioning.
            // App Configuration resolves these versionless secret URIs at runtime for the API.
            _ = new KeyValue("appconfig-kvref-orchestrator-agent-id", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAI:OrchestratorAgentId",
                ContentType = kvRefContentType,
                Value = Output.Format($"{{\"uri\":\"https://{keyVault.Name}.vault.azure.net/secrets/MCR-ORCHESTRATOR-AGENT-ID\"}}")
            });

            _ = new KeyValue("appconfig-kvref-vectorsearch-agent-id", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAI:VectorSearchAgentId",
                ContentType = kvRefContentType,
                Value = Output.Format($"{{\"uri\":\"https://{keyVault.Name}.vault.azure.net/secrets/MCR-VECTORSEARCH-AGENT-ID\"}}")
            });

            _ = new KeyValue("appconfig-kvref-websearch-agent-id", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAI:WebSearchAgentId",
                ContentType = kvRefContentType,
                Value = Output.Format($"{{\"uri\":\"https://{keyVault.Name}.vault.azure.net/secrets/MCR-WEBSEARCH-AGENT-ID\"}}")
            });

            _ = new KeyValue("appconfig-kvref-pdfsearch-agent-id", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAI:PDFSearchAgentId",
                ContentType = kvRefContentType,
                Value = Output.Format($"{{\"uri\":\"https://{keyVault.Name}.vault.azure.net/secrets/MCR-PDFSEARCH-AGENT-ID\"}}")
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
            _ = new RoleAssignment($"{namePrefix}-pulumi-sp-kv-officer-role", new RoleAssignmentArgs {
                PrincipalId = "6e42ecfc-2f4a-4c08-b518-5aec4fffd355", // Pulumi service principal OID
                RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/b86a8fe4-44ce-4948-aee5-eccb2c155cd7", // Key Vault Secrets Officer
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

            // Blob container for BFF DataProtection key persistence
            var dpBlobContainer = new BlobContainer($"{namePrefix}-dp-keys-container", new BlobContainerArgs {
                ResourceGroupName = resourceGroup.Name,
                AccountName = storageAccount.Name,
                ContainerName = "dataprotection-keys",
                PublicAccess = PublicAccess.None,
            });

            // RBAC: Search Index Data Contributor for API (query + index documents)
            _ = new RoleAssignment($"{namePrefix}-api-search-role", new RoleAssignmentArgs {
                PrincipalId = apiApp.Identity.Apply(i => i!.PrincipalId),
                RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/8ebe5a00-799e-43f5-93ac-243d3dce84a7", // Search Index Data Contributor
                Scope = searchService.Id,
                PrincipalType = Pulumi.AzureNative.Authorization.PrincipalType.ServicePrincipal
            });

            // RBAC: Cognitive Services User for API (Azure AI Foundry chat completions + Document Intelligence)
            _ = new RoleAssignment($"{namePrefix}-api-aiservices-role", new RoleAssignmentArgs {
                PrincipalId = apiApp.Identity.Apply(i => i!.PrincipalId),
                RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/a97b65f3-24c7-4388-baec-2e87135dc908", // Cognitive Services User
                Scope = aiServices.Id,
                PrincipalType = Pulumi.AzureNative.Authorization.PrincipalType.ServicePrincipal
            });

            // RBAC: Storage Blob Data Contributor for BFF to persist DataProtection keys
            _ = new RoleAssignment($"{namePrefix}-ui-storage-dp-role", new RoleAssignmentArgs {
                PrincipalId = uiApp.Identity.Apply(i => i!.PrincipalId),
                RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/ba92f5b4-2d11-453d-a403-e96b0029c9fe", // Storage Blob Data Contributor
                Scope = dpBlobContainer.Id,
                PrincipalType = Pulumi.AzureNative.Authorization.PrincipalType.ServicePrincipal
            });

            // App Config: BFF DataProtection blob URI
            _ = new KeyValue("appconfig-kv-dp-blob-uri", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "DataProtection:BlobUri",
                Value = Output.Format($"https://{storageAccount.Name}.blob.core.windows.net/dataprotection-keys/bff-keys.xml")
            });

            // AllowedHosts for HostHeaderValidationMiddleware (depends on Container App FQDNs)
            _ = new KeyValue("appconfig-kv-allowed-hosts", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AllowedHosts",
                Value = Output.Format($"localhost;127.0.0.1;::1;{apiApp.Configuration.Apply(c => c!.Ingress!.Fqdn)};{uiApp.Configuration.Apply(c => c!.Ingress!.Fqdn)}")
            });

            // Outputs
            this.AiServicesEndpoint = aiServicesEndpoint;
            this.FoundryProjectEndpoint = foundryProjectEndpoint;
            this.KeyVaultUri = Output.Format($"https://{keyVault.Name}.vault.azure.net");
            this.StorageAccountName = storageAccount.Name;
            this.LogAnalyticsWorkspaceName = logAnalytics.Name;
            this.ApcUrl = uiApp.Configuration.Apply(c => $"https://{c!.Ingress!.Fqdn}");
            this.ApiUrl = apiApp.Configuration.Apply(c => $"https://{c!.Ingress!.Fqdn}");
            this.AcrLoginServer = registry.LoginServer;
            this.SearchEndpoint = searchService.Name.Apply(name => $"https://{name}.search.windows.net");
            this.SqlServerName = sqlServer.Name;
            this.SqlDatabaseName = sqlDatabase.Name;
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

        [Output("foundryProjectEndpoint")]
        public Output<string> FoundryProjectEndpoint { get; set; }

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
