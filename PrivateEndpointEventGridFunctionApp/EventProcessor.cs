#region Copyright
//=======================================================================================
// This sample is supplemental to the technical guidance published on the community
// blog at https://github.com/paolosalvatori. 
// 
// Author: Paolo Salvatori
//=======================================================================================
// Copyright © 2019 Microsoft Corporation. All rights reserved.
// 
// THIS CODE AND INFORMATION IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER 
// EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF 
// MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE. YOU BEAR THE RISK OF USING IT.
//=======================================================================================
#endregion

#region Local Endpoint
// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}
#endregion

#region Using Directives
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.EventGrid;
using System.Text.Json;
using System.Reflection.PortableExecutable;
#endregion

namespace PrivateEndpointEventGridFunctionApp
{
    public static class EventProcessor
    {
        #region Public Methods
        [FunctionName("ProcessEvents")]
        public static async Task Run([EventGridTrigger] EventGridEvent eventGridEvent,
                                     Microsoft.Azure.WebJobs.ExecutionContext context, // <- You need this to add the local.settings.json file for local execution
                                     ILogger logger)
        {
            // Create utilities object
            var utilities = new Utilities(logger);

            try
            {
                // Read configuration settings
                var config = new ConfigurationBuilder()
                    .SetBasePath(context.FunctionAppDirectory)
                    .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true) // <- This gives you access to your application settings in your local development environment
                    .AddEnvironmentVariables() // <- This is what actually gets you the application settings in Azure
                    .Build();

                // Specifies the id of the subscription that contains the private DNS zones
                var subscriptionId = config["SubscriptionId"];
                if (string.IsNullOrWhiteSpace(subscriptionId))
                {
                    throw new ArgumentNullException(nameof(subscriptionId), $"{nameof(subscriptionId)} app setting cannot be null or empty.");
                }

                // Specifies the name of the resource group that contains the private DNS zones
                var resourceGroup = config["ResourceGroup"];
                if (string.IsNullOrWhiteSpace(resourceGroup))
                {
                    throw new ArgumentNullException(nameof(resourceGroup), $"{nameof(resourceGroup)} app setting cannot be null or empty.");
                }

                // Validate input parameter
                if (eventGridEvent == null)
                {
                    throw new ArgumentNullException("Event Grid event cannot be null.");
                }

                if (string.IsNullOrWhiteSpace(eventGridEvent.EventType))
                {
                    throw new ArgumentNullException("The type of the Event Grid event cannot be null or empty.");
                }

                // Extract and process payload
                if (eventGridEvent.Data is JObject jObject)
                {
                    //azure.PrivateLinkServices.
                    switch (eventGridEvent.EventType)
                    {
                        case Utilities.ResourceWriteSuccess:
                            // Log Event Grid event
                            await HandlePrivateEndpointCreatedEvent(eventGridEvent,
                                                                    utilities,
                                                                    config,
                                                                    subscriptionId,
                                                                    resourceGroup);
                            break;
                        case Utilities.ResourceDeleteSuccess:
                            await HandlePrivateEndpointDeletedEvent(eventGridEvent,
                                                                    utilities,
                                                                    config,
                                                                    subscriptionId,
                                                                    resourceGroup);
                            break;
                    }
                }

            }
            catch (Exception ex)
            {
                utilities.LogError(ex, eventGridEvent);
                throw;
            }
        }
        #endregion

        #region Private Static Methods
        private static IAzure Authenticate(string subscriptionId, IConfiguration config)
        {
            // Check if the Azure Function runs locally
            var debug = config["Debug"];
            if (!string.IsNullOrWhiteSpace(debug) &&
                string.Compare(debug, "true", 0) == 0)
            {
                // Specifies the client id of the service principal to use when debugging locally
                var clientId = config["ClientId"];
                if (string.IsNullOrWhiteSpace(clientId))
                {
                    throw new ArgumentNullException(nameof(clientId), $"{nameof(clientId)} app setting cannot be null or empty.");
                }

                // Specifies the client secret of the service principal to use when debugging locally
                var clientSecret = config["ClientSecret"];
                if (string.IsNullOrWhiteSpace(clientSecret))
                {
                    throw new ArgumentNullException(nameof(clientSecret), $"{nameof(clientSecret)} app setting cannot be null or empty.");
                }

                // Specifies the tenant id to use when debugging locally
                var tenantId = config["TenantId"];
                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    throw new ArgumentNullException(nameof(tenantId), $"{nameof(tenantId)} app setting cannot be null or empty.");
                }

                var creds = new AzureCredentialsFactory().FromServicePrincipal(clientId, clientSecret, tenantId, AzureEnvironment.AzureGlobalCloud);
                return Azure.Authenticate(creds).WithSubscription(subscriptionId);
            }
            else
            {
                var factory = new AzureCredentialsFactory();
                var credentials = factory.FromMSI(new MSILoginInformation(MSIResourceType.AppService),
                                                  AzureEnvironment.AzureGlobalCloud);
                return Azure.Configure().
                       WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic).
                       Authenticate(credentials).WithSubscription(subscriptionId);
            }
        }

        /// <summary>
        /// Handles a private endpoint created event by creating an A record set in the corresponding private DNS zone
        /// </summary>
        /// <param name="jObject">The Event Grid event</param>
        /// <param name="utilities">The utilities object</param>
        /// <param name="config">Confirguation object</param>
        /// <param name="subscriptionId">Specifies the id of the subscription that contains the private DNS zones</param>
        /// <param name="resourceGroup">Specifies the name of the resource group that contains the private DNS zones</param>
        /// <returns>Task object to wait for the completion of the method</returns>
        private static async Task HandlePrivateEndpointCreatedEvent(EventGridEvent eventGridEvent, 
                                                                    Utilities utilities, 
                                                                    IConfigurationRoot config,
                                                                    string subscriptionId, 
                                                                    string resourceGroup)
        {
            try
            {
                // Log event
                utilities.LogInformation($"A new [{Utilities.ResourceWriteSuccess}] event has been received:", eventGridEvent);

                // Cast the payload to JObject
                var jObject = eventGridEvent.Data as JObject;

                // Get the resource id from the payload
                jObject.TryGetValue("resourceUri", out JToken jToken);
                var resourceUri = jToken.Value<string>();

                // Get the event subscription id from the payload
                jObject.TryGetValue("subscriptionId", out jToken);
                var eventSubscriptionId = jToken.Value<string>();

                // Verify that the resource id is not null or empty
                if (string.IsNullOrWhiteSpace(resourceUri))
                {
                    throw new ApplicationException("Resource id cannot be null or empty.");
                }

                // Verify that the event subscription id is not null or empty
                if (string.IsNullOrWhiteSpace(eventSubscriptionId))
                {
                    throw new ApplicationException("Event subscriptionId id cannot be null or empty.");
                }

                // Authenticate using the credentials of the service principal
                utilities.LogInformation($"Authenticating using the system assigned managed identity against [{subscriptionId}] subscription...", eventGridEvent);
                var azure = Authenticate(eventSubscriptionId, config);
                utilities.LogInformation("Authentication successfully completed", eventGridEvent);

                // Define a cancellation token
                var source = new CancellationTokenSource();
                var token = source.Token;

                // Retrieve the network interface associated to the private endpoint
                var nic = await azure.NetworkInterfaces.GetByIdAsync(resourceUri, token);
                if (nic == null)
                {
                    throw new ApplicationException($"The network interface object cannot be null.");
                }

                // Check if the nic is associated to a private endpoint rather than to a virtual machine
                if (nic.Inner.PrivateEndpoint == null)
                {
                    throw new ApplicationException("The network interface is not associated to a private endpoint.");
                }

                // Retrieve the resource id of the private endpoint associated to the nic
                if (string.IsNullOrWhiteSpace(nic.Inner.PrivateEndpoint.Id))
                {
                    throw new ApplicationException("The resource id of the private endpoint associated to the network interface cannot be null or empty.");
                }

                // Get the name of the private endpoint associated to the network interface
                var elements = utilities.SplitResourceId(nic.Inner.PrivateEndpoint.Id);

                if (!elements.Any() || elements.Length < Utilities.ResourceNameIndex + 1)
                {
                    throw new ApplicationException("The resource id of the private endpoint associated to the network interface is not correct");
                }
                var privateEndpointName = elements[Utilities.ResourceNameIndex];

                // Get the DNS FQDN associated to the NIC
                if (string.IsNullOrWhiteSpace(nic?.Inner?.IpConfigurations?[0].PrivateIPAddress))
                {
                    throw new ApplicationException("The private IP address of the network interface cannot be null.");
                }
                var privateIPAddress = nic.Inner.IpConfigurations[0].PrivateIPAddress;

                // Get the DNS FQDN associated to the NIC
                if (nic?.Inner?.IpConfigurations?[0].PrivateLinkConnectionProperties?.Fqdns?[0] == null)
                {
                    throw new ApplicationException("The fqdn of the private link connection associated to the network interface cannot be null.");
                }
                var fqdn = nic.Inner.IpConfigurations[0].PrivateLinkConnectionProperties.Fqdns[0];

                // Extrapolate the name of the A record and the name of the private DNS zone
                var index = fqdn.IndexOf(".");
                if (index == -1)
                {
                    throw new ApplicationException("The fqdn of the private link connection has a wrong format.");
                }
                var aRecordSetName = fqdn.Substring(0, index);
                var privateDnsZoneName = $"privatelink.{fqdn.Substring(index + 1)}";

                // Authenticate using the credentials of the service principal
                Authenticate(subscriptionId, config);

                // Get the private DNS zone
                var privateDnsZone = await azure.PrivateDnsZones.GetByResourceGroupAsync(resourceGroup, privateDnsZoneName, token);

                // Verify that the private DNS zone is not null
                if (privateDnsZone == null)
                {
                    throw new ApplicationException("The private DNS zone cannot be null.");
                }

                // Create A record set
                utilities.LogInformation($"Creating [{aRecordSetName}] A record set with [{privateIPAddress}] private IP address in [{privateDnsZoneName}] private DNS zone in [{resourceGroup}] resource group...", eventGridEvent);
                privateDnsZone = await privateDnsZone.Update()
                                                    .DefineARecordSet(aRecordSetName)
                                                    .WithIPv4Address(privateIPAddress)
                                                    .WithMetadata(Utilities.NicIdMetadataKey, nic.Id)
                                                    .Attach()
                                                    .ApplyAsync();

                utilities.LogInformation($"[{aRecordSetName}] A record set with [{privateIPAddress}] private IP address has been successfully created in [{privateDnsZoneName}] private DNS zone in [{resourceGroup}] resource group", eventGridEvent);
            }
            catch (Exception ex)
            {
                utilities.LogError(ex.Message, eventGridEvent);
                throw;
            }
        }

        /// <summary>
        /// Handles a private endpoint deleted event by removing an A record set from the corresponding private DNS zone
        /// </summary>
        /// <param name="jObject">The Event Grid event</param>
        /// <param name="utilities">The utilities object</param>
        /// <param name="config">Confirguation object</param>
        /// <param name="subscriptionId">Specifies the id of the subscription that contains the private DNS zones</param>
        /// <param name="resourceGroup">Specifies the name of the resource group that contains the private DNS zones</param>
        /// <returns>Task object to wait for the completion of the method</returns>
        private static async Task HandlePrivateEndpointDeletedEvent(EventGridEvent eventGridEvent,
                                                                    Utilities utilities,
                                                                    IConfigurationRoot config,
                                                                    string subscriptionId,
                                                                    string resourceGroup)
        {
            try
            {
                // Log event
                utilities.LogInformation($"A new [{Utilities.ResourceDeleteSuccess}] event has been received:", eventGridEvent);

                // Cast the payload to JObject
                var jObject = eventGridEvent.Data as JObject;

                // Get the resource id from the payload
                jObject.TryGetValue("resourceUri", out JToken jToken);
                var resourceUri = jToken.Value<string>();

                // Get the event subscription id from the payload
                jObject.TryGetValue("subscriptionId", out jToken);
                var eventSubscriptionId = jToken.Value<string>();

                // Verify that the resource id is not null or empty
                if (string.IsNullOrWhiteSpace(resourceUri))
                {
                    throw new ApplicationException("Resource id cannot be null or empty.");
                }

                // Verify that the event subscription id is not null or empty
                if (string.IsNullOrWhiteSpace(eventSubscriptionId))
                {
                    throw new ApplicationException("Event subscriptionId id cannot be null or empty.");
                }

                // Authenticate using the credentials of the service principal
                utilities.LogInformation($"Authenticating using the system assigned managed identity against [{subscriptionId}] subscription...", eventGridEvent);
                var azure = Authenticate(eventSubscriptionId, config);
                utilities.LogInformation("Authentication successfully completed", eventGridEvent);

                // Define a cancellation token
                var source = new CancellationTokenSource();
                var token = source.Token;

                // Get the private DNS zone
                var privateDnsZones = await azure.PrivateDnsZones.ListByResourceGroupAsync(resourceGroup);

                if (!privateDnsZones.Any())
                {
                    throw new ApplicationException($"No private DNS zone exists in [{resourceGroup}] resource group");
                }

                foreach (var privateDnsZone in privateDnsZones)
                {
                    utilities.LogInformation($"Searching for an A record set with [{Utilities.NicIdMetadataKey}] metadata key equal to the network interface Id in the [{privateDnsZone.Name}] private DNS zone in [{resourceGroup}] resource group...", eventGridEvent);

                    var aRecordSets = privateDnsZone.ARecordSets.List();
                    foreach (var aRecordSet in aRecordSets)
                    {
                        if (aRecordSet.Metadata.ContainsKey(Utilities.NicIdMetadataKey) &&
                            string.Compare(aRecordSet.Metadata[Utilities.NicIdMetadataKey], resourceUri, 0) == 0)
                        {
                            var privateIPAddress = aRecordSet.IPv4Addresses.Any() ? aRecordSet.IPv4Addresses[0] : "unknown";
                            utilities.LogInformation($"Removing [{aRecordSet.Name}] A record set with [{privateIPAddress}] private IP address from [{privateDnsZone.Name}] private DNS zone in [{resourceGroup}] resource group...", eventGridEvent);

                            var zone = privateDnsZone.Update()
                                          .WithoutARecordSet(aRecordSet.Name)
                                          .Apply();

                            utilities.LogInformation($"[{aRecordSet.Name}] A record set with [{privateIPAddress}] private IP address has been successfully removed from [{privateDnsZone.Name}] private DNS zone in [{resourceGroup}] resource group", eventGridEvent);
                            return;
                        }
                    }
                    utilities.LogInformation($"No A record set with [{Utilities.NicIdMetadataKey}] metadata key equal to the network interface Id exists in the [{privateDnsZone.Name}] private DNS zone in [{resourceGroup}] resource group", eventGridEvent);
                }
                utilities.LogInformation($"No A record set with [{Utilities.NicIdMetadataKey}] metadata key equal to the network interface Id has been successfully found in any private DNS zone in [{resourceGroup}] resource group", eventGridEvent);
            }
            catch (Exception ex)
            {
                utilities.LogError(ex.Message, eventGridEvent);
                throw;
            }
        }
        #endregion
    }
}
