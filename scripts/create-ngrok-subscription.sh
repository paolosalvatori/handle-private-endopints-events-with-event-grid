#!/bin/bash

# Prefix
prefix="PrivateEndpoint"

# Subscription endpoint type
endpointType="webhook"

# Event types
eventTypes="Microsoft.Resources.ResourceWriteSuccess Microsoft.Resources.ResourceDeleteSuccess"

# Operations to monitor
operationNames="Microsoft.Network/networkInterfaces/write Microsoft.Network/networkInterfaces/delete"

# Webhook URL
functionName="ProcessEvents"
ngrockSubdomain="0978bcd64f4e"
endpointUrl="https://"$ngrockSubdomain".ngrok.io/runtime/webhooks/EventGrid?functionName="$functionName

# Name of the Event Grid subscription
eventGridSubscriptionName="${prefix}NgrokSubscriber"

# Location 
location="WestEurope"

# Formatting
redPrefix="\033[38;5;1m"
redPostfix="\033[m"

# SubscriptionId of the current subscription
subscriptionId=$(az account show --query id --output tsv)
subscriptionName=$(az account show --query name --output tsv)

# Resource id
resourceId="/subscriptions/$subscriptionId"

# Check if the Event Grid subscription exists
az eventgrid event-subscription show \
--name "$eventGridSubscriptionName" \
--source-resource-id "/subscriptions/$subscriptionId" &>/dev/null

if [[ $? != 0 ]]; then
    echo "No [$eventGridSubscriptionName] Event Grid subscription actually exists for [$subscriptionName] subscription events"
    echo "Creating [$eventGridSubscriptionName] Event Grid subscription for [$subscriptionName] subscription events..."
    echo "Webhook URL=[$endpointUrl]"
    
    # Create Event Hub subscription
    error=$(az eventgrid event-subscription create \
            --endpoint-type "$endpointType" \
            --endpoint "$endpointUrl" \
            --name "$eventGridSubscriptionName" \
            --included-event-types $eventTypes \
            --advanced-filter data.operationName stringin $operationNames \
            --source-resource-id "$resourceId" 2>&1)
                                           
    # Create the Event Grid subscription
    if [[ $? == 0 ]]; then
        echo "[$eventGridSubscriptionName] Event Grid subscription successfully created in the [$subscriptionName] subscription"
    else
        echo "Failed to create [$eventGridSubscriptionName] Event Grid subscription in the [$subscriptionName] subscription"
        echo -e "${redPrefix}${error}${redPostfix}"
        exit
    fi
else
    echo "[$eventGridSubscriptionName] Event Grid subscription already exists in the [$subscriptionName] subscription"
fi