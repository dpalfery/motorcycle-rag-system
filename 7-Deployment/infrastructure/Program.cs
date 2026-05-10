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
            var developerObjectId = cfg.Get("developerObjectId");

            // General
            var location = cfg.Get("location") ?? "centralus";
            var foundryLocation = cfg.Get("foundryLocation") ?? "eastus";
            var qwen36DeploymentName = cfg.Get("qwen36DeploymentName") ?? "qwen3-6-35b-a3b-fp8";
            var qwen35DeploymentName = cfg.Get("qwen35DeploymentName") ?? "qwen3-5-35b-a3b";
            var orchestratorFallbackDeploymentName = cfg.Get("orchestratorFallbackDeploymentName") ?? "gpt-4-1";
            var orchestratorFallbackModelName = cfg.Get("orchestratorFallbackModelName") ?? "gpt-4.1";
            var orchestratorFallbackModelVersion = cfg.Get("orchestratorFallbackModelVersion") ?? "2025-04-14";
            var orchestratorFallbackModelSku = cfg.Get("orchestratorFallbackModelSku") ?? "GlobalStandard";
            var orchestratorFallbackModelCapacity = cfg.GetInt32("orchestratorFallbackModelCapacity") ?? 1;
            var subAgentDeploymentName = cfg.Get("subAgentDeploymentName") ?? "gpt-4-1-mini";
            var subAgentModelName = cfg.Get("subAgentModelName") ?? "gpt-4.1-mini";
            var subAgentModelVersion = cfg.Get("subAgentModelVersion") ?? "2025-04-14";
            var subAgentModelSku = cfg.Get("subAgentModelSku") ?? "GlobalStandard";
            var subAgentModelCapacity = cfg.GetInt32("subAgentModelCapacity") ?? 1;

            // Naming convention: <org>-<workload>-<env>-<loc>-<resType>[<instance>]
            const string org = "mcr";           // motorcycle
            const string workload = "rag";      // rag system
            const string env = "dev";           // development
            const string loc = "cus";           // central us
            const string namePrefix = $"{org}-{workload}-{env}-{loc}";
            var foundryLoc = string.Equals(foundryLocation, "eastus2", StringComparison.OrdinalIgnoreCase)
                ? "eus2"
                : foundryLocation.Replace(" ", string.Empty).ToLowerInvariant();
            var foundryNamePrefix = $"{org}-{workload}-{env}-{foundryLoc}";

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
            var aiServicesSubdomain = resourceGroup.Name.Apply(rgName => $"{org}-{workload}-{env}-cog01-{rgName[^8..]}");
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

            var foundryAiAccountName = $"{foundryNamePrefix}-cog01";
            var foundryAiSubdomain = resourceGroup.Name.Apply(rgName => $"{org}-{workload}-{env}-foundry-{foundryLoc}-{rgName[^8..]}");
            var foundryAiServices = new Pulumi.AzureNative.Resources.Resource(foundryAiAccountName, new Pulumi.AzureNative.Resources.ResourceArgs {
                ResourceGroupName = resourceGroup.Name,
                ResourceProviderNamespace = "Microsoft.CognitiveServices",
                ResourceType = "accounts",
                ResourceName = foundryAiAccountName,
                ParentResourcePath = "",
                ApiVersion = "2025-06-01",
                Location = foundryLocation,
                Kind = "AIServices",
                Identity = new Pulumi.AzureNative.Resources.Inputs.IdentityArgs {
                    Type = Pulumi.AzureNative.Resources.ResourceIdentityType.SystemAssigned
                },
                Sku = new Pulumi.AzureNative.Resources.Inputs.SkuArgs {
                    Name = "S0"
                },
                Properties = new Dictionary<string, object?> {
                    ["allowProjectManagement"] = true,
                    ["customSubDomainName"] = foundryAiSubdomain,
                    ["publicNetworkAccess"] = "Enabled"
                }
            });

            var orchestratorFallbackModelDeployment = CreateFoundryModelDeployment(
                $"{foundryNamePrefix}-orchestrator-fallback-model",
                resourceGroup.Name,
                foundryAiServices.Name,
                orchestratorFallbackDeploymentName,
                "OpenAI",
                orchestratorFallbackModelName,
                orchestratorFallbackModelVersion,
                orchestratorFallbackModelSku,
                orchestratorFallbackModelCapacity,
                new Pulumi.Resource[] { foundryAiServices });

            var subAgentModelDeployment = CreateFoundryModelDeployment(
                $"{foundryNamePrefix}-subagent-model",
                resourceGroup.Name,
                foundryAiServices.Name,
                subAgentDeploymentName,
                "OpenAI",
                subAgentModelName,
                subAgentModelVersion,
                subAgentModelSku,
                subAgentModelCapacity,
                new Pulumi.Resource[] { orchestratorFallbackModelDeployment });

            const string foundryProjectName = "motorcycle-rag";
            var foundryProject = new Project($"{foundryNamePrefix}-foundry-project", new ProjectArgs {
                ResourceGroupName = resourceGroup.Name,
                AccountName = foundryAiServices.Name,
                Location = foundryLocation,
                ProjectName = foundryProjectName,
                Identity = new Pulumi.AzureNative.CognitiveServices.Inputs.IdentityArgs {
                    Type = Pulumi.AzureNative.CognitiveServices.ResourceIdentityType.SystemAssigned
                },
                Properties = new ProjectPropertiesArgs {
                    DisplayName = "Motorcycle RAG",
                    Description = "Azure AI Foundry project for the Motorcycle RAG system."
                }
            }, new CustomResourceOptions {
                DependsOn = new Pulumi.Resource[] { subAgentModelDeployment }
            });

            var foundryProjectEndpoint = Output.Tuple(foundryAiServices.Name, foundryProject.Properties).Apply(values => {
                var accountName = values.Item1;
                var projectProperties = values.Item2;
                if (projectProperties.Endpoints is not null && projectProperties.Endpoints.Count > 0) {
                    return projectProperties.Endpoints.Values.First();
                }

                return $"https://{accountName}.services.ai.azure.com/api/projects/{foundryProjectName}";
            });

            // Qwen catalog endpoints are preferred candidates for the orchestrator, but Persistent
            // Agents may reject them. The provisioning CLI validates them by attempting the agent
            // upsert and falls back to the Pulumi-managed OpenAI deployment below.
            var orchestratorModelDeploymentCandidates = string.Join(
                ",",
                new[] { qwen36DeploymentName, qwen35DeploymentName, orchestratorFallbackDeploymentName });

            // 8. Azure Container Registry
            var registry = new Registry($"{org}{workload}{env}acr", new RegistryArgs {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                Sku = new Pulumi.AzureNative.ContainerRegistry.Inputs.SkuArgs {
                    Name = Pulumi.AzureNative.ContainerRegistry.SkuName.Basic
                },
                AdminUserEnabled = false // Use managed identity for ACR access instead of admin user
            });

            // 8b. User-assigned managed identity for Container Apps (ACR pull)
            var containerAppIdentity = new Pulumi.AzureNative.ManagedIdentity.UserAssignedIdentity($"{namePrefix}-identity", new Pulumi.AzureNative.ManagedIdentity.UserAssignedIdentityArgs {
                ResourceGroupName = resourceGroup.Name,
                Location = location
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

            // Custom Domain: Managed Certificates
            // Uses CNAME validation (faster/more reliable than TXT since CNAMEs already resolve correctly).
            // CustomTimeouts allow up to 30 minutes for DigiCert to issue the certificate.
            var apiCert = new Pulumi.AzureNative.App.ManagedCertificate($"{namePrefix}-api-cert", new Pulumi.AzureNative.App.ManagedCertificateArgs {
                ResourceGroupName = resourceGroup.Name,
                EnvironmentName = managedEnvironment.Name,
                Properties = new Pulumi.AzureNative.App.Inputs.ManagedCertificatePropertiesArgs {
                    DomainControlValidation = Pulumi.AzureNative.App.ManagedCertificateDomainControlValidation.CNAME,
                    SubjectName = "motorag.api.palfery.com"
                }
            }, new CustomResourceOptions {
                CustomTimeouts = new CustomTimeouts { Create = System.TimeSpan.FromMinutes(30), Update = System.TimeSpan.FromMinutes(30) }
            });

            var uiCert = new Pulumi.AzureNative.App.ManagedCertificate($"{namePrefix}-ui-cert", new Pulumi.AzureNative.App.ManagedCertificateArgs {
                ResourceGroupName = resourceGroup.Name,
                EnvironmentName = managedEnvironment.Name,
                Properties = new Pulumi.AzureNative.App.Inputs.ManagedCertificatePropertiesArgs {
                    DomainControlValidation = Pulumi.AzureNative.App.ManagedCertificateDomainControlValidation.CNAME,
                    SubjectName = "motorag.palfery.com"
                }
            }, new CustomResourceOptions {
                CustomTimeouts = new CustomTimeouts { Create = System.TimeSpan.FromMinutes(30), Update = System.TimeSpan.FromMinutes(30) }
            });

            // 10. API Container App
            var apiApp = new ContainerApp($"{namePrefix}-api", new ContainerAppArgs {
                ResourceGroupName = resourceGroup.Name,
                ManagedEnvironmentId = managedEnvironment.Id,
                Identity = new ManagedServiceIdentityArgs {
                    Type = Pulumi.AzureNative.App.ManagedServiceIdentityType.SystemAssigned_UserAssigned,
                    UserAssignedIdentities = new[] { containerAppIdentity.Id }
                },
                Configuration = new ConfigurationArgs {
                    Ingress = new IngressArgs {
                        External = true,
                        TargetPort = 8080, // Standard .NET 8/10 port
                        CustomDomains = new[] {
                            new Pulumi.AzureNative.App.Inputs.CustomDomainArgs {
                                Name = "motorag.api.palfery.com",
                                CertificateId = apiCert.Id,
                                BindingType = Pulumi.AzureNative.App.BindingType.SniEnabled
                            }
                        }
                    },
                    Registries = new[]
                    {
                        new RegistryCredentialsArgs
                        {
                            Server = registry.LoginServer,
                            Identity = containerAppIdentity.Id // Use user-assigned managed identity for ACR auth
                        }
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
                    Scale = new ScaleArgs {
                        MinReplicas = 0,
                        MaxReplicas = 10
                    }
                }
            });

            // 11. UI Container App (BFF)
            var uiApp = new ContainerApp($"{namePrefix}-ui", new ContainerAppArgs {
                ResourceGroupName = resourceGroup.Name,
                ManagedEnvironmentId = managedEnvironment.Id,
                Identity = new ManagedServiceIdentityArgs {
                    Type = Pulumi.AzureNative.App.ManagedServiceIdentityType.SystemAssigned_UserAssigned,
                    UserAssignedIdentities = new[] { containerAppIdentity.Id }
                },
                Configuration = new ConfigurationArgs {
                    Ingress = new IngressArgs {
                        External = true,
                        TargetPort = 8080,
                        CustomDomains = new[] {
                            new Pulumi.AzureNative.App.Inputs.CustomDomainArgs {
                                Name = "motorag.palfery.com",
                                CertificateId = uiCert.Id,
                                BindingType = Pulumi.AzureNative.App.BindingType.SniEnabled
                            }
                        }
                    },
                    Registries = new[]
                    {
                        new RegistryCredentialsArgs
                        {
                            Server = registry.LoginServer,
                            Identity = containerAppIdentity.Id // Use user-assigned managed identity for ACR auth
                        }
                    }
                },
                Template = new TemplateArgs {
                    Containers = new[]
                    {
                    new ContainerArgs
                    {
                        Name = "ui",
                        Image = "mcr.microsoft.com/k8se/quickstart:latest", // Placeholder - pipeline will update with real image
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
                    Scale = new ScaleArgs {
                        MinReplicas = 1,
                        MaxReplicas = 10
                    }
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
            // NOTE: Using 0.0.0.0 allows ALL Azure services - acceptable for dev environment
            // In production, this should be restricted to specific VNet subnet or managed identity
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

            _ = new KeyValue("appconfig-kv-api-local-processor-client-id", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAd:LocalProcessorClientId$api",
                Value = "d09d356d-62ac-4f38-b636-64169119ea25"
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

            // Foundry agent references are written by the deploy pipeline into Key Vault after provisioning.
            // App Configuration resolves these versionless secret URIs at runtime for the API.
            _ = new KeyValue("appconfig-kvref-orchestrator-agent-name", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAI:OrchestratorAgentName",
                ContentType = kvRefContentType,
                Value = Output.Format($"{{\"uri\":\"https://{keyVault.Name}.vault.azure.net/secrets/MCR-ORCHESTRATOR-AGENT-NAME\"}}")
            });

            _ = new KeyValue("appconfig-kvref-orchestrator-agent-version", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAI:OrchestratorAgentVersion",
                ContentType = kvRefContentType,
                Value = Output.Format($"{{\"uri\":\"https://{keyVault.Name}.vault.azure.net/secrets/MCR-ORCHESTRATOR-AGENT-VERSION\"}}")
            });

            _ = new KeyValue("appconfig-kvref-vectorsearch-agent-name", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAI:VectorSearchAgentName",
                ContentType = kvRefContentType,
                Value = Output.Format($"{{\"uri\":\"https://{keyVault.Name}.vault.azure.net/secrets/MCR-VECTORSEARCH-AGENT-NAME\"}}")
            });

            _ = new KeyValue("appconfig-kvref-vectorsearch-agent-version", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAI:VectorSearchAgentVersion",
                ContentType = kvRefContentType,
                Value = Output.Format($"{{\"uri\":\"https://{keyVault.Name}.vault.azure.net/secrets/MCR-VECTORSEARCH-AGENT-VERSION\"}}")
            });

            _ = new KeyValue("appconfig-kvref-websearch-agent-name", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAI:WebSearchAgentName",
                ContentType = kvRefContentType,
                Value = Output.Format($"{{\"uri\":\"https://{keyVault.Name}.vault.azure.net/secrets/MCR-WEBSEARCH-AGENT-NAME\"}}")
            });

            _ = new KeyValue("appconfig-kvref-websearch-agent-version", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAI:WebSearchAgentVersion",
                ContentType = kvRefContentType,
                Value = Output.Format($"{{\"uri\":\"https://{keyVault.Name}.vault.azure.net/secrets/MCR-WEBSEARCH-AGENT-VERSION\"}}")
            });

            _ = new KeyValue("appconfig-kvref-pdfsearch-agent-name", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAI:PDFSearchAgentName",
                ContentType = kvRefContentType,
                Value = Output.Format($"{{\"uri\":\"https://{keyVault.Name}.vault.azure.net/secrets/MCR-PDFSEARCH-AGENT-NAME\"}}")
            });

            _ = new KeyValue("appconfig-kvref-pdfsearch-agent-version", new KeyValueArgs {
                ResourceGroupName = resourceGroup.Name,
                ConfigStoreName = appConfig.Name,
                KeyValueName = "AzureAI:PDFSearchAgentVersion",
                ContentType = kvRefContentType,
                Value = Output.Format($"{{\"uri\":\"https://{keyVault.Name}.vault.azure.net/secrets/MCR-PDFSEARCH-AGENT-VERSION\"}}")
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
            _ = new RoleAssignment($"{namePrefix}-pulumi-sp-foundry-agent-role", new RoleAssignmentArgs {
                PrincipalId = "6e42ecfc-2f4a-4c08-b518-5aec4fffd355", // Pulumi service principal OID
                RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/53ca6127-db72-4b80-b1b0-d745d6d5456d", // Azure AI User
                Scope = foundryProject.Id,
                PrincipalType = Pulumi.AzureNative.Authorization.PrincipalType.ServicePrincipal
            }, new CustomResourceOptions {
                DependsOn = new Pulumi.Resource[] { foundryProject }
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

            // RBAC: Storage Blob Data Contributor for API to read/write ingestion source and processor artifacts.
            _ = new RoleAssignment($"{namePrefix}-api-storage-role", new RoleAssignmentArgs {
                PrincipalId = apiApp.Identity.Apply(i => i!.PrincipalId),
                RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/ba92f5b4-2d11-453d-a403-e96b0029c9fe", // Storage Blob Data Contributor
                Scope = storageAccount.Id,
                PrincipalType = Pulumi.AzureNative.Authorization.PrincipalType.ServicePrincipal
            });

            // RBAC: Cognitive Services User for API (Azure AI Foundry chat completions + Document Intelligence)
            _ = new RoleAssignment($"{namePrefix}-api-aiservices-role", new RoleAssignmentArgs {
                PrincipalId = apiApp.Identity.Apply(i => i!.PrincipalId),
                RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/a97b65f3-24c7-4388-baec-2e87135dc908", // Cognitive Services User
                Scope = foundryAiServices.Id,
                PrincipalType = Pulumi.AzureNative.Authorization.PrincipalType.ServicePrincipal
            });

            // RBAC: Storage Blob Data Contributor for BFF to persist DataProtection keys
            _ = new RoleAssignment($"{namePrefix}-ui-storage-dp-role", new RoleAssignmentArgs {
                PrincipalId = uiApp.Identity.Apply(i => i!.PrincipalId),
                RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/ba92f5b4-2d11-453d-a403-e96b0029c9fe", // Storage Blob Data Contributor
                Scope = dpBlobContainer.Id,
                PrincipalType = Pulumi.AzureNative.Authorization.PrincipalType.ServicePrincipal
            });

            // RBAC: AcrPull for Container Apps to pull images from ACR
            _ = new RoleAssignment($"{namePrefix}-acr-pull-role", new RoleAssignmentArgs {
                PrincipalId = containerAppIdentity.PrincipalId,
                RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/7f951dda-4ed3-4680-a7ca-43fe172d538d", // AcrPull
                Scope = registry.Id,
                PrincipalType = Pulumi.AzureNative.Authorization.PrincipalType.ServicePrincipal
            });

            if (!string.IsNullOrEmpty(developerObjectId)) {
                // RBAC: Local developer access for debugging
                _ = new RoleAssignment($"{namePrefix}-dev-search-role", new RoleAssignmentArgs {
                    PrincipalId = developerObjectId,
                    RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/1407120a-92aa-4202-b7e9-c0e197c71c8f", // Search Index Data Reader
                    Scope = searchService.Id,
                    PrincipalType = Pulumi.AzureNative.Authorization.PrincipalType.User
                });
                _ = new RoleAssignment($"{namePrefix}-dev-aiservices-role", new RoleAssignmentArgs {
                    PrincipalId = developerObjectId,
                    RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/a97b65f3-24c7-4388-baec-2e87135dc908", // Cognitive Services User
                    Scope = foundryAiServices.Id,
                    PrincipalType = Pulumi.AzureNative.Authorization.PrincipalType.User
                });
            }

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
            this.OrchestratorModelDeploymentCandidates = Output.Create(orchestratorModelDeploymentCandidates);
            this.SubAgentModelDeploymentName = Output.Create(subAgentDeploymentName);
        }
#pragma warning restore S138 // Functions should not have too many lines of code
#pragma warning restore S1200 // Pulumi stacks naturally have many dependencies; splitting would require major refactor
#pragma warning restore S3059 // Pulumi requires public class for deployment
#pragma warning restore CA1515 // Pulumi requires public class for deployment

        private static Pulumi.AzureNative.CognitiveServices.Deployment CreateFoundryModelDeployment(
            string resourceName,
            Input<string> resourceGroupName,
            Input<string> accountName,
            string deploymentName,
            string modelFormat,
            string modelName,
            string modelVersion,
            string skuName,
            int capacity,
            Pulumi.Resource[] dependsOn)
        {
            return new Pulumi.AzureNative.CognitiveServices.Deployment(resourceName, new Pulumi.AzureNative.CognitiveServices.DeploymentArgs
            {
                ResourceGroupName = resourceGroupName,
                AccountName = accountName,
                DeploymentName = deploymentName,
                Properties = new Pulumi.AzureNative.CognitiveServices.Inputs.DeploymentPropertiesArgs
                {
                    Model = new Pulumi.AzureNative.CognitiveServices.Inputs.DeploymentModelArgs
                    {
                        Format = modelFormat,
                        Name = modelName,
                        Version = modelVersion
                    }
                },
                Sku = new Pulumi.AzureNative.CognitiveServices.Inputs.SkuArgs
                {
                    Name = skuName,
                    Capacity = capacity
                }
            }, new CustomResourceOptions
            {
                DependsOn = dependsOn
            });
        }

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

        [Output("orchestratorModelDeploymentCandidates")]
        public Output<string> OrchestratorModelDeploymentCandidates { get; set; }

        [Output("subAgentModelDeploymentName")]
        public Output<string> SubAgentModelDeploymentName { get; set; }
    }
#pragma warning restore CA1506 // Avoid excessive class coupling
}
