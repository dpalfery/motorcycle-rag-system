// CrossGuard policy rules for Azure Storage accounts.
//
// Resource type: azure-native:storage:StorageAccount
//
// All rules inherit the pack-level enforcement level ("advisory") so findings
// surface in `pulumi preview --policy-pack` without gating deploys.
//
// Refs:
//   - https://www.pulumi.com/registry/packages/azure-native/api-docs/storage/storageaccount/

import { validateResourceOfType } from "@pulumi/policy";
import * as storage from "@pulumi/azure-native/storage";

/**
 * Rule set for azure-native:storage:StorageAccount.
 *
 * Each entry is a {@link PolicyRecord} so it can be spread directly into the
 * pack's `policies` array.
 */
export const storagePolicies = [
    // Control: storage-no-public-blob
    // CIS Azure 3.5 / Checkov CKV_AZURE_59 — Storage accounts must not allow
    // anonymous/public blob access. Currently satisfied by Program.cs
    // (AllowBlobPublicAccess = false) -> lock-in-good.
    {
        name: "storage-no-public-blob",
        description: "Storage accounts must not allow anonymous public blob access.",
        enforcementLevel: "advisory" as const,
        validateResource: validateResourceOfType(storage.StorageAccount, (props, args, reportViolation) => {
            if (props.allowBlobPublicAccess === true) {
                reportViolation(
                    "Storage account allows public blob access. Set allowBlobPublicAccess to false.",
                );
            }
        }),
    },

    // Control: storage-minimum-tls-12
    // CIS Azure 3.8 / Checkov CKV_AZURE_109 — Enforce TLS 1.2 or later for the
    // storage account. Currently satisfied by Program.cs (MinimumTlsVersion = TLS1_2)
    // -> lock-in-good.
    {
        name: "storage-minimum-tls-12",
        description: "Storage accounts must require a minimum of TLS 1.2.",
        enforcementLevel: "advisory" as const,
        validateResource: validateResourceOfType(storage.StorageAccount, (props, args, reportViolation) => {
            const tls = props.minimumTlsVersion;
            const allowed = ["TLS1_2", "TLS1_3"];
            if (typeof tls === "string" && !allowed.includes(tls)) {
                reportViolation(
                    `Storage account minimum TLS version '${tls}' is below TLS 1.2. Set minimumTlsVersion to TLS1_2.`,
                );
            }
        }),
    },

    // Control: storage-network-default-deny
    // Checkov CKV_AZURE_138 — The network rule set should explicitly default to
    // Deny so the storage account is not reachable from arbitrary public
    // endpoints. Program.cs does not currently set a networkRuleSet -> dev debt.
    {
        name: "storage-network-default-deny",
        description: "Storage accounts should define a network rule set with a Deny default action.",
        enforcementLevel: "advisory" as const,
        validateResource: validateResourceOfType(storage.StorageAccount, (props, args, reportViolation) => {
            // CrossGuard delivers plain values at validation time; nested object
            // access uses an explicit cast because the Input<T> union (which
            // includes Promise/Output wrappers) is awkward to narrow in TS.
            const ruleSet = (props as any).networkRuleSet;
            if (!ruleSet) {
                reportViolation(
                    "Storage account has no networkRuleSet. Configure a network rule set with defaultAction 'Deny'.",
                );
                return;
            }
            if (ruleSet.defaultAction && ruleSet.defaultAction !== "Deny") {
                reportViolation(
                    `Storage account network rule set defaultAction is '${ruleSet.defaultAction}'. Set defaultAction to 'Deny'.`,
                );
            }
        }),
    },
];
