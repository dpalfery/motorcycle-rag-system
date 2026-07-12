// CrossGuard policy rules for Azure Key Vault.
//
// Resource type: azure-native:keyvault:Vault
//
// Key Vault data-plane authorization settings are nested under
// `properties` (VaultProperties). CrossGuard resolves these to plain values at
// validation time, so the validators cast `props` to access nested fields.
//
// Refs:
//   - https://www.pulumi.com/registry/packages/azure-native/api-docs/keyvault/vault/

import { validateResourceOfType } from "@pulumi/policy";
import * as keyvault from "@pulumi/azure-native/keyvault";

export const keyVaultPolicies = [
    // Control: keyvault-rbac-authorization
    // CIS Azure 8.4 / Checkov CKV_AZURE_41 — Key Vault should use Azure RBAC
    // for data-plane authorization instead of legacy access policies.
    // Currently satisfied by Program.cs (EnableRbacAuthorization = true) ->
    // lock-in-good.
    {
        name: "keyvault-rbac-authorization",
        description: "Key Vault must use RBAC authorization for data-plane access.",
        enforcementLevel: "advisory" as const,
        validateResource: validateResourceOfType(keyvault.Vault, (props, args, reportViolation) => {
            const properties = (props as any).properties;
            if (!properties || properties.enableRbacAuthorization !== true) {
                reportViolation(
                    "Key Vault is not using RBAC authorization. Set properties.enableRbacAuthorization to true.",
                );
            }
        }),
    },

    // Control: keyvault-soft-delete-enabled
    // CIS Azure 8.34 / Checkov CKV_AZURE_42 — Soft delete must be enabled so
    // deleted vaults/secrets can be recovered. Azure defaults this to true for
    // new vaults; flag only when explicitly disabled.
    {
        name: "keyvault-soft-delete-enabled",
        description: "Key Vault must have soft delete enabled.",
        enforcementLevel: "advisory" as const,
        validateResource: validateResourceOfType(keyvault.Vault, (props, args, reportViolation) => {
            const properties = (props as any).properties;
            if (properties && properties.enableSoftDelete === false) {
                reportViolation(
                    "Key Vault has soft delete explicitly disabled. Remove enableSoftDelete=false or set it to true.",
                );
            }
        }),
    },

    // Control: keyvault-network-acls
    // Checkov CKV_AZURE_122 — Key Vault should restrict network access via a
    // networkAcls firewall rule set. Program.cs does not currently set
    // networkAcls -> dev debt (acceptable while the platform is fully public,
    // but should be tracked).
    {
        name: "keyvault-network-acls",
        description: "Key Vault should define a networkAcls firewall rule set.",
        enforcementLevel: "advisory" as const,
        validateResource: validateResourceOfType(keyvault.Vault, (props, args, reportViolation) => {
            const properties = (props as any).properties;
            if (!properties || !properties.networkAcls) {
                reportViolation(
                    "Key Vault has no networkAcls. Configure a networkAcls firewall rule set (defaultAction 'Deny' is recommended).",
                );
            }
        }),
    },
];
