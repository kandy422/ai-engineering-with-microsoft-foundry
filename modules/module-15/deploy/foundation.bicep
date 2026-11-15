param location string = resourceGroup().location
@minLength(5)
@maxLength(14)
param prefix string
param storageAccount string = 'ppcourse20261115ac7hzq'
param expiresOn string = '2026-11-18T12:00:00Z'

var suffix = uniqueString(resourceGroup().id)
var compactPrefix = replace(prefix, '-', '')
var tags = {
  project: 'pixelprotocol-ai-course'
  environment: 'disposable-lab'
  recordingBatch: 'production-remediation-20261115'
  expiresOn: expiresOn
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${prefix}-runtime'
  location: location
  tags: tags
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: '${compactPrefix}${suffix}'
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

resource pull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, identity.id, 'AcrPull')
  scope: registry
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccount
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' existing = {
  name: 'default'
  parent: storage
}

resource approvalTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: tableService
  name: 'SupportActions'
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: json('0.1') }
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${prefix}-traces'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    RetentionInDays: 30
  }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-consumption'
  location: location
  tags: tags
  properties: {
    workloadProfiles: [
      { name: 'Consumption', workloadProfileType: 'Consumption' }
    ]
    appLogsConfiguration: { destination: 'azure-monitor' }
  }
}

resource diagnostic 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'capstone-logs'
  scope: environment
  properties: {
    workspaceId: workspace.id
    logs: [
      { category: 'ContainerAppConsoleLogs', enabled: true }
      { category: 'ContainerAppSystemLogs', enabled: true }
    ]
  }
}

output ids object = {
  environment: environment.id
  registry: registry.id
  registryLogin: registry.properties.loginServer
  identity: identity.id
  clientId: identity.properties.clientId
  principalId: identity.properties.principalId
  storageAccount: storage.name
  table: approvalTable.id
  tableName: approvalTable.name
  tableEndpoint: storage.properties.primaryEndpoints.table
  workspace: workspace.id
  insights: insights.id
  insightsName: insights.name
}
