[CmdletBinding()]
param(
    [string]$BaselinePath,
    [string]$CurrentPath,
    [switch]$SkipSelfTest,
    [switch]$SelfTestOnly
)

$ErrorActionPreference = "Stop"
$script:HttpMethods = @("get", "put", "post", "delete", "options", "head", "patch", "trace")
if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
    $BaselinePath = Join-Path $PSScriptRoot "../docs/commercialization/openapi/open-v1.json"
}

function Get-PropertyValue {
    param($Object, [string]$Name)

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Test-Property {
    param($Object, [string]$Name)

    return $null -ne $Object -and $null -ne $Object.PSObject.Properties[$Name]
}

function Get-PropertyNames {
    param($Object)

    if ($null -eq $Object) {
        return @()
    }

    return @($Object.PSObject.Properties | ForEach-Object { $_.Name })
}

function Set-PropertyValue {
    param($Object, [string]$Name, $Value)

    $property = $Object.PSObject.Properties[$Name]
    if ($null -ne $property) {
        $property.Value = $Value
        return
    }

    $Object | Add-Member -MemberType NoteProperty -Name $Name -Value $Value
}

function Remove-PropertyValue {
    param($Object, [string]$Name)

    $Object.PSObject.Properties.Remove($Name)
}

function Resolve-LocalReference {
    param($Root, $Node)

    $reference = Get-PropertyValue $Node '$ref'
    if ($reference -isnot [string] -or !$reference.StartsWith("#/", [StringComparison]::Ordinal)) {
        return $Node
    }

    $current = $Root
    foreach ($segment in $reference.Substring(2).Split('/')) {
        $name = $segment.Replace("~1", "/").Replace("~0", "~")
        $current = Get-PropertyValue $current $name
        if ($null -eq $current) {
            return $null
        }
    }

    return $current
}

function ConvertTo-StringSet {
    param($Value)

    if ($null -eq $Value) {
        return @()
    }

    return @($Value | ForEach-Object { [string]$_ } | Sort-Object -Unique)
}

function Test-ValueSetEqual {
    param($Baseline, $Current)

    $baselineValues = @(ConvertTo-StringSet $Baseline)
    $currentValues = @(ConvertTo-StringSet $Current)
    return $baselineValues.Count -eq $currentValues.Count -and
        @($baselineValues | Where-Object { $_ -notin $currentValues }).Count -eq 0
}

function Add-Change {
    param([Collections.Generic.List[string]]$Changes, [string]$Message)

    if (!$Changes.Contains($Message)) {
        [void]$Changes.Add($Message)
    }
}

function Compare-NumericConstraint {
    param(
        $Baseline,
        $Current,
        [string]$Name,
        [bool]$HigherIsNarrower,
        [string]$Location,
        [Collections.Generic.List[string]]$Changes
    )

    $baselineHasValue = Test-Property $Baseline $Name
    $currentHasValue = Test-Property $Current $Name
    if (!$currentHasValue) {
        return
    }

    $currentValue = [double](Get-PropertyValue $Current $Name)
    if (!$baselineHasValue) {
        Add-Change $Changes "Schema constraint added at ${Location}: $Name=$currentValue"
        return
    }

    $baselineValue = [double](Get-PropertyValue $Baseline $Name)
    $narrowed = if ($HigherIsNarrower) {
        $currentValue -gt $baselineValue
    } else {
        $currentValue -lt $baselineValue
    }
    if ($narrowed) {
        Add-Change $Changes "Schema constraint narrowed at ${Location}: $Name $baselineValue -> $currentValue"
    }
}

function Compare-Schema {
    param(
        $BaselineRoot,
        $CurrentRoot,
        $BaselineSchema,
        $CurrentSchema,
        [string]$Location,
        [Collections.Generic.List[string]]$Changes,
        [Collections.Generic.HashSet[string]]$Visited
    )

    if ($null -eq $BaselineSchema) {
        return
    }
    if ($null -eq $CurrentSchema) {
        Add-Change $Changes "Removed schema at $Location"
        return
    }

    $baselineReference = Get-PropertyValue $BaselineSchema '$ref'
    $currentReference = Get-PropertyValue $CurrentSchema '$ref'
    if ($baselineReference -is [string] -or $currentReference -is [string]) {
        $visitKey = "$baselineReference|$currentReference"
        if (!$Visited.Add($visitKey)) {
            return
        }
        if (($baselineReference -is [string]) -and ($currentReference -isnot [string])) {
            Add-Change $Changes "Schema reference changed at ${Location}: $baselineReference -> inline schema"
        } elseif (($baselineReference -isnot [string]) -and ($currentReference -is [string])) {
            Add-Change $Changes "Schema reference changed at ${Location}: inline schema -> $currentReference"
        }

        $BaselineSchema = Resolve-LocalReference $BaselineRoot $BaselineSchema
        $CurrentSchema = Resolve-LocalReference $CurrentRoot $CurrentSchema
        if ($null -eq $BaselineSchema -or $null -eq $CurrentSchema) {
            Add-Change $Changes "Unresolvable schema reference at $Location"
            return
        }
    }

    $baselineHasType = Test-Property $BaselineSchema "type"
    $currentHasType = Test-Property $CurrentSchema "type"
    $typesEqual = !$baselineHasType -or (Test-ValueSetEqual -Baseline (Get-PropertyValue $BaselineSchema "type") -Current (Get-PropertyValue $CurrentSchema "type"))
    if ($baselineHasType -ne $currentHasType -or
        ($baselineHasType -and !$typesEqual)) {
        Add-Change $Changes "Schema type changed at $Location"
    }

    $baselineFormat = Get-PropertyValue $BaselineSchema "format"
    $currentFormat = Get-PropertyValue $CurrentSchema "format"
    if ($baselineFormat -ne $currentFormat -and
        ($null -ne $baselineFormat -or $null -ne $currentFormat)) {
        Add-Change $Changes "Schema format changed at ${Location}: $baselineFormat -> $currentFormat"
    }

    if ((Get-PropertyValue $BaselineSchema "nullable") -eq $true -and
        (Get-PropertyValue $CurrentSchema "nullable") -ne $true) {
        Add-Change $Changes "Schema became non-nullable at $Location"
    }

    $baselineEnum = Get-PropertyValue $BaselineSchema "enum"
    $currentEnum = Get-PropertyValue $CurrentSchema "enum"
    if ($null -eq $baselineEnum -and $null -ne $currentEnum) {
        Add-Change $Changes "Schema enum added at $Location"
    } elseif ($null -ne $baselineEnum -and $null -ne $currentEnum) {
        $currentEnumValues = @(ConvertTo-StringSet $currentEnum)
        foreach ($value in @(ConvertTo-StringSet $baselineEnum)) {
            if ($value -notin $currentEnumValues) {
                Add-Change $Changes "Schema enum narrowed at ${Location}: removed '$value'"
            }
        }
    }

    $baselinePattern = Get-PropertyValue $BaselineSchema "pattern"
    $currentPattern = Get-PropertyValue $CurrentSchema "pattern"
    if ($baselinePattern -ne $currentPattern -and $null -ne $currentPattern) {
        Add-Change $Changes "Schema pattern added or changed at $Location"
    }

    Compare-NumericConstraint $BaselineSchema $CurrentSchema "minimum" $true $Location $Changes
    Compare-NumericConstraint $BaselineSchema $CurrentSchema "exclusiveMinimum" $true $Location $Changes
    Compare-NumericConstraint $BaselineSchema $CurrentSchema "minLength" $true $Location $Changes
    Compare-NumericConstraint $BaselineSchema $CurrentSchema "minItems" $true $Location $Changes
    Compare-NumericConstraint $BaselineSchema $CurrentSchema "maximum" $false $Location $Changes
    Compare-NumericConstraint $BaselineSchema $CurrentSchema "exclusiveMaximum" $false $Location $Changes
    Compare-NumericConstraint $BaselineSchema $CurrentSchema "maxLength" $false $Location $Changes
    Compare-NumericConstraint $BaselineSchema $CurrentSchema "maxItems" $false $Location $Changes

    $baselineAdditional = Get-PropertyValue $BaselineSchema "additionalProperties"
    $currentAdditional = Get-PropertyValue $CurrentSchema "additionalProperties"
    if ($currentAdditional -eq $false -and $baselineAdditional -ne $false) {
        Add-Change $Changes "Schema disallows additional properties at $Location"
    }

    $baselineRequired = @(ConvertTo-StringSet (Get-PropertyValue $BaselineSchema "required"))
    $currentRequired = @(ConvertTo-StringSet (Get-PropertyValue $CurrentSchema "required"))
    foreach ($propertyName in $currentRequired) {
        if ($propertyName -notin $baselineRequired) {
            Add-Change $Changes "New required property at ${Location}: $propertyName"
        }
    }

    $baselineProperties = Get-PropertyValue $BaselineSchema "properties"
    $currentProperties = Get-PropertyValue $CurrentSchema "properties"
    foreach ($propertyName in Get-PropertyNames $baselineProperties) {
        $baselineProperty = Get-PropertyValue $baselineProperties $propertyName
        $currentProperty = Get-PropertyValue $currentProperties $propertyName
        if ($null -eq $currentProperty) {
            Add-Change $Changes "Removed schema property at ${Location}: $propertyName"
            continue
        }
        Compare-Schema $BaselineRoot $CurrentRoot $baselineProperty $currentProperty "$Location.properties.$propertyName" $Changes $Visited
    }

    $baselineItems = Get-PropertyValue $BaselineSchema "items"
    if ($null -ne $baselineItems) {
        Compare-Schema $BaselineRoot $CurrentRoot $baselineItems (Get-PropertyValue $CurrentSchema "items") "$Location.items" $Changes $Visited
    }
}

function Get-EffectiveParameters {
    param($Root, $PathItem, $Operation)

    $parameters = @{}
    foreach ($source in @(
        (Get-PropertyValue $PathItem "parameters"),
        (Get-PropertyValue $Operation "parameters"))) {
        foreach ($parameter in @($source)) {
            if ($null -eq $parameter) {
                continue
            }
            $resolved = Resolve-LocalReference $Root $parameter
            $name = Get-PropertyValue $resolved "name"
            $location = Get-PropertyValue $resolved "in"
            if ($null -ne $name -and $null -ne $location) {
                $parameters["${location}:$name"] = $resolved
            }
        }
    }

    return $parameters
}

function Compare-MediaContent {
    param(
        $BaselineRoot,
        $CurrentRoot,
        $BaselineContainer,
        $CurrentContainer,
        [string]$Location,
        [Collections.Generic.List[string]]$Changes,
        [Collections.Generic.HashSet[string]]$Visited
    )

    $baselineContent = Get-PropertyValue $BaselineContainer "content"
    $currentContent = Get-PropertyValue $CurrentContainer "content"
    foreach ($mediaType in Get-PropertyNames $baselineContent) {
        $baselineMedia = Get-PropertyValue $baselineContent $mediaType
        $currentMedia = Get-PropertyValue $currentContent $mediaType
        if ($null -eq $currentMedia) {
            Add-Change $Changes "Removed media type at ${Location}: $mediaType"
            continue
        }
        Compare-Schema $BaselineRoot $CurrentRoot (Get-PropertyValue $baselineMedia "schema") (Get-PropertyValue $currentMedia "schema") "$Location.content.$mediaType.schema" $Changes $Visited
    }
}

function Compare-ExistingSecurityProperty {
    param(
        $Baseline,
        $Current,
        [string]$PropertyName,
        [string]$Location,
        [string]$ChangeKind,
        [Collections.Generic.List[string]]$Changes
    )

    if (!(Test-Property $Baseline $PropertyName)) {
        return
    }

    $baselineValue = Get-PropertyValue $Baseline $PropertyName
    $currentValue = Get-PropertyValue $Current $PropertyName
    if (!(Test-Property $Current $PropertyName) -or
        ![object]::Equals($baselineValue, $currentValue)) {
        Add-Change $Changes "${ChangeKind} at ${Location}: $PropertyName '$baselineValue' -> '$currentValue'"
    }
}

function Compare-OAuthFlows {
    param(
        $BaselineScheme,
        $CurrentScheme,
        [string]$Location,
        [Collections.Generic.List[string]]$Changes
    )

    $baselineFlows = Get-PropertyValue $BaselineScheme "flows"
    $currentFlows = Get-PropertyValue $CurrentScheme "flows"
    $currentFlowNames = @(Get-PropertyNames $currentFlows)
    foreach ($flowName in Get-PropertyNames $baselineFlows) {
        if ($currentFlowNames -cnotcontains $flowName) {
            Add-Change $Changes "Removed OAuth flow at ${Location}.flows: $flowName"
            continue
        }

        $baselineFlow = Get-PropertyValue $baselineFlows $flowName
        $currentFlow = Get-PropertyValue $currentFlows $flowName
        $flowLocation = "${Location}.flows.$flowName"
        foreach ($propertyName in @("authorizationUrl", "tokenUrl", "refreshUrl")) {
            Compare-ExistingSecurityProperty `
                $baselineFlow $currentFlow $propertyName $flowLocation `
                "OAuth flow property changed" $Changes
        }

        $baselineScopes = Get-PropertyValue $baselineFlow "scopes"
        $currentScopes = Get-PropertyValue $currentFlow "scopes"
        $currentScopeNames = @(Get-PropertyNames $currentScopes)
        foreach ($scopeName in Get-PropertyNames $baselineScopes) {
            if ($currentScopeNames -cnotcontains $scopeName) {
                Add-Change $Changes "Removed OAuth scope at ${flowLocation}.scopes: $scopeName"
            }
        }
    }
}

function Compare-SecuritySchemes {
    param(
        $BaselineRoot,
        $CurrentRoot,
        [Collections.Generic.List[string]]$Changes
    )

    $baselineSchemes = Get-PropertyValue (Get-PropertyValue $BaselineRoot "components") "securitySchemes"
    $currentSchemes = Get-PropertyValue (Get-PropertyValue $CurrentRoot "components") "securitySchemes"
    $currentSchemeNames = @(Get-PropertyNames $currentSchemes)
    foreach ($schemeName in Get-PropertyNames $baselineSchemes) {
        $baselineScheme = Get-PropertyValue $baselineSchemes $schemeName
        if ($currentSchemeNames -cnotcontains $schemeName) {
            Add-Change $Changes "Removed security scheme: $schemeName"
            continue
        }
        $currentScheme = Get-PropertyValue $currentSchemes $schemeName

        $baselineType = Get-PropertyValue $baselineScheme "type"
        $currentType = Get-PropertyValue $currentScheme "type"
        if (![object]::Equals($baselineType, $currentType)) {
            Add-Change $Changes "Security scheme type changed at components.securitySchemes.${schemeName}: $baselineType -> $currentType"
        }

        $schemeLocation = "components.securitySchemes.$schemeName"
        foreach ($propertyName in @("name", "in", "scheme", "bearerFormat", "openIdConnectUrl")) {
            Compare-ExistingSecurityProperty `
                $baselineScheme $currentScheme $propertyName $schemeLocation `
                "Security scheme property changed" $Changes
        }

        if ($baselineType -ceq "oauth2" -and $currentType -ceq "oauth2") {
            Compare-OAuthFlows $baselineScheme $currentScheme $schemeLocation $Changes
        }
    }
}

function Get-EffectiveSecurityRequirements {
    param($Root, $PathItem, $Operation)

    if (Test-Property $Operation "security") {
        $security = Get-PropertyValue $Operation "security"
    } elseif (Test-Property $PathItem "security") {
        $security = Get-PropertyValue $PathItem "security"
    } elseif (Test-Property $Root "security") {
        $security = Get-PropertyValue $Root "security"
    } else {
        return ,([pscustomobject]@{})
    }

    $requirements = @($security)
    if ($requirements.Count -eq 0) {
        return ,([pscustomobject]@{})
    }

    return $requirements
}

function Test-SecurityAlternativeCovered {
    param($BaselineRequirement, $CurrentRequirement)

    $baselineSchemeNames = @(Get-PropertyNames $BaselineRequirement)
    foreach ($schemeName in Get-PropertyNames $CurrentRequirement) {
        if ($baselineSchemeNames -cnotcontains $schemeName) {
            return $false
        }

        $baselineScopes = @((Get-PropertyValue $BaselineRequirement $schemeName) | ForEach-Object { [string]$_ })
        $currentScopes = @((Get-PropertyValue $CurrentRequirement $schemeName) | ForEach-Object { [string]$_ })
        foreach ($scope in $currentScopes) {
            if ($baselineScopes -cnotcontains $scope) {
                return $false
            }
        }
    }

    return $true
}

function Compare-SecurityRequirements {
    param(
        $BaselineRoot,
        $CurrentRoot,
        $BaselinePathItem,
        $CurrentPathItem,
        $BaselineOperation,
        $CurrentOperation,
        [string]$Location,
        [Collections.Generic.List[string]]$Changes
    )

    $baselineRequirements = @(Get-EffectiveSecurityRequirements $BaselineRoot $BaselinePathItem $BaselineOperation)
    $currentRequirements = @(Get-EffectiveSecurityRequirements $CurrentRoot $CurrentPathItem $CurrentOperation)
    foreach ($baselineRequirement in $baselineRequirements) {
        $covered = $false
        foreach ($currentRequirement in $currentRequirements) {
            if (Test-SecurityAlternativeCovered $baselineRequirement $currentRequirement) {
                $covered = $true
                break
            }
        }

        if (!$covered) {
            if (@(Get-PropertyNames $baselineRequirement).Count -eq 0) {
                Add-Change $Changes "Security requirements tightened at ${Location}: anonymous access is no longer allowed"
            } else {
                $schemeNames = @(Get-PropertyNames $baselineRequirement | Sort-Object)
                Add-Change $Changes "Security alternative removed or tightened at ${Location}: $($schemeNames -join ' + ')"
            }
        }
    }
}

function Compare-Operation {
    param(
        $BaselineRoot,
        $CurrentRoot,
        $BaselinePathItem,
        $CurrentPathItem,
        $BaselineOperation,
        $CurrentOperation,
        [string]$Location,
        [Collections.Generic.List[string]]$Changes
    )

    $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    Compare-SecurityRequirements `
        $BaselineRoot $CurrentRoot `
        $BaselinePathItem $CurrentPathItem `
        $BaselineOperation $CurrentOperation `
        $Location $Changes
    $baselineParameters = Get-EffectiveParameters $BaselineRoot $BaselinePathItem $BaselineOperation
    $currentParameters = Get-EffectiveParameters $CurrentRoot $CurrentPathItem $CurrentOperation

    foreach ($key in $baselineParameters.Keys) {
        if (!$currentParameters.ContainsKey($key)) {
            Add-Change $Changes "Removed parameter at ${Location}: $key"
            continue
        }
        $baselineParameter = $baselineParameters[$key]
        $currentParameter = $currentParameters[$key]
        if ((Get-PropertyValue $baselineParameter "required") -ne $true -and
            (Get-PropertyValue $currentParameter "required") -eq $true) {
            Add-Change $Changes "New required parameter at ${Location}: $key"
        }
        Compare-Schema $BaselineRoot $CurrentRoot (Get-PropertyValue $baselineParameter "schema") (Get-PropertyValue $currentParameter "schema") "$Location.parameters.$key" $Changes $visited
    }

    foreach ($key in $currentParameters.Keys) {
        if (!$baselineParameters.ContainsKey($key) -and
            (Get-PropertyValue $currentParameters[$key] "required") -eq $true) {
            Add-Change $Changes "New required parameter at ${Location}: $key"
        }
    }

    $baselineRequest = Get-PropertyValue $BaselineOperation "requestBody"
    $currentRequest = Get-PropertyValue $CurrentOperation "requestBody"
    if ($null -ne $baselineRequest -and $null -eq $currentRequest) {
        Add-Change $Changes "Removed request body at $Location"
    } elseif ($null -ne $baselineRequest -and $null -ne $currentRequest) {
        if ((Get-PropertyValue $baselineRequest "required") -ne $true -and
            (Get-PropertyValue $currentRequest "required") -eq $true) {
            Add-Change $Changes "Request body became required at $Location"
        }
        Compare-MediaContent $BaselineRoot $CurrentRoot $baselineRequest $currentRequest "$Location.requestBody" $Changes $visited
    } elseif ($null -eq $baselineRequest -and $null -ne $currentRequest -and
        (Get-PropertyValue $currentRequest "required") -eq $true) {
        Add-Change $Changes "New required request body at $Location"
    }

    $baselineResponses = Get-PropertyValue $BaselineOperation "responses"
    $currentResponses = Get-PropertyValue $CurrentOperation "responses"
    foreach ($status in Get-PropertyNames $baselineResponses) {
        $baselineResponse = Get-PropertyValue $baselineResponses $status
        $currentResponse = Get-PropertyValue $currentResponses $status
        if ($null -eq $currentResponse) {
            Add-Change $Changes "Removed response at ${Location}: $status"
            continue
        }
        Compare-MediaContent $BaselineRoot $CurrentRoot $baselineResponse $currentResponse "$Location.responses.$status" $Changes $visited
    }
}

function Compare-OpenApiContract {
    param($Baseline, $Current)

    $changes = [Collections.Generic.List[string]]::new()
    Compare-SecuritySchemes $Baseline $Current $changes
    $baselinePaths = Get-PropertyValue $Baseline "paths"
    $currentPaths = Get-PropertyValue $Current "paths"
    foreach ($path in Get-PropertyNames $baselinePaths) {
        $baselinePathItem = Get-PropertyValue $baselinePaths $path
        $currentPathItem = Get-PropertyValue $currentPaths $path
        if ($null -eq $currentPathItem) {
            Add-Change $changes "Removed path: $path"
            continue
        }

        foreach ($method in $script:HttpMethods) {
            $baselineOperation = Get-PropertyValue $baselinePathItem $method
            if ($null -eq $baselineOperation) {
                continue
            }
            $currentOperation = Get-PropertyValue $currentPathItem $method
            if ($null -eq $currentOperation) {
                Add-Change $changes "Removed method: $($method.ToUpperInvariant()) $path"
                continue
            }
            Compare-Operation $Baseline $Current $baselinePathItem $currentPathItem $baselineOperation $currentOperation "$($method.ToUpperInvariant()) $path" $changes
        }
    }

    $baselineSchemas = Get-PropertyValue (Get-PropertyValue $Baseline "components") "schemas"
    $currentSchemas = Get-PropertyValue (Get-PropertyValue $Current "components") "schemas"
    foreach ($schemaName in Get-PropertyNames $baselineSchemas) {
        $baselineSchema = Get-PropertyValue $baselineSchemas $schemaName
        $currentSchema = Get-PropertyValue $currentSchemas $schemaName
        if ($null -eq $currentSchema) {
            Add-Change $changes "Removed component schema: $schemaName"
            continue
        }
        $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        Compare-Schema $Baseline $Current $baselineSchema $currentSchema "components.schemas.$schemaName" $changes $visited
    }

    return $changes.ToArray()
}

function ConvertFrom-ContractJson {
    param([string]$Json, [string]$Description)

    try {
        return $Json | ConvertFrom-Json
    } catch {
        throw "Invalid OpenAPI JSON in $Description`: $($_.Exception.Message)"
    }
}

function Copy-ContractDocument {
    param($Document)

    return ConvertFrom-ContractJson ($Document | ConvertTo-Json -Depth 100 -Compress) "self-test fixture"
}

function Invoke-ComparatorSelfTest {
    $fixtureJson = @'
{
  "openapi": "3.0.0",
  "paths": {
    "/api/open/v1/items/{id}": {
      "security": [
        { "BearerAuth": [] },
        { "OAuth": ["items:read"] }
      ],
      "get": {
        "parameters": [
          { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }
        ],
        "responses": {
          "200": {
            "content": {
              "application/json": { "schema": { "$ref": "#/components/schemas/Item" } }
            }
          }
        }
      },
      "post": {
        "security": [
          { "BearerAuth": [] },
          { "OAuth": ["items:read"] }
        ],
        "responses": {
          "204": {}
        }
      }
    },
    "/api/open/v1/status": {
      "get": {
        "responses": {
          "200": {}
        }
      }
    }
  },
  "components": {
    "securitySchemes": {
      "BearerAuth": {
        "type": "http",
        "scheme": "bearer",
        "bearerFormat": "JWT"
      },
      "OAuth": {
        "type": "oauth2",
        "flows": {
          "authorizationCode": {
            "authorizationUrl": "https://identity.example.test/oauth/authorize",
            "tokenUrl": "https://identity.example.test/oauth/token",
            "refreshUrl": "https://identity.example.test/oauth/refresh",
            "scopes": {
              "items:read": "Read items",
              "items:write": "Write items"
            }
          },
          "clientCredentials": {
            "tokenUrl": "https://identity.example.test/oauth/token",
            "scopes": {
              "items:read": "Read items"
            }
          }
        }
      },
      "OpenId": {
        "type": "openIdConnect",
        "openIdConnectUrl": "https://identity.example.test/.well-known/openid-configuration"
      },
      "ApiKey": {
        "type": "apiKey",
        "in": "header",
        "name": "X-API-Key"
      },
      "MutualTls": {
        "type": "mutualTLS"
      }
    },
    "schemas": {
      "Item": {
        "type": "object",
        "required": ["id"],
        "properties": {
          "id": { "type": "string" },
          "name": { "type": "string", "nullable": true }
        }
      }
    }
  }
}
'@
    $fixture = ConvertFrom-ContractJson $fixtureJson "self-test fixture"
    $cases = @(
        @{
            Name = "removed path"; Expected = "Removed path"
            Mutate = {
                param($document)
                Remove-PropertyValue (Get-PropertyValue $document "paths") "/api/open/v1/items/{id}"
            }
        },
        @{
            Name = "removed method"; Expected = "Removed method"
            Mutate = {
                param($document)
                $pathItem = Get-PropertyValue (Get-PropertyValue $document "paths") "/api/open/v1/items/{id}"
                Remove-PropertyValue $pathItem "get"
            }
        },
        @{
            Name = "removed response"; Expected = "Removed response"
            Mutate = {
                param($document)
                $pathItem = Get-PropertyValue (Get-PropertyValue $document "paths") "/api/open/v1/items/{id}"
                $responses = Get-PropertyValue (Get-PropertyValue $pathItem "get") "responses"
                Remove-PropertyValue $responses "200"
            }
        },
        @{
            Name = "new required parameter"; Expected = "New required parameter"
            Mutate = {
                param($document)
                $pathItem = Get-PropertyValue (Get-PropertyValue $document "paths") "/api/open/v1/items/{id}"
                $operation = Get-PropertyValue $pathItem "get"
                $parameters = @((Get-PropertyValue $operation "parameters")) + @(
                    [pscustomobject]@{
                        name = "filter"; in = "query"; required = $true
                        schema = [pscustomobject]@{ type = "string" }
                    })
                Set-PropertyValue $operation "parameters" $parameters
            }
        },
        @{
            Name = "new required property"; Expected = "New required property"
            Mutate = {
                param($document)
                $schemas = Get-PropertyValue (Get-PropertyValue $document "components") "schemas"
                $schema = Get-PropertyValue $schemas "Item"
                Set-PropertyValue $schema "required" @("id", "name")
            }
        },
        @{
            Name = "schema type change"; Expected = "Schema type changed"
            Mutate = {
                param($document)
                $schemas = Get-PropertyValue (Get-PropertyValue $document "components") "schemas"
                $properties = Get-PropertyValue (Get-PropertyValue $schemas "Item") "properties"
                Set-PropertyValue (Get-PropertyValue $properties "name") "type" "integer"
            }
        },
        @{
            Name = "schema narrowing"; Expected = "Schema became non-nullable"
            Mutate = {
                param($document)
                $schemas = Get-PropertyValue (Get-PropertyValue $document "components") "schemas"
                $properties = Get-PropertyValue (Get-PropertyValue $schemas "Item") "properties"
                Set-PropertyValue (Get-PropertyValue $properties "name") "nullable" $false
            }
        },
        @{
            Name = "removed security scheme"; Expected = "Removed security scheme"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                Remove-PropertyValue $schemes "BearerAuth"
            }
        },
        @{
            Name = "security scheme name case change"; Expected = "Removed security scheme"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                $scheme = Get-PropertyValue $schemes "BearerAuth"
                Remove-PropertyValue $schemes "BearerAuth"
                Set-PropertyValue $schemes "bearerauth" $scheme
            }
        },
        @{
            Name = "security scheme type change"; Expected = "Security scheme type changed"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                Set-PropertyValue (Get-PropertyValue $schemes "BearerAuth") "type" "apiKey"
            }
        },
        @{
            Name = "HTTP security scheme change"; Expected = "Security scheme property changed"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                Set-PropertyValue (Get-PropertyValue $schemes "BearerAuth") "scheme" "basic"
            }
        },
        @{
            Name = "bearer format change"; Expected = "Security scheme property changed"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                Set-PropertyValue (Get-PropertyValue $schemes "BearerAuth") "bearerFormat" "opaque"
            }
        },
        @{
            Name = "OpenID Connect URL change"; Expected = "Security scheme property changed"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                Set-PropertyValue (Get-PropertyValue $schemes "OpenId") "openIdConnectUrl" "https://login.example.test/.well-known/openid-configuration"
            }
        },
        @{
            Name = "API key name change"; Expected = "Security scheme property changed"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                Set-PropertyValue (Get-PropertyValue $schemes "ApiKey") "name" "X-New-API-Key"
            }
        },
        @{
            Name = "API key location change"; Expected = "Security scheme property changed"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                Set-PropertyValue (Get-PropertyValue $schemes "ApiKey") "in" "query"
            }
        },
        @{
            Name = "mutual TLS type change"; Expected = "Security scheme type changed"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                Set-PropertyValue (Get-PropertyValue $schemes "MutualTls") "type" "http"
            }
        },
        @{
            Name = "OAuth flow removed"; Expected = "Removed OAuth flow"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                $flows = Get-PropertyValue (Get-PropertyValue $schemes "OAuth") "flows"
                Remove-PropertyValue $flows "clientCredentials"
            }
        },
        @{
            Name = "OAuth authorization URL changed"; Expected = "OAuth flow property changed"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                $flows = Get-PropertyValue (Get-PropertyValue $schemes "OAuth") "flows"
                $flow = Get-PropertyValue $flows "authorizationCode"
                Set-PropertyValue $flow "authorizationUrl" "https://login.example.test/oauth/authorize"
            }
        },
        @{
            Name = "OAuth token URL removed"; Expected = "OAuth flow property changed"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                $flows = Get-PropertyValue (Get-PropertyValue $schemes "OAuth") "flows"
                $flow = Get-PropertyValue $flows "authorizationCode"
                Remove-PropertyValue $flow "tokenUrl"
            }
        },
        @{
            Name = "OAuth refresh URL changed"; Expected = "OAuth flow property changed"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                $flows = Get-PropertyValue (Get-PropertyValue $schemes "OAuth") "flows"
                $flow = Get-PropertyValue $flows "authorizationCode"
                Set-PropertyValue $flow "refreshUrl" "https://login.example.test/oauth/refresh"
            }
        },
        @{
            Name = "OAuth scope removed"; Expected = "Removed OAuth scope"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                $flows = Get-PropertyValue (Get-PropertyValue $schemes "OAuth") "flows"
                $flow = Get-PropertyValue $flows "authorizationCode"
                Remove-PropertyValue (Get-PropertyValue $flow "scopes") "items:write"
            }
        },
        @{
            Name = "anonymous operation becomes secured"; Expected = "Security requirements tightened"
            Mutate = {
                param($document)
                $pathItem = Get-PropertyValue (Get-PropertyValue $document "paths") "/api/open/v1/status"
                Set-PropertyValue (Get-PropertyValue $pathItem "get") "security" @(
                    [pscustomobject]@{ BearerAuth = @() })
            }
        },
        @{
            Name = "path security alternative removed"; Expected = "Security alternative removed or tightened"
            Mutate = {
                param($document)
                $pathItem = Get-PropertyValue (Get-PropertyValue $document "paths") "/api/open/v1/items/{id}"
                Set-PropertyValue $pathItem "security" @(
                    [pscustomobject]@{ BearerAuth = @() })
            }
        },
        @{
            Name = "operation security alternative removed"; Expected = "Security alternative removed or tightened"
            Mutate = {
                param($document)
                $pathItem = Get-PropertyValue (Get-PropertyValue $document "paths") "/api/open/v1/items/{id}"
                $operation = Get-PropertyValue $pathItem "post"
                Set-PropertyValue $operation "security" @(
                    [pscustomobject]@{ OAuth = @("items:read") })
            }
        },
        @{
            Name = "security scopes tightened"; Expected = "Security alternative removed or tightened"
            Mutate = {
                param($document)
                $pathItem = Get-PropertyValue (Get-PropertyValue $document "paths") "/api/open/v1/items/{id}"
                $operation = Get-PropertyValue $pathItem "post"
                Set-PropertyValue $operation "security" @(
                    [pscustomobject]@{ BearerAuth = @() },
                    [pscustomobject]@{ OAuth = @("items:read", "items:write") })
            }
        },
        @{
            Name = "security scope case change"; Expected = "Security alternative removed or tightened"
            Mutate = {
                param($document)
                $pathItem = Get-PropertyValue (Get-PropertyValue $document "paths") "/api/open/v1/items/{id}"
                $operation = Get-PropertyValue $pathItem "post"
                Set-PropertyValue $operation "security" @(
                    [pscustomobject]@{ BearerAuth = @() },
                    [pscustomobject]@{ OAuth = @("ITEMS:READ") })
            }
        }
    )

    foreach ($case in $cases) {
        $current = Copy-ContractDocument $fixture
        & $case.Mutate $current
        $changes = @(Compare-OpenApiContract $fixture $current)
        if (!($changes | Where-Object { $_ -like "*$($case.Expected)*" })) {
            throw "Comparator self-test '$($case.Name)' failed. Changes: $($changes -join '; ')"
        }
    }

    $additiveCases = @(
        @{
            Name = "optional parameter and schema property"
            Mutate = {
                param($document)
                $pathItem = Get-PropertyValue (Get-PropertyValue $document "paths") "/api/open/v1/items/{id}"
                $operation = Get-PropertyValue $pathItem "get"
                $optionalParameters = @((Get-PropertyValue $operation "parameters")) + @(
                    [pscustomobject]@{
                        name = "filter"; in = "query"; required = $false
                        schema = [pscustomobject]@{ type = "string" }
                    })
                Set-PropertyValue $operation "parameters" $optionalParameters
                $schemas = Get-PropertyValue (Get-PropertyValue $document "components") "schemas"
                $properties = Get-PropertyValue (Get-PropertyValue $schemas "Item") "properties"
                Set-PropertyValue $properties "description" ([pscustomobject]@{ type = "string"; nullable = $true })
            }
        },
        @{
            Name = "new security scheme"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                Set-PropertyValue $schemes "CookieKey" ([pscustomobject]@{
                    type = "apiKey"; in = "cookie"; name = "session"
                })
            }
        },
        @{
            Name = "new OAuth flow"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                $flows = Get-PropertyValue (Get-PropertyValue $schemes "OAuth") "flows"
                Set-PropertyValue $flows "implicit" ([pscustomobject]@{
                    authorizationUrl = "https://identity.example.test/oauth/authorize"
                    scopes = [pscustomobject]@{ "items:read" = "Read items" }
                })
            }
        },
        @{
            Name = "new OAuth scope"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                $flows = Get-PropertyValue (Get-PropertyValue $schemes "OAuth") "flows"
                $scopes = Get-PropertyValue (Get-PropertyValue $flows "authorizationCode") "scopes"
                Set-PropertyValue $scopes "items:admin" "Administer items"
            }
        },
        @{
            Name = "new OAuth optional URL"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                $flows = Get-PropertyValue (Get-PropertyValue $schemes "OAuth") "flows"
                $flow = Get-PropertyValue $flows "clientCredentials"
                Set-PropertyValue $flow "refreshUrl" "https://identity.example.test/oauth/refresh"
            }
        },
        @{
            Name = "security scheme metadata fields"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                $scheme = Get-PropertyValue $schemes "MutualTls"
                Set-PropertyValue $scheme "description" "Client certificate authentication"
                Set-PropertyValue $scheme "x-documentation-url" "https://docs.example.test/auth/mtls"
            }
        },
        @{
            Name = "OAuth scope description change"
            Mutate = {
                param($document)
                $schemes = Get-PropertyValue (Get-PropertyValue $document "components") "securitySchemes"
                $flows = Get-PropertyValue (Get-PropertyValue $schemes "OAuth") "flows"
                $scopes = Get-PropertyValue (Get-PropertyValue $flows "authorizationCode") "scopes"
                Set-PropertyValue $scopes "items:read" "Read item records"
            }
        },
        @{
            Name = "new security alternative"
            Mutate = {
                param($document)
                $pathItem = Get-PropertyValue (Get-PropertyValue $document "paths") "/api/open/v1/items/{id}"
                $operation = Get-PropertyValue $pathItem "post"
                Set-PropertyValue $operation "security" @(
                    [pscustomobject]@{ BearerAuth = @() },
                    [pscustomobject]@{ OAuth = @("items:read") },
                    [pscustomobject]@{ OpenId = @() })
            }
        },
        @{
            Name = "anonymous security alternative"
            Mutate = {
                param($document)
                $pathItem = Get-PropertyValue (Get-PropertyValue $document "paths") "/api/open/v1/items/{id}"
                $operation = Get-PropertyValue $pathItem "post"
                Set-PropertyValue $operation "security" @(
                    [pscustomobject]@{})
            }
        },
        @{
            Name = "operation disables inherited security"
            Mutate = {
                param($document)
                $pathItem = Get-PropertyValue (Get-PropertyValue $document "paths") "/api/open/v1/items/{id}"
                Set-PropertyValue (Get-PropertyValue $pathItem "get") "security" @()
            }
        },
        @{
            Name = "security scopes relaxed"
            Mutate = {
                param($document)
                $pathItem = Get-PropertyValue (Get-PropertyValue $document "paths") "/api/open/v1/items/{id}"
                $operation = Get-PropertyValue $pathItem "post"
                Set-PropertyValue $operation "security" @(
                    [pscustomobject]@{ BearerAuth = @() },
                    [pscustomobject]@{ OAuth = @() })
            }
        }
    )

    foreach ($case in $additiveCases) {
        $current = Copy-ContractDocument $fixture
        & $case.Mutate $current
        $changes = @(Compare-OpenApiContract $fixture $current)
        if ($changes.Count -ne 0) {
            throw "Comparator rejected additive self-test '$($case.Name)': $($changes -join '; ')"
        }
    }

    Write-Host "OpenAPI comparator self-tests passed ($($cases.Count) breaking cases, $($additiveCases.Count) additive cases)."
}

function Resolve-FullPath {
    param([string]$Path, [string]$BasePath)

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function New-CurrentContract {
    param([string]$RepositoryRoot)

    $temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ("gzctf-openapi-" + [guid]::NewGuid().ToString("N"))
    [IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null
    $outputPath = Join-Path $temporaryDirectory "open-v1.current.json"
    $previousOutput = $env:OPENAPI_CURRENT_PATH
    $previousRyuk = $env:TESTCONTAINERS_RYUK_DISABLED
    try {
        $env:OPENAPI_CURRENT_PATH = $outputPath
        if ([string]::IsNullOrWhiteSpace($env:TESTCONTAINERS_RYUK_DISABLED)) {
            $env:TESTCONTAINERS_RYUK_DISABLED = "true"
        }
        Push-Location $RepositoryRoot
        try {
            $testArguments = @(
                "test",
                "src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj",
                "--filter", "FullyQualifiedName~OpenV1_MatchesCommittedContract",
                "--no-restore",
                "--logger", "console;verbosity=minimal")
            & dotnet @testArguments | Out-Host
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to generate the current OpenAPI contract: dotnet test exited with code $LASTEXITCODE."
            }
        } finally {
            Pop-Location
        }
    } finally {
        $env:OPENAPI_CURRENT_PATH = $previousOutput
        $env:TESTCONTAINERS_RYUK_DISABLED = $previousRyuk
    }

    if (!(Test-Path -LiteralPath $outputPath -PathType Leaf)) {
        throw "Failed to generate the current OpenAPI contract through the integration test host."
    }

    return $outputPath
}

if (!$SkipSelfTest) {
    Invoke-ComparatorSelfTest
}
if ($SelfTestOnly) {
    exit 0
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$baselineFullPath = Resolve-FullPath $BaselinePath $repositoryRoot
if (!(Test-Path -LiteralPath $baselineFullPath -PathType Leaf)) {
    throw "OpenAPI baseline not found: $baselineFullPath"
}

$generatedCurrent = $false
if ([string]::IsNullOrWhiteSpace($CurrentPath)) {
    $currentFullPath = New-CurrentContract $repositoryRoot
    $generatedCurrent = $true
} else {
    $currentFullPath = Resolve-FullPath $CurrentPath $repositoryRoot
}
if (!(Test-Path -LiteralPath $currentFullPath -PathType Leaf)) {
    throw "Current OpenAPI contract not found: $currentFullPath"
}

try {
    $baseline = ConvertFrom-ContractJson ([IO.File]::ReadAllText($baselineFullPath)) $baselineFullPath
    $current = ConvertFrom-ContractJson ([IO.File]::ReadAllText($currentFullPath)) $currentFullPath
    $breakingChanges = @(Compare-OpenApiContract $baseline $current)
    if ($breakingChanges.Count -gt 0) {
        Write-Error ("Breaking OpenAPI changes detected:`n - " + ($breakingChanges -join "`n - "))
        exit 1
    }

    Write-Host "OpenAPI contract is backward compatible."
    Write-Host "Baseline: $baselineFullPath"
    Write-Host "Current:  $currentFullPath"
    exit 0
} finally {
    if ($generatedCurrent) {
        Remove-Item -LiteralPath (Split-Path -Parent $currentFullPath) -Recurse -Force -ErrorAction SilentlyContinue
    }
}
