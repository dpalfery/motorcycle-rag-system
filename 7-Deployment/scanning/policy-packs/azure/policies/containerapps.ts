// CrossGuard policy rules for Azure Container Apps.
//
// Resource type: azure-native:app:ContainerApp
//
// Note: Container Apps live under the `app` namespace in azure-native
// (token is `azure-native:app:ContainerApp`), not under a `containerapps`
// namespace.
//
// Refs:
//   - https://www.pulumi.com/registry/packages/azure-native/api-docs/app/containerapp/

import { validateResourceOfType } from "@pulumi/policy";
import * as app from "@pulumi/azure-native/app";

export const containerAppPolicies = [
    // Control: containerapp-managed-identity
    // CIS Azure 7.6 / Checkov CKV_AZURE_240 — A Container App should run with a
    // managed identity (system- and/or user-assigned) so it can authenticate to
    // Azure resources without embedded credentials. Currently satisfied by
    // Program.cs (SystemAssigned_UserAssigned) -> lock-in-good.
    {
        name: "containerapp-managed-identity",
        description: "Container Apps must use a managed identity.",
        enforcementLevel: "advisory" as const,
        validateResource: validateResourceOfType(app.ContainerApp, (props, args, reportViolation) => {
            const identity = (props as any).identity;
            const identityType = identity?.type;
            if (!identityType || identityType === "None") {
                reportViolation(
                    "Container App has no managed identity. Configure identity.type to 'SystemAssigned', 'UserAssigned', or 'SystemAssigned, UserAssigned'.",
                );
            }
        }),
    },
];
