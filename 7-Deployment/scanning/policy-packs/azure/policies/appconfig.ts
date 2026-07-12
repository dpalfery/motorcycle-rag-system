// CrossGuard policy rules for Azure App Configuration.
//
// Resource type: azure-native:appconfiguration:ConfigurationStore
//
// Program.cs currently sets DisableLocalAuth=false, SoftDeleteRetentionInDays=0,
// and EnablePurgeProtection=false, so all three rules are expected to surface as
// advisory findings (dev debt). See the comment in Program.cs:
//   // Local auth required for Pulumi ARM provider to manage key-values
//
// Refs:
//   - https://www.pulumi.com/registry/packages/azure-native/api-docs/appconfiguration/configurationstore/

import { validateResourceOfType } from "@pulumi/policy";
import * as appconfiguration from "@pulumi/azure-native/appconfiguration";

export const appConfigPolicies = [
    // Control: appconfig-disable-local-auth
    // Local auth (access keys) should be disabled in favor of Entra ID / managed
    // identity. Currently disabled=false -> dev debt (Pulumi ARM provider still
    // uses it; see Program.cs comment).
    {
        name: "appconfig-disable-local-auth",
        description: "App Configuration should disable local (key-based) authentication.",
        enforcementLevel: "advisory" as const,
        validateResource: validateResourceOfType(
            appconfiguration.ConfigurationStore,
            (props, args, reportViolation) => {
                if (props.disableLocalAuth !== true) {
                    reportViolation(
                        "App Configuration allows local auth. Set disableLocalAuth to true once ARM provider auth is migrated to managed identity.",
                    );
                }
            },
        ),
    },

    // Control: appconfig-purge-protection
    // Purge protection prevents irreversible deletion of the store. Currently
    // false -> dev debt.
    {
        name: "appconfig-purge-protection",
        description: "App Configuration should enable purge protection.",
        enforcementLevel: "advisory" as const,
        validateResource: validateResourceOfType(
            appconfiguration.ConfigurationStore,
            (props, args, reportViolation) => {
                if (props.enablePurgeProtection !== true) {
                    reportViolation(
                        "App Configuration purge protection is disabled. Set enablePurgeProtection to true.",
                    );
                }
            },
        ),
    },

    // Control: appconfig-soft-delete-retention
    // Soft delete retention must be greater than zero to allow recovery.
    // Currently 0 -> dev debt.
    {
        name: "appconfig-soft-delete-retention",
        description: "App Configuration soft-delete retention must be greater than zero.",
        enforcementLevel: "advisory" as const,
        validateResource: validateResourceOfType(
            appconfiguration.ConfigurationStore,
            (props, args, reportViolation) => {
                const retention = props.softDeleteRetentionInDays;
                if (typeof retention !== "number" || retention <= 0) {
                    reportViolation(
                        `App Configuration softDeleteRetentionInDays is ${retention ?? "unset"}. Set it to a value greater than 0 (recommended: 7-90).`,
                    );
                }
            },
        ),
    },
];
