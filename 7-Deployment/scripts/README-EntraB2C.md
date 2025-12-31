# Entra ID B2C Setup for MotorcycleRAG Mobile App

This directory contains automation scripts for configuring Microsoft Entra External ID / B2C authentication for the mobile application.

## Overview

The `setup-entra-b2c.ps1` script automates the creation and configuration of an Entra B2C app registration with:

- **Multi-platform support**: Redirect URIs for iOS, Android, and Windows
- **Social identity providers**: Configured for Google, Microsoft, GitHub, Facebook
- **Secure secret management**: Client secrets stored in Azure Key Vault
- **Environment-specific**: Separate configurations for Dev, Staging, Prod

## Prerequisites

### 1. Azure CLI

Install Azure CLI from: https://docs.microsoft.com/en-us/cli/azure/install-azure-cli

Verify installation:
```powershell
az --version
```

### 2. Azure Authentication

Authenticate to Azure:
```powershell
az login
```

Verify you're authenticated:
```powershell
az account show
```

### 3. Required Permissions

You need one of the following roles in the Entra B2C tenant:
- **Application Administrator** (recommended)
- **Cloud Application Administrator**
- **Global Administrator** (not recommended for production)

Verify your role:
```powershell
az ad signed-in-user show --query "userPrincipalName"
```

### 4. Azure Key Vault

A Key Vault must exist in your Azure subscription for storing client secrets.

Create Key Vault (if needed):
```powershell
az keyvault create `
    --name "kv-motorcyclerag-dev" `
    --resource-group "rg-motorcyclerag-dev" `
    --location "eastus"
```

Grant yourself access to set secrets:
```powershell
az keyvault set-policy `
    --name "kv-motorcyclerag-dev" `
    --upn "your-email@domain.com" `
    --secret-permissions get set delete list
```

### 5. B2C Tenant Configuration

Ensure your Entra B2C tenant has:
- **User flows** configured (e.g., `B2C_1_SignUpSignIn`)
- **Social identity providers** added:
  - Google
  - Microsoft Account
  - GitHub
  - Facebook

## Usage

### Basic Usage - Development Environment

```powershell
# Navigate to scripts directory
cd 7-Deployment/scripts

# Run setup for development
.\setup-entra-b2c.ps1 `
    -TenantId "motorcyclerag.onmicrosoft.com" `
    -Environment Dev `
    -KeyVaultName "kv-motorcyclerag-dev"
```

### Staging Environment

```powershell
.\setup-entra-b2c.ps1 `
    -TenantId "motorcyclerag.onmicrosoft.com" `
    -Environment Staging `
    -KeyVaultName "kv-motorcyclerag-staging"
```

### Production Environment

```powershell
.\setup-entra-b2c.ps1 `
    -TenantId "motorcyclerag.onmicrosoft.com" `
    -Environment Prod `
    -KeyVaultName "kv-motorcyclerag-prod"
```

### Force Recreate (Use with Caution)

```powershell
# WARNING: This deletes and recreates the app registration
.\setup-entra-b2c.ps1 `
    -TenantId "motorcyclerag.onmicrosoft.com" `
    -Environment Dev `
    -KeyVaultName "kv-motorcyclerag-dev" `
    -Force
```

## Script Output

The script will output configuration in JSON format that you should add to `appsettings.{Environment}.json`:

```json
{
  "AzureAdB2C": {
    "Instance": "https://motorcyclerag.b2clogin.com",
    "Domain": "motorcyclerag.onmicrosoft.com",
    "ClientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "SignUpSignInPolicyId": "B2C_1_SignUpSignIn",
    "Scopes": [
      "https://motorcyclerag.onmicrosoft.com/api://motorcyclerag-mobile/user_impersonation"
    ]
  }
}
```

**IMPORTANT**: The client secret is NOT included in this output. It is stored securely in Azure Key Vault and retrieved at runtime.

## Redirect URIs

The script configures the following redirect URIs:

### iOS
```
msauth.com.motorcyclerag.mobile://auth
```

Configured in `Platforms/iOS/Info.plist`:
```xml
<key>CFBundleURLTypes</key>
<array>
    <dict>
        <key>CFBundleURLSchemes</key>
        <array>
            <string>msauth.com.motorcyclerag.mobile</string>
        </array>
    </dict>
</array>
```

### Android

**Placeholder** (requires actual signature hash):
```
msauth://com.motorcyclerag.mobile/{signature_hash}
```

To get your signature hash:
```powershell
# Debug keystore (development only)
keytool -list -v -keystore ~/.android/debug.keystore -alias androiddebugkey -storepass android -keypass android

# Production keystore
keytool -list -v -keystore /path/to/release.keystore -alias your-alias
```

Configured in `Platforms/Android/AndroidManifest.xml`:
```xml
<activity android:name="microsoft.identity.client.BrowserTabActivity">
    <intent-filter>
        <action android:name="android.intent.action.VIEW" />
        <category android:name="android.intent.category.DEFAULT" />
        <category android:name="android.intent.category.BROWSABLE" />
        <data
            android:scheme="msauth"
            android:host="com.motorcyclerag.mobile"
            android:path="/{signature_hash}" />
    </intent-filter>
</activity>
```

### Windows
```
https://login.microsoftonline.com/common/oauth2/nativeclient
```

Standard redirect URI for Windows native apps.

## Client Secret Retrieval

The client secret is stored in Azure Key Vault and should be retrieved at runtime.

### In MauiProgram.cs

```csharp
public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();

    // ... other configuration

    // Retrieve Entra B2C configuration
    var azureAdB2C = builder.Configuration.GetSection("AzureAdB2C");
    var clientId = azureAdB2C["ClientId"];
    var tenantId = azureAdB2C["Domain"];

    // Retrieve client secret from Key Vault (development)
    // In production, this would be retrieved from device secure storage
    // after initial setup via authentication service
    var keyVaultName = builder.Configuration["KeyVault:Name"];
    var secretName = $"EntraB2C-ClientSecret-{builder.Configuration["Environment"]}";

    // Register MSAL authentication service
    builder.Services.AddSingleton<IAuthenticationService>(sp =>
        new AuthenticationService(clientId, tenantId, keyVaultName, secretName));

    return builder.Build();
}
```

## Security Best Practices

### ✅ DO

- **Store secrets in Key Vault**: Never in source control, appsettings, or console output
- **Use environment-specific configurations**: Separate Dev/Staging/Prod
- **Rotate secrets regularly**: Generate new secrets and update Key Vault
- **Use least privilege**: Grant minimum required permissions
- **Audit access**: Review Key Vault access logs regularly

### ❌ DON'T

- **Hardcode secrets**: Never in code or configuration files
- **Display secrets**: Never log or print client secrets
- **Commit secrets**: Never commit secrets to source control
- **Share secrets**: Don't share via email, chat, or insecure channels
- **Use production secrets in dev**: Keep environments isolated

## Troubleshooting

### "Azure CLI not found"

**Solution**: Install Azure CLI from https://docs.microsoft.com/en-us/cli/azure/install-azure-cli

### "Not authenticated to Azure"

**Solution**: Run `az login` and authenticate with your Azure account

### "Key Vault not found or inaccessible"

**Solution**: Verify the Key Vault exists and you have access:
```powershell
az keyvault show --name "kv-motorcyclerag-dev"
```

### "Insufficient privileges"

**Solution**: Request Application Administrator or Cloud Application Administrator role from your Azure AD admin

### "App registration already exists"

**Solution**:
- Use the existing app registration (script will update it)
- OR use `-Force` flag to recreate (WARNING: destroys existing app)

### "Failed to store secret in Key Vault"

**Solution**: Ensure you have `set` permission for secrets:
```powershell
az keyvault set-policy `
    --name "kv-motorcyclerag-dev" `
    --upn "your-email@domain.com" `
    --secret-permissions set
```

## Next Steps

After running the script:

1. **Update appsettings.json**: Add the output configuration
2. **Configure Android signature**: Update redirect URI with actual hash
3. **Implement MSAL in app**: Configure authentication service in MauiProgram.cs
4. **Test authentication**: Verify sign-in flow on all platforms
5. **Document Client ID**: Store app ID in project documentation

## Additional Resources

- [Microsoft Entra External ID Documentation](https://learn.microsoft.com/en-us/entra/external-id/)
- [MSAL for .NET MAUI](https://learn.microsoft.com/en-us/azure/active-directory/develop/msal-net-xamarin-android-considerations)
- [Azure Key Vault Best Practices](https://learn.microsoft.com/en-us/azure/key-vault/general/best-practices)
- [B2C Social Identity Providers](https://learn.microsoft.com/en-us/azure/active-directory-b2c/add-identity-provider)

## Constitution Compliance

This automation aligns with:

- **Security (Principle I)**: Secrets stored in Key Vault, never in source control
- **Process & Workflow (Principle VII)**: PowerShell for Windows environment
- **Deployment (7-Deployment layer)**: Infrastructure scripts in deployment layer

---

**Version**: 1.0.0
**Last Updated**: 2025-12-26
**Maintainer**: MotorcycleRAG Team
