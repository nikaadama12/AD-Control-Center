#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess, ConfirmImpact='High')]
param(
    [ValidateSet('Plan','Apply','Rollback')][string]$Mode = 'Plan',
    [PSCredential]$Credential,
    [ValidatePattern('^[0-9A-Fa-f]{64}:[0-9A-Fa-f]{64}$')][string]$ReviewId,
    [ValidatePattern('^[0-9A-Fa-f]{64}$')][string]$ReviewedOrderedBaseline,
    [string[]]$Additions
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.DirectoryServices.Protocols
$dc = 'DC01.ad.lab.test'
$domain = 'ad.lab.test'
$nc = 'DC=ad,DC=lab,DC=test'
$ou = "OU=AppTesting,$nc"
$roleName = 'ADControl-Lab-Operators'
$operatorName = 'adcontrol.operator'
$attributes = 'givenName','sn','displayName','department','title','mail','description','userAccountControl','pwdLastSet'
$resetGuid = [guid]'00299570-246d-11d0-a768-00aa006e0529'

function Hash-Bytes([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-','') } finally { $sha.Dispose() }
}
function Hash-Text([string]$Text) { Hash-Bytes ([Text.Encoding]::UTF8.GetBytes($Text)) }
function Binary($Object) {
    $bytes = [byte[]]::new($Object.BinaryLength)
    $Object.GetBinaryForm($bytes,0)
    return ,$bytes
}
function Read-One([string]$Base,[string]$Filter,[string]$Scope,[string[]]$Names,[switch]$Dacl) {
    $req = [DirectoryServices.Protocols.SearchRequest]::new($Base,$Filter,$Scope,$Names)
    if ($Dacl) {
        $control = [DirectoryServices.Protocols.SecurityDescriptorFlagControl]::new('Dacl')
        $control.IsCritical = $true
        [void]$req.Controls.Add($control)
    }
    $result = $ldap.SendRequest($req)
    if ($result.Entries.Count -ne 1) { throw 'A required directory or schema identity is missing or ambiguous.' }
    $result.Entries[0]
}
function Strings($Entry,[string]$Name) {
    if ($Entry.Attributes.Contains($Name)) { $Entry.Attributes[$Name].GetValues([string]) }
}
function Octets($Entry,[string]$Name) {
    if (!$Entry.Attributes.Contains($Name) -or $Entry.Attributes[$Name].Count -ne 1) { throw "Missing or ambiguous $Name." }
    return ,([byte[]]$Entry.Attributes[$Name].GetValues([byte[]])[0])
}
function Object-Guid($Entry) { [guid]::new((Octets $Entry objectGUID)) }
function Object-Sid($Entry) { [Security.Principal.SecurityIdentifier]::new((Octets $Entry objectSid),0) }
function Read-Sd($Entry) {
    $sd = [Security.AccessControl.RawSecurityDescriptor]::new((Octets $Entry nTSecurityDescriptor),0)
    if ($null -eq $sd.DiscretionaryAcl) { throw 'A DACL is missing or unreadable.' }
    return $sd
}
function Read-Ou {
    $entry = Read-One $ou '(&(objectClass=organizationalUnit)(!(isCriticalSystemObject=TRUE)))' Base @('objectGUID','nTSecurityDescriptor','uSNChanged') -Dacl
    if ($entry.DistinguishedName -ne $ou) { throw 'Unexpected OU identity.' }
    [pscustomobject]@{ Id=Object-Guid $entry; Sd=Read-Sd $entry; Revision=Strings $entry uSNChanged }
}
function Read-Users {
    # Each SD is inspected in memory. Only hashes and aggregate counts leave the function.
    $req = [DirectoryServices.Protocols.SearchRequest]::new($ou,'(&(objectCategory=person)(objectClass=user))','Subtree',
        [string[]]@('objectGUID','adminCount','isCriticalSystemObject','nTSecurityDescriptor','objectSid'))
    $paging = [DirectoryServices.Protocols.PageResultRequestControl]::new(200)
    $paging.IsCritical = $true
    $sdControl = [DirectoryServices.Protocols.SecurityDescriptorFlagControl]::new('Dacl')
    $sdControl.IsCritical = $true
    [void]$req.Controls.Add($paging); [void]$req.Controls.Add($sdControl)
    do {
        $result = $ldap.SendRequest($req)
        foreach ($entry in $result.Entries) {
            $id = Object-Guid $entry
            $sid = Object-Sid $entry
            $sd = Read-Sd $entry
            $protected = (Strings $entry adminCount) -eq '1' -or (Strings $entry isCriticalSystemObject) -eq 'TRUE' -or
                $sd.ControlFlags.HasFlag([Security.AccessControl.ControlFlags]::DiscretionaryAclProtected) -or
                $id -eq $operatorGuid -or $sid.AccountDomainSid -ne $trustedSid -or [uint32]$sid.Value.Split('-')[-1] -lt 1000
            [pscustomobject]@{ Id=$id; Protected=$protected; AclHash=Hash-Bytes (Binary $sd.DiscretionaryAcl) }
        }
        $ack = @($result.Controls | Where-Object { $_ -is [DirectoryServices.Protocols.PageResultResponseControl] })
        if ($ack.Count -ne 1) { throw 'Incomplete descendant inventory.' }
        $paging.Cookie = $ack[0].Cookie
    } while ($paging.Cookie.Length)
}
function Spec([string]$Key,[int]$Mask,[guid]$ObjectType,[guid]$PropertySet,[bool]$Descendant) {
    $flags = [Security.AccessControl.ObjectAceFlags]::ObjectAceTypePresent
    $inherit = [Security.AccessControl.AceFlags]::None
    $class = [guid]::Empty
    if ($Descendant) {
        $flags = $flags -bor [Security.AccessControl.ObjectAceFlags]::InheritedObjectAceTypePresent
        $inherit = [Security.AccessControl.AceFlags]'ContainerInherit,InheritOnly'
        $class = $userClass
    }
    $ace = [Security.AccessControl.ObjectAce]::new($inherit,'AccessAllowed',$Mask,$roleSid,$flags,$ObjectType,$class,$false,$null)
    [pscustomobject]@{ Key=$Key; Mask=$Mask; Type=$ObjectType; Set=$PropertySet; Descendant=$Descendant; Ace=$ace }
}
function Find-Coverage($Acl,$Item) {
    $covered = $false
    foreach ($ace in $Acl) {
        if ($ace -isnot [Security.AccessControl.QualifiedAce]) { throw 'Unsupported ACE type. Review the DACL manually.' }
        if (!$trustees.Contains($ace.SecurityIdentifier.Value)) { continue }
        $deny = $ace.AceQualifier -eq 'AccessDenied'
        $mask = $ace.AccessMask
        $grants = ($mask -band $Item.Mask) -or ($mask -band 0x10000000) -or ($Item.Mask -eq 32 -and ($mask -band 0x40000000))
        if (!$grants) { continue }
        if ($Item.Descendant) {
            if (!$ace.AceFlags.HasFlag([Security.AccessControl.AceFlags]::ContainerInherit)) { continue }
            if (!$deny -and $ace.AceFlags.HasFlag([Security.AccessControl.AceFlags]::NoPropagateInherit)) { continue }
        } elseif ($ace.AceFlags.HasFlag([Security.AccessControl.AceFlags]::InheritOnly)) { continue }
        if ($ace -is [Security.AccessControl.ObjectAce]) {
            if ($ace.ObjectAceFlags.HasFlag([Security.AccessControl.ObjectAceFlags]::ObjectAceTypePresent) -and
                $ace.ObjectAceType -ne $Item.Type -and ($Item.Set -eq [guid]::Empty -or $ace.ObjectAceType -ne $Item.Set)) { continue }
            if ($Item.Descendant -and $ace.ObjectAceFlags.HasFlag([Security.AccessControl.ObjectAceFlags]::InheritedObjectAceTypePresent) -and
                $ace.InheritedObjectAceType -ne $userClass) { continue }
        }
        if ($ace.IsCallback -or $deny) { throw "An applicable deny or conditional ACE requires manual review for $($Item.Key)." }
        if ($ace.AceQualifier -eq 'AccessAllowed') { $covered = $true }
    }
    $covered
}
function Acl-Signature($View) {
    # AD may canonicalize ACE order. Hash every complete ACE including flags, trustee, mask and GUIDs, retaining multiplicity.
    $entries = @($View.Sd.DiscretionaryAcl | ForEach-Object { [Convert]::ToBase64String((Binary $_)) })
    [Array]::Sort($entries,[StringComparer]::Ordinal)
    Hash-Text ("$($View.Id)|$($roleSid.Value)|$([int]$View.Sd.ControlFlags)|" + ($entries -join '|'))
}
function Review-Fingerprint($View,$Items,[string]$PopulationHash) {
    $rows = @($Items | ForEach-Object { $_.Key+'='+[Convert]::ToBase64String((Binary $_.Ace)) })
    # Bind the review to the operator as well as the OU, trustee, baseline and precise additions.
    $digest = Hash-Text ("$(Acl-Signature $View)|$operatorGuid|$PopulationHash|" + ($rows -join '|'))
    "$($PopulationHash):$digest"
}
function Edit-Draft($Acl,$Items,[bool]$Remove) {
    foreach ($item in $Items) {
        $bytes = [Convert]::ToBase64String((Binary $item.Ace))
        $matches = @(for ($i=0;$i -lt $Acl.Count;$i++) {
            if ([Convert]::ToBase64String((Binary $Acl[$i])) -ceq $bytes) { $i }
        })
        if ($Remove) {
            if ($matches.Count -ne 1) { throw "Rollback needs exactly one reviewed explicit ACE for $($item.Key)." }
            $Acl.RemoveAce($matches[0])
        } else {
            if ($matches.Count) { throw "Unexpected existing ACE: $($item.Key)." }
            $index = 0
            while ($index -lt $Acl.Count -and !$Acl[$index].IsInherited) { $index++ }
            $Acl.InsertAce($index,$item.Ace)
        }
    }
}

$identifier = [DirectoryServices.Protocols.LdapDirectoryIdentifier]::new($dc,389,$true,$false)
$ldap = [DirectoryServices.Protocols.LdapConnection]::new($identifier)
try {
    $ldap.AuthType = 'Kerberos'
    $ldap.Timeout = [TimeSpan]::FromSeconds(15)
    $ldap.AutoBind = $false
    $ldap.SessionOptions.ProtocolVersion = 3
    $ldap.SessionOptions.AutoReconnect = $false
    $ldap.SessionOptions.ReferralChasing = 'None'
    $ldap.SessionOptions.Signing = $true
    $ldap.SessionOptions.Sealing = $true
    if ($Credential) {
        $parts = $Credential.UserName.Split('@')
        if ($parts.Count -ne 2 -or !$parts[0] -or $parts[1] -ne $domain) { throw 'Use a lab-domain UPN.' }
        $ldap.Bind([Net.NetworkCredential]::new($parts[0],$Credential.Password,$domain))
    } else { $ldap.Bind() }

    $root = Read-One '' '(objectClass=*)' Base @('defaultNamingContext','dnsHostName','schemaNamingContext')
    if ((Strings $root defaultNamingContext) -ne $nc -or (Strings $root dnsHostName) -ne $dc) { throw 'Unexpected domain or controller.' }
    $schema = Strings $root schemaNamingContext
    $trustedSid = Object-Sid (Read-One $nc '(objectClass=domainDNS)' Base @('objectSid'))
    $role = Read-One $nc "(&(objectClass=group)(sAMAccountName=$roleName))" Subtree @('objectSid','objectGUID','groupType','memberOf','adminCount','isCriticalSystemObject','sIDHistory')
    $roleSid = Object-Sid $role
    if ($roleSid.AccountDomainSid -ne $trustedSid -or [uint32]$roleSid.Value.Split('-')[-1] -lt 1000 -or
        [int](Strings $role groupType) -ge 0 -or ([int](Strings $role groupType) -band 1) -or
        @($role.Attributes.AttributeNames -match '^(memberOf|sIDHistory)(;|$)').Count -or
        (Strings $role adminCount) -eq '1' -or (Strings $role isCriticalSystemObject) -eq 'TRUE') { throw 'Ineligible delegation group.' }

    $actor = Read-One $nc "(&(objectCategory=person)(objectClass=user)(sAMAccountName=$operatorName))" Subtree @('objectGUID','objectSid')
    $operatorGuid = Object-Guid $actor
    $token = Read-One $actor.DistinguishedName '(objectClass=user)' Base @('tokenGroups','objectGUID')
    if ((Object-Guid $token) -ne $operatorGuid -or !$token.Attributes.Contains('tokenGroups') -or
        @($token.Attributes.AttributeNames -like 'tokenGroups;*').Count) { throw 'Incomplete operator membership evidence.' }
    $trustees = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($value in $token.Attributes['tokenGroups'].GetValues([byte[]])) { [void]$trustees.Add([Security.Principal.SecurityIdentifier]::new($value,0).Value) }
    if (!$trustees.Contains($roleSid.Value)) { throw 'Operator is absent from the configured delegation group.' }
    foreach ($sid in (Object-Sid $actor).Value,'S-1-5-11','S-1-1-0') { [void]$trustees.Add($sid) }

    $userClass = [guid]::new((Octets (Read-One $schema '(&(objectClass=classSchema)(lDAPDisplayName=user))' Subtree @('schemaIDGUID')) schemaIDGUID))
    $items = @(
        Spec CreateUser 1 $userClass ([guid]::Empty) $false
        Spec ResetPassword 256 $resetGuid ([guid]::Empty) $true
        foreach ($name in $attributes) {
            $entry = Read-One $schema "(&(objectClass=attributeSchema)(lDAPDisplayName=$name))" Subtree @('schemaIDGUID','attributeSecurityGUID')
            $propertySet = [guid]::Empty
            if ($entry.Attributes.Contains('attributeSecurityGUID')) { $propertySet = [guid]::new((Octets $entry attributeSecurityGUID)) }
            Spec "Write.$name" 32 ([guid]::new((Octets $entry schemaIDGUID))) $propertySet $true
        }
    )
    $view = Read-Ou
    $users = @(Read-Users)
    if (@($users | Where-Object Protected).Count) { throw 'Protected users are present. Review scope manually.' }
    $before = Acl-Signature $view
    $raw = Hash-Bytes (Binary $view.Sd.DiscretionaryAcl)
    if ($Mode -eq 'Apply' -and $ReviewedOrderedBaseline) {
        $orderedBaseline = Hash-Text ("$($view.Id.ToString('D'))|$($roleSid.Value)|$raw")
        if ($orderedBaseline -ne $ReviewedOrderedBaseline) { throw 'Ordered pre-change DACL, OU identity or trustee mismatch. Aborting.' }
    }
    $inventory = (@($users | ForEach-Object { "$($_.Id):$($_.AclHash)" } | Sort-Object) -join '|')
    $missing = @($items | Where-Object { !(Find-Coverage $view.Sd.DiscretionaryAcl $_) })
    $keys = @($missing | ForEach-Object Key)
    $count = $view.Sd.DiscretionaryAcl.Count

    if ($Mode -eq 'Rollback') {
        if (!$ReviewId -or !$Additions) { throw 'Supply the original review ID and added keys.' }
        $selected = @($items | Where-Object { $_.Key -cin $Additions })
        if ($selected.Count -ne $Additions.Count) { throw 'Unknown or duplicate rollback key.' }
        $previous = Review-Fingerprint $view $selected ($ReviewId.Split(':')[0])
        if ($previous -eq $ReviewId) { 'Already at the reviewed pre-change ACL.'; return }
        Edit-Draft $view.Sd.DiscretionaryAcl $selected $true
        $derived = Review-Fingerprint $view $selected ($ReviewId.Split(':')[0])
        if ($derived -ne $ReviewId) { throw 'Removing these exact ACEs would not reproduce the reviewed baseline. No rollback submitted.' }
    } else {
        $selected = $missing
        $derived = Review-Fingerprint $view $selected (Hash-Text $inventory)
        "OU: $ou; trustee: $roleName; visible descendant users inspected: $($users.Count)"
        "Before: $count ACEs. Proposed: $($count+$selected.Count) ACEs. Baseline and change review ID: $derived"
        foreach ($item in $selected) {
            $scope = if ($item.Descendant) { 'descendant user objects only; CI,IO' } else { 'this OU only; no inheritance' }
            "+ Allow $roleName $($item.Key); mask=$($item.Mask); type=$($item.Type); $scope"
        }
        if (!$selected.Count) { return }
        if ($Mode -eq 'Plan') {
            Edit-Draft $view.Sd.DiscretionaryAcl $selected $false
            Edit-Draft $view.Sd.DiscretionaryAcl $selected $true
            if ((Hash-Bytes (Binary $view.Sd.DiscretionaryAcl)) -ne $raw) { throw 'In-memory round trip did not preserve the original DACL.' }
            'In-memory apply/rollback preserved the original DACL bytes.'
            return
        }
        if (!$ReviewId -or $ReviewId -ne $derived -or !$Additions -or $Additions.Count -ne $keys.Count -or
            @(Compare-Object $Additions $keys -CaseSensitive).Count) { throw 'The exact reviewed diff and baseline are required.' }
        Edit-Draft $view.Sd.DiscretionaryAcl $selected $false
    }

    $after = Acl-Signature $view
    if (!$PSCmdlet.ShouldProcess("$ou on $dc","$Mode exactly $($selected.Count) reviewed ACEs for $roleName")) { return }
    $freshUsers = @(Read-Users)
    $freshInventory = (@($freshUsers | ForEach-Object { "$($_.Id):$($_.AclHash)" } | Sort-Object) -join '|')
    $fresh = Read-Ou
    if ($inventory -cne $freshInventory -or (Acl-Signature $fresh) -ne $before -or $fresh.Revision -ne $view.Revision -or
        (Hash-Bytes (Binary $fresh.Sd.DiscretionaryAcl)) -ne $raw) { throw 'Directory state changed during review. Read again.' }

    # Only the OU DACL is sent. A coordinated, single-writer ACL window is needed between the last read and this write.
    $change = [DirectoryServices.Protocols.DirectoryAttributeModification]::new()
    $change.Name = 'nTSecurityDescriptor'
    $change.Operation = 'Replace'
    [void]$change.Add((Binary $view.Sd))
    $request = [DirectoryServices.Protocols.ModifyRequest]::new($ou,$change)
    $control = [DirectoryServices.Protocols.SecurityDescriptorFlagControl]::new('Dacl')
    $control.IsCritical = $true
    [void]$request.Controls.Add($control)
    [void]$ldap.SendRequest($request)
    if ((Acl-Signature (Read-Ou)) -ne $after) { throw 'Controller readback differs. Inspect before any further action.' }
    "$Mode acknowledged. Exact ACE multiset and DACL flags verified on DC01."
} finally { $ldap.Dispose() }
