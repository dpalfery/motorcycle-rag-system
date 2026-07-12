// CrossGuard policy rules for Azure SQL.
//
// Resource types:
//   - azure-native:sql:Server
//   - azure-native:sql:FirewallRule
//
// Refs:
//   - https://www.pulumi.com/registry/packages/azure-native/api-docs/sql/firewallrule/

import { validateResourceOfType } from "@pulumi/policy";
import * as sql from "@pulumi/azure-native/sql";

export const sqlPolicies = [
    // Control: sql-no-allow-azure-services-firewall
    // CIS Azure 6.1 / Checkov CKV_AZURE_21 — A firewall rule with
    // StartIpAddress=0.0.0.0 and EndIpAddress=0.0.0.0 permits any Azure-internal
    // service to reach the logical server ("Allow Azure services and resources
    // to access this server"). Program.cs currently sets this for dev -> dev debt.
    {
        name: "sql-no-allow-azure-services-firewall",
        description: "SQL firewall rules must not use 0.0.0.0-0.0.0.0 (allow all Azure services).",
        enforcementLevel: "advisory" as const,
        validateResource: validateResourceOfType(sql.FirewallRule, (props, args, reportViolation) => {
            const start = props.startIpAddress;
            const end = props.endIpAddress;
            if (start === "0.0.0.0" && end === "0.0.0.0") {
                reportViolation(
                    "SQL firewall rule allows all Azure services (0.0.0.0-0.0.0.0). Restrict the range or use private endpoints / VNet rules instead.",
                );
            }
        }),
    },
];
