targetScope = 'resourceGroup'

@description('Environment label applied to the identity name and tag.')
@allowed(['test', 'production'])
param environment string

@description('Region for the managed identity. Verify service availability separately.')
param location string = resourceGroup().location

resource runtime 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'support-${environment}-runtime'
  location: location
  tags: {
    environment: environment
    workload: 'support-assistant'
  }
}

output resourceId string = runtime.id
output clientId string = runtime.properties.clientId
output principalId string = runtime.properties.principalId
