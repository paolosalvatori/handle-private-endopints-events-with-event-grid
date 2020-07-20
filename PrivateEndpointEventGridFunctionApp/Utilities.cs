#region Copyright
//=======================================================================================
// Microsoft 
//
// This sample is supplemental to the technical guidance published on my GitHub account
// at https://github.com/paolosalvatori. 
// 
// Author: Paolo Salvatori
//=======================================================================================
// Copyright (c) Microsoft Corporation. All rights reserved.
// 
// LICENSED UNDER THE APACHE LICENSE, VERSION 2.0 (THE "LICENSE"); YOU MAY NOT USE THESE 
// FILES EXCEPT IN COMPLIANCE WITH THE LICENSE. YOU MAY OBTAIN A COPY OF THE LICENSE AT 
// http://www.apache.org/licenses/LICENSE-2.0
// UNLESS REQUIRED BY APPLICABLE LAW OR AGREED TO IN WRITING, SOFTWARE DISTRIBUTED UNDER THE 
// LICENSE IS DISTRIBUTED ON AN "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY 
// KIND, EITHER EXPRESS OR IMPLIED. SEE THE LICENSE FOR THE SPECIFIC LANGUAGE GOVERNING 
// PERMISSIONS AND LIMITATIONS UNDER THE LICENSE.
//=======================================================================================
#endregion

#region Using Directives
using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.EventGrid.Models;
using YamlDotNet.Serialization;
using Newtonsoft.Json;
#endregion

namespace PrivateEndpointEventGridFunctionApp
{
    public class Utilities
    {
        #region Private Constants
        //****************************************
        // Resource Id indexes
        //****************************************
        public const int SubscriptionIdIndex = 1;
        public const int ResourceGroupIndex = 3;
        public const int ResourceProviderIndex = 5;
        public const int ResourceTypeIndex = 6;
        public const int ResourceNameIndex = 7;
        public const int SubResourceTypeIndex = 8;
        public const int SubResourceNameIndex = 9;

        //****************************************
        // String constants
        //****************************************
        public const string NicIdMetadataKey = "nicId";

        //****************************************
        // Event Types
        //****************************************
        public const string ResourceWriteSuccess = "Microsoft.Resources.ResourceWriteSuccess";
        public const string ResourceDeleteSuccess = "Microsoft.Resources.ResourceDeleteSuccess";
        #endregion

        #region Private Fields
        public ILogger logger;
        #endregion

        #region Public Constructors
        public Utilities(ILogger logger)
        {
            this.logger = logger;
        }
        #endregion

        #region Public Methods
        public void PrintJson(string text, object instance)
        {
            var json = JsonConvert.SerializeObject(instance, Formatting.Indented);
            LogInformation($"{text}\r\n{json}");
        }

        public void PrintYaml(string text, object instance)
        {
            var serializer = new SerializerBuilder().Build();
            var yaml = serializer.Serialize(instance);
            LogInformation($"{text}\r\n{yaml}");
        }

        public string[] SplitResourceId(string resourceId)
        {
            return string.IsNullOrWhiteSpace(resourceId) ?
                null :
                resourceId.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);
        }

        public string CreateResourceId(string subscriptionId,
                                               string resourceGroup,
                                               string resourceProvider,
                                               string resourceType,
                                               string resourceName)
        {
            return string.IsNullOrWhiteSpace(subscriptionId) ||
                   string.IsNullOrWhiteSpace(resourceGroup) ||
                   string.IsNullOrWhiteSpace(resourceProvider) ||
                   string.IsNullOrWhiteSpace(resourceType) ||
                   string.IsNullOrWhiteSpace(resourceName) ?
                   null :
                   $"/subscriptions/{subscriptionId}/resourcegroups/{resourceGroup}/providers/{resourceProvider}/{resourceType}/{resourceName}";

        }

        public string CreateResourceId(string subscriptionId,
                                       string resourceGroup,
                                       string resourceProvider,
                                       string resourceType,
                                       string resourceName,
                                       string subResourceType,
                                       string subResourceName)
        {
            return string.IsNullOrWhiteSpace(subscriptionId) ||
                   string.IsNullOrWhiteSpace(resourceGroup) ||
                   string.IsNullOrWhiteSpace(resourceProvider) ||
                   string.IsNullOrWhiteSpace(resourceType) ||
                   string.IsNullOrWhiteSpace(resourceName) ||
                   string.IsNullOrWhiteSpace(subResourceType) ||
                   string.IsNullOrWhiteSpace(subResourceName) ?
                   null :
                   $"/subscriptions/{subscriptionId}/resourcegroups/{resourceGroup}/providers/{resourceProvider}/{resourceType}/{resourceName}/{subResourceType}/{subResourceName}";

        }

        public void PrintResourceId(string resourceId)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                return;
            }
            var elements = resourceId.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);
            var builder = new StringBuilder();
            builder.Append("\n\tSubscriptionId: ").Append(elements[SubscriptionIdIndex])
                .Append("\n\tResourceGroup: ").Append(elements[ResourceGroupIndex])
                .Append("\n\tResourceProvider: ").Append(elements[ResourceProviderIndex])
                .Append("\n\tResourceType: ").Append(elements[ResourceTypeIndex])
                .Append("\n\tResourceName: ").Append(elements[ResourceNameIndex]);
            if (elements.Length >= 10)
            {
                builder.Append("\n\tSubResourceType: ").Append(elements[SubResourceTypeIndex]);
                builder.Append("\n\tSubResourceName: ").Append(elements[SubResourceNameIndex]);
            }
            LogInformation(builder.ToString());
        }

        public void LogInformation(string text, EventGridEvent eventGridEvent = null)
        {
            try
            {
                var builder = new StringBuilder();
                builder.AppendLine(text);

                try
                {
                    if (eventGridEvent != null)
                    {
                        builder.AppendLine(JsonConvert.SerializeObject(eventGridEvent, Formatting.Indented));
                    }
                }
                catch (Exception)
                { }
                logger.LogInformation(builder.ToString());
            }
            catch (Exception)
            { }
        }

        public void LogError(Exception ex, EventGridEvent eventGridEvent = null)
        {
            LogError(ex.GetFullMessage(), eventGridEvent);
        }

        public void LogError(string text, EventGridEvent eventGridEvent = null)
        {
            try
            {
                var builder = new StringBuilder();
                builder.AppendLine(text);

                try
                {
                    if (eventGridEvent != null)
                    {
                        builder.AppendLine(JsonConvert.SerializeObject(eventGridEvent, Formatting.Indented));
                    }
                }
                catch (Exception) 
                { }
                logger.LogError(builder.ToString());
            }
            catch (Exception)
            { }
        }

        public string YamlToJson(string yaml)
        {
            if (string.IsNullOrWhiteSpace(yaml))
            {
                throw new ArgumentNullException(nameof(yaml), $"{nameof(yaml)} parameter cannot be null.");
            }
            var stringReader = new StringReader(yaml);
            var deserializer = new DeserializerBuilder().Build();
            var obj = deserializer.Deserialize(stringReader);
            var serializer = new SerializerBuilder()
                .JsonCompatible()
                .Build();
            return serializer.Serialize(obj);
        }

        public string JsonToYaml(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentNullException(nameof(json), $"{nameof(json)} parameter cannot be null.");
            }
            var stringReader = new StringReader(json);
            var deserializer = new DeserializerBuilder().Build();
            var obj = deserializer.Deserialize(stringReader);
            var serializer = new SerializerBuilder().Build();
            return serializer.Serialize(obj);
        }
        #endregion
    }
}