param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z][a-z0-9-]{2,24}$')][string]$Prefix,
    [guid]$AllowedUserObjectId,
    [guid]$DeniedUserObjectId,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$StableOnly
)
$ErrorActionPreference='Stop'
if (!$StableOnly -and ($AllowedUserObjectId -eq $DeniedUserObjectId -or [guid]::Empty -in @($AllowedUserObjectId,$DeniedUserObjectId))) {
    throw 'Supply two distinct nonempty Entra user object IDs.'
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$records=@(
    @{id='returns-opened';title='Opened accessory return policy';content='Opened accessories are eligible for a return only if defective.';marker=''},
    @{id='returns-unopened';title='Unopened accessory return policy';content='Unopened accessories can be returned within 30 days.';marker='course10-public-control'},
    @{id='shipping-standard';title='Standard shipping policy';content='Standard shipping takes five business days.';marker=''}
)
$restricted=@{id='returns-unopened-internal';title='Unopened accessory return policy handling';content='For an unopened accessory return under the 30 day window, staff route a returned parcel to fictional inspection station Indigo.';marker='course10-restricted-control'}
$fields=@(
    @{name='id';type='Edm.String';key=$true;searchable=$false;retrievable=$true},
    @{name='title';type='Edm.String';searchable=$true;retrievable=$true},
    @{name='content';type='Edm.String';searchable=$true;retrievable=$true},
    @{name='marker';type='Edm.String';searchable=$false;retrievable=$true}
)
function Save($Name,$Value) {
    ConvertTo-Json -InputObject $Value -Depth 20 | Set-Content -Encoding utf8NoBOM -LiteralPath (Join-Path $OutputDirectory $Name)
}
foreach ($mode in @('stable','acl')) {
    $preview=$mode -eq 'acl'
    if ($preview -and $StableOnly) { continue }
    $api=if($preview){'2026-08-01-preview'}else{'2026-04-01'}
    $index="$Prefix-$mode-index"
    $source="$Prefix-$mode-source"
    $base="$Prefix-$mode-base"
    $schema=@{name=$index;fields=$fields;semantic=@{defaultConfiguration='policy';configurations=@(@{
        name='policy';prioritizedFields=@{titleField=@{fieldName='title'};prioritizedContentFields=@(@{fieldName='content'})}
    })}}
    if($preview){
        $schema.permissionFilterOption='enabled'
        $schema.fields+=@{name='allowedUsers';type='Collection(Edm.String)';searchable=$false;filterable=$true;retrievable=$false;permissionFilter='userIds'}
    }
    $docs=@(foreach($doc in $records){
        $write=$doc.Clone()
        $write['@search.action']='upload'
        if($preview){$write.allowedUsers=@('all')}
        $write
    })
    if($preview){$docs+=($restricted+@{'@search.action'='upload';allowedUsers=@($AllowedUserObjectId.ToString())})}
    $kb=@{name=$base;description='Fictional course policy passages';knowledgeSources=@(@{name=$source})}
    if($preview){$kb.outputMode='extractiveData';$kb.retrievalReasoningEffort=@{kind='minimal'}}
    Save "$mode-index.json" $schema
    Save "$mode-documents.json" @{value=$docs}
    Save "$mode-source.json" @{name=$source;kind='searchIndex';description='Fictional returns and shipping policies';searchIndexParameters=@{searchIndexName=$index;sourceDataFields=@('id','title','content','marker'|ForEach-Object {@{name=$_}})}}
    Save "$mode-base.json" $kb
    Save "$mode-routes.json" @(
        @{method='PUT';route="/indexes/${index}?api-version=$api";body="$mode-index.json"},
        @{method='POST';route="/indexes/$index/docs/index?api-version=$api";body="$mode-documents.json"},
        @{method='PUT';route="/knowledgesources('${source}')?api-version=$api";body="$mode-source.json"},
        @{method='PUT';route="/knowledgebases('${base}')?api-version=$api";body="$mode-base.json"}
    )
}


