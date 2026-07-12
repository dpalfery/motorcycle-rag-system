// CrossGuard policy rules for Azure Container Registry.
//
// Resource type: azure-native:containerregistry:Registry
//
// Refs:
//   - https://www.pulumi.com/registry/packages/azure-native/api-docs/containerregistry/registry/

import { validateResourceOfType } from "@pulumi/policy";
import * as containerregistry from "@pulumi/azure-native/containerregistry";

export const acrPolicies = [
    // Control: acr-admin-user-disabled
    // CIS Azure 7.1 / Checkov CKV_AZURE_136 — The ACR admin user must be
    // disabled; registry access should use managed identity + RBAC. Currently
    // satisfied by Program.cs (AdminUserEnabled = false) -> lock-in-good.
    {
        name: "acr-admin-user-disabled",
        description: "Container Registry must not enable the admin user.",
        enforcementLevel: "advisory" as const,
        validateResource: validateResourceOfType(
            containerregistry.Registry,
            (props, args, reportViolation) => {
                if (props.adminUserEnabled === true) {
                    reportViolation(
                        "Container Registry has the admin user enabled. Set adminUserEnabled to false and use managed identity for access.",
                    );
                }
            },
        ),
    },
];
