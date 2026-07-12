// MotorcycleRAG Azure Native CrossGuard policy pack.
//
// This pack is the entry point used by `pulumi preview --policy-pack`.
// It is registered with enforcementLevel "advisory" so findings are
// reported but never block a preview or update. Individual policy rules
// are defined in ./policies/<family>.ts.
//
// Refs:
//   - https://www.pulumi.com/docs/using-pulumi/crossguard/
//   - https://www.pulumi.com/registry/packages/azure-native/

import { PolicyPack } from "@pulumi/policy";

import { acrPolicies } from "./policies/acr";
import { aiServicesPolicies } from "./policies/aiservices";
import { appConfigPolicies } from "./policies/appconfig";
import { containerAppPolicies } from "./policies/containerapps";
import { keyVaultPolicies } from "./policies/keyvault";
import { sqlPolicies } from "./policies/sql";
import { storagePolicies } from "./policies/storage";

/**
 * MotorcycleRAG Azure Native policy pack.
 *
 * Enforcement is advisory by design: the pipeline runs
 * `pulumi preview --policy-pack` to surface findings in CI without
 * gating deploys. Per-rule `enforcementLevel` may be raised to `mandatory`
 * in a later task once findings are triaged.
 */
export const motorcycleRagAzurePolicyPack = new PolicyPack(
    "motorcycle-rag-azure",
    {
        policies: [
            ...storagePolicies,
            ...keyVaultPolicies,
            ...appConfigPolicies,
            ...sqlPolicies,
            ...aiServicesPolicies,
            ...acrPolicies,
            ...containerAppPolicies,
        ],
    },
    {
        enforcementLevel: "advisory",
    },
);
