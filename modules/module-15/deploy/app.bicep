param location string = resourceGroup().location
param name string
param environmentId string
param identityId string
param registryLogin string
param imageDigest string
param suffix string
param previousRevision string = ''
param tenantId string
param appClientId string
param storageAccount string
param applicationInsightsName string = ''
param authority string = environment().authentication.loginEndpoint
param expiresOn string = '2026-11-18T12:00:00Z'
@minLength(1)
param allowedPrincipalIds array
@minLength(1)
param allowedClientIds array
@secure()
param settings object

resource insights 'Microsoft.Insights/components@2020-02-02' existing = if (!empty(applicationInsightsName)) {
  name: applicationInsightsName
}

var appSettings = [for setting in items(union(settings, {
  ASPNETCORE_HTTP_PORTS: '8080'
  ASPNETCORE_ENVIRONMENT: 'Production'
})): {
  name: setting.key
  value: string(setting.value)
}]

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  tags: {
    project: 'pixelprotocol-ai-course'
    environment: 'disposable-lab'
    recordingBatch: 'production-remediation-20261115'
    expiresOn: expiresOn
  }
  identity: {
    type: 'SystemAssigned, UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    managedEnvironmentId: environmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Multiple'
      ingress: {
        external: true
        allowInsecure: false
        targetPort: 8080
        transport: 'auto'
        traffic: [
          { revisionName: empty(previousRevision) ? '${name}--${suffix}' : previousRevision, weight: 100 }
        ]
      }
      registries: [
        { server: registryLogin, identity: identityId }
      ]
    }
    template: {
      revisionSuffix: suffix
      containers: [
        {
          name: 'capstone'
          image: '${registryLogin}/capstone@${imageDigest}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(appSettings, empty(applicationInsightsName) ? [] : [
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights!.properties.ConnectionString }
          ])
          probes: [
            {
              type: 'Startup'
              tcpSocket: { port: 8080 }
              periodSeconds: 2
              failureThreshold: 30
            }
            {
              type: 'Liveness'
              tcpSocket: { port: 8080 }
              periodSeconds: 10
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health', port: 8080 }
              periodSeconds: 5
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [
          { name: 'http', http: { metadata: { concurrentRequests: '10' } } }
        ]
      }
    }
  }
}

resource auth 'Microsoft.App/containerApps/authConfigs@2025-07-01' = {
  parent: app
  name: 'current'
  properties: {
    platform: { enabled: true }
    globalValidation: {
      // Return401 is the ARM setting for requiring authentication on other paths.
      unauthenticatedClientAction: 'Return401'
      excludedPaths: [
        '/'
        '/hosted'
        '/hosted/'
        '/hosted/index.html'
        '/hosted/redirect.html'
        '/hosted/desk.js'
        '/hosted/desk.css'
        '/hosted/vendor/msal-browser-4.30.0.min.js'
        '/hosted/config'
        '/health'
      ]
    }
    httpSettings: { requireHttps: true }
    identityProviders: {
      azureActiveDirectory: {
        enabled: true
        registration: {
          clientId: appClientId
          openIdIssuer: uri(authority, '${tenantId}/v2.0')
        }
        validation: {
          allowedAudiences: [appClientId]
          defaultAuthorizationPolicy: {
            allowedApplications: allowedClientIds
            allowedPrincipals: { identities: allowedPrincipalIds }
          }
        }
      }
    }
  }
}

output host string = app.properties.configuration.ingress.fqdn
output revision string = '${name}--${suffix}'
output principalId string = app.identity.principalId

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccount
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' existing = {
  name: 'default'
  parent: storage
}

resource approvalTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' existing = {
  parent: tableService
  name: 'SupportActions'
}

resource actions 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(approvalTable.id, app.id, 'StorageTableDataContributor')
  scope: approvalTable
  properties: {
    principalId: app.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
  }
}
