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
| Disposable managed user objects | Creation sets the `cn` naming attribute and initializes `sAMAccountName`, `userPrincipalName`, `givenName`, `sn`, `displayName`, `department`, `title`, `mail`, `description` and `userAccountControl`. Subsequent edits affect the name/profile fields, email, description or enabled flag. The editor locks sAMAccountName and UPN. Allow the relevant attribute writes only on the dedicated fixtures. |
| Newly created disposable users | Delegate the **Reset Password** extended right for initial assignment and write access to `pwdLastSet` for the requested next-logon behavior. Reserve any OU with inherited rights for disposable test users. |
| Each approved security group | Delegate write access to the **member** attribute on that specific group object. The app adds or removes a single user value. Keep the group in an approved OU. |

Review effective rights, inherited ACEs, object ownership and CREATOR OWNER behavior for both existing and newly created users. The application policy narrows what the UI can submit. The account's AD ACLs independently govern what its credential can do, including outside this application. Keep domain-wide rights, generic write/full control, delete, move, ownership, ACL edits and privileged role assignments outside this delegation. Have the lab owner check that approved custom groups grant no elevated rights to machines, applications or other resources.

## Enforced restrictions

- A protected LDAPS bind or Kerberos bind with signing and sealing is required. Authentication uses the controller FQDN. Referrals and automatic reconnects are disabled. Enabling administration reuses that same bound connection.
- Only exact approved OU DNs are accepted. Child OU components support ordinary letters, digits, spaces, dots, underscores and hyphens. The provider checks the actual server object class and excludes critical system OUs.
- Existing users and groups are located by GUID and checked again at write time. Users created by this wizard are also eligible for the current administration session. To edit them in later sessions, the lab owner must add them to the policy. A deleted and recreated object gets a different GUID.
- The provider rejects built-in and protected principals, administrative effective memberships, SID history, critical objects, nonstandard primary groups, and trust or delegation-enabled target accounts. Approved groups must be ordinary security groups without parent group memberships. Unavailable effective membership evidence blocks a write.
- Enabling the mode verifies identity and scope. AD evaluates delegated rights on each actual request. A timeout after submission can leave an unknown result. Review the server and its security events before authorizing another change.

The client journal records application activity. AD security auditing remains the independent source for domain changes. [Microsoft LDAP Who Am I specification](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-adts/faf0b8c6-8c59-439f-ac62-dc4c078ed715) · [Microsoft LDAP password handling](https://learn.microsoft.com/en-us/troubleshoot/windows-server/active-directory/change-windows-active-directory-user-password)
