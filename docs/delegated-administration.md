# Delegate administration in the isolated lab

Administration is available only on `DC01.ad.lab.test`, with the default naming context `DC=ad,DC=lab,DC=test`. Create, edit, enable/disable and group membership changes are scoped to `OU=AppTesting,DC=ad,DC=lab,DC=test` and individually listed child OUs. A named child does not authorize its descendants. The application runs as a standard local user. Provisioning the policy and delegating server permissions are separate lab-owner tasks.

## Prepare the Windows client policy

1. Start from the [fictional example](../samples/administration.example.json). In a private copy, replace the placeholder GUIDs with the AD **objectGUID** of the delegated operator, each approved disposable existing user and each approved security group. Use AD Administrative Center or an existing read-only directory tool to inspect these values. Set `ApprovedOperatorGroups` to the canonical object SID of each approved ordinary security group for operator authorization. Obtain the SID on the authenticated lab controller. Names, GUIDs, foreign-domain SIDs, reserved RIDs and duplicates are ineligible. Leave unused target lists empty. List a child OU only when it exists and is approved. The protected root is always eligible.
2. Map each department name under `Templates` to an array of approved group GUIDs. The template proposes those memberships in the wizard. An operator reviews the checklist before applying it.
3. As a local administrator, install the private copy at `%ProgramData%\AD Control Center\administration.json`. Use **Properties → Security → Advanced** for the folder and file. Set the owner to local **Administrators** or **SYSTEM**. Give those principals full control and give the intended Windows operator read/traverse access. Remove inherited or explicit allowances for other principals to write, delete, take ownership or change permissions. Use an ordinary local directory and file. The app verifies ownership and write ACLs on both.
4. Start AD Control Center normally. The live policy is loaded when administration is explicitly enabled. End administration and enable it again to load policy revisions. A missing or rejected policy keeps administration unavailable. Keep the configured policy and journal on the client outside source control.

## Delegate the minimum rights in Active Directory

Use one ordinary, dedicated lab user with **Domain Users** as its primary group. Delegate the necessary object-specific ACEs to its approved ordinary lab security group or directly to its SID. The operator must be a direct member of at least one approved operator group even when an ACE is assigned directly.

After the explicit confirmation, v1.2.1 resolves the operator GUID and compares the bound LDAP Who Am I identity on the authenticated controller. It reads the domain SID from the domain object, resolves each configured group by binary SID, and checks protection flags, group type and parent memberships. It compares the current direct member links with a fresh, complete `tokenGroups` read for the same operator GUID and SID. Unexpected token SIDs, including privileged groups, fail the check. The same verification runs during preparation and again before a write.

Operator groups authorize the named account. Writable membership targets use separate object GUIDs. The application rejects a policy overlap discovered on the controller. The operator cannot manage its own account.

### Upgrade a v1.2.0 machine policy

A legacy policy can still be read. Live Administration requires at least one verified `ApprovedOperatorGroups` entry. Add that single array to the machine-owned policy, preserving the operator GUID, target GUIDs, OU list and templates. Preserve the file and directory owner and protected ACLs. Leave administration and re-enable it to reload the policy. The v1.2.0 binary rejects the new field if you roll back the executable.

### Check delegation without modifying AD

On the exact destination OU, read `allowedChildClassesEffective` and inspect the DACL for **Create Child / user** granted to the operator or its effective groups. For initial password assignment, inspect the OU for **Reset Password** extended-right ACEs inherited by descendant user objects. Check inheritance restrictions and deny ACEs. On a separately approved existing fixture, review effective rights for the individual user attributes in the table below. An empty constructed attribute alone cannot establish the cause of a denial. An OU ACL cannot establish a future user object's complete effective access.

| Target | Rights needed for this workflow |
| --- | --- |
| Directory and Global Catalog reads | Read the visible inventory, principal GUIDs/SIDs, group type and links, account flags, protection attributes, primary group and computed `tokenGroups`. The controller must be able to resolve effective memberships. |
| Each approved creation OU | **Create Child** for the **user** object class on that OU. Limit inheritance to the intended objects and verify the effective rights in any child OU. |
| Disposable managed user objects | Creation sets the `cn` naming attribute and initializes `sAMAccountName`, `userPrincipalName`, `givenName`, `sn`, `displayName`, `department`, `title`, `mail`, `description` and `userAccountControl`. Subsequent edits affect the name/profile fields, email, description or enabled flag. The editor locks sAMAccountName and UPN. The wizard initializes `cn`, `sAMAccountName` and `userPrincipalName` during creation. It never edits those three attributes later. The delegation plan grants individual property writes only for subsequent Modify requests. |
| Newly created disposable users | Delegate the **Reset Password** extended right for initial assignment and write access to `pwdLastSet` for the requested next-logon behavior. Reserve any OU with inherited rights for disposable test users. |
| Each approved security group | Delegate write access to the **member** attribute on that specific group object. The app adds or removes a single user value. Keep the group in an approved OU. |

Review effective rights, inherited ACEs, object ownership and CREATOR OWNER behavior for both existing and newly created users. The application policy narrows what the UI can submit. The account's AD ACLs independently govern what its credential can do, including outside this application. Keep domain-wide rights, generic write/full control, delete, move, ownership, ACL edits and privileged role assignments outside this delegation. Have the lab owner check that approved custom groups grant no elevated rights to machines, applications or other resources.

## Review and apply the AppTesting ACL

[Set-AppTestingDelegation.ps1](../scripts/Set-AppTestingDelegation.ps1) uses Microsoft’s Windows LDAP client with Kerberos signing and sealing. It targets DC01, resolves the delegation group SID from the trusted domain, and reads schema GUIDs and property sets from the same controller. Plan is the default mode. Run it on the Windows client with the operator credential, entered locally.

```powershell
$operator = Get-Credential -UserName 'adcontrol.operator@ad.lab.test' -Message 'Read-only delegation review'
.\scripts\Set-AppTestingDelegation.ps1 -Mode Plan -Credential $operator
```

The per-connection application policy controls what the wizard will submit. The AD grants apply to the named delegation group and to inheriting user objects below AppTesting. A property ACE for `userAccountControl` covers that attribute. The application changes the enabled bit, and the controller enforces restrictions on sensitive flags. Keep the OU reserved for disposable ordinary accounts.

### Read-only lab inspection on 2026-11-23

The operator authenticated to DC01 over signed and sealed Kerberos, then passed the v1.2.1 application authorization check. The AppTesting DACL had 41 ACEs. The operator could see zero descendant users, so there was no user-object DACL or `allowedAttributesEffective` result for a user to verify. Both approved staff groups reported write access to `member`.

| Proposed explicit Allow ACE for ADControl-Lab-Operators | Applies to |
| --- | --- |
| Create Child of class `user` | AppTesting itself only |
| Reset Password extended right | Descendant user objects |
| Write Property on `givenName`, `sn`, `title`, `mail`, `userAccountControl` and `pwdLastSet` | Descendant user objects, one ACE per attribute |

The inspected OU had no matching allow or deny for those eight rights. Existing matching allows covered `displayName`, `department` and `description`. The proposed result has 49 ACEs. The two group DACLs require no change. The password operation replaces `unicodePwd` on the protected connection using the Reset Password extended right. The plan adds no ordinary property write for `unicodePwd`. [Microsoft AD Add security checks](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-adts/af608d52-1427-4dbc-aa74-dd51b33ae46b) · [Microsoft Reset Password extended right](https://learn.microsoft.com/en-us/windows/win32/adschema/r-user-force-change-password)

### Approved application attempt on 2026-11-23

The ACL administrator credential supplied for the attempt authenticated, and the live plan matched the approved eight additions. DC01 returned insufficient access rights during Apply. A separate signed and sealed operator session then verified the original OU and trustee fingerprint, all 41 original ACEs in their original order, and zero of the eight proposed entries. Application authorization passed. Both staff groups still advertised write access to `member`; the OU did not advertise creation of a user for the operator. No visible descendant user was available to verify inherited permissions. No account, password or membership operation was performed.

The denied attempt left the baseline intact. A subsequent authorized lab administrator authenticated to DC01 using signed and sealed Kerberos. DC01 advertised effective DACL write access on AppTesting. The ordered 41-ACE baseline, OU and trustee identity, review ID and eight proposed entries matched the original review. After the administrator confirmed the displayed diff, DC01 acknowledged the update. Readback matched the expected 49-entry DACL, including the original entries and their multiplicities. A fresh LDAP bind verified the inverse against the original review in memory. The private receipts include the approved keys, rollback script and verification results.

A separate operator session passed v1.2.1 authorization and found exactly one explicit ACE with the approved trustee, mask, object type and inheritance flags for each of the eight additions. Removing those eight from the local copy reproduced the original ordered 41-ACE fingerprint. DC01 reported effective user creation on AppTesting and no effective DACL write access for the operator. Both approved staff groups still advertised member writes. There were zero visible descendant users. Reset Password and the six attribute-write ACEs were verified on the parent OU; inheritance and effective access on a particular user remain untested.

### Approval, verification and rollback

Keep the review ID and exact added keys from the private Plan output. Apply requires both, an authorized ACL administrator on the lab domain, and the operator’s explicit approval of that diff. The example below uses the keys from this inspection. A different plan requires fresh approval.

```powershell
$keys = 'CreateUser','ResetPassword','Write.givenName','Write.sn','Write.title','Write.mail','Write.userAccountControl','Write.pwdLastSet'
$review = Read-Host 'Paste the approved Plan review ID'
$aclAdmin = Get-Credential -Message 'Lab identity authorized to edit the AppTesting DACL'
.\scripts\Set-AppTestingDelegation.ps1 -Mode Apply -Credential $aclAdmin -ReviewId $review -Additions $keys -WhatIf
# After explicit approval, run the same command with -Confirm in place of -WhatIf.
```

Coordinate the brief ACL edit window with other lab administrators. The script rereads the OU GUID, DACL, revision and visible user inventory after confirmation. LDAP DACL replacement uses a separate request after that read. Directory readback checks every ACE and its multiplicity, including inherited entries. Changes to the target, pre-existing ACL or reviewed population require a new plan. On a timeout or incomplete acknowledgement, inspect DC01 before any retry.

After approval and application, use a fresh operator bind on DC01. Rerun Plan and look for zero missing grants. Independently inspect the OU's advanced security entries, the operator's effective child classes, inherited user-specific permissions on any **separately approved** disposable fixture, and `member` on each approved staff group. Recheck Connection Manager authorization. Actual creation, password assignment and membership changes each need separate approval.

To preview rollback, use the **original** review ID and keys:

```powershell
.\scripts\Set-AppTestingDelegation.ps1 -Mode Rollback -ReviewId $review -Additions $keys -Credential $aclAdmin -WhatIf
```

Rollback removes only the exact explicit ACEs matching the reviewed trustee, right, type GUID and inheritance flags. It first checks that this simulated removal reproduces the original reviewed ACL signature. An unrelated ACL edit, ambiguous duplicate or modified ACE stops rollback for manual review. Keep any unrelated ACEs and review a new exact delta with the lab owner. After separate rollback approval, use `-Confirm` and verify the OU again. The approved OU DACL update is complete. End-to-end account creation, temporary password assignment and group modification remain separate tests requiring explicit authorization.

## Enforced restrictions

- A protected LDAPS bind or Kerberos bind with signing and sealing is required. Authentication uses the controller FQDN. Referrals and automatic reconnects are disabled. Enabling administration reuses that same bound connection.
- Only exact approved OU DNs are accepted. Child OU components support ordinary letters, digits, spaces, dots, underscores and hyphens. The provider checks the actual server object class and excludes critical system OUs.
- Existing users and groups are located by GUID and checked again at write time. Users created by this wizard are also eligible for the current administration session. To edit them in later sessions, the lab owner must add them to the policy. A deleted and recreated object gets a different GUID.
- The provider rejects built-in and protected principals, administrative effective memberships, SID history, critical objects, nonstandard primary groups, and trust or delegation-enabled target accounts. Approved groups must be ordinary security groups without parent group memberships. Unavailable effective membership evidence blocks a write.
- Enabling the mode verifies identity and scope. AD evaluates delegated rights on each actual request. A timeout after submission can leave an unknown result. Review the server and its security events before authorizing another change.

The client journal records application activity. AD security auditing remains the independent source for domain changes. [Microsoft LDAP Who Am I specification](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-adts/faf0b8c6-8c59-439f-ac62-dc4c078ed715) · [Microsoft LDAP password handling](https://learn.microsoft.com/en-us/troubleshoot/windows-server/active-directory/change-windows-active-directory-user-password)
