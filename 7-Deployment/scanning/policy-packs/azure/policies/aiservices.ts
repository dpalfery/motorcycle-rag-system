// CrossGuard policy rules for Azure AI / Cognitive Services accounts.
//
// Resource type: azure-native:cognitiveservices:Account
//
// IMPORTANT GAP: In Program.cs, the two primary AI Services accounts
// (`aiServices`, `foundryAiServices`) are provisioned via the GENERIC
// `azure-native:resources:Resource` type (ResourceProviderNamespace =
// "Microsoft.CognitiveServices") rather than the typed
// `azure-native:cognitiveservices:Account`. CrossGuard's
// `validateResourceOfType` matches on the resource type token, so this rule will
// currently only catch the Document Intelligence account (`docIntel`), which is
// the only typed Account and which sets PublicNetworkAccess = "Enabled".
//
// A separate token-based rule for the generic resource would be needed to cover
// the other two accounts; that is tracked as a follow-up concern.
//
// Refs:
//   - https://www.pulumi.com/registry/packages/azure-native/api-docs/cognitiveservices/account/

import { validateResourceOfType } from "@pulumi/policy";
import * as cognitiveservices from "@pulumi/azure-native/cognitiveservices";

export const aiServicesPolicies = [
    // Control: ai-services-public-network-disabled
    // Checkov CKV_AZURE_237 / CIS Azure 9.1 — Cognitive Services / AI Services
    // accounts should disable public network access and be reached via private
    // endpoints. Currently PublicNetworkAccess = "Enabled" on docIntel -> dev debt.
    {
        name: "ai-services-public-network-disabled",
        description: "Cognitive Services accounts should disable public network access.",
        enforcementLevel: "advisory" as const,
        validateResource: validateResourceOfType(
            cognitiveservices.Account,
            (props, args, reportViolation) => {
                const properties = (props as any).properties;
                const pna = properties?.publicNetworkAccess;
                if (pna && String(pna).toLowerCase() === "enabled") {
                    reportViolation(
                        "Cognitive Services account has public network access enabled. Set properties.publicNetworkAccess to 'Disabled' and use private endpoints.",
                    );
                }
            },
        ),
    },
];
