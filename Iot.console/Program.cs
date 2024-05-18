using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.UaFx;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using Opc.UaFx.Client;
using System.Linq;
using Microsoft.Azure.Devices.Shared;

namespace ProductionMonitoringAgent
{
    class Program
    {
        private static List<string> deviceNames = new List<string>();
        static async Task Main(string[] args)
        {
            // OPC UA server configuration
            var opcServerUrl = "opc.tcp://localhost:4840"; // Replace with actual OPC server URL
            var opcClient = new Opc.UaFx.Client.OpcClient(opcServerUrl);

            // Azure IoT Hub configuration
            var iotHubConnectionString = "HostName=Zajecia-wmii.azure-devices.net;DeviceId=Device1;SharedAccessKey=BsFRd2Vy4SWlmqLLkpX8fqf54rD+xiUrNAIoTA2IEJc="; // Replace with actual IoT Hub connection string
            var deviceClient = DeviceClient.CreateFromConnectionString(iotHubConnectionString, TransportType.Mqtt);

            try
            {
                // Connect to OPC UA server
                opcClient.Connect();

                var deviceNames = new List<string>();
                // Browse nodes to get device names
                var nodes =  opcClient.BrowseNode(OpcObjectTypes.ObjectsFolder);
                Browse(nodes);
                void Browse(OpcNodeInfo node, int level = 0)
                {
                    if (node.NodeId.Namespace == 2 && level==1)
                    {
                    deviceNames.Add(node.Attribute(OpcAttribute.DisplayName).Value.ToString());
                    }

                    level++;

                    foreach (var childNode in node.Children())
                    {
                        Browse(childNode, level);
                    }
                    
                }

                await deviceClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, null);

                

                // Main loop for reading OPC data and sending to IoT Hub

                while (true)
                {
                    // Read OPC data for each device
                    foreach (var deviceName in deviceNames)
                    {

                        // Read OPC data for each device
                        
                        var productionStatus = opcClient.ReadNode($"ns=2;s={deviceName}/ProductionStatus").Value;
                        var workorderId = opcClient.ReadNode($"ns=2;s={deviceName}/WorkorderId").Value;
                        var productionRate = Convert.ToDouble(opcClient.ReadNode($"ns=2;s={deviceName}/ProductionRate").Value);
                        var goodCount = Convert.ToInt64(opcClient.ReadNode($"ns=2;s={deviceName}/GoodCount").Value);
                        var badCount = Convert.ToInt64(opcClient.ReadNode($"ns=2;s={deviceName}/BadCount").Value);
                        var temperature = Convert.ToDouble(opcClient.ReadNode($"ns=2;s={deviceName}/Temperature").Value);
                        var deviceErrors = Convert.ToInt32(opcClient.ReadNode($"ns=2;s={deviceName}/DeviceErrors").Value);
                        // Create telemetry message
                        var telemetryData = new
                        {
                            deviceName,
                            productionStatus,
                            workorderId,
                            productionRate,
                            goodCount,
                            badCount,
                            temperature,
                            deviceErrors
                        };
                        var messageString = JsonConvert.SerializeObject(telemetryData);
                        var message = new Message(System.Text.Encoding.UTF8.GetBytes(messageString));

                        // Send telemetry message to IoT Hub
                        await deviceClient.SendEventAsync(message);
                    }

                    // Wait for next iteration
                    await Task.Delay(TimeSpan.FromSeconds(10)); // Adjust as needed
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
                    
            
            finally
            {
                // Disconnect from OPC UA server
                opcClient.Disconnect();
            }
            async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
            {
                foreach (KeyValuePair<string, object> property in desiredProperties)
                {
                    if (property.Key == "ProductionRate")
                    {
                        // Assume deviceName is available and corresponds to the device you want to update
                        foreach (var deviceName in deviceNames)
                        {
                            // Update the production rate on the OPC UA server
                            var desiredProductionRate = Convert.ToDouble(property.Value);
                            opcClient.WriteNode($"ns=2;s={deviceName}/ProductionRate", desiredProductionRate);
                            Console.WriteLine(desiredProductionRate.ToString());
                            // Update reported properties to reflect the change
                            var reportedProperties = new TwinCollection
                            {
                                ["ProductionRate"] = desiredProductionRate
                            };
                            Console.WriteLine(desiredProductionRate.ToString());
                            await deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
                        }
                    }
                }
            }
        }
    }
}