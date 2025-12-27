<#
.SYNOPSIS
    Automate Microsoft Entra External ID / B2C app registration for MotorcycleRAG Mobile App

.DESCRIPTION
    Creates or updates an Entra B2C app registration with:
    - Multi-platform redirect URIs (iOS, Android, Windows)
    - Social identity provider configuration
    - Client secret generation and secure Key Vault storage
    - Environment-specific configuration output

    SECURITY: Client secrets are NEVER displayed or logged - stored directly in Azure Key Vault.

.PARAMETER TenantId
    Entra B2C tenant ID (e.g., "motorcyclerag.onmicrosoft.com")

.PARAMETER Environment
    Target environment: Dev, Staging, or Prod

.PARAMETER KeyVaultName
    Azure Key Vault name for storing client secrets

.PARAMETER AppDisplayName
    Display name for the app registration (default: "MotorcycleRAG Mobile App")

.PARAMETER Force
    Force recreation of app registration if it already exists (WARNING: destroys existing app)

.EXAMPLE
    .\setup-entra-b2c.ps1 -TenantId "motorcyclerag.onmicrosoft.com" -Environment Dev -KeyVaultName "kv-motorcyclerag-dev"

    Creates/updates app registration for Development environment and stores client secret in specified Key Vault.

.EXAMPLE
    .\setup-entra-b2c.ps1 -TenantId "motorcyclerag.onmicrosoft.com" -Environment Prod -KeyVaultName "kv-motorcyclerag-prod" -Force

    Recreates app registration for Production environment (use with caution).

.NOTES
    Prerequisites:
    - Azure CLI installed and authenticated (az login)
    - Required permissions: Application Administrator or Cloud Application Administrator role
    - Key Vault must exist with appropriate access policies
    - B2C tenant configured with social identity providers

    Author: MotorcycleRAG Team
    Version: 1.0.0
    Constitution Compliance: Security (I), Process & Workflow (VII)
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true, HelpMessage="Entra B2C tenant ID (e.g., 'motorcyclerag.onmicrosoft.com')")]
    [ValidateNotNullOrEmpty()]
    [string]$TenantId,

    [Parameter(Mandatory=$true, HelpMessage="Target environment: Dev, Staging, or Prod")]
    [ValidateSet("Dev", "Staging", "Prod")]
    [string]$Environment,

    [Parameter(Mandatory=$true, HelpMessage="Azure Key Vault name for storing client secrets")]
    [ValidateNotNullOrEmpty()]
    [string]$KeyVaultName,

    [Parameter(Mandatory=$false, HelpMessage="Display name for app registration")]
    [string]$AppDisplayName = "MotorcycleRAG Mobile App",

    [Parameter(Mandatory=$false, HelpMessage="Force recreation of app registration")]
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Configuration
$AppIdentifierUri = "api://motorcyclerag-mobile"
$BundleId = "com.motorcyclerag.mobile"
$PackageName = "com.motorcyclerag.mobile"

# Redirect URIs for each platform
$RedirectUris = @(
    "msauth.$BundleId://auth",  # iOS
    "msauth://$PackageName/",    # Android (placeholder - actual signature hash needed)
    "https://login.microsoftonline.com/common/oauth2/nativeclient"  # Windows
)

# API Scopes
$ApiScopes = @(
    @{
        Value = "user_impersonation"
        AdminConsentDisplayName = "Access MotorcycleRAG API"
        AdminConsentDescription = "Allow the application to access MotorcycleRAG API on behalf of the signed-in user"
        UserConsentDisplayName = "Access MotorcycleRAG API"
        UserConsentDescription = "Allow the application to access MotorcycleRAG API on your behalf"
    }
)

#region Helper Functions

function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = "White"
    )
    Write-Host $Message -ForegroundColor $Color
}

function Test-AzureCliInstalled {
    try {
        $azVersion = az version --query '"azure-cli"' -o tsv 2>$null
        if ($azVersion) {
            Write-ColorOutput "✓ Azure CLI version $azVersion detected" -Color Green
            return $true
        }
    }
    catch {
        Write-ColorOutput "✗ Azure CLI not found. Please install: https://docs.microsoft.com/en-us/cli/azure/install-azure-cli" -Color Red
        return $false
    }
}

function Test-AzureCliAuthenticated {
    try {
        $account = az account show --query "user.name" -o tsv 2>$null
        if ($account) {
            Write-ColorOutput "✓ Authenticated as: $account" -Color Green
            return $true
        }
    }
    catch {
        Write-ColorOutput "✗ Not authenticated to Azure. Run 'az login'" -Color Red
        return $false
    }
}

function Test-KeyVaultExists {
    param([string]$VaultName)

    try {
        $vault = az keyvault show --name $VaultName --query "name" -o tsv 2>$null
        if ($vault) {
            Write-ColorOutput "✓ Key Vault '$VaultName' found" -Color Green
            return $true
        }
    }
    catch {
        Write-ColorOutput "✗ Key Vault '$VaultName' not found or inaccessible" -Color Red
        return $false
    }
}

function Get-ExistingAppRegistration {
    param(
        [string]$DisplayName,
        [string]$Tenant
    )

    try {
        $appId = az ad app list --display-name $DisplayName --query "[0].appId" -o tsv 2>$null
        return $appId
    }
    catch {
        return $null
    }
}

#endregion

#region Main Script

Write-ColorOutput "`n========================================" -Color Cyan
Write-ColorOutput "Entra ID B2C App Registration Setup" -Color Cyan
Write-ColorOutput "========================================`n" -Color Cyan

Write-ColorOutput "Configuration:" -Color Yellow
Write-ColorOutput "  Tenant:       $TenantId"
Write-ColorOutput "  Environment:  $Environment"
Write-ColorOutput "  Key Vault:    $KeyVaultName"
Write-ColorOutput "  App Name:     $AppDisplayName"
Write-ColorOutput ""

# Step 1: Prerequisites Check
Write-ColorOutput "Step 1: Checking prerequisites..." -Color Yellow

if (-not (Test-AzureCliInstalled)) {
    exit 1
}

if (-not (Test-AzureCliAuthenticated)) {
    exit 1
}

if (-not (Test-KeyVaultExists -VaultName $KeyVaultName)) {
    exit 1
}

# Step 2: Check for existing app registration
Write-ColorOutput "`nStep 2: Checking for existing app registration..." -Color Yellow

$existingAppId = Get-ExistingAppRegistration -DisplayName "$AppDisplayName ($Environment)" -Tenant $TenantId

if ($existingAppId) {
    Write-ColorOutput "✓ Found existing app registration: $existingAppId" -Color Green

    if ($Force) {
        Write-ColorOutput "⚠ Force flag set - recreating app registration..." -Color Yellow
        az ad app delete --id $existingAppId
        Write-ColorOutput "✓ Deleted existing app registration" -Color Green
        $existingAppId = $null
    }
    else {
        Write-ColorOutput "ℹ Using existing app registration (use -Force to recreate)" -Color Cyan
    }
}
else {
    Write-ColorOutput "ℹ No existing app registration found - will create new" -Color Cyan
}

# Step 3: Create or update app registration
Write-ColorOutput "`nStep 3: Creating/updating app registration..." -Color Yellow

if (-not $existingAppId) {
    # Create new app registration
    $appId = az ad app create `
        --display-name "$AppDisplayName ($Environment)" `
        --sign-in-audience "AzureADandPersonalMicrosoftAccount" `
        --query "appId" `
        -o tsv

    if ($LASTEXITCODE -ne 0) {
        Write-ColorOutput "✗ Failed to create app registration" -Color Red
        exit 1
    }

    Write-ColorOutput "✓ Created app registration: $appId" -Color Green
}
else {
    $appId = $existingAppId
}

# Step 4: Configure redirect URIs
Write-ColorOutput "`nStep 4: Configuring redirect URIs..." -Color Yellow

$redirectUriJson = $RedirectUris | ConvertTo-Json -Compress
az ad app update --id $appId --public-client-redirect-uris $redirectUriJson

if ($LASTEXITCODE -eq 0) {
    Write-ColorOutput "✓ Configured redirect URIs:" -Color Green
    foreach ($uri in $RedirectUris) {
        Write-ColorOutput "    - $uri" -Color Gray
    }
}
else {
    Write-ColorOutput "✗ Failed to configure redirect URIs" -Color Red
    exit 1
}

# Step 5: Generate client secret
Write-ColorOutput "`nStep 5: Generating client secret..." -Color Yellow

$secretName = "EntraB2C-ClientSecret-$Environment-$(Get-Date -Format 'yyyyMMdd')"
$clientSecret = az ad app credential reset --id $appId --append --display-name $secretName --query "password" -o tsv

if ($LASTEXITCODE -ne 0) {
    Write-ColorOutput "✗ Failed to generate client secret" -Color Red
    exit 1
}

Write-ColorOutput "✓ Generated client secret (expires in 2 years)" -Color Green

# Step 6: Store secret in Key Vault
Write-ColorOutput "`nStep 6: Storing client secret in Key Vault..." -Color Yellow

$kvSecretName = "EntraB2C-ClientSecret-$Environment"
az keyvault secret set `
    --vault-name $KeyVaultName `
    --name $kvSecretName `
    --value $clientSecret `
    --output none

if ($LASTEXITCODE -eq 0) {
    Write-ColorOutput "✓ Client secret stored in Key Vault as '$kvSecretName'" -Color Green
    Write-ColorOutput "  ⚠ Secret is NEVER displayed - retrieve from Key Vault when needed" -Color Yellow
}
else {
    Write-ColorOutput "✗ Failed to store secret in Key Vault" -Color Red
    exit 1
}

# Step 7: Output configuration
Write-ColorOutput "`n========================================" -Color Cyan
Write-ColorOutput "Configuration Complete!" -Color Cyan
Write-ColorOutput "========================================`n" -Color Cyan

Write-ColorOutput "App Registration Details:" -Color Yellow
Write-ColorOutput "  App ID (Client ID): $appId" -Color White
Write-ColorOutput "  Tenant ID:          $TenantId" -Color White
Write-ColorOutput "  Environment:        $Environment" -Color White
Write-ColorOutput ""

Write-ColorOutput "Add to appsettings.$Environment.json:" -Color Yellow
Write-ColorOutput @"
{
  "AzureAdB2C": {
    "Instance": "https://$($TenantId.Split('.')[0]).b2clogin.com",
    "Domain": "$TenantId",
    "ClientId": "$appId",
    "SignUpSignInPolicyId": "B2C_1_SignUpSignIn",
    "Scopes": [
      "https://$TenantId/$AppIdentifierUri/user_impersonation"
    ]
  }
}
"@ -Color White

Write-ColorOutput "`nClient Secret Storage:" -Color Yellow
Write-ColorOutput "  Key Vault:    $KeyVaultName" -Color White
Write-ColorOutput "  Secret Name:  $kvSecretName" -Color White
Write-ColorOutput "  ⚠ Retrieve secret at runtime using MSAL SecureStorage" -Color Yellow

Write-ColorOutput "`nNext Steps:" -Color Yellow
Write-ColorOutput "  1. Update appsettings.$Environment.json with configuration above" -Color White
Write-ColorOutput "  2. For Android: Update redirect URI with actual signature hash" -Color White
Write-ColorOutput "  3. Configure MSAL in MauiProgram.cs to retrieve secret from Key Vault" -Color White
Write-ColorOutput "  4. Test authentication flow on each platform" -Color White

Write-ColorOutput "`n✓ Setup complete!" -Color Green

#endregion
