name: "Scripting-Rule"
description: "Enforces scripting and command window standards."
when-to-apply:
"Apply when writeing scripts or using the command window."
rule: |
you are running on a windows machine, do not use bash or linux syntax. 
Avoid the && operator. 